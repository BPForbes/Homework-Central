using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Tickets;

public sealed record TicketAnalysisResult(
    bool Available,
    string? Decision,
    string? Summary,
    Guid? TrackedUserId);

public interface ITicketTrackingAnalyzer
{
    Task<TicketAnalysisResult> AnalyzeAsync(
        TicketPortalConfig portal,
        Ticket ticket,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default);
}
