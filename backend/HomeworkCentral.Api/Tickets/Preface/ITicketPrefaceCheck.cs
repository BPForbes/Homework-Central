namespace HomeworkCentral.Api.Tickets.Preface;

/// <summary>
/// Strict intake rejects unverified tokens; lenient extraction keeps verified hits and skips unknowns
/// (used for narrative mod report reasons and monitoring prose).
/// </summary>
public enum TicketPrefaceMode
{
    Strict,
    Lenient,
}

/// <summary>
/// Preface check run before ticket creation / used to extract structured categories from free text.
/// Tutor subjects and mod report concepts share one vocabulary engine; custom portals can register
/// additional <see cref="ITicketPrefaceCheck"/> implementations via DI.
/// </summary>
public interface ITicketPrefaceCheck
{
    /// <summary>Stable id for logging / diagnostics (e.g. tutor-subjects, moderation-concepts).</summary>
    string CheckId { get; }

    /// <summary>Intake question id this check binds to (e.g. tutor-subjects, report-reason).</summary>
    string QuestionId { get; }

    /// <summary>
    /// Optional portal filter name binding (Tutor, Mod-Mail). Null means question-id only —
    /// preferred for custom portals that reuse the same question id.
    /// </summary>
    string? FilterName { get; }

    TicketPrefaceMode Mode { get; }

    /// <summary>When true and processing succeeds, intake rewrites the answer to CanonicalDisplay.</summary>
    bool RewriteAnswerOnSuccess { get; }

    /// <summary>Process according to <see cref="Mode"/>.</summary>
    TicketPrefaceResult Process(string? freeText);

    /// <summary>Always lenient structured extraction for monitoring / cascade inputs.</summary>
    TicketPrefaceExtraction Extract(string? freeText);
}

public interface ITicketPrefaceCheckResolver
{
    /// <summary>Resolve a check for an intake question, optionally scoped by portal filter name.</summary>
    ITicketPrefaceCheck? Resolve(string questionId, string? filterName = null);

    IReadOnlyList<ITicketPrefaceCheck> All { get; }
}

public sealed record TicketPrefaceHit(
    string Category,
    string Label,
    bool IsSpecific,
    string MatchedKey,
    string? RawToken = null);

/// <summary>Lenient extraction for monitoring / cascade inputs; never blocks ticket intake.</summary>
public sealed record TicketPrefaceExtraction(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> SpecificLabels,
    IReadOnlyList<TicketPrefaceHit> Hits,
    string? PrimaryCategory)
{
    public static TicketPrefaceExtraction Empty { get; } = new([], [], [], null);
}

public sealed record TicketPrefaceTokenResult(
    string RawToken,
    string NormalizedToken,
    string? Category,
    string? Label,
    bool Verified,
    bool IsSpecific,
    string? FailureReason);

/// <summary>
/// When <see cref="Ok"/> is false, ticket open fails with <see cref="ErrorMessage"/>.
/// On success, intake may rewrite the answer to <see cref="CanonicalDisplay"/>.
/// </summary>
public sealed record TicketPrefaceResult(
    bool Ok,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> SpecificLabels,
    string? PrimaryCategory,
    string CanonicalDisplay,
    string? ErrorMessage,
    IReadOnlyList<TicketPrefaceTokenResult> Tokens);
