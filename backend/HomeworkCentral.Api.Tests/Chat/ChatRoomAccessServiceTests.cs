using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatRoomAccessServiceTests
{
    private readonly ChatRoomAccessService _service = new(new EmptyCustomChannelStore(), new FixedAccessScopeAccessor());
    private static readonly Guid UserId = Guid.Empty;

    [Fact]
    public void General_room_is_public_and_visible_without_role_or_expertise()
    {
        EffectiveMaskDto masks = CreateMasks();

        ChatNavDto nav = _service.GetAccessibleNav(masks, UserId);

        ChatNavCategoryDto general = Assert.Single(nav.Categories);
        Assert.Equal(ChatRoomBlueprint.GeneralCategoryKey, general.Key);
        Assert.False(general.IsPrivateCategory);
        Assert.Equal(2, general.Rooms.Count);
        Assert.Equal("General", general.Rooms[0].Name);
        Assert.False(general.Rooms[0].IsPrivate);
        Assert.Contains(general.Rooms, room => room.Name == "Get Roles" && !room.IsPrivate);
        Assert.True(_service.CanAccessRoom(masks, ChatRoomCatalog.GeneralRoom.Id));
        Assert.True(_service.CanAccessRoom(masks, ChatRoomCatalog.GetRolesRoom.Id));
    }

    [Fact]
    public void Staff_rooms_are_private_and_require_matching_role()
    {
        ChatRoomDefinition moderatorsRoom = ChatRoomCatalog.StaffRooms
            .Single(room => room.RoomDisplayName == "Moderators");

        Assert.True(moderatorsRoom.IsPrivate);
        Assert.Equal(ChatCategoryKind.Staff, moderatorsRoom.CategoryKind);

        EffectiveMaskDto masks = CreateMasks(roles: [PlatformRoles.Moderator]);
        ChatNavDto nav = _service.GetAccessibleNav(masks, UserId);

        ChatNavCategoryDto staff = nav.Categories.Single(c => c.Key == ChatRoomCatalog.StaffCategoryKey);
        Assert.True(staff.IsPrivateCategory);
        Assert.All(staff.Rooms, room => Assert.True(room.IsPrivate));
        Assert.Contains(staff.Rooms, room => room.Name == "Moderators");
    }

    [Fact]
    public void Science_and_biology_shows_science_category_with_biology_only()
    {
        EffectiveMaskDto masks = CreateMasks(
            subjectExpertise: new Dictionary<string, short[]>
            {
                [SubjectMaskNames.Science] = [ScienceExpertise.Biology],
            });

        ChatNavDto nav = _service.GetAccessibleNav(masks, UserId);

        ChatNavCategoryDto? science = nav.Categories.SingleOrDefault(c => c.Key == SubjectMaskNames.Science);
        Assert.NotNull(science);
        Assert.True(science.IsPrivateCategory);
        Assert.Single(science.Rooms);
        Assert.Equal("Biology", science.Rooms[0].Name);
        Assert.True(science.Rooms[0].IsPrivate);
        Assert.Contains(nav.Categories, c => c.Key == ChatRoomBlueprint.GeneralCategoryKey);
    }

    [Fact]
    public void Calculus_only_shows_mathematics_category_with_calculus_room()
    {
        EffectiveMaskDto masks = CreateMasks(
            subjectExpertise: new Dictionary<string, short[]>
            {
                [SubjectMaskNames.Mathematics] = [MathematicsExpertise.Calculus],
            });

        ChatNavDto nav = _service.GetAccessibleNav(masks, UserId);

        ChatNavCategoryDto mathematics = nav.Categories.Single(c => c.Key == SubjectMaskNames.Mathematics);
        Assert.Equal("Mathematics", mathematics.Name);
        Assert.Equal("Calculus", mathematics.Rooms[0].Name);
        Assert.True(mathematics.Rooms[0].IsPrivate);
    }

    [Fact]
    public void No_expertise_bits_shows_general_but_no_subject_categories()
    {
        ChatNavDto nav = _service.GetAccessibleNav(CreateMasks(), UserId);

        Assert.Contains(nav.Categories, c => c.Key == ChatRoomBlueprint.GeneralCategoryKey);
        Assert.DoesNotContain(nav.Categories, c =>
            c.Key is not ChatRoomBlueprint.GeneralCategoryKey and not ChatRoomCatalog.StaffCategoryKey);
    }

    [Fact]
    public void Tutor_role_shows_staff_category_with_tutors_room()
    {
        EffectiveMaskDto masks = CreateMasks(roles: [PlatformRoles.Tutor]);

        ChatNavDto nav = _service.GetAccessibleNav(masks, UserId);

        ChatNavCategoryDto staff = nav.Categories.Single(c => c.Key == ChatRoomCatalog.StaffCategoryKey);
        Assert.Equal("Staff", staff.Name);
        Assert.Contains(staff.Rooms, room => room.Name == "Tutors" && room.IsPrivate);
    }

    [Fact]
    public void Owner_sees_all_subject_and_staff_rooms()
    {
        EffectiveMaskDto masks = CreateMasks(roles: [PlatformRoles.Owner]);

        ChatNavDto nav = _service.GetAccessibleNav(masks, UserId);

        Assert.True(_service.CanAccessAllRooms(masks));
        Assert.True(nav.Categories.Count > ChatRoomCatalog.StaffRooms.Count);
        Assert.Contains(nav.Categories, c => c.Key == SubjectMaskNames.Mathematics);
        Assert.Contains(nav.Categories, c => c.Key == ChatRoomCatalog.StaffCategoryKey);
        Assert.Contains(nav.Categories, c => c.Key == ChatRoomBlueprint.GeneralCategoryKey);
    }

    [Fact]
    public void Administrator_sees_all_rooms()
    {
        EffectiveMaskDto masks = CreateMasks(roles: [PlatformRoles.Administrator]);

        Assert.True(_service.CanAccessAllRooms(masks));
        Assert.True(_service.GetAccessibleNav(masks, UserId).Categories.Count > 1);
    }

    [Fact]
    public void General_subject_claim_opens_that_category_with_all_private_rooms()
    {
        EffectiveMaskDto masks = CreateMasks(generalSubjects: [GeneralSubjects.Science]);

        ChatNavDto nav = _service.GetAccessibleNav(masks, UserId);

        ChatNavCategoryDto science = nav.Categories.Single(c => c.Key == SubjectMaskNames.Science);
        Assert.Equal("Science", science.Name);
        Assert.True(science.IsPrivateCategory);
        Assert.True(science.Rooms.Count > 1);
        Assert.All(science.Rooms, room => Assert.True(room.IsPrivate));
        Assert.Contains(nav.Categories, c => c.Key == ChatRoomBlueprint.GeneralCategoryKey);
    }

    [Fact]
    public void Custom_general_room_merges_into_existing_general_category()
    {
        Guid channelId = Guid.NewGuid();
        CustomChannelSnapshot customGeneral = new(
            channelId,
            $"custom:{channelId:N}",
            "Announcements",
            "fas:bullhorn",
            ChatRoomBlueprint.GeneralCategoryKey,
            ChatRoomBlueprint.GeneralCategoryDisplayName,
            CustomRoomType.Chat,
            IsPrivate: false,
            InfoContent: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            OwnerAccountClass: AccountClass.RealAccount,
            TieType: ChannelTieType.None,
            TieSubjectMask: null,
            TieSubjectBitIndex: null,
            TiePlatformRoleBit: null,
            AccessRules: []);

        ChatRoomAccessService service = new(new FixedCustomChannelStore(customGeneral), new FixedAccessScopeAccessor());
        ChatNavDto nav = service.GetAccessibleNav(CreateMasks(), UserId);

        ChatNavCategoryDto general = nav.Categories.Single(c => c.Key == ChatRoomBlueprint.GeneralCategoryKey);
        Assert.Equal(3, general.Rooms.Count);
        Assert.Contains(general.Rooms, room => room.Name == "Announcements");
        Assert.Contains(general.Rooms, room => room.Name == "General");
        Assert.Contains(general.Rooms, room => room.Name == "Get Roles");
    }

    [Fact]
    public void Developer_viewer_sees_developer_ticket_portals_but_not_real_ones()
    {
        Guid realId = Guid.NewGuid();
        Guid devId = Guid.NewGuid();
        CustomChannelSnapshot realPortal = TicketPortalSnapshot(
            realId,
            "Notify Mods",
            AccountClass.RealAccount);
        CustomChannelSnapshot devPortal = TicketPortalSnapshot(
            devId,
            "Notify Mods",
            AccountClass.DeveloperAccount);

        ChatRoomAccessService service = new(
            new FixedCustomChannelStore(realPortal, devPortal),
            new FixedAccessScopeAccessor(AccountClass.DeveloperAccount));

        ChatNavDto nav = service.GetAccessibleNav(CreateMasks(), UserId);

        ChatNavCategoryDto general = nav.Categories.Single(c => c.Key == ChatRoomBlueprint.GeneralCategoryKey);
        Assert.Contains(general.Rooms, room => room.Id == $"custom:{devId:N}" && room.RoomType == "Ticket");
        Assert.DoesNotContain(general.Rooms, room => room.Id == $"custom:{realId:N}");
    }

    private static CustomChannelSnapshot TicketPortalSnapshot(
        Guid channelId,
        string displayName,
        AccountClass ownerAccountClass) =>
        new(
            channelId,
            $"custom:{channelId:N}",
            displayName,
            "ticket",
            ChatRoomBlueprint.GeneralCategoryKey,
            ChatRoomBlueprint.GeneralCategoryDisplayName,
            CustomRoomType.Ticket,
            IsPrivate: false,
            InfoContent: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            OwnerAccountClass: ownerAccountClass,
            TieType: ChannelTieType.None,
            TieSubjectMask: null,
            TieSubjectBitIndex: null,
            TiePlatformRoleBit: null,
            AccessRules: []);

    private static EffectiveMaskDto CreateMasks(
        IEnumerable<short>? roles = null,
        IEnumerable<short>? generalSubjects = null,
        Dictionary<string, short[]>? subjectExpertise = null)
    {
        BitArray roleMask = BitMask.Create(64);
        foreach (short bit in roles ?? [])
            BitMask.SetBit(roleMask, bit);

        BitArray generalSubjectMask = BitMask.Create(128);
        foreach (short bit in generalSubjects ?? [])
            BitMask.SetBit(generalSubjectMask, bit);

        Dictionary<string, string> expertiseMasks = new(StringComparer.Ordinal);
        foreach (string category in SubjectExpertiseCatalog.AllExpertiseCategoryNames())
        {
            BitArray mask = BitMask.Create(128);
            if (subjectExpertise is not null && subjectExpertise.TryGetValue(category, out short[]? bits))
            {
                foreach (short bit in bits)
                    BitMask.SetBit(mask, bit);
            }

            expertiseMasks[category] = BitMask.ToBase64(mask);
        }

        return new EffectiveMaskDto
        {
            RoleMask = BitMask.ToBase64(roleMask),
            ModerationMask = BitMask.ToBase64(BitMask.Create(256)),
            FeatureMask = BitMask.ToBase64(BitMask.Create(256)),
            GeneralSubjectMask = BitMask.ToBase64(generalSubjectMask),
            SubjectExpertiseMasks = expertiseMasks,
            StatusMask = BitMask.ToBase64(BitMask.Create(64)),
        };
    }
}

