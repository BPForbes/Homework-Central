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

public sealed class ChatMessageService(
    AppDbContext masterDb,
    ITenantDbContextFactory tenantFactory,
    IHttpContextAccessor httpContextAccessor,
    IEffectiveMaskService effectiveMaskService,
    IChatRoomAccessService chatRoomAccess,
    IContentSanitizer contentSanitizer,
    IHubContext<ChatHub> hubContext) : IChatMessageService, IDisposable
{
    private const int MaxMessageLength = 4000;
    private const int DefaultPageSize = 50;

    private AppDbContext? _tenantDb;

    public async Task<bool> CanAccessRoomAsync(string roomId, Guid userId, CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await GetMasksAsync(userId, ct);
        if (!HasGroupMessagesFeature(masks))
            return false;

        return chatRoomAccess.CanAccessRoom(masks, roomId);
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
        AppDbContext db = await GetDbAsync(ct);

        IQueryable<ChatMessage> query = db.ChatMessages
            .AsNoTracking()
            .Where(message => message.RoomId == roomId);

        if (beforeUtc is not null)
            query = query.Where(message => message.CreatedAtUtc < beforeUtc.Value);

        List<ChatMessage> messages = await query
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(pageSize)
            .ToListAsync(ct);

        messages.Reverse();
        return messages.Select(message => ToDto(message, userId)).ToArray();
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

        AppDbContext db = await GetDbAsync(ct);
        User? sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.UserId == userId, ct);
        if (sender is null)
            return null;

        (AccountClass accountClass, string? tenantDatabaseName) = ResolveScope();
        string sanitized = contentSanitizer.Sanitize(trimmed);

        ChatMessage message = new()
        {
            MessageId = Guid.NewGuid(),
            RoomId = roomId,
            SenderId = userId,
            SenderUsername = sender.Username,
            RawContent = trimmed,
            SanitizedContent = sanitized,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = accountClass,
            TenantDatabaseName = tenantDatabaseName,
        };

        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);

        ChatMessageDto dto = ToDto(message, userId);
        string groupKey = ChatRoomGroupKey.Build(roomId, accountClass, tenantDatabaseName);
        await hubContext.Clients.Group(groupKey).SendAsync("ReceiveMessage", dto, ct);
        return dto;
    }

    public void Dispose() => _tenantDb?.Dispose();

    private async Task<EffectiveMaskDto> GetMasksAsync(Guid userId, CancellationToken ct)
    {
        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);
        return ToEffectiveMaskDto(mask);
    }

    private async Task<AppDbContext> GetDbAsync(CancellationToken ct)
    {
        string? tenantDatabase = ResolveTenantDatabaseName();
        if (string.IsNullOrEmpty(tenantDatabase))
            return masterDb;

        _tenantDb ??= await tenantFactory.CreateForRegisteredTenantAsync(tenantDatabase, ct);
        return _tenantDb;
    }

    private string? ResolveTenantDatabaseName()
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return null;

        return httpContext.User.FindFirst(TenancyConstants.TenantDbClaimName)?.Value;
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

    private static ChatMessageDto ToDto(ChatMessage message, Guid currentUserId) =>
        new()
        {
            MessageId = message.MessageId,
            RoomId = message.RoomId,
            SenderId = message.SenderId,
            SenderUsername = message.SenderId == currentUserId ? null : message.SenderUsername,
            Content = message.SanitizedContent ?? message.RawContent,
            CreatedAtUtc = message.CreatedAtUtc,
            IsOwn = message.SenderId == currentUserId,
        };

    private static EffectiveMaskDto ToEffectiveMaskDto(UserEffectiveMask effectiveMask)
    {
        Dictionary<string, string> subjectExpertiseMasks = SubjectExpertiseCatalog.AllExpertiseCategoryNames()
            .ToDictionary(
                category => category,
                category =>
                {
                    UserSubjectExpertiseMask? row = effectiveMask.SubjectExpertiseMasks
                        .FirstOrDefault(m => m.Category == category);
                    return BitMask.ToBase64(row?.ExpertiseMask ?? BitMask.Create(128));
                },
                StringComparer.Ordinal);

        return new EffectiveMaskDto
        {
            RoleMask = BitMask.ToBase64(effectiveMask.EffectiveRoleMask),
            ModerationMask = BitMask.ToBase64(effectiveMask.EffectiveModerationMask),
            FeatureMask = BitMask.ToBase64(effectiveMask.EffectiveFeatureMask),
            GeneralSubjectMask = BitMask.ToBase64(effectiveMask.GeneralSubjectMask),
            SubjectExpertiseMasks = subjectExpertiseMasks,
            StatusMask = BitMask.ToBase64(effectiveMask.StatusMask),
        };
    }
}
