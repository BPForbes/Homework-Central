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
    short? PlatformRoleBit);

public sealed record CustomChannelSnapshot(
    Guid ChannelId,
    string RoomId,
    string DisplayName,
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
    private IReadOnlyList<CustomChannelSnapshot> _channels = [];

    public IReadOnlyList<CustomChannelSnapshot> Channels => _channels;

    public CustomChannelSnapshot? FindByRoomId(string roomId) =>
        _channels.FirstOrDefault(channel => string.Equals(channel.RoomId, roomId, StringComparison.Ordinal));

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<CustomChannel> channels = await db.CustomChannels
            .AsNoTracking()
            .Include(c => c.AccessRules)
            .Where(c => !c.IsArchived)
            .ToListAsync(ct);

        _channels = channels
            .Select(channel => new CustomChannelSnapshot(
                channel.ChannelId,
                channel.RoomId,
                channel.DisplayName,
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
                    .Select(rule => new CustomChannelAccessSnapshot(rule.CustomRoleId, rule.PlatformRoleBit))
                    .ToList()))
            .ToList();
    }
}

public static class CustomChannelIds
{
    public static string BuildRoomId(Guid channelId) => $"custom:{channelId:N}";
}

public static class InfrastructureRoleClaimRooms
{
    public static readonly string DefaultClaimRoomId = ChatRoomBlueprint.GetRolesRoomId;
}
