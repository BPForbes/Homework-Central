using HomeworkCentral.Api.Tickets.Preface;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Compatibility facade over <see cref="TutorSubjectPrefaceCheck"/>. Prefer injecting
/// <see cref="ITicketPrefaceCheck"/> / <see cref="ITicketPrefaceCheckResolver"/> for new code.
/// </summary>
public static class TutorSubjectTextProcessor
{
    public const string TutorSubjectsQuestionId = TutorSubjectPrefaceCheck.QuestionIdValue;

    public static ProcessResult ProcessStrict(string? freeText) =>
        Map(TutorSubjectPrefaceCheck.Instance.ProcessStrict(freeText));

    public static ProcessResult ProcessLenient(string? freeText) =>
        Map(TutorSubjectPrefaceCheck.Instance.ProcessLenient(freeText));

    public static ProcessResult Process(string? freeText, bool requireAllVerified) =>
        requireAllVerified ? ProcessStrict(freeText) : ProcessLenient(freeText);

    public static SubjectExtraction ExtractSubjects(string? freeText)
    {
        TicketPrefaceExtraction extraction = TutorSubjectPrefaceCheck.Instance.Extract(freeText);
        return new SubjectExtraction(
            extraction.Categories,
            extraction.SpecificLabels,
            extraction.Hits.Select(h => new SubjectHit(h.Category, h.Label, h.IsSpecific, h.MatchedKey, h.RawToken)).ToList());
    }

    public static IReadOnlyList<string> ExtractGeneralMasks(string? freeText) =>
        ExtractSubjects(freeText).GeneralMasks;

    public static IReadOnlyList<string> ExtractExpertiseLabels(string? freeText) =>
        ExtractSubjects(freeText).ExpertiseLabels;

    private static ProcessResult Map(TicketPrefaceResult result) =>
        new(
            result.Ok,
            result.Categories,
            result.SpecificLabels,
            result.CanonicalDisplay,
            result.ErrorMessage,
            result.Tokens.Select(t => new SubjectTokenResult(
                t.RawToken, t.NormalizedToken, t.Category, t.Label, t.Verified, t.IsSpecific, t.FailureReason)).ToList());

    public sealed record SubjectHit(
        string GeneralMask,
        string Label,
        bool IsSpecificExpertise,
        string MatchedKey,
        string? RawToken = null);

    public sealed record SubjectExtraction(
        IReadOnlyList<string> GeneralMasks,
        IReadOnlyList<string> ExpertiseLabels,
        IReadOnlyList<SubjectHit> Hits)
    {
        public static SubjectExtraction Empty { get; } = new([], [], []);
    }

    public sealed record SubjectTokenResult(
        string RawToken,
        string NormalizedToken,
        string? GeneralMask,
        string? MatchedLabel,
        bool Verified,
        bool IsSpecificExpertise,
        string? FailureReason);

    public sealed record ProcessResult(
        bool Ok,
        IReadOnlyList<string> GeneralMasks,
        IReadOnlyList<string> ExpertiseLabels,
        string CanonicalDisplay,
        string? ErrorMessage,
        IReadOnlyList<SubjectTokenResult> Tokens);
}
