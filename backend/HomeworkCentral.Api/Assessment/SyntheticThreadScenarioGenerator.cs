using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

public sealed class SyntheticThreadScenarioGenerator(ILlmClient llm)
{
    public async Task<SyntheticThreadScenario?> GenerateAsync(NeuralTrainingMode mode, CancellationToken ct)
    {
        string selected = mode == NeuralTrainingMode.Both ? "moderation or tutoring" : mode.ToString().ToLowerInvariant();
        const string system = "Generate a fictional school-chat ticket scenario only. Return JSON with category, requirement, initialContext, and messages. Each message needs authorId, authorRole, channel, content, isDistractor, channelRelevance (0..1), proposedApproval (0..1), proposedVoterCount (1..200), controversy (0..1), reasons array. Include relevant messages, unrelated casual distractors, multiple users/roles/channels, and a short thread. Never use real people or data.";
        string? json = await llm.ChatJsonAsync(system, $"Create one {selected} training scenario with 4 to 8 messages.", ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
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
            return messages.Count == 0 ? null : new SyntheticThreadScenario(category[..Math.Min(category.Length, 64)], requirement[..Math.Min(requirement.Length, 4000)], context[..Math.Min(context.Length, 2500)], messages);
        }
        catch (JsonException) { return null; }
    }
    private static string String(JsonElement root, string property, string fallback = "") => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static int Int(JsonElement root, string property, int fallback) => root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;
    private static float Unit(JsonElement root, string property, float fallback) => root.TryGetProperty(property, out JsonElement value) && value.TryGetSingle(out float result) ? Math.Clamp(result, 0, 1) : fallback;
    private static bool Bool(JsonElement root, string property) => root.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True;
}