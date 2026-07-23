using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Builds fictional ticket threads for neural training without using real student data.
/// </summary>
public sealed class SyntheticThreadScenarioGenerator(ILlmClient llm)
{
    private const string ScenarioSystemPrompt =
        "Generate a fictional school-chat ticket scenario only. Return JSON with category, requirement, initialContext, and messages. "
        + "Each message needs authorId, authorRole, channel, content, isDistractor, channelRelevance (0..1), expectedScore (0..1 evidence teacher label), expectedRelevance (0..1), proposedApproval (0..1), proposedVoterCount (1..200), controversy (0..1), reasons array. "
        + "For moderation scenarios, set category to one precise kebab-case concept slug such as payment-solicitation, tip-pressure, staff-impersonation, doxxing, violent-intent, credential-theft, coordinated-brigading, or fabricated-source — not broad labels like spam or harassment. "
        + "Include relevant messages, hard-negative near-misses when useful, unrelated casual distractors, multiple users/roles/channels, and a short thread. Never use real people or data.";

    public Task<SyntheticThreadScenario?> GenerateAsync(NeuralTrainingMode mode, CancellationToken ct) =>
        GenerateAsync(mode, hints: null, ct);

    public async Task<SyntheticThreadScenario?> GenerateAsync(
        NeuralTrainingMode mode,
        IReadOnlyList<string>? hints,
        CancellationToken ct)
    {
        string? json = await llm.ChatJsonAsync(ScenarioSystemPrompt, BuildUserPrompt(mode, hints), ct);
        if (string.IsNullOrWhiteSpace(json))
            return CreateFallback(mode);

        try
        {
            SyntheticThreadScenario? scenario = ParseScenario(json);
            return scenario is { Messages.Count: 0 } ? CreateFallback(mode) : scenario;
        }
        catch (JsonException)
        {
            return CreateFallback(mode);
        }
    }

    private static string BuildUserPrompt(NeuralTrainingMode mode, IReadOnlyList<string>? hints)
    {
        string selected = mode == NeuralTrainingMode.Both
            ? "moderation or tutoring"
            : mode.ToString().ToLowerInvariant();
        string prompt = $"Create one {selected} training scenario with 4 to 8 messages.";
        if (hints is null || hints.Count == 0)
            return prompt;

        // Balanced prior feedback: short constraints only — do not paste raw scores or over-steer.
        string joined = string.Join("\n- ", hints.Take(6));
        return prompt
            + "\n\nPrior evaluator notes (keep scenario diversity; do not overfit to these):\n- "
            + joined;
    }

