using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Infrastructure;

public interface IInfoEntryService
{
    /// <summary>Null return means the room doesn't exist, isn't an Info room, or the caller can't access it.</summary>
    Task<InfoEntryFeedDto?> ListEntriesAsync(Guid userId, string roomId, CancellationToken ct = default);
    Task<InfoEntryDto?> CreateEntryAsync(
        Guid actorUserId,
        string actorUsername,
        string roomId,
        CreateInfoEntryRequest request,
        CancellationToken ct = default);
    Task<InfoEntryDto?> UpdateEntryAsync(Guid actorUserId, Guid entryId, UpdateInfoEntryRequest request, CancellationToken ct = default);
}

public sealed class InfoEntryService(
    AppDbContext db,
    IEffectiveMaskService effectiveMaskService,
    IChatRoomAccessService chatRoomAccess,
    IAccessScopeAccessor accessScope) : IInfoEntryService
{
    public async Task<InfoEntryFeedDto?> ListEntriesAsync(Guid userId, string roomId, CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, roomId))
            return null;

        CustomChannel? channel = await FindInfoChannelAsync(roomId, ct);
        if (channel is null || !CanViewChannelScope(channel))
            return null;

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);
        bool hasEditPermission = BitMask.HasBit(mask.EffectiveModerationMask, ModerationPermissions.ManageServerInfrastructure);

        List<InfoEntry> entries = await db.InfoEntries
            .AsNoTracking()
            .Where(e => e.ChannelId == channel.ChannelId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);

        return new InfoEntryFeedDto
        {
            Entries = entries
                .Select(entry => MapEntry(
                    entry,
                    hasEditPermission && InfoRoomEditPolicy.CanEditInfoContent(mask.EffectiveRoleMask, entry.CreatedAtUtc)))
                .ToList(),
            CanCreate = hasEditPermission && InfoRoomEditPolicy.CanEditInfoContent(mask.EffectiveRoleMask, DateTime.UtcNow),
        };
    }

    public async Task<InfoEntryDto?> CreateEntryAsync(
        Guid actorUserId,
        string actorUsername,
        string roomId,
        CreateInfoEntryRequest request,
        CancellationToken ct = default)
    {
        string content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Entry content is required.");

        CustomChannel? channel = await FindInfoChannelAsync(roomId, ct);
        if (channel is null || !CanViewChannelScope(channel))
            return null;

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(actorUserId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(actorUserId, ct);

        DateTime now = DateTime.UtcNow;
        if (!InfoRoomEditPolicy.CanEditInfoContent(mask.EffectiveRoleMask, now))
            throw new InvalidOperationException("You cannot add entries to this info room.");

        InfoEntry entry = new()
        {
            EntryId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            AuthorUserId = actorUserId,
            AuthorUsername = actorUsername,
            Content = content,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.InfoEntries.Add(entry);
        channel.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        return MapEntry(entry, canEdit: true);
    }

    public async Task<InfoEntryDto?> UpdateEntryAsync(
        Guid actorUserId,
        Guid entryId,
        UpdateInfoEntryRequest request,
        CancellationToken ct = default)
    {
        string content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Entry content is required.");

        InfoEntry? entry = await db.InfoEntries
            .Include(e => e.Channel)
            .FirstOrDefaultAsync(e => e.EntryId == entryId, ct);
        if (entry is null || !CanViewChannelScope(entry.Channel))
            return null;

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(actorUserId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(actorUserId, ct);

        if (!InfoRoomEditPolicy.CanEditInfoContent(mask.EffectiveRoleMask, entry.CreatedAtUtc))
        {
            throw new InvalidOperationException(
                "This entry is outside its editable window. Only Owner or System Administrator can edit it now.");
        }

        entry.Content = content;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return MapEntry(entry, canEdit: true);
    }

    private async Task<CustomChannel?> FindInfoChannelAsync(string roomId, CancellationToken ct) =>
        await db.CustomChannels
            .FirstOrDefaultAsync(c => c.RoomId == roomId && c.RoomType == CustomRoomType.Info && !c.IsArchived, ct);

    private bool CanViewChannelScope(CustomChannel channel)
    {
        AccessScope? scope = accessScope.ResolveCurrent();
        return scope is not null && InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass);
    }

    private static InfoEntryDto MapEntry(InfoEntry entry, bool canEdit) =>
        new()
        {
            EntryId = entry.EntryId,
            ChannelId = entry.ChannelId,
            AuthorUsername = entry.AuthorUsername,
            Content = entry.Content,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            CanEdit = canEdit,
        };
}