public class ChatRoomBlueprintTests
{
    [Fact]
    public void GeneralLobby_is_public_general_category()
    {
        ChatRoomDefinition room = ChatRoomBlueprint.GeneralLobby();

        Assert.False(room.IsPrivate);
        Assert.Equal(ChatRoomKind.General, room.Kind);
        Assert.Equal(ChatCategoryKind.General, room.CategoryKind);
        Assert.Equal(ChatRoomBlueprint.GeneralRoomId, room.Id);
    }

    [Fact]
    public void GetRolesLobby_is_public_general_category()
    {
        ChatRoomDefinition room = ChatRoomBlueprint.GetRolesLobby();

        Assert.False(room.IsPrivate);
        Assert.Equal(ChatRoomKind.General, room.Kind);
        Assert.Equal(ChatCategoryKind.General, room.CategoryKind);
        Assert.Equal(ChatRoomBlueprint.GetRolesRoomId, room.Id);
    }

    [Fact]
    public void StaffRole_is_private_staff_category()
    {
        ChatRoomDefinition room = ChatRoomBlueprint.StaffRole(PlatformRoles.Moderator, "Moderators");

        Assert.True(room.IsPrivate);
        Assert.Equal(ChatCategoryKind.Staff, room.CategoryKind);
        Assert.Equal(PlatformRoles.Moderator, room.RequiredRoleBit);
    }

    [Fact]
    public void SubjectExpertise_is_private_subject_category()
    {
        ChatRoomDefinition room = ChatRoomBlueprint.SubjectExpertise(
            SubjectMaskNames.Science,
            "Science",
            "Biology",
            ScienceExpertise.Biology);

        Assert.True(room.IsPrivate);
        Assert.Equal(ChatCategoryKind.Subject, room.CategoryKind);
        Assert.Equal("subject:Science:0", room.Id);
    }
}
