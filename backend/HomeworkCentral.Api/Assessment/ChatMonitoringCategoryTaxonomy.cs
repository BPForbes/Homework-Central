using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Fixed category vocabularies for each chat-monitor lineage. Softmax over these classes
/// is the 3Blue1Brown multi-class path; keyword heuristics remain only as a bootstrap labeler
/// when no trained category target is supplied.
/// Tutoring categories mirror every Mask-C general subject in <see cref="SubjectExpertiseCatalog"/>.
/// Moderation categories are the fine-grained concepts in <see cref="ChatMonitoringModerationConcepts"/>.
/// </summary>
public static class ChatMonitoringCategoryTaxonomy
{
    /// <summary>
    /// 100 precise moderation concepts + catch-all. Prefer fine slugs (e.g. payment-solicitation)
    /// over legacy broad labels (spam, profanity, …) which <see cref="NormalizeCategory"/> remaps.
    /// </summary>
    public static readonly string[] Moderation = ChatMonitoringModerationConcepts.SoftmaxLabels.ToArray();

    /// <summary>
    /// One softmax class per claimable general subject, plus a competency catch-all.
    /// Slugs stay stable for checkpoints: tutoring-{kebab-case subject}.
    /// </summary>
    public static readonly string[] Tutoring =
    [
        "tutoring-mathematics",
        "tutoring-science",
        "tutoring-computer-science",
        "tutoring-languages",
        "tutoring-history",
        "tutoring-business",
        "tutoring-art",
        "tutoring-music",
        "tutoring-engineering",
        "tutoring-medicine",
        "tutoring-finance",
        "tutoring-economics",
        "tutoring-education",
        "tutoring-competency",
    ];

    public static IReadOnlyList<string> For(NeuralModelKindChatMonitoring kind) =>
        kind == NeuralModelKindChatMonitoring.Tutoring ? Tutoring : Moderation;

