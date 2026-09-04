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

        if (!TryNormalizeCategory(kind, category, out string normalized))
            return false;

        // A recognised name can still belong to the other lineage's vocabulary, so confirm it
        // actually sits on this axis before reporting an index.
        IReadOnlyList<string> labels = For(kind);
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
        TryNormalizeCategory(kind, category, out string normalized);
        return normalized;
    }

    /// <summary>
    /// Normalization that reports whether the name was actually recognised. Both vocabularies end
    /// in a catch-all, so normalization alone cannot distinguish "this is the general bucket" from
    /// "nothing matched, here is the general bucket" — the recognition and the fallback are the
    /// same string. Callers that must not treat an unknown name as a real label need the flag, so
    /// it is produced here rather than reconstructed by comparing against the catch-all.
    /// </summary>
    private static bool TryNormalizeCategory(
        NeuralModelKindChatMonitoring kind,
        string category,
        out string normalized)
    {
        string raw = category.Trim();
        string value = raw.ToLowerInvariant();
        if (kind == NeuralModelKindChatMonitoring.Moderation)
            return TryNormalizeModerationCategory(value, out normalized);

        return TryNormalizeTutoringCategory(raw, value, out normalized);
    }

    private static bool TryNormalizeTutoringCategory(string raw, string value, out string normalized)
    {
        string? recognized =
            TryNormalizeExactSubject(raw)
            ?? TryNormalizeTutoringAlias(value)
            ?? (Tutoring.Any(label => string.Equals(label, value, StringComparison.Ordinal)) ? value : null)
            ?? TryFindSubjectSlugInText(value);

        if (recognized is not null)
        {
            normalized = recognized;
            return true;
        }

        // A "tutoring-" prefix is passed through unchanged for the lenient path, but it is a shape,
        // not a match: the stem may name no subject at all, so it is not a recognition.
        normalized = value.StartsWith("tutoring-", StringComparison.Ordinal) ? value : "tutoring-competency";
        return false;
    }

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

    private static bool TryNormalizeModerationCategory(string value, out string normalized)
    {
        // Reaching the catch-all through a legacy alias ("general", "profanity") is a genuine
        // match; reaching it at the end of this method is not.
        string mapped = ChatMonitoringModerationConcepts.MapLegacyBroadLabel(value);
        if (string.Equals(mapped, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal))
        {
            normalized = ChatMonitoringModerationConcepts.CatchAll;
            return true;
        }

        if (ChatMonitoringModerationConcepts.TryGet(mapped, out _))
        {
            normalized = mapped;
            return true;
        }

        string? exactSlug = ChatMonitoringModerationConcepts.Slugs
            .FirstOrDefault(slug => string.Equals(slug, value, StringComparison.OrdinalIgnoreCase));
        if (exactSlug is not null)
        {
            normalized = exactSlug;
            return true;
        }

        // Prefer the longest embedded slug so "hate-speech-harassment" wins over "hate".
        string? embeddedSlug = ChatMonitoringModerationConcepts.Slugs
            .Where(slug => value.Contains(slug, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(slug => slug.Length)
            .FirstOrDefault();
        if (embeddedSlug is not null)
        {
            normalized = embeddedSlug;
            return true;
        }

        normalized = ChatMonitoringModerationConcepts.CatchAll;
        return false;
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
