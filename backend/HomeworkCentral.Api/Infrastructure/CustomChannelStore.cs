using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeworkCentral.Api.Infrastructure;

public interface ICustomChannelStore
{
    IReadOnlyList<CustomChannelSnapshot> Channels { get; }
    CustomChannelSnapshot? FindByRoomId(string roomId);
    Task RefreshAsync(CancellationToken ct = default);
}

public sealed record CustomChannelAccessSnapshot(
    Guid? CustomRoleId,
    short? PlatformRoleBit,
    Guid? AllowedUserId);

public sealed record CustomChannelSnapshot(
    Guid ChannelId,
    string RoomId,
    string DisplayName,
    string? IconName,
    string CategoryKey,
    string CategoryDisplayName,
    CustomRoomType RoomType,
    bool IsPrivate,
    string? InfoContent,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    AccountClass OwnerAccountClass,
    ChannelTieType TieType,
    string? TieSubjectMask,
    short? TieSubjectBitIndex,
    short? TiePlatformRoleBit,
    IReadOnlyList<CustomChannelAccessSnapshot> AccessRules);

public sealed class CustomChannelStore(IServiceScopeFactory scopeFactory) : ICustomChannelStore
{
    private StoreSnapshot _snapshot = new(
        [],
        new Dictionary<string, CustomChannelSnapshot>(StringComparer.Ordinal));

    public IReadOnlyList<CustomChannelSnapshot> Channels => _snapshot.Channels;

    public CustomChannelSnapshot? FindByRoomId(string roomId) =>
        _snapshot.ByRoomId.GetValueOrDefault(roomId);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<CustomChannel> channels = await db.CustomChannels
            .AsNoTracking()
            .Include(c => c.AccessRules)
            .Where(c => !c.IsArchived)
            .ToListAsync(ct);

        List<CustomChannelSnapshot> snapshots = channels
            .Select(channel => new CustomChannelSnapshot(
                channel.ChannelId,
                channel.RoomId,
                channel.DisplayName,
                channel.IconName,
                channel.CategoryKey,
                channel.CategoryDisplayName,
                channel.RoomType,
                channel.IsPrivate,
                channel.InfoContent,
                channel.CreatedAtUtc,
                channel.UpdatedAtUtc,
                channel.OwnerAccountClass,
                channel.TieType,
                channel.TieSubjectMask,
                channel.TieSubjectBitIndex,
                channel.TiePlatformRoleBit,
                channel.AccessRules
                    .Select(rule => new CustomChannelAccessSnapshot(
                        rule.CustomRoleId,
                        rule.PlatformRoleBit,
                        rule.AllowedUserId))
                    .ToList()))
            .ToList();

        Dictionary<string, CustomChannelSnapshot> byRoomId = snapshots.ToDictionary(
            channel => channel.RoomId,
            StringComparer.Ordinal);
        _snapshot = new StoreSnapshot(snapshots, byRoomId);
    }

    private sealed record StoreSnapshot(
        IReadOnlyList<CustomChannelSnapshot> Channels,
        IReadOnlyDictionary<string, CustomChannelSnapshot> ByRoomId);
}

public static class CustomChannelIds
{
    public static string BuildRoomId(Guid channelId) => $"custom:{channelId:N}";
}

public static class InfrastructureRoleClaimRooms
{
    public static readonly string DefaultClaimRoomId = ChatRoomBlueprint.GetRolesRoomId;
}
