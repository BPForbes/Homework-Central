using System.Collections;
using System.Security.Claims;
using System.Text.Json;
using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Tickets;
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
        CancellationToken ct = default,
        IReadOnlyList<Guid>? attachmentIds = null,
        ChatForwardSnapshotDto? forwardedFrom = null);

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
    IHubContext<ChatHub> hubContext,
    IAssessmentQueue assessmentQueue,
    Uploads.IAttachmentAccessTokenService accessTokenService) : IChatMessageService
{
    private const int MaxMessageLength = 4000;
    private const int DefaultPageSize = 50;
    private static readonly TimeSpan MentionCooldown = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions ForwardSnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record MessageScope(AccountClass AccountClass, string? TenantDatabaseName);

    private sealed record MentionContext(
        BitArray RoleMask,
        MentionParseResult ParsedMessage,
        IReadOnlyList<ParsedMention> ActiveMentions);

    private sealed record RoomNotificationContext(
        string RoomDisplayName,
        string CategoryKey,
        string CategoryDisplayName);

    private sealed record VoteSummary(
        int Score,
        int UpvoteCount,
        int DownvoteCount,
        string? ViewerVote);

    public async Task<bool> CanAccessRoomAsync(string roomId, Guid userId, CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await GetMasksAsync(userId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, userId, roomId))
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
        bool isTicketRoom = await TicketRoomLookup.IsTicketChatRoomAsync(masterDb, roomId, ct);

        // Real-vs-developer traffic is filtered by the IShareableScopedResource EF global query filter.
        IQueryable<ChatMessage> query = masterDb.ChatMessages
            .AsNoTracking()
            .Where(message => message.RoomId == roomId);

        if (beforeUtc is not null)
            query = query.Where(message => message.CreatedAtUtc < beforeUtc.Value);

        if (!isTicketRoom)
            query = query.Include(m => m.Votes);

        List<ChatMessage> messages = await query
            .Include(m => m.Attachments).ThenInclude(a => a.Attachment)
            .Include(m => m.LinkPreviews)
            // These are independent collections. Splitting avoids multiplying vote,
            // attachment, and preview rows in one large joined result.
            .AsSplitQuery()
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(pageSize)
            .ToListAsync(ct);

        messages.Reverse();
        return messages.Select(m => ToDto(m, userId, includeVotes: !isTicketRoom, mintAccessTokens: true)).ToArray();
    }

    public async Task<ChatMessageDto?> SendMessageAsync(
        string roomId,
        Guid userId,
        string content,
        Guid? replyToMessageId = null,
        CancellationToken ct = default,
        IReadOnlyList<Guid>? attachmentIds = null,
        ChatForwardSnapshotDto? forwardedFrom = null)
    {
        if (!await CanAccessRoomAsync(roomId, userId, ct))
            return null;

        bool hasAttachments = attachmentIds is { Count: > 0 };
        bool hasForward = forwardedFrom is not null;
        if (!TryNormalizeSendContent(content, hasAttachments, hasForward, out string trimmed))
            return null;

        MentionContext mentionContext = await ParseAndAuthorizeMentionsAsync(userId, trimmed, ct);

        // The sender's User row may only exist in their own tenant database (dev personas are
        // fully isolated per tenant), so the username is read from the JWT claim already
        // present on every authenticated request rather than looked up in a tenant-scoped
        // Users table — keeping this service entirely master-db-only.
        string senderUsername = ResolveUsername(userId);
        MessageScope messageScope = ResolveScope();
        ChatMessage? replyTarget = await FindReplyTargetAsync(roomId, replyToMessageId, ct);
        string senderColor = await roleAppearanceService.ResolveSenderColorAsync(mentionContext.RoleMask, ct);
        ChatMessage message = CreateChatMessage(
            roomId,
            userId,
            senderUsername,
            senderColor,
            mentionContext.ParsedMessage.DisplayContent,
            messageScope,
            replyTarget,
            forwardedFrom);

        masterDb.ChatMessages.Add(message);
        await AttachFilesAsync(message, attachmentIds, userId, messageScope, ct);

        RoomNotificationContext roomNotificationContext = BuildRoomNotificationContext(roomId);
        HashSet<Guid> notifiedRecipients = [];
        await AddMentionNotificationsAsync(
            message,
            mentionContext,
            messageScope,
            roomNotificationContext,
            userId,
            senderUsername,
            notifiedRecipients,
            ct);
        AddReplyNotificationIfAllowed(
            message,
            replyTarget,
            messageScope,
            roomNotificationContext,
            userId,
            senderUsername,
            notifiedRecipients);

        await masterDb.SaveChangesAsync(ct);

        await LoadLinkedAttachmentsAsync(message, hasAttachments, ct);

        assessmentQueue.TryEnqueue(
            new AssessmentMessageJob(message.MessageId, roomId, userId, message.RawContent));

        return await BroadcastSavedMessageAsync(message, roomId, messageScope, userId, ct);
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
        return chatRoomAccess.GetAccessibleNav(masks, userId);
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

    private MessageScope ResolveScope()
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return new MessageScope(AccountClass.RealAccount, null);

        ClaimsPrincipal user = httpContext.User;
        AccountClass accountClass = AccountClass.RealAccount;
        string? accountClassClaim = user.FindFirst(TenancyConstants.AccountClassClaimName)?.Value;
        if (!string.IsNullOrWhiteSpace(accountClassClaim)
            && Enum.TryParse(accountClassClaim, ignoreCase: false, out AccountClass parsed))
        {
            accountClass = parsed;
        }

        string? tenantDatabaseName = user.FindFirst(TenancyConstants.TenantDbClaimName)?.Value;
        string? normalizedTenantDatabaseName = string.IsNullOrWhiteSpace(tenantDatabaseName)
            ? null
            : tenantDatabaseName;

        // Shared chat stays in the master database while the row scope records the
        // account-class bucket that separates real and developer traffic. See docs/chat.md.
        return new MessageScope(accountClass, normalizedTenantDatabaseName);
    }

    private static bool HasGroupMessagesFeature(EffectiveMaskDto masks)
    {
        BitArray featureMask = BitMask.FromBase64(masks.FeatureMask, 256);
        return BitMask.HasBit(featureMask, PlatformFeatures.GroupMessages);
    }

    private static bool TryNormalizeSendContent(
        string content,
        bool hasAttachments,
        bool hasForward,
        out string normalizedContent)
    {
        normalizedContent = (content ?? string.Empty).Trim();
        if ((string.IsNullOrEmpty(normalizedContent) && !hasAttachments && !hasForward)
            || normalizedContent.Length > MaxMessageLength)
        {
            return false;
        }

        if (string.IsNullOrEmpty(normalizedContent))
            normalizedContent = hasForward ? "(forwarded message)" : "(attachment)";

        return true;
    }

    private async Task<MentionContext> ParseAndAuthorizeMentionsAsync(
        Guid userId,
        string content,
        CancellationToken ct)
    {
        EffectiveMaskDto masks = await GetMasksAsync(userId, ct);
        BitArray roleMask = BitMask.FromBase64(masks.RoleMask, 64);
        bool canUseBroadcastMentions = MentionPermissions.CanUseBroadcastMentions(roleMask);
        MentionParseResult parsedMessage = MentionParser.Parse(content, canUseBroadcastMentions);
        IReadOnlyList<ParsedMention> activeMentions = parsedMessage.ActiveMentions
            .Where(mention => mention.IsActive)
            .ToArray();

        AssertMentionSendAllowed(userId, roleMask, activeMentions);
        return new MentionContext(roleMask, parsedMessage, activeMentions);
    }

    private void AssertMentionSendAllowed(
        Guid userId,
        BitArray roleMask,
        IReadOnlyList<ParsedMention> activeMentions)
    {
        if (activeMentions.Count == 0)
            return;

        if (MentionPermissions.IsGuest(roleMask))
            throw new SendMessageMentionException(SendMessageMentionError.GuestCannotMention);

        if (!MentionPermissions.IsSeniorStaff(roleMask)
            && !mentionCooldownTracker.TryRecordMention(userId, MentionCooldown, out TimeSpan retryAfter))
        {
            throw new SendMessageMentionException(SendMessageMentionError.MentionCooldown, retryAfter);
        }
    }

    private async Task<ChatMessage?> FindReplyTargetAsync(
        string roomId,
        Guid? replyToMessageId,
        CancellationToken ct)
    {
        if (replyToMessageId is not Guid targetMessageId)
            return null;

        // The shared-resource EF filter confines reply lookup to messages visible
        // within the sender's account-class bucket. See docs/chat.md.
        return await masterDb.ChatMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(message => message.MessageId == targetMessageId && message.RoomId == roomId, ct);
    }

    private static ChatMessage CreateChatMessage(
        string roomId,
        Guid senderId,
        string senderUsername,
        string senderColor,
        string displayContent,
        MessageScope messageScope,
        ChatMessage? replyTarget,
        ChatForwardSnapshotDto? forwardedFrom)
    {
        return new ChatMessage
        {
            MessageId = Guid.NewGuid(),
            RoomId = roomId,
            SenderId = senderId,
            SenderUsername = senderUsername,
            SenderMessageColor = senderColor,
            RawContent = displayContent,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = messageScope.AccountClass,
            TenantDatabaseName = messageScope.TenantDatabaseName,
            ReplyToMessageId = replyTarget?.MessageId,
            ReplyToSenderId = replyTarget?.SenderId,
            ReplyToSenderUsername = replyTarget?.SenderUsername,
            ReplyToContentSnippet = replyTarget is null ? null : ChatReplySnippet.Build(replyTarget.RawContent),
            ForwardedFromJson = forwardedFrom is null
                ? null
                : JsonSerializer.Serialize(forwardedFrom, ForwardSnapshotJsonOptions),
        };
    }

    private async Task AttachFilesAsync(
        ChatMessage message,
        IReadOnlyList<Guid>? attachmentIds,
        Guid userId,
        MessageScope messageScope,
        CancellationToken ct)
    {
        if (attachmentIds is not { Count: > 0 })
            return;

        Guid[] distinctAttachmentIds = attachmentIds.Distinct().ToArray();
        Dictionary<Guid, ChatAttachment> attachmentsById = await masterDb.ChatAttachments
            .Where(attachment => distinctAttachmentIds.Contains(attachment.AttachmentId))
            .ToDictionaryAsync(attachment => attachment.AttachmentId, ct);

        int attachmentOrder = 0;
        foreach (Guid attachmentId in distinctAttachmentIds)
        {
            attachmentsById.TryGetValue(attachmentId, out ChatAttachment? attachment);

            switch (attachment)
            {
                case null:
                    continue;
                case { UploadedByUserId: Guid ownerId } when ownerId != userId:
                    throw new InvalidOperationException("You can only attach files you uploaded.");
                case ChatAttachment scopedAttachment
                    when scopedAttachment.OwnerAccountClass != messageScope.AccountClass
                        || scopedAttachment.TenantDatabaseName != messageScope.TenantDatabaseName:
                    throw new InvalidOperationException("Attachment belongs to a different account scope.");
                case { }:
                    masterDb.ChatMessageAttachments.Add(new ChatMessageAttachment
                    {
                        MessageId = message.MessageId,
                        AttachmentId = attachmentId,
                        SortOrder = attachmentOrder++,
                    });
                    break;
            }
        }
    }

    private static RoomNotificationContext BuildRoomNotificationContext(string roomId)
    {
        ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);
        return new RoomNotificationContext(
            room?.RoomDisplayName ?? roomId,
            room?.CategoryKey ?? ChatRoomBlueprint.GeneralCategoryKey,
            room?.CategoryDisplayName ?? ChatRoomBlueprint.GeneralCategoryDisplayName);
    }

    private async Task AddMentionNotificationsAsync(
        ChatMessage message,
        MentionContext mentionContext,
        MessageScope messageScope,
        RoomNotificationContext roomContext,
        Guid senderId,
        string senderUsername,
        HashSet<Guid> notifiedRecipients,
        CancellationToken ct)
    {
        if (mentionContext.ActiveMentions.Count == 0)
            return;

        string groupKey = ChatRoomGroupKey.Build(message.RoomId, messageScope.AccountClass);
        HashSet<Guid> recipients = await mentionRecipientResolver.ResolveRecipientsAsync(
            message.RoomId,
            groupKey,
            mentionContext.ActiveMentions,
            senderId,
            messageScope.AccountClass,
            messageScope.TenantDatabaseName,
            ct);

        string mentionKind = mentionContext.ActiveMentions.Count == 1
            ? mentionContext.ActiveMentions[0].Kind.ToString()
            : "Multiple";

        foreach (Guid recipientId in recipients)
        {
            AddNotificationIfNew(
                message,
                recipientId,
                senderId,
                senderUsername,
                mentionKind,
                messageScope,
                roomContext,
                notifiedRecipients);
        }
    }

    private void AddReplyNotificationIfAllowed(
        ChatMessage message,
        ChatMessage? replyTarget,
        MessageScope messageScope,
        RoomNotificationContext roomContext,
        Guid senderId,
        string senderUsername,
        HashSet<Guid> notifiedRecipients)
    {
        if (replyTarget is null || replyTarget.SenderId == senderId)
            return;

        bool canNotifyReplySender = MentionNotifyScope.CanNotify(
            messageScope.AccountClass,
            messageScope.TenantDatabaseName,
            replyTarget.OwnerAccountClass,
            replyTarget.TenantDatabaseName);

        if (!canNotifyReplySender)
            return;

        AddNotificationIfNew(
            message,
            replyTarget.SenderId,
            senderId,
            senderUsername,
            "Reply",
            messageScope,
            roomContext,
            notifiedRecipients);
    }

    private void AddNotificationIfNew(
        ChatMessage message,
        Guid recipientId,
        Guid senderId,
        string senderUsername,
        string mentionKind,
        MessageScope messageScope,
        RoomNotificationContext roomContext,
        HashSet<Guid> notifiedRecipients)
    {
        // One notification row represents one sent message per recipient, even
        // when mention and reply rules both match. See docs/chat.md.
        if (!notifiedRecipients.Add(recipientId))
            return;

        masterDb.ChatMentionNotifications.Add(new ChatMentionNotification
        {
            NotificationId = Guid.NewGuid(),
            MessageId = message.MessageId,
            RecipientUserId = recipientId,
            SenderId = senderId,
            SenderUsername = senderUsername,
            RoomId = message.RoomId,
            RoomDisplayName = roomContext.RoomDisplayName,
            CategoryKey = roomContext.CategoryKey,
            CategoryDisplayName = roomContext.CategoryDisplayName,
            MessageContent = message.RawContent,
            MentionKind = mentionKind,
            CreatedAtUtc = message.CreatedAtUtc,
            OwnerAccountClass = messageScope.AccountClass,
            TenantDatabaseName = messageScope.TenantDatabaseName,
        });
    }

    private async Task LoadLinkedAttachmentsAsync(
        ChatMessage message,
        bool hasAttachments,
        CancellationToken ct)
    {
        if (!hasAttachments)
            return;

        await masterDb.Entry(message)
            .Collection(chatMessage => chatMessage.Attachments)
            .Query()
            .Include(messageAttachment => messageAttachment.Attachment)
            .LoadAsync(ct);
    }

    private async Task<ChatMessageDto> BroadcastSavedMessageAsync(
        ChatMessage message,
        string roomId,
        MessageScope messageScope,
        Guid viewerId,
        CancellationToken ct)
    {
        bool isTicketRoom = await TicketRoomLookup.IsTicketChatRoomAsync(masterDb, roomId, ct);
        bool includeVotes = !isTicketRoom;
        ChatMessageDto viewerDto = ToDto(message, viewerId, includeVotes, mintAccessTokens: true);
        ChatMessageDto broadcastDto = ToDto(message, viewerId: null, includeVotes, mintAccessTokens: false);
        string broadcastGroupKey = ChatRoomGroupKey.Build(roomId, messageScope.AccountClass);
        await hubContext.Clients.Group(broadcastGroupKey).SendAsync("ReceiveMessage", broadcastDto, ct);
        return viewerDto;
    }

    private ChatMessageDto ToDto(
        ChatMessage message,
        Guid? viewerId = null,
        bool includeVotes = true,
        bool mintAccessTokens = true)
    {
        VoteSummary voteSummary = BuildVoteSummary(message, viewerId, includeVotes);
        ChatForwardSnapshotDto? forwardedFrom = DeserializeForwardSnapshot(message.ForwardedFromJson);

        return new ChatMessageDto
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
            Score = voteSummary.Score,
            UpvoteCount = voteSummary.UpvoteCount,
            DownvoteCount = voteSummary.DownvoteCount,
            ViewerVote = voteSummary.ViewerVote,
            ForwardedFrom = forwardedFrom,
            Attachments = MapAttachments(message, viewerId, mintAccessTokens),
            LinkPreviews = MapLinkPreviews(message),
        };
    }

    private static VoteSummary BuildVoteSummary(ChatMessage message, Guid? viewerId, bool includeVotes)
    {
        if (!includeVotes)
            return new VoteSummary(0, 0, 0, null);

        int upvoteCount = message.Votes?.Count(vote => vote.Value > 0) ?? 0;
        int downvoteCount = message.Votes?.Count(vote => vote.Value < 0) ?? 0;
        short? viewerVoteValue = viewerId is Guid viewerGuid
            ? message.Votes?.FirstOrDefault(vote => vote.UserId == viewerGuid)?.Value
            : null;
        string? viewerVote = viewerVoteValue switch
        {
            null => null,
            > 0 => "up",
            _ => "down",
        };

        return new VoteSummary(upvoteCount - downvoteCount, upvoteCount, downvoteCount, viewerVote);
    }

    private static ChatForwardSnapshotDto? DeserializeForwardSnapshot(string? forwardedFromJson)
    {
        if (string.IsNullOrWhiteSpace(forwardedFromJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ChatForwardSnapshotDto>(
                forwardedFromJson,
                ForwardSnapshotJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private List<ChatAttachmentInfoDto> MapAttachments(
        ChatMessage message,
        Guid? viewerId,
        bool mintAccessTokens)
    {
        if (message.Attachments is null)
            return [];

        return message.Attachments
            .OrderBy(messageAttachment => messageAttachment.SortOrder)
            .Select(messageAttachment => new ChatAttachmentInfoDto
            {
                AttachmentId = messageAttachment.AttachmentId,
                FileName = messageAttachment.Attachment.OriginalFileName,
                ContentType = messageAttachment.Attachment.ContentType,
                SizeBytes = messageAttachment.Attachment.SizeBytes,
                DownloadUrl = BuildAttachmentDownloadUrl(
                    messageAttachment.AttachmentId,
                    viewerId,
                    mintAccessTokens),
                IsHazard = messageAttachment.Attachment.IsHazard,
                InlinePreviewKind = messageAttachment.Attachment.InlinePreviewKind,
                ScanStatus = messageAttachment.Attachment.ScanStatus.ToString(),
            })
            .ToList();
    }

    private string BuildAttachmentDownloadUrl(Guid attachmentId, Guid? viewerId, bool mintAccessTokens) =>
        mintAccessTokens && viewerId is Guid viewerGuid
            ? accessTokenService.MintDownloadUrl(attachmentId, viewerGuid)
            : $"/api/chat/attachments/{attachmentId}";

    private static List<LinkPreviewDto> MapLinkPreviews(ChatMessage message)
    {
        if (message.LinkPreviews is null)
            return [];

        return message.LinkPreviews
            .Select(linkPreview => new LinkPreviewDto
            {
                Url = linkPreview.Url,
                Title = linkPreview.Title,
                Description = linkPreview.Description,
                ImageUrl = linkPreview.ImageUrl,
            })
            .ToList();
    }

    private static ChatInboxItemDto ToInboxDto(ChatMentionNotification notification)
    {
        TicketOpenedPayloadDto? openedPayload = notification.MentionKind == TicketMentionKinds.Opened
            ? TicketJson.TryDeserializeOpenedPayload(notification.TicketPayloadJson)
            : null;
        TicketDecisionPayloadDto? decisionPayload = notification.MentionKind == TicketMentionKinds.Decision
            ? TicketJson.TryDeserializeDecisionPayload(notification.TicketPayloadJson)
            : null;

        return new ChatInboxItemDto
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
            TicketId = notification.TicketId,
            TicketIntakeAnswers = openedPayload?.IntakeAnswers,
            TicketDecision = decisionPayload?.Decision,
            TicketDecisionSummary = decisionPayload?.Summary,
            CreatedAtUtc = notification.CreatedAtUtc,
            ReadAtUtc = notification.ReadAtUtc,
        };
    }
}
