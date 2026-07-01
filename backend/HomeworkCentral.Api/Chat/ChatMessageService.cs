using System.Security.Claims;
using HomeworkCentral.Api.Authorization;
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
    IHubContext<ChatHub> hubContext) : IChatMessageService
{
    private const int MaxMessageLength = 4000;
    private const int DefaultPageSize = 50;

    public async Task<bool> CanAccessRoomAsync(string roomId, Guid userId, CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await GetMasksAsync(userId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, roomId))
            return false;

        // The public General room is intentionally open to any authenticated user (see
        // ChatRoomAccessService.CanAccessRoom's ChatRoomKind.General case, and
        // GetAccessibleNav, which lists it for everyone with no feature check). The
        // GroupMessages feature bit is meant to additionally gate the role/expertise-scoped
        // "group" rooms (subject-expertise and staff rooms) on top of their role/expertise
        // requirement — it must not also block General, or a role that lacks GroupMessages
        // (e.g. Guest, VerifiedUser) would see General in their sidebar but get a 403 the
        // moment they tried to actually read or send a message there.
        if (string.Equals(roomId, ChatRoomCatalog.GeneralRoom.Id, StringComparison.Ordinal))
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
        bool viewerIsRealAccount = ResolveScope().AccountClass == AccountClass.RealAccount;

        IQueryable<ChatMessage> query = masterDb.ChatMessages
            .AsNoTracking()
            .Where(message => message.RoomId == roomId)
            .Where(message => viewerIsRealAccount
                ? message.OwnerAccountClass == AccountClass.RealAccount
                : message.OwnerAccountClass != AccountClass.RealAccount);

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

        // The sender's User row may only exist in their own tenant database (dev personas are
        // fully isolated per tenant), so the username is read from the JWT claim already
        // present on every authenticated request rather than looked up in a tenant-scoped
        // Users table — keeping this service entirely master-db-only.
        string senderUsername = ResolveUsername(userId);
        (AccountClass accountClass, string? tenantDatabaseName) = ResolveScope();
        string sanitized = contentSanitizer.Sanitize(trimmed);

        ChatMessage message = new()
        {
            MessageId = Guid.NewGuid(),
            RoomId = roomId,
            SenderId = userId,
            SenderUsername = senderUsername,
            RawContent = trimmed,
            SanitizedContent = sanitized,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = accountClass,
            TenantDatabaseName = tenantDatabaseName,
        };

        masterDb.ChatMessages.Add(message);
        await masterDb.SaveChangesAsync(ct);

        ChatMessageDto dto = ToDto(message);
        string groupKey = ChatRoomGroupKey.Build(roomId, accountClass);
        await hubContext.Clients.Group(groupKey).SendAsync("ReceiveMessage", dto, ct);
        return dto;
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
        System.Collections.BitArray featureMask = BitMask.FromBase64(masks.FeatureMask, 256);
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
}
