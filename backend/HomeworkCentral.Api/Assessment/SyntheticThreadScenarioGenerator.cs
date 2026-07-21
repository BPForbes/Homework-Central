using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

public sealed class SyntheticThreadScenarioGenerator(ILlmClient llm)
{
    public async Task<SyntheticThreadScenario?> GenerateAsync(NeuralTrainingMode mode, CancellationToken ct)
    {
        string selected = mode == NeuralTrainingMode.Both ? "moderation or tutoring" : mode.ToString().ToLowerInvariant();
        const string system = "Generate a fictional school-chat ticket scenario only. Return JSON with category, requirement, initialContext, and messages. Each message needs authorId, authorRole, channel, content, isDistractor, channelRelevance (0..1), proposedApproval (0..1), proposedVoterCount (1..200), controversy (0..1), reasons array. Include relevant messages, unrelated casual distractors, multiple users/roles/channels, and a short thread. Never use real people or data.";
        string? json = await llm.ChatJsonAsync(system, $"Create one {selected} training scenario with 4 to 8 messages.", ct);
        if (string.IsNullOrWhiteSpace(json)) return CreateFallback(mode);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string category = String(root, "category"), requirement = String(root, "requirement"), context = String(root, "initialContext");
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(requirement) || !root.TryGetProperty("messages", out JsonElement source) || source.ValueKind != JsonValueKind.Array) return null;
            List<SyntheticThreadMessage> messages = [];
            int index = 0;
            foreach (JsonElement item in source.EnumerateArray().Take(8))
            {
                string content = String(item, "content"); if (string.IsNullOrWhiteSpace(content)) continue;
                JsonElement reasons = item.TryGetProperty("reasons", out JsonElement rawReasons) ? rawReasons : default;
                messages.Add(new SyntheticThreadMessage(index++, String(item, "authorId", "user-" + index), String(item, "authorRole", "student"), String(item, "channel", "general"), content[..Math.Min(content.Length, 2000)], Bool(item, "isDistractor"), Unit(item, "channelRelevance", .5f), new SyntheticCommunityIntent(Unit(item, "proposedApproval", .5f), Math.Clamp(Int(item, "proposedVoterCount", 10), 1, 200), Unit(item, "controversy", .5f), reasons.ValueKind == JsonValueKind.Array ? reasons.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToList() : [])));
            }
            return messages.Count == 0 ? CreateFallback(mode) : new SyntheticThreadScenario(category[..Math.Min(category.Length, 64)], requirement[..Math.Min(requirement.Length, 4000)], context[..Math.Min(context.Length, 2500)], messages);
        }
        catch (JsonException) { return CreateFallback(mode); }
    }

    private static SyntheticThreadScenario CreateFallback(NeuralTrainingMode mode)
    {
        bool tutoring = mode == NeuralTrainingMode.Tutoring;
        string category = tutoring ? "tutoring-competency" : "moderation-profanity";
        string requirement = tutoring
            ? "Assess whether the applicant demonstrates helpful, accurate math tutoring in the appropriate subject channel."
            : "Assess whether the reported user is engaging in prohibited profanity or harassment in the reported channel.";
        string context = tutoring
            ? "A math-tutor applicant is being observed across math, English, and casual channels."
            : "A member was reported for repeated profanity. Evaluate the reported behavior in its channel context.";
        IReadOnlyList<SyntheticThreadMessage> messages = tutoring
            ? [
                new(0, "synthetic-applicant", "tutor applicant", "math-help", "To solve x² - 5x + 6 = 0, factor it into (x - 2)(x - 3), so x is 2 or 3.", false, 1f, new(.9f, 30, .1f, ["accurate math explanation"])),
                new(1, "synthetic-applicant", "tutor applicant", "english-help", "The main idea is the message the author wants the reader to understand.", false, .55f, new(.75f, 12, .2f, ["helpful outside primary subject"])),
                new(2, "synthetic-peer", "student", "lounge", "Anyone watching a movie after homework?", true, .05f, new(.5f, 6, .3f, ["unrelated casual message"])),
                new(3, "synthetic-applicant", "tutor applicant", "math-help", "I think 8 × 7 is 54, so the answer is 54.", false, 1f, new(.15f, 24, .15f, ["incorrect math response"]))]
            : [
                new(0, "synthetic-reported", "student", "general", "You are such a damn idiot. Stop talking.", false, 1f, new(.05f, 34, .15f, ["reported profanity"])),
                new(1, "synthetic-peer", "student", "general", "Please keep the conversation respectful.", true, .35f, new(.8f, 18, .1f, ["de-escalation"])),
                new(2, "synthetic-reported", "student", "lounge", "I finally finished my homework.", true, .05f, new(.55f, 8, .25f, ["unrelated casual message"])),
                new(3, "synthetic-reported", "student", "general", "This is stupid as hell and I do not care.", false, 1f, new(.08f, 28, .1f, ["continued profanity"]))];
        return new SyntheticThreadScenario(category, requirement, context, messages);
    }
    private static string String(JsonElement root, string property, string fallback = "") => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static int Int(JsonElement root, string property, int fallback) => root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;
    private static float Unit(JsonElement root, string property, float fallback) => root.TryGetProperty(property, out JsonElement value) && value.TryGetSingle(out float result) ? Math.Clamp(result, 0, 1) : fallback;
    private static bool Bool(JsonElement root, string property) => root.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True;
}
