namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Maps each Mask C general subject category to its dedicated expertise bitmask column/table key.
/// Every general subject in <see cref="GeneralSubjects"/> has a corresponding expertise mask.
/// </summary>
public static class SubjectExpertiseCatalog
{
    public static readonly IReadOnlyList<SubjectExpertiseCategory> Categories =
    [
        new(SubjectMaskNames.Mathematics, GeneralSubjects.Mathematics),
        new(SubjectMaskNames.Science, GeneralSubjects.Science),
        new(SubjectMaskNames.ComputerScience, GeneralSubjects.ComputerScience),
        new(SubjectMaskNames.Languages, GeneralSubjects.Languages),
        new(SubjectMaskNames.History, GeneralSubjects.History),
        new(SubjectMaskNames.Business, GeneralSubjects.Business),
        new(SubjectMaskNames.Art, GeneralSubjects.Art),
        new(SubjectMaskNames.Music, GeneralSubjects.Music),
        new(SubjectMaskNames.Engineering, GeneralSubjects.Engineering),
        new(SubjectMaskNames.Medicine, GeneralSubjects.Medicine),
        new(SubjectMaskNames.Finance, GeneralSubjects.Finance),
        new(SubjectMaskNames.Economics, GeneralSubjects.Economics),
        new(SubjectMaskNames.Education, GeneralSubjects.Education),
    ];

    private static readonly IReadOnlyDictionary<string, short> GeneralSubjectBitByExpertiseCategory =
        Categories.ToDictionary(c => c.ExpertiseMaskName, c => c.GeneralSubjectBit, StringComparer.Ordinal);

    public static bool IsExpertiseCategory(string subjectMask) =>
        subjectMask != SubjectMaskNames.General &&
        GeneralSubjectBitByExpertiseCategory.ContainsKey(subjectMask);

    public static bool TryGetGeneralSubjectBit(string expertiseCategory, out short generalSubjectBit) =>
        GeneralSubjectBitByExpertiseCategory.TryGetValue(expertiseCategory, out generalSubjectBit);

    public static IEnumerable<string> AllExpertiseCategoryNames() =>
        Categories.Select(c => c.ExpertiseMaskName);
}

public sealed record SubjectExpertiseCategory(string ExpertiseMaskName, short GeneralSubjectBit);
