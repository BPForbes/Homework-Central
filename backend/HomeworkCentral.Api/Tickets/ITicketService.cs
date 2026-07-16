using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Tickets;

public interface ITicketService
{
    Task<TicketPortalConfigDto?> GetPortalConfigAsync(
        Guid channelId,
        Guid actorUserId,
        CancellationToken ct = default);
    Task<TicketPortalConfigDto?> GetPortalConfigByRoomAsync(string roomId, CancellationToken ct = default);
    Task<TicketPortalConfigDto?> UpdatePortalConfigAsync(
        Guid channelId,
        Guid actorUserId,
        UpdateTicketPortalConfigRequest request,
        CancellationToken ct = default);

    Task<TicketDto> OpenTicketAsync(
        string portalRoomId,
        Guid openerUserId,
        OpenTicketRequest request,
        CancellationToken ct = default);

    Task<TicketDto?> GetTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default);
    Task<TicketDto?> GetTicketByRoomAsync(string roomId, Guid actorUserId, CancellationToken ct = default);
    Task<TicketDto> CloseTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default);
    Task<TicketDto> ReopenTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default);
    Task<TicketUserWatchDto?> UpsertWatchAsync(
        Guid ticketId,
        Guid actorUserId,
        UpsertTicketWatchRequest request,
        CancellationToken ct = default);
    Task<TicketAnalyzeResultDto> AnalyzeAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default);
    Task<TicketDto> ApproveDecisionAsync(
        Guid ticketId,
        Guid actorUserId,
        ApproveTicketDecisionRequest request,
        CancellationToken ct = default);
}
