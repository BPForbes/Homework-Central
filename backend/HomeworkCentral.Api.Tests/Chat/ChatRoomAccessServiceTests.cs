using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatRoomAccessServiceTests
{
    private readonly ChatRoomAccessService _service = new();

    [Fact]
    public void Science_and_biology_shows_science_category_with_biology_only()
    {
        EffectiveMaskDto masks = CreateMasks(
            subjectExpertise: new Dictionary<string, short[]>
            {
                [SubjectMaskNames.Science] = [ScienceExpertise.Biology],
            });

        ChatNavDto nav = _service.GetAccessibleNav(masks);

        ChatNavCategoryDto? science = nav.Categories.SingleOrDefault(c => c.Key == SubjectMaskNames.Science);
        Assert.NotNull(science);
        Assert.Single(science.Rooms);
        Assert.Equal("Biology", science.Rooms[0].Name);
    }

    [Fact]
    public void Calculus_only_shows_mathematics_category_with_calculus_room()
    {
        EffectiveMaskDto masks = CreateMasks(
            subjectExpertise: new Dictionary<string, short[]>
            {
                [SubjectMaskNames.Mathematics] = [MathematicsExpertise.Calculus],
            });

        ChatNavDto nav = _service.GetAccessibleNav(masks);

        ChatNavCategoryDto mathematics = Assert.Single(nav.Categories);
        Assert.Equal("Mathematics", mathematics.Name);
        Assert.Equal("Calculus", mathematics.Rooms[0].Name);
    }

    [Fact]
    public void No_expertise_bits_shows_no_subject_categories()
    {
        ChatNavDto nav = _service.GetAccessibleNav(CreateMasks());

        Assert.DoesNotContain(nav.Categories, c => c.Key != ChatRoomCatalog.StaffCategoryKey);
    }

    [Fact]
    public void Tutor_role_shows_staff_category_with_tutors_room()
    {
        EffectiveMaskDto masks = CreateMasks(roles: [PlatformRoles.Tutor]);

        ChatNavDto nav = _service.GetAccessibleNav(masks);

        ChatNavCategoryDto staff = Assert.Single(nav.Categories);
        Assert.Equal("Staff", staff.Name);
        Assert.Contains(staff.Rooms, room => room.Name == "Tutors");
    }

    [Fact]
    public void Owner_sees_all_subject_and_staff_rooms()
    {
        EffectiveMaskDto masks = CreateMasks(roles: [PlatformRoles.Owner]);

        ChatNavDto nav = _service.GetAccessibleNav(masks);

        Assert.True(_service.CanAccessAllRooms(masks));
        Assert.True(nav.Categories.Count > ChatRoomCatalog.StaffRooms.Count);
        Assert.Contains(nav.Categories, c => c.Key == SubjectMaskNames.Mathematics);
        Assert.Contains(nav.Categories, c => c.Key == ChatRoomCatalog.StaffCategoryKey);
    }

    [Fact]
    public void Administrator_sees_all_rooms()
    {
        EffectiveMaskDto masks = CreateMasks(roles: [PlatformRoles.Administrator]);

        Assert.True(_service.CanAccessAllRooms(masks));
        Assert.True(_service.GetAccessibleNav(masks).Categories.Count > 1);
    }

    [Fact]
    public void General_subject_bit_without_expertise_does_not_open_category()
    {
        EffectiveMaskDto masks = CreateMasks(generalSubjects: [GeneralSubjects.Science]);

        ChatNavDto nav = _service.GetAccessibleNav(masks);

        Assert.DoesNotContain(nav.Categories, c => c.Key == SubjectMaskNames.Science);
    }

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
