namespace HomeworkCentral.Api.Tickets.Preface;

/// <summary>
/// Resolves preface checks by intake question id (primary) and optional portal filter name.
/// Custom ticket portals register additional <see cref="ITicketPrefaceCheck"/> implementations
/// in DI; they are discovered automatically here.
/// </summary>
public sealed class TicketPrefaceCheckResolver(IEnumerable<ITicketPrefaceCheck> checks) : ITicketPrefaceCheckResolver
{
    private readonly IReadOnlyList<ITicketPrefaceCheck> _checks = checks.ToList();

    public IReadOnlyList<ITicketPrefaceCheck> All => _checks;

    public ITicketPrefaceCheck? Resolve(string questionId, string? filterName = null)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return null;

        List<ITicketPrefaceCheck> byQuestion = _checks
            .Where(c => string.Equals(c.QuestionId, questionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byQuestion.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(filterName))
        {
            ITicketPrefaceCheck? filterMatch = byQuestion.FirstOrDefault(c =>
                c.FilterName is not null
                && string.Equals(c.FilterName, filterName, StringComparison.OrdinalIgnoreCase));
            if (filterMatch is not null)
                return filterMatch;
        }

        // Prefer unbound (custom-portal-friendly) checks, then first registered.
        return byQuestion.FirstOrDefault(c => c.FilterName is null) ?? byQuestion[0];
    }
}