    public static int IndexOf(NeuralModelKindChatMonitoring kind, string? category)
    {
        IReadOnlyList<string> labels = For(kind);
        if (string.IsNullOrWhiteSpace(category))
            return labels.Count - 1;

        string normalized = NormalizeCategory(kind, category);
        for (int i = 0; i < labels.Count; i++)
        {
            if (string.Equals(labels[i], normalized, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return labels.Count - 1;
    }

    /// <summary>
    /// Strict lookup. Unlike <see cref="IndexOf"/>, an unrecognised category reports failure rather
    /// than falling back to the general bucket. That distinction matters for teacher-supplied
    /// distributions: a hallucinated category name silently resolved to "general" would dump its
    /// probability mass onto a real label, which is worse than dropping it.
    /// </summary>
    public static bool TryIndexOf(NeuralModelKindChatMonitoring kind, string? category, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(category))
            return false;

        IReadOnlyList<string> labels = For(kind);
        string normalized = NormalizeCategory(kind, category);
        for (int i = 0; i < labels.Count; i++)
        {
            if (!string.Equals(labels[i], normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            index = i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Projects a sparse, name-keyed teacher distribution onto this taxonomy's axis, or returns
    /// null when nothing usable survives.
    ///
    /// Name-keyed rather than positional because that is what an LLM can actually produce: the
    /// moderation taxonomy has a hundred labels, and asking for a hundred-element array in order
    /// invites silent misalignment, whereas naming the two or three categories that apply is a
    /// request a model can satisfy reliably.
    ///
    /// Weights are left unnormalised here; the model normalises when it consumes them, so there is
    /// exactly one place where that rule lives.
    /// </summary>
    public static float[]? BuildDistribution(
        NeuralModelKindChatMonitoring kind,
        IReadOnlyDictionary<string, double>? weights)
    {
        if (weights is null || weights.Count == 0)
            return null;

        float[] distribution = new float[For(kind).Count];
        bool anyUsable = false;
        foreach (KeyValuePair<string, double> weight in weights)
        {
            if (double.IsNaN(weight.Value) || double.IsInfinity(weight.Value) || weight.Value <= 0)
                continue;

            if (!TryIndexOf(kind, weight.Key, out int index))
                continue;

            // Summed rather than assigned: two aliases of one category (say "harassment" and a
            // legacy spelling that normalises onto it) contribute together instead of one silently
            // overwriting the other.
            distribution[index] += (float)weight.Value;
            anyUsable = true;
        }

        return anyUsable ? distribution : null;
    }

    public static string Label(NeuralModelKindChatMonitoring kind, int index)
    {
        IReadOnlyList<string> labels = For(kind);
        if (index < 0 || index >= labels.Count)
            return labels[^1];
        return labels[index];
    }

    /// <summary>Maps free-text / legacy category strings onto the current softmax vocabulary.</summary>
    public static string NormalizeCategory(NeuralModelKindChatMonitoring kind, string category)
    {
        string raw = category.Trim();
        string value = raw.ToLowerInvariant();
        return kind switch
        {
            NeuralModelKindChatMonitoring.Moderation => NormalizeModerationCategory(value),
            _ => NormalizeTutoringCategory(raw, value),
        };
    }

    private static string NormalizeTutoringCategory(string raw, string value) =>
        (
            TryNormalizeExactSubject(raw),
            TryNormalizeTutoringAlias(value),
            Tutoring.Any(label => string.Equals(label, value, StringComparison.Ordinal)) ? value : null,
            TryFindSubjectSlugInText(value)
        ) switch
        {
            (string exactSubjectSlug, _, _, _) => exactSubjectSlug,
            (null, string aliasSlug, _, _) => aliasSlug,
            (null, null, string tutoringLabel, _) => tutoringLabel,
            (null, null, null, string textSlug) => textSlug,
            _ when value.StartsWith("tutoring-", StringComparison.Ordinal) => value,
            _ => "tutoring-competency",
        };

    private static string? TryNormalizeExactSubject(string raw) =>
        SubjectExpertiseCatalog.Categories
            .OrderByDescending(subject => subject.ExpertiseMaskName.Length)
            .Where(subject => string.Equals(subject.ExpertiseMaskName, raw, StringComparison.OrdinalIgnoreCase))
            .Select(subject => SubjectToTutoringSlug(subject.ExpertiseMaskName))
            .FirstOrDefault();

    private static string? TryNormalizeTutoringAlias(string value) => value switch
    {
        "tutoring-math" or "math" or "mathematics" or "algebra" or "calculus" or "quadratic"
            => "tutoring-mathematics",
        "tutoring-english" or "english" or "writing" or "essay" or "language" or "languages"
            => "tutoring-languages",
        "cs" or "compsci" or "computer science" or "computer-science" or "computerscience"
            or "programming" or "coding"
            => "tutoring-computer-science",
        _ => null,
    };

    private static string? TryFindSubjectSlugInText(string value) =>
        SubjectExpertiseCatalog.Categories
            .Select(subject =>
            {
                string slug = SubjectToTutoringSlug(subject.ExpertiseMaskName);
                string stem = slug["tutoring-".Length..];
                string spaced = stem.Replace('-', ' ');
                string compact = stem.Replace("-", "", StringComparison.Ordinal);
                bool matched = value.Contains(spaced, StringComparison.Ordinal)
                    || value.Contains(stem, StringComparison.Ordinal)
                    || value.Contains(compact, StringComparison.Ordinal);
                return (Slug: slug, StemLength: stem.Length, Matched: matched);
            })
            .Where(candidate => candidate.Matched)
            .OrderByDescending(candidate => candidate.StemLength)
            .Select(candidate => candidate.Slug)
            .FirstOrDefault();

    private static string NormalizeModerationCategory(string value)
    {
        string mapped = ChatMonitoringModerationConcepts.MapLegacyBroadLabel(value);
        if (string.Equals(mapped, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal))
            return ChatMonitoringModerationConcepts.CatchAll;
        if (ChatMonitoringModerationConcepts.TryGet(mapped, out _))
            return mapped;

        string? exactSlug = ChatMonitoringModerationConcepts.Slugs
            .FirstOrDefault(slug => string.Equals(slug, value, StringComparison.OrdinalIgnoreCase));
        if (exactSlug is not null)
            return exactSlug;

        // Prefer the longest embedded slug so "hate-speech-harassment" wins over "hate".
        return ChatMonitoringModerationConcepts.Slugs
            .Where(slug => value.Contains(slug, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(slug => slug.Length)
            .FirstOrDefault()
            ?? ChatMonitoringModerationConcepts.CatchAll;
    }

    public static string SubjectToTutoringSlug(string subjectMaskName) => subjectMaskName switch
    {
        SubjectMaskNames.Mathematics => "tutoring-mathematics",
        SubjectMaskNames.Science => "tutoring-science",
        SubjectMaskNames.ComputerScience => "tutoring-computer-science",
        SubjectMaskNames.Languages => "tutoring-languages",
        SubjectMaskNames.History => "tutoring-history",
        SubjectMaskNames.Business => "tutoring-business",
        SubjectMaskNames.Art => "tutoring-art",
        SubjectMaskNames.Music => "tutoring-music",
        SubjectMaskNames.Engineering => "tutoring-engineering",
        SubjectMaskNames.Medicine => "tutoring-medicine",
        SubjectMaskNames.Finance => "tutoring-finance",
        SubjectMaskNames.Economics => "tutoring-economics",
        SubjectMaskNames.Education => "tutoring-education",
        _ => "tutoring-competency",
    };
}
