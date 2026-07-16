using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tickets;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Chat;

public sealed record MessageVoteDto(
    Guid MessageId,
    string RoomId,
    int Score,
    int UpvoteCount,
    int DownvoteCount,
    string? ViewerVote);

public interface IChatMessageVoteService
{
    Task<MessageVoteDto?> CastVoteAsync(Guid messageId, Guid userId, short value, CancellationToken ct = default);
}

public sealed class ChatMessageVoteService(
    AppDbContext db,
    IEffectiveMaskService effectiveMaskService,
    IChatRoomAccessService chatRoomAccess,
    IHubContext<ChatHub> hubContext,
    IAssessmentQueue assessmentQueue) : IChatMessageVoteService
{
    public async Task<MessageVoteDto?> CastVoteAsync(
        Guid messageId,
        Guid userId,
        short value,
        CancellationToken ct = default)
    {
        if (value is not (1 or -1))
            throw new InvalidOperationException("Vote value must be 1 or -1.");

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        if (MentionPermissions.IsGuest(BitMask.FromBase64(masks.RoleMask, 64)))
            throw new InvalidOperationException("Guests cannot vote.");

        ChatMessage? message = await db.ChatMessages.FirstOrDefaultAsync(m => m.MessageId == messageId, ct);
        if (message is null)
            return null;

        if (!chatRoomAccess.CanAccessRoom(masks, userId, message.RoomId))
            throw new InvalidOperationException("You cannot access this room.");

        if (await TicketRoomLookup.IsTicketChatRoomAsync(db, message.RoomId, ct))
            throw new InvalidOperationException("Voting is not available in ticket rooms.");

        if (message.SenderId == userId)
            throw new InvalidOperationException("You cannot vote on your own message.");

        ChatMessageVote? existing = await db.ChatMessageVotes
            .FirstOrDefaultAsync(v => v.MessageId == messageId && v.UserId == userId, ct);

        if (existing is not null && existing.Value == value)
        {
            return await BuildDtoAsync(message, userId, ct);
        }

        if (existing is null)
        {
            db.ChatMessageVotes.Add(new ChatMessageVote
            {
                MessageId = messageId,
                UserId = userId,
                Value = value,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        MessageVoteDto dto = await BuildDtoAsync(message, userId, ct);
        string groupKey = ChatRoomGroupKey.Build(message.RoomId, message.OwnerAccountClass);
        await hubContext.Clients.Group(groupKey).SendAsync("MessageVoteUpdated", dto, ct);

        // Extend the assessment queue path used by SendMessage — community-only recalc, no LLM.
        await assessmentQueue.EnqueueAsync(
            new AssessmentMessageJob(
                message.MessageId,
                message.RoomId,
                message.SenderId,
                message.RawContent,
                AssessmentJobKind.CommunityRecalc),
            ct);

        return dto;
    }

    private async Task<MessageVoteDto> BuildDtoAsync(ChatMessage message, Guid viewerId, CancellationToken ct)
    {
        List<ChatMessageVote> votes = await db.ChatMessageVotes.AsNoTracking()
            .Where(v => v.MessageId == message.MessageId)
            .ToListAsync(ct);
        int ups = votes.Count(v => v.Value > 0);
        int downs = votes.Count(v => v.Value < 0);
        short? viewer = votes.FirstOrDefault(v => v.UserId == viewerId)?.Value;
        return new MessageVoteDto(
            message.MessageId,
            message.RoomId,
            ups - downs,
            ups,
            downs,
            viewer is null ? null : viewer > 0 ? "up" : "down");
    }
}
