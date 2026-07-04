using System.Collections;
using System.Security.Claims;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Security;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Chat;

public interface IChatMessageService
{
    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        string roomId,
        Guid userId,
        DateTime? beforeUtc,
        int limit,
        CancellationToken ct = default);

    Task<ChatMessageDto?> SendMessageAsync(
        string roomId,
        Guid userId,
        string content,
        CancellationToken ct = default);

    Task<bool> CanAccessRoomAsync(string roomId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ChatInboxItemDto>> GetInboxAsync(Guid userId, CancellationToken ct = default);

    Task<ChatInboxSummaryDto> GetInboxSummaryAsync(Guid userId, CancellationToken ct = default);

    Task<bool> MarkInboxReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkInboxAllReadAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Chat rooms are shared community spaces, not per-tenant private data (see
/// <see cref="ChatMessage"/>), so every read/write against <see cref="AppDbContext.ChatMessages"/>
/// in this service intentionally always targets <paramref name="masterDb"/> — never a
/// per-persona tenant database. Each dev persona has its own isolated tenant database (used for
/// homework/grades, which genuinely are tenant-private); if chat messages were persisted there
/// instead, two personas (or a persona and DevAdmin) could never see each other's chat history,
/// since they'd be reading and writing to entirely different physical databases.
/// </summary>
public sealed class ChatMessageService(
    AppDbContext masterDb,
    IHttpContextAccessor httpContextAccessor,
    IEffectiveMaskService effectiveMaskService,
    IChatRoomAccessService chatRoomAccess,
    IContentSanitizer contentSanitizer,
    IMentionCooldownTracker mentionCooldownTracker,
    IMentionRecipientResolver mentionRecipientResolver,
    IHubContext<ChatHub> hubContext) : IChatMessageService
{
    private const int MaxMessageLength = 4000;
    private const int DefaultPageSize = 50;
    private static readonly TimeSpan MentionCooldown = TimeSpan.FromSeconds(3);

    public async Task<bool> CanAccessRoomAsync(string roomId, Guid userId, CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await GetMasksAsync(userId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, roomId))
            return false;

        // GroupMessages feature bit additionally gates staff rooms. Subject-expertise rooms are
        // already scoped by role/expertise/general-subject claims in ChatRoomAccessService, so
        // a VerifiedUser who claimed Computer Science can read and send in C# without needing a
        // separate GroupMessages grant beyond room access.
        if (string.Equals(roomId, ChatRoomCatalog.GeneralRoom.Id, StringComparison.Ordinal))
            return true;

        ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);
        if (room?.Kind == ChatRoomKind.SubjectExpertise)
            return true;

        return HasGroupMessagesFeature(masks);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        string roomId,
        Guid userId,
        DateTime? beforeUtc,
        int limit,
        CancellationToken ct = default)
    {
        if (!await CanAccessRoomAsync(roomId, userId, ct))
            return [];

        int pageSize = limit is > 0 and <= 100 ? limit : DefaultPageSize;

        // Real-vs-developer traffic is filtered by the IShareableScopedResource EF global query filter.
        IQueryable<ChatMessage> query = masterDb.ChatMessages
            .AsNoTracking()
            .Where(message => message.RoomId == roomId);

        if (beforeUtc is not null)
            query = query.Where(message => message.CreatedAtUtc < beforeUtc.Value);

        List<ChatMessage> messages = await query
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(pageSize)
            .ToListAsync(ct);

        messages.Reverse();
        return messages.Select(ToDto).ToArray();
    }

    public async Task<ChatMessageDto?> SendMessageAsync(
        string roomId,
        Guid userId,
        string content,
        CancellationToken ct = default)
    {
        if (!await CanAccessRoomAsync(roomId, userId, ct))
            return null;

        string trimmed = content.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxMessageLength)
            return null;

        EffectiveMaskDto masks = await GetMasksAsync(userId, ct);
        BitArray roleMask = BitMask.FromBase64(masks.RoleMask, 64);
        bool canUseBroadcastMentions = MentionPermissions.CanUseBroadcastMentions(roleMask);
        MentionParseResult parsed = MentionParser.Parse(trimmed, canUseBroadcastMentions);

        if (parsed.ActiveMentions.Any(mention => mention.IsActive))
        {
            if (MentionPermissions.IsGuest(roleMask))
                throw new SendMessageMentionException(SendMessageMentionError.GuestCannotMention);

            if (!MentionPermissions.IsSeniorStaff(roleMask)
                && !mentionCooldownTracker.TryRecordMention(userId, MentionCooldown, out TimeSpan retryAfter))
            {
                throw new SendMessageMentionException(SendMessageMentionError.MentionCooldown, retryAfter);
            }
        }

        // The sender's User row may only exist in their own tenant database (dev personas are
        // fully isolated per tenant), so the username is read from the JWT claim already
        // present on every authenticated request rather than looked up in a tenant-scoped
        // Users table — keeping this service entirely master-db-only.
        string senderUsername = ResolveUsername(userId);
        (AccountClass accountClass, string? tenantDatabaseName) = ResolveScope();
        string sanitized = contentSanitizer.Sanitize(parsed.DisplayContent);

        ChatMessage message = new()
        {
            MessageId = Guid.NewGuid(),
            RoomId = roomId,
            SenderId = userId,
            SenderUsername = senderUsername,
            RawContent = parsed.DisplayContent,
            SanitizedContent = sanitized,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = accountClass,
            TenantDatabaseName = tenantDatabaseName,
        };

        masterDb.ChatMessages.Add(message);

        IReadOnlyList<ParsedMention> activeMentions = parsed.ActiveMentions.Where(mention => mention.IsActive).ToArray();
        if (activeMentions.Count > 0)
        {
            string groupKey = ChatRoomGroupKey.Build(roomId, accountClass);
            HashSet<Guid> recipients = await mentionRecipientResolver.ResolveRecipientsAsync(
                roomId,
                groupKey,
                activeMentions,
                userId,
                ct);

            ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);
            string roomDisplayName = room?.RoomDisplayName ?? roomId;
            string categoryKey = room?.CategoryKey ?? ChatRoomBlueprint.GeneralCategoryKey;
            string categoryDisplayName = room?.CategoryDisplayName ?? ChatRoomBlueprint.GeneralCategoryDisplayName;

            foreach (Guid recipientId in recipients)
            {
                string mentionKind = activeMentions.Count == 1
                    ? activeMentions[0].Kind.ToString()
                    : "Multiple";

                masterDb.ChatMentionNotifications.Add(new ChatMentionNotification
                {
                    NotificationId = Guid.NewGuid(),
                    MessageId = message.MessageId,
                    RecipientUserId = recipientId,
                    SenderId = userId,
                    SenderUsername = senderUsername,
                    RoomId = roomId,
                    RoomDisplayName = roomDisplayName,
                    CategoryKey = categoryKey,
                    CategoryDisplayName = categoryDisplayName,
                    MessageContent = sanitized,
                    MentionKind = mentionKind,
                    CreatedAtUtc = message.CreatedAtUtc,
                    OwnerAccountClass = accountClass,
                    TenantDatabaseName = tenantDatabaseName,
                });
            }
        }

        await masterDb.SaveChangesAsync(ct);

        ChatMessageDto dto = ToDto(message);
        string broadcastGroupKey = ChatRoomGroupKey.Build(roomId, accountClass);
        await hubContext.Clients.Group(broadcastGroupKey).SendAsync("ReceiveMessage", dto, ct);
        return dto;
    }

    public async Task<IReadOnlyList<ChatInboxItemDto>> GetInboxAsync(Guid userId, CancellationToken ct = default)
    {
        List<ChatMentionNotification> items = await masterDb.ChatMentionNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return items.Select(ToInboxDto).ToArray();
    }

    public async Task<ChatInboxSummaryDto> GetInboxSummaryAsync(Guid userId, CancellationToken ct = default)
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

    public async Task<bool> MarkInboxReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
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

    public async Task MarkInboxAllReadAsync(Guid userId, CancellationToken ct = default)
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

    private async Task<EffectiveMaskDto> GetMasksAsync(Guid userId, CancellationToken ct)
    {
        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);
        return mask.ToEffectiveMaskDto();
    }

    private string ResolveUsername(Guid userId)
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        string? claimed = user?.FindFirst("username")?.Value;
        return string.IsNullOrWhiteSpace(claimed) ? userId.ToString() : claimed;
    }

    private (AccountClass AccountClass, string? TenantDatabaseName) ResolveScope()
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return (AccountClass.RealAccount, null);

        ClaimsPrincipal user = httpContext.User;
        AccountClass accountClass = AccountClass.RealAccount;
        string? accountClassClaim = user.FindFirst(TenancyConstants.AccountClassClaimName)?.Value;
        if (!string.IsNullOrWhiteSpace(accountClassClaim)
            && Enum.TryParse(accountClassClaim, ignoreCase: false, out AccountClass parsed))
        {
            accountClass = parsed;
        }

        string? tenantDatabaseName = user.FindFirst(TenancyConstants.TenantDbClaimName)?.Value;
        return (accountClass, string.IsNullOrWhiteSpace(tenantDatabaseName) ? null : tenantDatabaseName);
    }

    private static bool HasGroupMessagesFeature(EffectiveMaskDto masks)
    {
        BitArray featureMask = BitMask.FromBase64(masks.FeatureMask, 256);
        return BitMask.HasBit(featureMask, PlatformFeatures.GroupMessages);
    }

    private static ChatMessageDto ToDto(ChatMessage message) =>
        new()
        {
            MessageId = message.MessageId,
            RoomId = message.RoomId,
            SenderId = message.SenderId,
            SenderUsername = message.SenderUsername,
            Content = message.SanitizedContent ?? message.RawContent,
            CreatedAtUtc = message.CreatedAtUtc,
        };

    private static ChatInboxItemDto ToInboxDto(ChatMentionNotification notification) =>
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
