using System.Text.Json;
using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Tickets;

public static class TicketTrackingTemplateBuilder
{
    public static string Build(
        string filterName,
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        var payload = new
        {
            filterName,
            builtAtUtc = DateTime.UtcNow,
            intake = schema.Select(q => new
            {
                q.Id,
                q.Prompt,
                q.Type,
                q.TracksUser,
                answer = answers.TryGetValue(q.Id, out JsonElement value) ? value : default,
            }),
            instructions = string.Equals(filterName, DefaultTicketPortalPresets.TutorFilterName, StringComparison.OrdinalIgnoreCase)
                ? "Tutor trial: monitor applied subjects strictly, related subjects mildly, unrelated reward-only."
                : string.Equals(filterName, DefaultTicketPortalPresets.ModFilterName, StringComparison.OrdinalIgnoreCase)
                    ? "Mod report: score when reported users' messages match the reported reason or similar."
                    : "Generic ticket tracking from intake answers and chat evidence.",
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }
}
