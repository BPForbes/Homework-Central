using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Chat.Mentions;

public interface IChatInboxService
{
    Task<IReadOnlyList<ChatInboxItemDto>> GetInboxAsync(Guid userId, CancellationToken ct = default);
    Task<ChatInboxSummaryDto> GetSummaryAsync(Guid userId, CancellationToken ct = default);
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}

public sealed class ChatInboxService(AppDbContext masterDb) : IChatInboxService
{
    public async Task<IReadOnlyList<ChatInboxItemDto>> GetInboxAsync(Guid userId, CancellationToken ct = default)
    {
        List<ChatMentionNotification> items = await masterDb.ChatMentionNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return items.Select(ToDto).ToArray();
    }

    public async Task<ChatInboxSummaryDto> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        List<ChatInboxSummaryItemDto> categories = await masterDb.ChatMentionNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId && notification.ReadAtUtc == null)
            .GroupBy(notification => new { notification.CategoryKey, notification.CategoryDisplayName })
            .Select(group => new ChatInboxSummaryItemDto
            {
                CategoryKey = group.Key.CategoryKey,
                CategoryDisplayName = group.Key.CategoryDisplayName,
                UnreadCount = group.Count(),
            })
            .OrderBy(item => item.CategoryDisplayName)
            .ToListAsync(ct);

        return new ChatInboxSummaryDto { Categories = categories };
    }

    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        ChatMentionNotification? notification = await masterDb.ChatMentionNotifications
            .FirstOrDefaultAsync(
                item => item.NotificationId == notificationId && item.RecipientUserId == userId,
                ct);

        if (notification is null)
            return false;

        if (notification.ReadAtUtc is null)
        {
            notification.ReadAtUtc = DateTime.UtcNow;
            await masterDb.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        List<ChatMentionNotification> unread = await masterDb.ChatMentionNotifications
            .Where(notification => notification.RecipientUserId == userId && notification.ReadAtUtc == null)
            .ToListAsync(ct);

        if (unread.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        foreach (ChatMentionNotification notification in unread)
            notification.ReadAtUtc = now;

        await masterDb.SaveChangesAsync(ct);
    }

    private static ChatInboxItemDto ToDto(ChatMentionNotification notification) =>
        new()
        {
            NotificationId = notification.NotificationId,
            MessageId = notification.MessageId,
            SenderId = notification.SenderId,
            SenderUsername = notification.SenderUsername,
            RoomId = notification.RoomId,
            RoomDisplayName = notification.RoomDisplayName,
            CategoryKey = notification.CategoryKey,
            CategoryDisplayName = notification.CategoryDisplayName,
            MessageContent = notification.MessageContent,
            MentionKind = notification.MentionKind,
            CreatedAtUtc = notification.CreatedAtUtc,
            ReadAtUtc = notification.ReadAtUtc,
        };
}
