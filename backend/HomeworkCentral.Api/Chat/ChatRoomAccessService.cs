using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Chat;

public sealed class ChatRoomAccessService(
    ICustomChannelStore channelStore,
    IAccessScopeAccessor accessScope) : IChatRoomAccessService
{
    public bool CanAccessAllRooms(EffectiveMaskDto masks) => HasElevatedRoomAccess(masks);

    public bool CanAccessRoom(EffectiveMaskDto masks, string roomId)
    {
        ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);
        if (room is not null)
            return CanAccessRoom(masks, room);

        CustomChannelSnapshot? custom = FindVisibleCustomChannel(roomId);
        return custom is not null && CanAccessCustomChannel(masks, custom);
    }

    public bool CanAccessRoom(EffectiveMaskDto masks, ChatRoomDefinition room)
    {
        if (HasElevatedRoomAccess(masks))
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

        List<ChatRoomDefinition> accessibleStaffRooms = ChatRoomCatalog.StaffRooms
            .Where(room => CanAccessRoom(masks, room))
            .ToList();

        if (accessibleStaffRooms.Count > 0)
        {
            categories.Add(BuildCategoryDto(accessibleStaffRooms[0], accessibleStaffRooms, preserveRoomOrder: true));
        }

        foreach (IGrouping<string, CustomChannelSnapshot> customGroup in VisibleCustomChannels()
                     .Where(channel => CanAccessCustomChannel(masks, channel))
                     .GroupBy(channel => channel.CategoryKey, StringComparer.Ordinal))
        {
            CustomChannelSnapshot first = customGroup.First();
            categories.Add(new ChatNavCategoryDto
            {
                Key = first.CategoryKey,
                Name = first.CategoryDisplayName,
                CategoryKind = "Custom",
                IsPrivateCategory = customGroup.Any(c => c.IsPrivate),
                Rooms = customGroup
                    .OrderBy(c => c.DisplayName, StringComparer.Ordinal)
                    .Select(channel => new ChatNavRoomDto
                    {
                        Id = channel.RoomId,
                        Name = channel.DisplayName,
                        IsPrivate = channel.IsPrivate,
                        CategoryKey = channel.CategoryKey,
                        CategoryKind = "Custom",
                        RoomType = channel.RoomType.ToString(),
                        IconName = channel.IconName,
                    })
                    .ToList(),
            });
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

    private CustomChannelSnapshot? FindVisibleCustomChannel(string roomId)
    {
        CustomChannelSnapshot? channel = channelStore.FindByRoomId(roomId);
        if (channel is null)
            return null;

        AccessScope? scope = accessScope.ResolveCurrent();
        return scope is not null
            && InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass)
            ? channel
            : null;
    }

    private IEnumerable<CustomChannelSnapshot> VisibleCustomChannels()
    {
        AccessScope? scope = accessScope.ResolveCurrent();
        if (scope is null)
            return [];

        return channelStore.Channels.Where(channel =>
            InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass));
    }

    private static bool CanAccessCustomChannel(EffectiveMaskDto masks, CustomChannelSnapshot channel)
    {
        if (HasElevatedRoomAccess(masks))
            return true;

        if (!channel.IsPrivate)
            return true;

        foreach (CustomChannelAccessSnapshot rule in channel.AccessRules)
        {
            if (rule.PlatformRoleBit is short platformBit && HasRole(masks.RoleMask, platformBit))
                return true;

            if (rule.CustomRoleId is Guid customRoleId && masks.CustomRoleIds.Contains(customRoleId))
                return true;
        }

        return false;
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
                    RoomType = "Chat",
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

        return SubjectExpertiseCatalog.TryGetGeneralSubjectBit(category, out short generalSubjectBit)
            && HasGeneralSubject(masks, generalSubjectBit);
    }

    private static bool HasGeneralSubject(EffectiveMaskDto masks, short bit) =>
        BitMask.HasBit(BitMask.FromBase64(masks.GeneralSubjectMask, 128), bit);

    private static bool HasElevatedRoomAccess(EffectiveMaskDto masks) =>
        HasRole(masks.RoleMask, PlatformRoles.Owner)
        || HasRole(masks.RoleMask, PlatformRoles.Administrator)
        || HasRole(masks.RoleMask, PlatformRoles.SystemAdministrator);
}
