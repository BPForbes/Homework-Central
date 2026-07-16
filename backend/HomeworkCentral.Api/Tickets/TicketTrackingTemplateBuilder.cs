using System.Text.Json;
using System.Text.Json.Serialization;
using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Tickets;

public static class TicketTrackingTemplateBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Build(
        string filterName,
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        TrackingTemplatePayload payload = new()
        {
            FilterName = filterName,
            BuiltAtUtc = DateTime.UtcNow,
            Intake = schema.Select(q => new TrackingTemplateIntakeItem
            {
                Id = q.Id,
                Prompt = q.Prompt,
                Type = q.Type,
                TracksUser = q.TracksUser,
                Answer = answers.TryGetValue(q.Id, out JsonElement value) ? value : default,
            }).ToList(),
            Instructions = string.Equals(filterName, DefaultTicketPortalPresets.TutorFilterName, StringComparison.OrdinalIgnoreCase)
                ? "Tutor trial: monitor applied subjects strictly, related subjects mildly, unrelated reward-only."
                : string.Equals(filterName, DefaultTicketPortalPresets.ModFilterName, StringComparison.OrdinalIgnoreCase)
                    ? "Mod report: score when reported users' messages match the reported reason or similar."
                    : "Generic ticket tracking from intake answers and chat evidence.",
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed class TrackingTemplatePayload
    {
        public string FilterName { get; set; } = string.Empty;
        public DateTime BuiltAtUtc { get; set; }
        public List<TrackingTemplateIntakeItem> Intake { get; set; } = [];
        public string Instructions { get; set; } = string.Empty;
    }

    private sealed class TrackingTemplateIntakeItem
    {
        public string Id { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool TracksUser { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public JsonElement Answer { get; set; }
    }
}
