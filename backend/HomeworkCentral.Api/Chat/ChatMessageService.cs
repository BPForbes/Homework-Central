using System.Collections;
using System.Security.Claims;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
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
        Guid? replyToMessageId = null,
        CancellationToken ct = default);

    Task<bool> CanAccessRoomAsync(string roomId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ChatInboxItemDto>> GetInboxAsync(
        Guid userId,
        string? categoryKey = null,
        CancellationToken ct = default);

    Task<ChatInboxSummaryDto> GetInboxSummaryAsync(Guid userId, CancellationToken ct = default);

    Task<bool> MarkInboxReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkInboxAllReadAsync(Guid userId, CancellationToken ct = default);

    Task<int> DeleteInboxItemsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> notificationIds,
        CancellationToken ct = default);

    Task<int> DeleteInboxAllAsync(Guid userId, CancellationToken ct = default);

    Task<int> DeleteInboxCategoryAsync(
        Guid userId,
        string categoryKey,
        CancellationToken ct = default);
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
    IMentionCooldownTracker mentionCooldownTracker,
    IMentionRecipientResolver mentionRecipientResolver,
    IRoleAppearanceService roleAppearanceService,
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

        if (room is null)
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
        Guid? replyToMessageId = null,
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

        // The IShareableScopedResource query filter already confines this lookup to messages the
        // sender's account class may see, so a reply target in a different room, a different
        // real-vs-developer scope, or one that simply doesn't exist all resolve to null here —
        // the message is then sent as a normal (non-reply) message rather than failing the send.
        ChatMessage? replyTarget = replyToMessageId is Guid targetId
            ? await masterDb.ChatMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.MessageId == targetId && m.RoomId == roomId, ct)
            : null;

        ChatMessage message = new()
        {
            MessageId = Guid.NewGuid(),
            RoomId = roomId,
            SenderId = userId,
            SenderUsername = senderUsername,
            SenderMessageColor = await roleAppearanceService.ResolveSenderColorAsync(roleMask, ct),
            RawContent = parsed.DisplayContent,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = accountClass,
            TenantDatabaseName = tenantDatabaseName,
            ReplyToMessageId = replyTarget?.MessageId,
            ReplyToSenderId = replyTarget?.SenderId,
            ReplyToSenderUsername = replyTarget?.SenderUsername,
            ReplyToContentSnippet = replyTarget is null ? null : ChatReplySnippet.Build(replyTarget.RawContent),
        };

        masterDb.ChatMessages.Add(message);

        ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);
        string roomDisplayName = room?.RoomDisplayName ?? roomId;
        string categoryKey = room?.CategoryKey ?? ChatRoomBlueprint.GeneralCategoryKey;
        string categoryDisplayName = room?.CategoryDisplayName ?? ChatRoomBlueprint.GeneralCategoryDisplayName;

        // Tracks who has already been queued a notification for this message so a user who is
        // both @mentioned and the original sender being replied to only gets a single row.
        HashSet<Guid> notifiedRecipients = [];

        IReadOnlyList<ParsedMention> activeMentions = parsed.ActiveMentions.Where(mention => mention.IsActive).ToArray();
        if (activeMentions.Count > 0)
        {
            string groupKey = ChatRoomGroupKey.Build(roomId, accountClass);
            HashSet<Guid> recipients = await mentionRecipientResolver.ResolveRecipientsAsync(
                roomId,
                groupKey,
                activeMentions,
                userId,
                accountClass,
                tenantDatabaseName,
                ct);

            string mentionKind = activeMentions.Count == 1
                ? activeMentions[0].Kind.ToString()
                : "Multiple";

            foreach (Guid recipientId in recipients)
            {
                if (!notifiedRecipients.Add(recipientId))
                    continue;

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
                    MessageContent = message.RawContent,
                    MentionKind = mentionKind,
                    CreatedAtUtc = message.CreatedAtUtc,
                    OwnerAccountClass = accountClass,
                    TenantDatabaseName = tenantDatabaseName,
                });
            }
        }

        // A reply notifies the original sender the same way a mention does (surfaced in their
        // Inbox), as long as they aren't replying to themselves and cross-scope notify rules
        // (real accounts never notify developer accounts and vice versa) allow it.
        if (replyTarget is not null
            && replyTarget.SenderId != userId
            && MentionNotifyScope.CanNotify(accountClass, tenantDatabaseName, replyTarget.OwnerAccountClass, replyTarget.TenantDatabaseName)
            && notifiedRecipients.Add(replyTarget.SenderId))
        {
            masterDb.ChatMentionNotifications.Add(new ChatMentionNotification
            {
                NotificationId = Guid.NewGuid(),
                MessageId = message.MessageId,
                RecipientUserId = replyTarget.SenderId,
                SenderId = userId,
                SenderUsername = senderUsername,
                RoomId = roomId,
                RoomDisplayName = roomDisplayName,
                CategoryKey = categoryKey,
                CategoryDisplayName = categoryDisplayName,
                MessageContent = message.RawContent,
                MentionKind = "Reply",
                CreatedAtUtc = message.CreatedAtUtc,
                OwnerAccountClass = accountClass,
                TenantDatabaseName = tenantDatabaseName,
            });
        }

        await masterDb.SaveChangesAsync(ct);

        ChatMessageDto dto = ToDto(message);
        string broadcastGroupKey = ChatRoomGroupKey.Build(roomId, accountClass);
        await hubContext.Clients.Group(broadcastGroupKey).SendAsync("ReceiveMessage", dto, ct);
        return dto;
    }

    public async Task<IReadOnlyList<ChatInboxItemDto>> GetInboxAsync(
        Guid userId,
        string? categoryKey = null,
        CancellationToken ct = default)
    {
        ChatNavDto nav = await GetAccessibleInboxNavAsync(userId, ct);
        IReadOnlyList<string> accessibleRoomIds = SelectAccessibleRoomIds(nav, categoryKey);
        if (accessibleRoomIds.Count == 0)
            return [];

        List<ChatMentionNotification> items = await masterDb.ChatMentionNotifications
            .AsNoTracking()
            .Where(notification =>
                notification.RecipientUserId == userId
                && accessibleRoomIds.Contains(notification.RoomId))
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return items.Select(ToInboxDto).ToArray();
    }

    public async Task<ChatInboxSummaryDto> GetInboxSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        ChatNavDto nav = await GetAccessibleInboxNavAsync(userId, ct);
        List<string> accessibleRoomIds = nav.Categories
            .SelectMany(category => category.Rooms)
            .Select(room => room.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (accessibleRoomIds.Count == 0)
            return new ChatInboxSummaryDto();

        List<ChatInboxSummaryItemDto> unreadCategories = await masterDb.ChatMentionNotifications
            .AsNoTracking()
            .Where(notification =>
                notification.RecipientUserId == userId
                && notification.ReadAtUtc == null
                && accessibleRoomIds.Contains(notification.RoomId))
            .GroupBy(notification => notification.CategoryKey)
            .Select(group => new ChatInboxSummaryItemDto
            {
                CategoryKey = group.Key,
                CategoryDisplayName = string.Empty,
                UnreadCount = group.Count(),
            })
            .ToListAsync(ct);

        Dictionary<string, int> unreadByCategory = unreadCategories.ToDictionary(
            category => category.CategoryKey,
            category => category.UnreadCount,
            StringComparer.Ordinal);

        return new ChatInboxSummaryDto
        {
            Categories = nav.Categories
                .Select(category => new ChatInboxSummaryItemDto
                {
                    CategoryKey = category.Key,
                    CategoryDisplayName = category.Name,
                    UnreadCount = unreadByCategory.GetValueOrDefault(category.Key),
                })
                .ToArray(),
        };
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
        DateTime now = DateTime.UtcNow;
        await masterDb.ChatMentionNotifications
            .Where(notification => notification.RecipientUserId == userId && notification.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.ReadAtUtc, now),
                ct);
    }

    public async Task<int> DeleteInboxItemsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> notificationIds,
        CancellationToken ct = default)
    {
        if (notificationIds.Count == 0)
            return 0;

        return await masterDb.ChatMentionNotifications
            .Where(notification =>
                notification.RecipientUserId == userId
                && notificationIds.Contains(notification.NotificationId))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> DeleteInboxAllAsync(Guid userId, CancellationToken ct = default) =>
        await masterDb.ChatMentionNotifications
            .Where(notification => notification.RecipientUserId == userId)
            .ExecuteDeleteAsync(ct);

    public async Task<int> DeleteInboxCategoryAsync(
        Guid userId,
        string categoryKey,
        CancellationToken ct = default)
    {
        ChatNavDto nav = await GetAccessibleInboxNavAsync(userId, ct);
        IReadOnlyList<string> accessibleRoomIds = SelectAccessibleRoomIds(nav, categoryKey);
        if (accessibleRoomIds.Count == 0)
            return 0;

        return await masterDb.ChatMentionNotifications
            .Where(notification =>
                notification.RecipientUserId == userId
                && accessibleRoomIds.Contains(notification.RoomId))
            .ExecuteDeleteAsync(ct);
    }

    private async Task<EffectiveMaskDto> GetMasksAsync(Guid userId, CancellationToken ct) =>
        await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);

    private async Task<ChatNavDto> GetAccessibleInboxNavAsync(Guid userId, CancellationToken ct)
    {
        EffectiveMaskDto masks = await GetMasksAsync(userId, ct);
        return chatRoomAccess.GetAccessibleNav(masks);
    }

    private static IReadOnlyList<string> SelectAccessibleRoomIds(ChatNavDto nav, string? categoryKey)
    {
        IEnumerable<ChatNavCategoryDto> categories = nav.Categories;
        if (!string.IsNullOrWhiteSpace(categoryKey))
        {
            categories = categories.Where(category =>
                string.Equals(category.Key, categoryKey, StringComparison.Ordinal));
        }

        return categories
            .SelectMany(category => category.Rooms)
            .Select(room => room.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
            SenderMessageColor = message.SenderMessageColor,
            Content = message.RawContent,
            CreatedAtUtc = message.CreatedAtUtc,
            ReplyToMessageId = message.ReplyToMessageId,
            ReplyToSenderId = message.ReplyToSenderId,
            ReplyToSenderUsername = message.ReplyToSenderUsername,
            ReplyToContentSnippet = message.ReplyToContentSnippet,
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
