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

    public bool CanAccessRoom(EffectiveMaskDto masks, string roomId) =>
        CanAccessRoom(masks, Guid.Empty, roomId);

    public bool CanAccessRoom(EffectiveMaskDto masks, Guid userId, string roomId)
    {
        ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);
        if (room is not null)
            return CanAccessRoom(masks, room);

        CustomChannelSnapshot? custom = FindVisibleCustomChannel(roomId);
        return custom is not null && CanAccessCustomChannel(masks, userId, custom);
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

    public ChatNavDto GetAccessibleNav(EffectiveMaskDto masks, Guid userId)
    {
        List<ChatNavCategoryDto> categories = new();

        // Catalog rooms are already in hand; filter with the definition overload so
        // nav does not re-resolve each room through FindById.
        List<ChatRoomDefinition> accessibleGeneralRooms = ChatRoomCatalog.GeneralRooms
            .Where(room => CanAccessRoom(masks, room))
            .ToList();

        if (accessibleGeneralRooms.Count > 0)
        {
            categories.Add(BuildCategoryDto(accessibleGeneralRooms[0], accessibleGeneralRooms));
        }

        categories.AddRange(ChatRoomCatalog.SubjectRooms
            .Where(room => CanAccessRoom(masks, room))
            .GroupBy(room => room.CategoryKey, StringComparer.Ordinal)
            .Select(subjectGroup => BuildCategoryDto(subjectGroup.First(), subjectGroup)));

        List<ChatRoomDefinition> accessibleStaffRooms = ChatRoomCatalog.StaffRooms
            .Where(room => CanAccessRoom(masks, room))
            .ToList();

        if (accessibleStaffRooms.Count > 0)
        {
            categories.Add(BuildCategoryDto(accessibleStaffRooms[0], accessibleStaffRooms, preserveRoomOrder: true));
        }

        List<CustomChannelSnapshot> accessibleCustomChannels = VisibleCustomChannels()
            .Where(channel => CanAccessCustomChannel(masks, userId, channel))
            .ToList();

        // Merge custom channels into existing sidebar categories by category key
        // instead of scanning the category list per channel.
        Dictionary<string, ChatNavCategoryDto> categoriesByKey = categories
            .ToDictionary(category => category.Key, StringComparer.Ordinal);

        // TryMergeCustomChannel mutates categories/categoriesByKey; evaluate merge first,
        // then keep only channels that still need a synthetic category.
        List<CustomChannelSnapshot> unmatchedCustomChannels = accessibleCustomChannels
            .Where(channel => !TryMergeCustomChannel(categories, categoriesByKey, channel))
            .ToList();

        categories.AddRange(unmatchedCustomChannels
            .GroupBy(channel => channel.CategoryKey, StringComparer.Ordinal)
            .Select(customGroup =>
            {
                CustomChannelSnapshot first = customGroup.First();
                return new ChatNavCategoryDto
                {
                    Key = first.CategoryKey,
                    Name = first.CategoryDisplayName,
                    CategoryKind = "Custom",
                    IsPrivateCategory = customGroup.Any(c => c.IsPrivate),
                    Rooms = customGroup
                        .OrderBy(c => c.DisplayName, StringComparer.Ordinal)
                        .Select(channel => MapCustomRoom(channel, channel.CategoryKey, "Custom"))
                        .ToList(),
                };
            }));

        categories.Sort((left, right) =>
        {
            int order = CategorySortOrder(left.Key).CompareTo(CategorySortOrder(right.Key));
            return order != 0
                ? order
                : string.Compare(left.Key, right.Key, StringComparison.Ordinal);
        });

        return new ChatNavDto { Categories = categories };
    }

    private static bool TryMergeCustomChannel(
        List<ChatNavCategoryDto> categories,
        Dictionary<string, ChatNavCategoryDto> categoriesByKey,
        CustomChannelSnapshot channel)
    {
        if (!ChatRoomCatalog.TryGetCatalogCategoryTemplate(channel.CategoryKey, out ChatRoomCatalog.CatalogCategoryTemplate template))
            return false;

        if (!categoriesByKey.TryGetValue(template.Key, out ChatNavCategoryDto? category))
        {
            category = new ChatNavCategoryDto
            {
                Key = template.Key,
                Name = template.DisplayName,
                CategoryKind = template.CategoryKind.ToString(),
                IsPrivateCategory = ChatRoomCatalog.IsPrivateCategory(template.CategoryKind),
            };
            categories.Add(category);
            categoriesByKey[template.Key] = category;
        }

        category.Rooms.Add(MapCustomRoom(channel, template.Key, template.CategoryKind.ToString()));
        if (channel.IsPrivate)
            category.IsPrivateCategory = true;

        return true;
    }

    private static ChatNavRoomDto MapCustomRoom(
        CustomChannelSnapshot channel,
        string categoryKey,
        string categoryKind) =>
        new()
        {
            Id = channel.RoomId,
            Name = channel.DisplayName,
            IsPrivate = channel.IsPrivate,
            CategoryKey = categoryKey,
            CategoryKind = categoryKind,
            RoomType = channel.RoomType.ToString(),
            IconName = channel.IconName,
        };

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

    private static bool CanAccessCustomChannel(EffectiveMaskDto masks, Guid userId, CustomChannelSnapshot channel)
    {
        if (HasElevatedRoomAccess(masks))
            return true;

        if (!channel.IsPrivate)
            return true;

        return channel.AccessRules.Any(rule => MatchesPrivateAccessRule(rule, masks, userId));
    }

    /// <summary>
    /// Private custom channels admit the allow-listed user, a matching platform role bit,
    /// or a matching custom role id. See docs/chat.md.
    /// </summary>
    private static bool MatchesPrivateAccessRule(
        CustomChannelAccessSnapshot rule,
        EffectiveMaskDto masks,
        Guid userId) =>
        rule switch
        {
            { AllowedUserId: Guid allowedUserId }
                when userId != Guid.Empty && allowedUserId == userId => true,
            { PlatformRoleBit: short platformBit }
                when HasRole(masks.RoleMask, platformBit) => true,
            { CustomRoleId: Guid customRoleId }
                when masks.CustomRoleIds.Contains(customRoleId) => true,
            _ => false,
        };

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
