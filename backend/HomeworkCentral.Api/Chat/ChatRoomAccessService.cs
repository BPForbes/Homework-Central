using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Chat;

public sealed class ChatRoomAccessService : IChatRoomAccessService
{
    public bool CanAccessAllRooms(EffectiveMaskDto masks) =>
        HasRole(masks.RoleMask, PlatformRoles.Owner)
        || HasRole(masks.RoleMask, PlatformRoles.Administrator);

    public bool CanAccessRoom(EffectiveMaskDto masks, string roomId)
    {
        ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);
        return room is not null && CanAccessRoom(masks, room);
    }

    public bool CanAccessRoom(EffectiveMaskDto masks, ChatRoomDefinition room)
    {
        if (CanAccessAllRooms(masks))
            return true;

        return room.Kind switch
        {
            ChatRoomKind.SubjectExpertise => HasSubjectExpertise(
                masks,
                room.ExpertiseCategory!,
                room.ExpertiseBit!.Value),
            ChatRoomKind.StaffRole => HasRole(masks.RoleMask, room.RequiredRoleBit!.Value),
            _ => false,
        };
    }

    public ChatNavDto GetAccessibleNav(EffectiveMaskDto masks)
    {
        List<ChatNavCategoryDto> categories = new();

        foreach (IGrouping<string, ChatRoomDefinition> subjectGroup in ChatRoomCatalog.SubjectRooms
                     .Where(room => CanAccessRoom(masks, room))
                     .GroupBy(room => room.CategoryKey, StringComparer.Ordinal))
        {
            ChatRoomDefinition first = subjectGroup.First();
            categories.Add(new ChatNavCategoryDto
            {
                Key = first.CategoryKey,
                Name = first.CategoryDisplayName,
                Rooms = subjectGroup
                    .OrderBy(room => room.RoomDisplayName, StringComparer.Ordinal)
                    .Select(room => new ChatNavRoomDto { Id = room.Id, Name = room.RoomDisplayName })
                    .ToList(),
            });
        }

        List<ChatNavRoomDto> staffRooms = ChatRoomCatalog.StaffRooms
            .Where(room => CanAccessRoom(masks, room))
            .OrderBy(room => room.RoomDisplayName, StringComparer.Ordinal)
            .Select(room => new ChatNavRoomDto { Id = room.Id, Name = room.RoomDisplayName })
            .ToList();

        if (staffRooms.Count > 0)
        {
            categories.Add(new ChatNavCategoryDto
            {
                Key = ChatRoomCatalog.StaffCategoryKey,
                Name = ChatRoomCatalog.StaffCategoryDisplayName,
                Rooms = staffRooms,
            });
        }

        categories.Sort((left, right) =>
        {
            if (left.Key == ChatRoomCatalog.StaffCategoryKey)
                return 1;
            if (right.Key == ChatRoomCatalog.StaffCategoryKey)
                return -1;
            return string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        });

        return new ChatNavDto { Categories = categories };
    }

    private static bool HasRole(string roleMaskBase64, short bit) =>
        BitMask.HasBit(BitMask.FromBase64(roleMaskBase64, 64), bit);

    private static bool HasSubjectExpertise(EffectiveMaskDto masks, string category, short bit)
    {
        if (!masks.SubjectExpertiseMasks.TryGetValue(category, out string? maskBase64))
            return false;

        return BitMask.HasBit(BitMask.FromBase64(maskBase64, 128), bit);
    }
}
