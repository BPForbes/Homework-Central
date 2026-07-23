using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Builds fictional ticket threads for neural training without using real student data.
/// When a target category is supplied, scenarios are forced onto that filterable taxonomy slug.
/// </summary>
public sealed class SyntheticThreadScenarioGenerator(ILlmClient llm)
{
    private const string ScenarioSystemPrompt =
        "Generate a fictional school-chat ticket scenario only. Return JSON with category, requirement, initialContext, and messages. "
        + "Each message needs authorId, authorRole, channel, content, isDistractor, channelRelevance (0..1), expectedScore (0..1 evidence teacher label), expectedRelevance (0..1), proposedApproval (0..1), proposedVoterCount (1..200), controversy (0..1), reasons array. "
        + "Set category exactly to the kebab-case slug supplied in the user prompt from the ChatMonitoring taxonomy. "
        + "Do not substitute a different concept, and do not collapse onto payment-solicitation unless that slug is the target. "
        + "Include relevant messages, hard-negative near-misses when useful, unrelated casual distractors, multiple users/roles/channels, and a short thread. Never use real people or data.";

    public Task<SyntheticThreadScenario?> GenerateAsync(NeuralTrainingMode mode, CancellationToken ct) =>
        GenerateAsync(mode, hints: null, targetCategory: null, ct);

    public Task<SyntheticThreadScenario?> GenerateAsync(
        NeuralTrainingMode mode,
        IReadOnlyList<string>? hints,
        CancellationToken ct) =>
        GenerateAsync(mode, hints, targetCategory: null, ct);

    public async Task<SyntheticThreadScenario?> GenerateAsync(
        NeuralTrainingMode mode,
        IReadOnlyList<string>? hints,
        string? targetCategory,
        CancellationToken ct)
    {
        string? resolvedTarget = ResolveTargetCategory(mode, targetCategory);
        string? json = await llm.ChatJsonAsync(
            ScenarioSystemPrompt,
            BuildUserPrompt(mode, hints, resolvedTarget),
            ct);
        if (string.IsNullOrWhiteSpace(json))
            return CreateFallback(mode, resolvedTarget);

        try
        {
            SyntheticThreadScenario? scenario = ParseScenario(json);
            if (scenario is null || scenario.Messages.Count == 0)
                return CreateFallback(mode, resolvedTarget);

            return AlignScenarioToTarget(scenario, mode, resolvedTarget);
        }
        catch (JsonException)
        {
            return CreateFallback(mode, resolvedTarget);
        }
    }

    public static string BuildUserPrompt(
        NeuralTrainingMode mode,
        IReadOnlyList<string>? hints,
        string? targetCategory)
    {
        string selected = mode == NeuralTrainingMode.Both
            ? "moderation or tutoring"
            : mode.ToString().ToLowerInvariant();
        string prompt = $"Create one {selected} training scenario with 4 to 8 messages.";
        if (!string.IsNullOrWhiteSpace(targetCategory))
            prompt += BuildTargetConstraint(mode, targetCategory);

        if (hints is null || hints.Count == 0)
            return prompt;

        // Balanced prior feedback: short constraints only — do not paste raw scores or over-steer.
        string joined = string.Join("\n- ", hints.Take(6));
        return prompt
            + "\n\nPrior evaluator notes (keep scenario diversity; do not overfit to these):\n- "
            + joined;
    }

    private static string BuildTargetConstraint(NeuralTrainingMode mode, string targetCategory)
    {
        NeuralModelKindChatMonitoring kind = InferKind(mode, targetCategory);
        string normalized = ChatMonitoringCategoryTaxonomy.NormalizeCategory(kind, targetCategory);
        if (kind == NeuralModelKindChatMonitoring.Tutoring)
        {
            return $"\n\nYou MUST set \"category\" exactly to \"{normalized}\". "
                + "Write the requirement and messages so the ticket is about that tutoring subject/competency label.";
        }

        string meaning = ChatMonitoringModerationConcepts.TryGet(normalized, out ModerationConceptDefinition concept)
            ? concept.Meaning
            : "General moderation catch-all when no finer concept fits.";
        IReadOnlyList<string> related = ChatMonitoringModerationConcepts.RelatedConcepts(normalized, max: 4);
        string relatedText = related.Count == 0
            ? string.Empty
            : " Related hard-negative / sibling concepts: " + string.Join(", ", related) + ".";
        return $"\n\nYou MUST set \"category\" exactly to \"{normalized}\". "
            + $"Concept meaning: {meaning}.{relatedText} "
            + $"Requirement must include reportedConcept={normalized}.";
    }

