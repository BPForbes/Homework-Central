using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Tickets;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

public interface ICommunityScoreAggregator
{
    Task<(double? Score, double ReliableWeight)> AggregateAsync(Guid messageId, CancellationToken ct = default);
}

/// <summary>
/// Reliability-weighted community score from message votes.
/// Upvote → v=1, downvote → v=0; reliability starts uniform (1.0) in MVP.
/// Votes inside ticket chat rooms are ignored.
/// </summary>
public sealed class CommunityScoreAggregator(AppDbContext db) : ICommunityScoreAggregator
{
    public async Task<(double? Score, double ReliableWeight)> AggregateAsync(
        Guid messageId,
        CancellationToken ct = default)
    {
        string? roomId = await db.ChatMessages.AsNoTracking()
            .Where(m => m.MessageId == messageId)
            .Select(m => m.RoomId)
            .FirstOrDefaultAsync(ct);
        if (roomId is null)
            return (null, 0);

        if (await TicketRoomLookup.IsTicketChatRoomAsync(db, roomId, ct))
            return (null, 0);

        List<short> votes = await db.ChatMessageVotes.AsNoTracking()
            .Where(v => v.MessageId == messageId)
            .Select(v => v.Value)
            .ToListAsync(ct);

        if (votes.Count == 0)
            return (null, 0);

        double numerator = 0;
        double denominator = 0;
        foreach (short vote in votes)
        {
            const double reliability = 1.0;
            double v = vote > 0 ? 1.0 : 0.0;
            numerator += reliability * v;
            denominator += reliability;
        }

        if (denominator <= 0)
            return (null, 0);

        return (numerator / denominator, denominator);
    }
}
