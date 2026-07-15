using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Tests.Services;

/// <summary>
/// Covers <see cref="EffectiveMaskService.BuildSubjectMasks"/>: a course assignment must set only
/// that course's expertise bit and never promote to the parent subject's general bit, since the
/// general bit unlocks every room under the subject (see ChatRoomAccessService.HasSubjectExpertise).
/// </summary>
public class EffectiveMaskSubjectMaskTests
{
    private static Subject TopLevel(string name, short bitIndex) => new()
    {
        SubjectId = AuthorizationGuids.Subject(SubjectMaskNames.General, bitIndex),
        SubjectMask = SubjectMaskNames.General,
        BitIndex = bitIndex,
        Name = name,
        ParentSubjectId = null,
    };

    private static Subject CourseOf(Subject parent, string name, string expertiseMask, short bitIndex) => new()
    {
        SubjectId = AuthorizationGuids.Subject(expertiseMask, bitIndex),
        SubjectMask = expertiseMask,
        BitIndex = bitIndex,
        Name = name,
        ParentSubjectId = parent.SubjectId,
        ParentSubject = parent,
    };

    [Fact]
    public void Course_assignment_sets_only_its_expertise_bit()
    {
        Subject science = TopLevel("Science", GeneralSubjects.Science);
        Subject biology = CourseOf(science, "Biology", SubjectMaskNames.Science, ScienceExpertise.Biology);

        (System.Collections.BitArray general, Dictionary<string, System.Collections.BitArray> expertise) =
            EffectiveMaskService.BuildSubjectMasks([biology]);

        Assert.True(BitMask.HasBit(expertise[SubjectMaskNames.Science], ScienceExpertise.Biology));
        Assert.False(BitMask.HasBit(general, GeneralSubjects.Science));
    }

    [Fact]
    public void Course_assignment_does_not_set_sibling_expertise_bits()
    {
        Subject science = TopLevel("Science", GeneralSubjects.Science);
        Subject biology = CourseOf(science, "Biology", SubjectMaskNames.Science, ScienceExpertise.Biology);

        (_, Dictionary<string, System.Collections.BitArray> expertise) =
            EffectiveMaskService.BuildSubjectMasks([biology]);

        Assert.False(BitMask.HasBit(expertise[SubjectMaskNames.Science], ScienceExpertise.Chemistry));
        Assert.False(BitMask.HasBit(expertise[SubjectMaskNames.Science], ScienceExpertise.Physics));
    }

    [Fact]
    public void TopLevel_subject_assignment_sets_only_the_general_bit()
    {
        Subject science = TopLevel("Science", GeneralSubjects.Science);

        (System.Collections.BitArray general, Dictionary<string, System.Collections.BitArray> expertise) =
            EffectiveMaskService.BuildSubjectMasks([science]);

        Assert.True(BitMask.HasBit(general, GeneralSubjects.Science));
        Assert.False(BitMask.HasBit(expertise[SubjectMaskNames.Science], ScienceExpertise.Biology));
    }

    [Fact]
    public void Course_and_subject_assignments_combine_independently()
    {
        Subject science = TopLevel("Science", GeneralSubjects.Science);
        Subject mathematics = TopLevel("Mathematics", GeneralSubjects.Mathematics);
        Subject biology = CourseOf(science, "Biology", SubjectMaskNames.Science, ScienceExpertise.Biology);

        (System.Collections.BitArray general, Dictionary<string, System.Collections.BitArray> expertise) =
            EffectiveMaskService.BuildSubjectMasks([mathematics, biology]);

        Assert.True(BitMask.HasBit(general, GeneralSubjects.Mathematics));
        Assert.True(BitMask.HasBit(expertise[SubjectMaskNames.Science], ScienceExpertise.Biology));
        Assert.False(BitMask.HasBit(general, GeneralSubjects.Science));
    }

    [Fact]
    public void Every_expertise_category_gets_an_empty_mask_when_nothing_is_assigned()
    {
        (System.Collections.BitArray general, Dictionary<string, System.Collections.BitArray> expertise) =
            EffectiveMaskService.BuildSubjectMasks([]);

        Assert.All(SubjectExpertiseCatalog.AllExpertiseCategoryNames(), category =>
            Assert.Contains(category, expertise.Keys));
        Assert.DoesNotContain(true, general.Cast<bool>());
    }
}
