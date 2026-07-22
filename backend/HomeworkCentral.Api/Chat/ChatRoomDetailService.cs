using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Chat;

public interface IChatRoomDetailService
{
    Task<ChatRoomDetailDto?> GetRoomAsync(string roomId, EffectiveMaskDto masks, CancellationToken ct = default);
}

public sealed class ChatRoomDetailService(
    AppDbContext db,
    ICustomChannelStore channelStore) : IChatRoomDetailService
{
    public async Task<ChatRoomDetailDto?> GetRoomAsync(
        string roomId,
        EffectiveMaskDto masks,
        CancellationToken ct = default)
    {
        if (string.Equals(roomId, ChatRoomBlueprint.GetRolesRoomId, StringComparison.Ordinal))
        {
            ChatRoomDefinition getRoles = ChatRoomCatalog.GetRolesRoom;
            return new ChatRoomDetailDto
            {
                Id = getRoles.Id,
                Name = getRoles.RoomDisplayName,
                CategoryKey = getRoles.CategoryKey,
                CategoryDisplayName = getRoles.CategoryDisplayName,
                CategoryKind = getRoles.CategoryKind.ToString(),
                IsPrivate = getRoles.IsPrivate,
                RoomType = "GetRoles",
            };
        }

        ChatRoomDefinition? catalogRoom = ChatRoomCatalog.FindById(roomId);
        if (catalogRoom is not null)
        {
            return new ChatRoomDetailDto
            {
                Id = catalogRoom.Id,
                Name = catalogRoom.RoomDisplayName,
                CategoryKey = catalogRoom.CategoryKey,
                CategoryDisplayName = catalogRoom.CategoryDisplayName,
                CategoryKind = catalogRoom.CategoryKind.ToString(),
                IsPrivate = catalogRoom.IsPrivate,
                RoomType = "Chat",
            };
        }

        // The store is refreshed atomically after channel mutations, so this avoids a query
        // every time an existing custom room is opened.
        CustomChannelSnapshot? snapshot = channelStore.FindByRoomId(roomId);
        if (snapshot is not null)
            return MapSnapshot(snapshot, masks);

        // Startup/recovery fallback in case the snapshot has not been warmed yet.
        CustomChannel? channel = await db.CustomChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RoomId == roomId && !c.IsArchived, ct);
        if (channel is null)
            return null;

        return MapChannel(channel, masks);
    }

    private static ChatRoomDetailDto MapSnapshot(CustomChannelSnapshot channel, EffectiveMaskDto masks) =>
        new()
        {
            Id = channel.RoomId,
            Name = channel.DisplayName,
            CategoryKey = channel.CategoryKey,
            CategoryDisplayName = channel.CategoryDisplayName,
            CategoryKind = "Custom",
            IsPrivate = channel.IsPrivate,
            RoomType = channel.RoomType.ToString(),
            InfoContent = channel.InfoContent,
            CanEditInfo = InfoRoomEditPolicy.CanEditInfoFromMask(masks, channel.RoomType, channel.CreatedAtUtc),
            CustomChannelId = channel.ChannelId,
            IconName = channel.IconName,
        };

    private static ChatRoomDetailDto MapChannel(CustomChannel channel, EffectiveMaskDto masks) =>
        new()
        {
            Id = channel.RoomId,
            Name = channel.DisplayName,
            CategoryKey = channel.CategoryKey,
            CategoryDisplayName = channel.CategoryDisplayName,
            CategoryKind = "Custom",
            IsPrivate = channel.IsPrivate,
            RoomType = channel.RoomType.ToString(),
            InfoContent = channel.InfoContent,
            CanEditInfo = InfoRoomEditPolicy.CanEditInfoFromMask(masks, channel.RoomType, channel.CreatedAtUtc),
            CustomChannelId = channel.ChannelId,
            IconName = channel.IconName,
        };
}
