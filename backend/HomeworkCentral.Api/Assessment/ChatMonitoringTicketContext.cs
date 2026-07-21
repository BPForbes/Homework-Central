using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Builds bounded ticket context for the preinstalled chat-monitoring neural models.</summary>
public static class ChatMonitoringTicketContext
{
    public static string BuildRequirement(TicketUserWatch watch, int maxCharacters)
    {
        string value = $"Filter: {watch.Ticket.FilterName}. Watch context: {watch.ContextLabel}. "
            + $"Instructions: {watch.Ticket.Portal.TrackingInstructions ?? "none"}. "
            + $"Frozen template: {watch.Ticket.TrackingTemplateJson ?? "none"}.";
        return value.Length <= maxCharacters ? value : value[..maxCharacters];
    }
}
