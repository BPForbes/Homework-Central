namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Picks the next synthetic-ticket category from the full filterable taxonomy.
/// Prefers least-covered labels so moderation training cannot collapse onto
/// prompt-primed concepts such as payment-solicitation.
/// </summary>
public sealed class SyntheticConceptCoverageSampler
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random;

    public SyntheticConceptCoverageSampler(int seed) =>
        _random = new Random(seed);

    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>
    /// Selects and records the next target slug for <paramref name="mode"/>.
    /// In <see cref="NeuralTrainingMode.Both"/>, lineages alternate by ticket index
    /// so moderation and tutoring each receive full-vocabulary coverage.
    /// </summary>
    public string NextTarget(NeuralTrainingMode mode, int ticketIndex)
    {
        NeuralModelKindChatMonitoring kind = ResolveKind(mode, ticketIndex);
        string chosen = PickLeastCovered(ChatMonitoringCategoryTaxonomy.For(kind));
        Record(chosen);
        return chosen;
    }

    public void Record(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return;
        _counts[category] = _counts.GetValueOrDefault(category) + 1;
    }

    /// <summary>Lowest-count labels for generator hints (coverage steering without overfit).</summary>
    public IReadOnlyList<string> Underrepresented(NeuralModelKindChatMonitoring kind, int take = 5)
    {
        IReadOnlyList<string> labels = ChatMonitoringCategoryTaxonomy.For(kind);
        int min = labels.Min(slug => _counts.GetValueOrDefault(slug));
        return labels
            .Where(slug => _counts.GetValueOrDefault(slug) == min)
            .OrderBy(slug => slug, StringComparer.Ordinal)
            .Take(Math.Clamp(take, 1, 12))
            .ToList();
    }

    private string PickLeastCovered(IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
            throw new InvalidOperationException("Category taxonomy is empty.");

        int min = labels.Min(slug => _counts.GetValueOrDefault(slug));
        List<string> candidates = labels
            .Where(slug => _counts.GetValueOrDefault(slug) == min)
            .ToList();
        return candidates[_random.Next(candidates.Count)];
    }

    private static NeuralModelKindChatMonitoring ResolveKind(NeuralTrainingMode mode, int ticketIndex) =>
        mode switch
        {
            NeuralTrainingMode.Moderation => NeuralModelKindChatMonitoring.Moderation,
            NeuralTrainingMode.Tutoring => NeuralModelKindChatMonitoring.Tutoring,
            // Odd tickets → moderation fine concepts; even → tutoring subjects.
            _ => ticketIndex % 2 == 1
                ? NeuralModelKindChatMonitoring.Moderation
                : NeuralModelKindChatMonitoring.Tutoring,
        };
}
