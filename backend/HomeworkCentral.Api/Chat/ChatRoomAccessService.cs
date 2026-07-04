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
            ChatRoomKind.General => true,
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

        List<ChatRoomDefinition> accessibleGeneralRooms = ChatRoomCatalog.GeneralRooms
            .Where(room => CanAccessRoom(masks, room))
            .ToList();

        if (accessibleGeneralRooms.Count > 0)
        {
            categories.Add(BuildCategoryDto(accessibleGeneralRooms[0], accessibleGeneralRooms));
        }

        foreach (IGrouping<string, ChatRoomDefinition> subjectGroup in ChatRoomCatalog.SubjectRooms
                     .Where(room => CanAccessRoom(masks, room))
                     .GroupBy(room => room.CategoryKey, StringComparer.Ordinal))
        {
            ChatRoomDefinition first = subjectGroup.First();
            categories.Add(BuildCategoryDto(first, subjectGroup));
        }

        // ChatRoomCatalog.StaffRooms is deliberately ordered by seniority/authority (Staff,
        // Tutor, Senior Tutor, ..., Admins, Community Managers), not alphabetically — .Where
        // preserves that source order, so it must not be re-sorted here or in BuildCategoryDto.
        List<ChatRoomDefinition> accessibleStaffRooms = ChatRoomCatalog.StaffRooms
            .Where(room => CanAccessRoom(masks, room))
            .ToList();

        if (accessibleStaffRooms.Count > 0)
        {
            categories.Add(BuildCategoryDto(accessibleStaffRooms[0], accessibleStaffRooms, preserveRoomOrder: true));
        }

        categories.Sort((left, right) =>
        {
            int order = CategorySortOrder(left.Key).CompareTo(CategorySortOrder(right.Key));
            return order != 0
                ? order
                : string.Compare(left.Key, right.Key, StringComparison.Ordinal);
        });

        return new ChatNavDto { Categories = categories };
    }

    private static ChatNavCategoryDto BuildCategoryDto(
        ChatRoomDefinition categorySample,
        IEnumerable<ChatRoomDefinition> rooms,
        bool preserveRoomOrder = false) =>
        new()
        {
            Key = categorySample.CategoryKey,
            Name = categorySample.CategoryDisplayName,
            CategoryKind = categorySample.CategoryKind.ToString(),
            IsPrivateCategory = ChatRoomCatalog.IsPrivateCategory(categorySample.CategoryKind),
            Rooms = (preserveRoomOrder ? rooms : rooms.OrderBy(room => room.RoomDisplayName, StringComparer.Ordinal))
                .Select(room => new ChatNavRoomDto
                {
                    Id = room.Id,
                    Name = room.RoomDisplayName,
                    IsPrivate = room.IsPrivate,
                    CategoryKey = room.CategoryKey,
                    CategoryKind = room.CategoryKind.ToString(),
                })
                .ToList(),
        };

    private static int CategorySortOrder(string categoryKey) =>
        categoryKey switch
        {
            ChatRoomBlueprint.GeneralCategoryKey => 0,
            ChatRoomCatalog.StaffCategoryKey => 2,
            _ => 1,
        };

    private static bool HasRole(string roleMaskBase64, short bit) =>
        BitMask.HasBit(BitMask.FromBase64(roleMaskBase64, 64), bit);

    private static bool HasSubjectExpertise(EffectiveMaskDto masks, string category, short bit)
    {
        if (masks.SubjectExpertiseMasks.TryGetValue(category, out string? maskBase64)
            && BitMask.HasBit(BitMask.FromBase64(maskBase64, 128), bit))
        {
            return true;
        }

        // Claiming a general subject on Get Roles grants every private room in that category.
        return SubjectExpertiseCatalog.TryGetGeneralSubjectBit(category, out short generalSubjectBit)
            && HasGeneralSubject(masks, generalSubjectBit);
    }

    private static bool HasGeneralSubject(EffectiveMaskDto masks, short bit) =>
        BitMask.HasBit(BitMask.FromBase64(masks.GeneralSubjectMask, 128), bit);
}
