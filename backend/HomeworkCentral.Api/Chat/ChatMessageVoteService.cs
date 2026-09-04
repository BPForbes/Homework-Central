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
using Npgsql;

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
    IHubContext<ChatHub> hubContext) : IChatMessageVoteService
{
    public async Task<MessageVoteDto?> CastVoteAsync(
        Guid messageId,
        Guid userId,
        short value,
        CancellationToken ct = default)
    {
        AssertSupportedVoteValue(value);

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        AssertVotingRoleAllowed(masks);

        ChatMessage? message = await db.ChatMessages.FirstOrDefaultAsync(m => m.MessageId == messageId, ct);
        if (message is null)
            return null;

        await AssertUserCanVoteOnMessageAsync(message, userId, masks, ct);
        await ApplyVoteUpsertOrToggleAsync(messageId, userId, value, ct);

        return await BroadcastVoteUpdateAsync(message, userId, ct);
    }

    private static void AssertSupportedVoteValue(short value)
    {
        if (value is not (1 or -1))
            throw new InvalidOperationException("Vote value must be 1 or -1.");
    }

    private static void AssertVotingRoleAllowed(EffectiveMaskDto masks)
    {
        if (MentionPermissions.IsGuest(BitMask.FromBase64(masks.RoleMask, 64)))
            throw new InvalidOperationException("Guests cannot vote.");
    }

    private async Task AssertUserCanVoteOnMessageAsync(
        ChatMessage message,
        Guid userId,
        EffectiveMaskDto masks,
        CancellationToken ct)
    {
        if (!chatRoomAccess.CanAccessRoom(masks, userId, message.RoomId))
            throw new InvalidOperationException("You cannot access this room.");

        if (await TicketRoomLookup.IsTicketChatRoomAsync(db, message.RoomId, ct))
            throw new InvalidOperationException("Voting is not available in ticket rooms.");

        if (message.SenderId == userId)
            throw new InvalidOperationException("You cannot vote on your own message.");
    }

    private async Task ApplyVoteUpsertOrToggleAsync(
        Guid messageId,
        Guid userId,
        short value,
        CancellationToken ct)
    {
        ChatMessageVote? existingVote = await db.ChatMessageVotes
            .FirstOrDefaultAsync(v => v.MessageId == messageId && v.UserId == userId, ct);

        if (existingVote is not null)
        {
            await ToggleOrUpdateExistingVoteAsync(existingVote, value, ct);
            return;
        }

        await InsertFirstVoteAsync(messageId, userId, value, ct);
    }

    private async Task InsertFirstVoteAsync(
        Guid messageId,
        Guid userId,
        short value,
        CancellationToken ct)
    {
        ChatMessageVote insertedVote = new ChatMessageVote
        {
            MessageId = messageId,
            UserId = userId,
            Value = value,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.ChatMessageVotes.Add(insertedVote);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateVote(ex))
        {
            db.Entry(insertedVote).State = EntityState.Detached;

            // Unique violations mean a concurrent request inserted the first vote after
            // the initial read, so the normal toggle/update rules still decide the state.
            ChatMessageVote? existingVote = await db.ChatMessageVotes
                .FirstOrDefaultAsync(v => v.MessageId == messageId && v.UserId == userId, ct);
            if (existingVote is null)
                throw;

            await ToggleOrUpdateExistingVoteAsync(existingVote, value, ct);
        }
    }

    private async Task ToggleOrUpdateExistingVoteAsync(ChatMessageVote existingVote, short value, CancellationToken ct)
    {
        if (existingVote.Value == value)
        {
            db.ChatMessageVotes.Remove(existingVote);
            await db.SaveChangesAsync(ct);
            return;
        }

        existingVote.Value = value;
        existingVote.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<MessageVoteDto> BroadcastVoteUpdateAsync(
        ChatMessage message,
        Guid userId,
        CancellationToken ct)
    {
        MessageVoteDto dto = await BuildDtoAsync(message, userId, ct);
        string groupKey = ChatRoomGroupKey.Build(message.RoomId, message.OwnerAccountClass);
        await hubContext.Clients.Group(groupKey).SendAsync("MessageVoteUpdated", dto, ct);

        return dto;
    }

    private async Task<MessageVoteDto> BuildDtoAsync(ChatMessage message, Guid viewerId, CancellationToken ct)
    {
        List<ChatMessageVote> votes = await db.ChatMessageVotes.AsNoTracking()
            .Where(v => v.MessageId == message.MessageId)
            .ToListAsync(ct);
        int upvoteCount = votes.Count(v => v.Value > 0);
        int downvoteCount = votes.Count(v => v.Value < 0);
        short? viewerVoteValue = votes.FirstOrDefault(v => v.UserId == viewerId)?.Value;
        string? viewerVote = viewerVoteValue switch
        {
            > 0 => "up",
            < 0 => "down",
            _ => null,
        };

        return new MessageVoteDto(
            message.MessageId,
            message.RoomId,
            upvoteCount - downvoteCount,
            upvoteCount,
            downvoteCount,
            viewerVote);
    }

    private static bool IsDuplicateVote(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
