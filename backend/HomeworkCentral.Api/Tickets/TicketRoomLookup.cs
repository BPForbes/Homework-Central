using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Tickets;

/// <summary>Helpers for distinguishing opened ticket chat rooms from ordinary chat rooms.</summary>
public static class TicketRoomLookup
{
    public static Task<bool> IsTicketChatRoomAsync(
        AppDbContext db,
        string roomId,
        CancellationToken ct = default) =>
        db.Tickets.AsNoTracking().AnyAsync(t => t.RoomId == roomId, ct);
}
