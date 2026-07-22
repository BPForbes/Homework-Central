using System.Text.Json;
using System.Text.Json.Serialization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tickets.Preface;

namespace HomeworkCentral.Api.Tickets;

public static class TicketTrackingTemplateBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(
        string filterName,
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers,
        IReadOnlyDictionary<string, TicketPrefaceResult>? prefaceResults = null)
    {
        PrefaceTemplateSummary prefaceSummary = BuildPrefaceSummary(prefaceResults);
        TrackingTemplatePayload payload = new()
        {
            FilterName = filterName,
            BuiltAtUtc = DateTime.UtcNow,
            Intake = BuildIntakeItems(schema, answers),
            PrefaceCategory = prefaceSummary.PrimaryCategory,
            PrefaceSpecifics = prefaceSummary.Specifics,
            Instructions = ResolveInstructions(filterName),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static PrefaceTemplateSummary BuildPrefaceSummary(
        IReadOnlyDictionary<string, TicketPrefaceResult>? prefaceResults)
    {
        string? prefaceCategory = null;
        List<string> prefaceSpecifics = [];
        if (prefaceResults is null)
            return new PrefaceTemplateSummary(prefaceCategory, prefaceSpecifics);

        foreach (TicketPrefaceResult result in prefaceResults
            .OrderBy(prefaceResult => prefaceResult.Key, StringComparer.Ordinal)
            .Select(prefaceResult => prefaceResult.Value))
        {
            if (string.IsNullOrWhiteSpace(prefaceCategory) && !string.IsNullOrWhiteSpace(result.PrimaryCategory))
                prefaceCategory = result.PrimaryCategory;

            AddSpecificLabels(result, prefaceSpecifics);
        }

        return new PrefaceTemplateSummary(prefaceCategory, prefaceSpecifics);
    }

    private static void AddSpecificLabels(TicketPrefaceResult result, List<string> prefaceSpecifics)
    {
        foreach (string label in result.SpecificLabels)
        {
            if (!prefaceSpecifics.Contains(label, StringComparer.OrdinalIgnoreCase))
                prefaceSpecifics.Add(label);
        }
    }

    private static List<TrackingTemplateIntakeItem> BuildIntakeItems(
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers) =>
        schema.Select(question => BuildIntakeItem(question, answers)).ToList();

    private static TrackingTemplateIntakeItem BuildIntakeItem(
        TicketIntakeQuestionDto question,
        IReadOnlyDictionary<string, JsonElement> answers) =>
        new()
        {
            Id = question.Id,
            Prompt = question.Prompt,
            Type = question.Type,
            TracksUser = question.TracksUser,
            Answer = CloneTemplateAnswer(question, answers),
        };

    private static JsonElement? CloneTemplateAnswer(
        TicketIntakeQuestionDto question,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        if (!answers.TryGetValue(question.Id, out JsonElement value)
            || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        // Tracking templates outlive request-bound JsonElements stored by model binding.
        return value.Clone();
    }

    private static string ResolveInstructions(string filterName) => filterName switch
    {
        string tutorFilter when string.Equals(
            tutorFilter,
            DefaultTicketPortalPresets.TutorFilterName,
            StringComparison.OrdinalIgnoreCase)
            => "Tutor trial: monitor applied subjects strictly, related subjects mildly, unrelated reward-only.",
        string modFilter when string.Equals(
            modFilter,
            DefaultTicketPortalPresets.ModFilterName,
            StringComparison.OrdinalIgnoreCase)
            => "Mod report: score when reported users' messages match the reported reason or similar.",
        _ => "Generic ticket tracking from intake answers and chat evidence.",
    };

    private sealed record PrefaceTemplateSummary(string? PrimaryCategory, List<string> Specifics);

    private sealed class TrackingTemplatePayload
    {
        public string FilterName { get; set; } = string.Empty;
        public DateTime BuiltAtUtc { get; set; }
        public List<TrackingTemplateIntakeItem> Intake { get; set; } = [];
        public string? PrefaceCategory { get; set; }
        public List<string> PrefaceSpecifics { get; set; } = [];
        public string Instructions { get; set; } = string.Empty;
    }

    private sealed class TrackingTemplateIntakeItem
    {
        public string Id { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool TracksUser { get; set; }
        public JsonElement? Answer { get; set; }
    }
}
