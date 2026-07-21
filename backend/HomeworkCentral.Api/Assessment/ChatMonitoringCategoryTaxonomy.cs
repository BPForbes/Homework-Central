using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Fixed category vocabularies for each chat-monitor lineage. Softmax over these classes
/// is the 3Blue1Brown multi-class path; keyword heuristics remain only as a bootstrap labeler
/// when no trained category target is supplied.
/// Tutoring categories mirror every Mask-C general subject in <see cref="SubjectExpertiseCatalog"/>.
/// </summary>
public static class ChatMonitoringCategoryTaxonomy
{
    public static readonly string[] Moderation =
    [
        "spam",
        "profanity",
        "threat",
        "harassment",
        "evasion",
        "moderation-general",
    ];

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

    public static string Label(NeuralModelKindChatMonitoring kind, int index)
    {
        IReadOnlyList<string> labels = For(kind);
        if (index < 0 || index >= labels.Count)
            return labels[^1];
        return labels[index];
    }

    /// <summary>Maps legacy / free-text category strings onto the current tutoring slug set.</summary>
    public static string NormalizeCategory(NeuralModelKindChatMonitoring kind, string category)
    {
        string raw = category.Trim();
        string value = raw.ToLowerInvariant();
        if (kind != NeuralModelKindChatMonitoring.Tutoring)
            return value;

        // Exact Mask-C subject names first (avoids "ComputerScience" → "science").
        foreach (SubjectExpertiseCategory subject in SubjectExpertiseCatalog.Categories
            .OrderByDescending(s => s.ExpertiseMaskName.Length))
        {
            if (string.Equals(subject.ExpertiseMaskName, raw, StringComparison.OrdinalIgnoreCase))
                return SubjectToTutoringSlug(subject.ExpertiseMaskName);
        }

        if (value is "tutoring-math" or "math" or "mathematics" or "algebra" or "calculus" or "quadratic")
            return "tutoring-mathematics";
        if (value is "tutoring-english" or "english" or "writing" or "essay" or "language" or "languages")
            return "tutoring-languages";
        if (value is "cs" or "compsci" or "computer science" or "computer-science" or "computerscience"
            or "programming" or "coding")
            return "tutoring-computer-science";

        if (Tutoring.Any(label => string.Equals(label, value, StringComparison.Ordinal)))
            return value;

        // Longest subject-stem containment (computer science before science).
        string? bestSlug = null;
        int bestLength = -1;
        foreach (SubjectExpertiseCategory subject in SubjectExpertiseCatalog.Categories)
        {
            string slug = SubjectToTutoringSlug(subject.ExpertiseMaskName);
            string stem = slug["tutoring-".Length..];
            string spaced = stem.Replace('-', ' ');
            string compact = stem.Replace("-", "", StringComparison.Ordinal);
            if ((value.Contains(spaced, StringComparison.Ordinal)
                    || value.Contains(stem, StringComparison.Ordinal)
                    || value.Contains(compact, StringComparison.Ordinal))
                && stem.Length > bestLength)
            {
                bestLength = stem.Length;
                bestSlug = slug;
            }
        }

        if (bestSlug is not null)
            return bestSlug;

        return value.StartsWith("tutoring-", StringComparison.Ordinal)
            ? value
            : "tutoring-competency";
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