    private static SyntheticThreadScenario? ParseScenario(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string category = String(root, "category");
        string requirement = String(root, "requirement");
        string context = String(root, "initialContext");

        if (string.IsNullOrWhiteSpace(category)
            || string.IsNullOrWhiteSpace(requirement)
            || !root.TryGetProperty("messages", out JsonElement source)
            || source.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<SyntheticThreadMessage> messages = ReadMessages(source);
        return new SyntheticThreadScenario(
            Truncate(category, 64),
            Truncate(requirement, 4000),
            Truncate(context, 2500),
            messages);
    }

    private static List<SyntheticThreadMessage> ReadMessages(JsonElement source)
    {
        List<SyntheticThreadMessage> messages = [];
        int index = 0;
        foreach (JsonElement item in source.EnumerateArray().Take(8))
        {
            string content = String(item, "content");
            if (string.IsNullOrWhiteSpace(content))
                continue;

            messages.Add(ReadMessage(item, content, ref index));
        }

        return messages;
    }

    private static SyntheticThreadMessage ReadMessage(JsonElement item, string content, ref int index)
    {
        int messageIndex = index;
        index++;
        float channelRelevance = Unit(item, "channelRelevance", .5f);
        return new SyntheticThreadMessage(
            messageIndex,
            String(item, "authorId", "user-" + index),
            String(item, "authorRole", "student"),
            String(item, "channel", "general"),
            Truncate(content, 2000),
            Bool(item, "isDistractor"),
            channelRelevance,
            ReadCommunityIntent(item),
            OptionalUnit(item, "expectedScore"),
            ReadTeacherRelevance(item, channelRelevance),
            OptionalUnit(item, "proposedApproval"),
            OptionalUnit(item, "evaluatorConfidence"));
    }

    private static SyntheticCommunityIntent ReadCommunityIntent(JsonElement item)
    {
        JsonElement reasons = item.TryGetProperty("reasons", out JsonElement rawReasons)
            ? rawReasons
            : default;

        return new SyntheticCommunityIntent(
            Unit(item, "proposedApproval", .5f),
            Math.Clamp(Int(item, "proposedVoterCount", 10), 1, 200),
            Unit(item, "controversy", .5f),
            ReadReasons(reasons));
    }

    private static List<string> ReadReasons(JsonElement reasons) =>
        reasons.ValueKind == JsonValueKind.Array
            ? reasons.EnumerateArray()
                .Where(static reason => reason.ValueKind == JsonValueKind.String)
                .Select(static reason => reason.GetString() ?? string.Empty)
                .ToList()
            : [];

    private static float? ReadTeacherRelevance(JsonElement item, float channelRelevance) =>
        OptionalUnit(item, "expectedRelevance")
        ?? (item.TryGetProperty("expectedRelevance", out _) ? null : channelRelevance);

    private static SyntheticThreadScenario CreateFallback(NeuralTrainingMode mode)
    {
        bool tutoring = mode == NeuralTrainingMode.Tutoring;
        string category = tutoring ? "tutoring-competency" : "payment-solicitation";
        string requirement = tutoring
            ? "Assess whether the applicant demonstrates helpful, accurate math tutoring in the appropriate subject channel."
            : "Monitor reportedConcept=payment-solicitation with relatedConcepts tip-solicitation, tip-pressure, off-platform-payment, paywalled-help.";
        string context = tutoring
            ? "A math-tutor applicant is being observed across math, English, and casual channels."
            : "A helper was reported for asking students to pay before finishing answers.";
        IReadOnlyList<SyntheticThreadMessage> messages = tutoring
            ? [
                new(0, "synthetic-applicant", "tutor applicant", "math-help", "To solve x² - 5x + 6 = 0, factor it into (x - 2)(x - 3), so x is 2 or 3.", false, 1f, new(.9f, 30, .1f, ["accurate math explanation"]), .9f, 1f, .9f, .85f),
                new(1, "synthetic-applicant", "tutor applicant", "english-help", "The main idea is the message the author wants the reader to understand.", false, .55f, new(.75f, 12, .2f, ["helpful outside primary subject"]), .7f, .55f, .75f, .7f),
                new(2, "synthetic-peer", "student", "lounge", "Anyone watching a movie after homework?", true, .05f, new(.5f, 6, .3f, ["unrelated casual message"]), .5f, .08f, .5f, .6f),
                new(3, "synthetic-applicant", "tutor applicant", "math-help", "I think 8 × 7 is 54, so the answer is 54.", false, 1f, new(.15f, 24, .15f, ["incorrect math response"]), .12f, 1f, .15f, .8f)]
            : [
                new(0, "synthetic-reported", "student", "math-help", "I know the answer, but send me $10 first.", false, 1f, new(.05f, 34, .15f, ["payment solicitation"]), .95f, 1f, .05f, .9f),
                new(1, "synthetic-peer", "student", "math-help", "Your explanation helped a lot. Can I tip you?", true, .9f, new(.8f, 18, .1f, ["voluntary tip offer hard-negative"]), .15f, .9f, .8f, .7f),
                new(2, "synthetic-reported", "student", "lounge", "I finally finished my homework.", true, .05f, new(.55f, 8, .25f, ["unrelated casual message"]), .5f, .08f, .55f, .6f),
                new(3, "synthetic-reported", "student", "math-help", "Tips are expected if you want the last step.", false, 1f, new(.08f, 28, .1f, ["tip pressure"]), .92f, 1f, .08f, .88f)];
        return new SyntheticThreadScenario(category, requirement, context, messages);
    }
    private static string Truncate(string value, int maxCharacters) =>
        value[..Math.Min(value.Length, maxCharacters)];

    private static string String(JsonElement root, string property, string fallback = "") =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int Int(JsonElement root, string property, int fallback) =>
        root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : fallback;

    private static float Unit(JsonElement root, string property, float fallback) =>
        root.TryGetProperty(property, out JsonElement value) && value.TryGetSingle(out float result)
            ? Math.Clamp(result, 0, 1)
            : fallback;

    private static float? OptionalUnit(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.TryGetSingle(out float result) ? Math.Clamp(result, 0, 1) : null;

    private static bool Bool(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True;
}