    public static SyntheticThreadScenario AlignScenarioToTarget(
        SyntheticThreadScenario scenario,
        NeuralTrainingMode mode,
        string? targetCategory)
    {
        if (string.IsNullOrWhiteSpace(targetCategory))
            return scenario;

        NeuralModelKindChatMonitoring kind = InferKind(mode, targetCategory);
        string normalizedTarget = ChatMonitoringCategoryTaxonomy.NormalizeCategory(kind, targetCategory);
        string normalizedActual = ChatMonitoringCategoryTaxonomy.NormalizeCategory(kind, scenario.Category);
        if (string.Equals(normalizedActual, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return EnsureReportedConcept(scenario, normalizedTarget, kind);

        // LLM drifted — keep the thread content but force the coverage target onto the label.
        string requirement = scenario.Requirement;
        if (kind == NeuralModelKindChatMonitoring.Moderation
            && !requirement.Contains($"reportedConcept={normalizedTarget}", StringComparison.OrdinalIgnoreCase))
        {
            requirement = $"Monitor reportedConcept={normalizedTarget}. {requirement}";
        }

        return scenario with
        {
            Category = Truncate(normalizedTarget, 64),
            Requirement = Truncate(requirement, 4000),
        };
    }

    private static SyntheticThreadScenario EnsureReportedConcept(
        SyntheticThreadScenario scenario,
        string normalizedTarget,
        NeuralModelKindChatMonitoring kind)
    {
        if (kind != NeuralModelKindChatMonitoring.Moderation)
            return scenario with { Category = Truncate(normalizedTarget, 64) };

        if (scenario.Requirement.Contains($"reportedConcept={normalizedTarget}", StringComparison.OrdinalIgnoreCase))
            return scenario with { Category = Truncate(normalizedTarget, 64) };

        return scenario with
        {
            Category = Truncate(normalizedTarget, 64),
            Requirement = Truncate($"Monitor reportedConcept={normalizedTarget}. {scenario.Requirement}", 4000),
        };
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

    public static SyntheticThreadScenario CreateFallback(NeuralTrainingMode mode, string? targetCategory)
    {
        string? resolved = ResolveTargetCategory(mode, targetCategory);
        NeuralModelKindChatMonitoring kind = InferKind(mode, resolved);
        if (kind == NeuralModelKindChatMonitoring.Tutoring)
            return CreateTutoringFallback(resolved);

        return CreateModerationFallback(resolved);
    }

    private static SyntheticThreadScenario CreateTutoringFallback(string? targetCategory)
    {
        string category = string.IsNullOrWhiteSpace(targetCategory)
            ? "tutoring-competency"
            : ChatMonitoringCategoryTaxonomy.NormalizeCategory(
                NeuralModelKindChatMonitoring.Tutoring, targetCategory);
        string subject = category.StartsWith("tutoring-", StringComparison.OrdinalIgnoreCase)
            ? category["tutoring-".Length..].Replace('-', ' ')
            : "general tutoring";
        string requirement =
            $"Assess whether the applicant demonstrates helpful, accurate {subject} tutoring in the appropriate subject channel.";
        string context = $"A tutor applicant is being observed across {subject} and casual channels.";
        IReadOnlyList<SyntheticThreadMessage> messages =
        [
            new(0, "synthetic-applicant", "tutor applicant", "subject-help",
                $"Here is a clear step-by-step explanation for a typical {subject} question.", false, 1f,
                new(.9f, 30, .1f, ["accurate subject explanation"]), .9f, 1f, .9f, .85f),
            new(1, "synthetic-applicant", "tutor applicant", "english-help",
                "The main idea is the message the author wants the reader to understand.", false, .55f,
                new(.75f, 12, .2f, ["helpful outside primary subject"]), .7f, .55f, .75f, .7f),
            new(2, "synthetic-peer", "student", "lounge",
                "Anyone watching a movie after homework?", true, .05f,
                new(.5f, 6, .3f, ["unrelated casual message"]), .5f, .08f, .5f, .6f),
            new(3, "synthetic-applicant", "tutor applicant", "subject-help",
                $"I am unsure about this {subject} detail, so check the textbook steps carefully.", false, 1f,
                new(.35f, 18, .2f, ["weaker subject response"]), .4f, 1f, .35f, .7f),
        ];
        return new SyntheticThreadScenario(category, requirement, context, messages);
    }

    private static SyntheticThreadScenario CreateModerationFallback(string? targetCategory)
    {
        string category = string.IsNullOrWhiteSpace(targetCategory)
            ? ChatMonitoringModerationConcepts.CatchAll
            : ChatMonitoringCategoryTaxonomy.NormalizeCategory(
                NeuralModelKindChatMonitoring.Moderation, targetCategory);
        string meaning = ChatMonitoringModerationConcepts.TryGet(category, out ModerationConceptDefinition concept)
            ? concept.Meaning
            : "Conduct that needs moderator review when no finer concept fits.";
        IReadOnlyList<string> related = ChatMonitoringModerationConcepts.RelatedConcepts(category, max: 4);
        string relatedText = related.Count == 0 ? "none" : string.Join(", ", related);
        string requirement =
            $"Monitor reportedConcept={category} with relatedConcepts {relatedText}. Concept: {meaning}";
        string context = $"A user was reported for possible {category.Replace('-', ' ')} behavior.";
        string concerning =
            $"Please look at this — it matches {category.Replace('-', ' ')}: {meaning}";
        IReadOnlyList<SyntheticThreadMessage> messages =
        [
            new(0, "synthetic-reported", "student", "math-help", concerning, false, 1f,
                new(.05f, 34, .15f, [category]), .95f, 1f, .05f, .9f),
            new(1, "synthetic-peer", "student", "math-help",
                "Thanks for the homework tip earlier — that was just friendly help.", true, .9f,
                new(.8f, 18, .1f, ["benign hard-negative"]), .15f, .9f, .8f, .7f),
            new(2, "synthetic-reported", "student", "lounge",
                "I finally finished my homework.", true, .05f,
                new(.55f, 8, .25f, ["unrelated casual message"]), .5f, .08f, .55f, .6f),
            new(3, "synthetic-reported", "student", "math-help",
                $"Continuing the same {category.Replace('-', ' ')} pattern after being asked to stop.", false, 1f,
                new(.08f, 28, .1f, [$"{category} escalation"]), .92f, 1f, .08f, .88f),
        ];
        return new SyntheticThreadScenario(category, requirement, context, messages);
    }

    private static string? ResolveTargetCategory(NeuralTrainingMode mode, string? targetCategory)
    {
        if (string.IsNullOrWhiteSpace(targetCategory))
            return null;

        NeuralModelKindChatMonitoring kind = InferKind(mode, targetCategory);
        return ChatMonitoringCategoryTaxonomy.NormalizeCategory(kind, targetCategory);
    }

    private static NeuralModelKindChatMonitoring InferKind(NeuralTrainingMode mode, string? targetCategory)
    {
        if (mode == NeuralTrainingMode.Tutoring)
            return NeuralModelKindChatMonitoring.Tutoring;
        if (mode == NeuralTrainingMode.Moderation)
            return NeuralModelKindChatMonitoring.Moderation;

        if (!string.IsNullOrWhiteSpace(targetCategory)
            && (targetCategory.Contains("tutor", StringComparison.OrdinalIgnoreCase)
                || targetCategory.StartsWith("tutoring-", StringComparison.OrdinalIgnoreCase)))
        {
            return NeuralModelKindChatMonitoring.Tutoring;
        }

        return NeuralModelKindChatMonitoring.Moderation;
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
        root.TryGetProperty(property, out JsonElement value) && value.TryGetSingle(out float result)
            ? Math.Clamp(result, 0, 1)
            : null;

    private static bool Bool(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True;
}
