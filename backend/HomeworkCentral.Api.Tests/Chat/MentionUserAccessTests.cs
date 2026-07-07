using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Tests.Chat;

/// <summary>
/// Documents the room-access gate applied to @username mentions (same rules as
/// <see cref="ChatRoomAccessService.CanAccessRoom"/> for @role / @everyone).
/// </summary>
public class MentionUserAccessTests
{
    private readonly ChatRoomAccessService _access = new(new EmptyCustomChannelStore(), new FixedAccessScopeAccessor());

    [Fact]
    public void User_with_subject_expertise_can_be_mentioned_in_that_room()
    {
        ChatRoomDefinition calculusRoom = ChatRoomCatalog.SubjectRooms
            .Single(room => room.RoomDisplayName == "Calculus");

        EffectiveMaskDto masks = CreateMasks(
            subjectExpertise: new Dictionary<string, short[]>
            {
                [SubjectMaskNames.Mathematics] = [MathematicsExpertise.Calculus],
            });

        Assert.True(_access.CanAccessRoom(masks, calculusRoom));
    }

    [Fact]
    public void User_without_subject_access_cannot_be_mentioned_in_subject_room()
    {
        ChatRoomDefinition calculusRoom = ChatRoomCatalog.SubjectRooms
            .Single(room => room.RoomDisplayName == "Calculus");

        EffectiveMaskDto masks = CreateMasks();

        Assert.False(_access.CanAccessRoom(masks, calculusRoom));
    }

    [Fact]
    public void User_who_claimed_general_subject_can_be_mentioned_in_any_room_in_category()
    {
        ChatRoomDefinition biologyRoom = ChatRoomCatalog.SubjectRooms
            .Single(room => room.RoomDisplayName == "Biology");

        EffectiveMaskDto masks = CreateMasks(generalSubjects: [GeneralSubjects.Science]);

        Assert.True(_access.CanAccessRoom(masks, biologyRoom));
    }

    [Fact]
    public void Staff_room_mention_requires_matching_role()
    {
        ChatRoomDefinition tutorsRoom = ChatRoomCatalog.StaffRooms
            .Single(room => room.RoomDisplayName == "Tutors");

        EffectiveMaskDto tutor = CreateMasks(roles: [PlatformRoles.Tutor]);
        EffectiveMaskDto student = CreateMasks(roles: [PlatformRoles.Student]);

        Assert.True(_access.CanAccessRoom(tutor, tutorsRoom));
        Assert.False(_access.CanAccessRoom(student, tutorsRoom));
    }

    private static EffectiveMaskDto CreateMasks(
        IEnumerable<short>? roles = null,
        IEnumerable<short>? generalSubjects = null,
        Dictionary<string, short[]>? subjectExpertise = null)
    {
        BitArray roleMask = BitMask.Create(64);
        foreach (short bit in roles ?? [PlatformRoles.VerifiedUser])
            BitMask.SetBit(roleMask, bit);

        BitArray generalSubjectMask = BitMask.Create(128);
        foreach (short bit in generalSubjects ?? [])
            BitMask.SetBit(generalSubjectMask, bit);

        Dictionary<string, string> expertiseMasks = SubjectExpertiseCatalog.AllExpertiseCategoryNames()
            .ToDictionary(category => category, _ => BitMask.ToBase64(BitMask.Create(128)), StringComparer.Ordinal);

        if (subjectExpertise is not null)
        {
            foreach ((string category, short[] bits) in subjectExpertise)
            {
                BitArray mask = BitMask.Create(128);
                foreach (short bit in bits)
                    BitMask.SetBit(mask, bit);
                expertiseMasks[category] = BitMask.ToBase64(mask);
            }
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
