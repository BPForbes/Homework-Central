using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Tickets;

/// <summary>No-op analyzer used when Ollama is disabled via configuration.</summary>
public sealed class NullTicketTrackingAnalyzer : ITicketTrackingAnalyzer
{
    public Task<TicketAnalysisResult> AnalyzeAsync(
        TicketPortalConfig portal,
        Ticket ticket,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default) =>
        Task.FromResult(new TicketAnalysisResult(
            Available: false,
            Decision: null,
            Summary: null,
            TrackedUserId: null));
}
