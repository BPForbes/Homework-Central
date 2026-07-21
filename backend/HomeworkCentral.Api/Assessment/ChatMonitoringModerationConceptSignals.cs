namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Hypothesis / related-concept signals for the moderation cascade stage-1 router.
/// Example report: reportedConcept=payment-solicitation with related tip-pressure, off-platform-payment, …
/// </summary>
public static class ChatMonitoringModerationConceptSignals
{
    public static readonly string[] FamilyOrder =
    [
        ChatMonitoringModerationConcepts.Families.Financial,
        ChatMonitoringModerationConcepts.Families.Fraud,
        ChatMonitoringModerationConcepts.Families.Privacy,
        ChatMonitoringModerationConcepts.Families.Sexual,
        ChatMonitoringModerationConcepts.Families.MinorSafety,
        ChatMonitoringModerationConcepts.Families.PhysicalSafety,
        ChatMonitoringModerationConcepts.Families.Cybersecurity,
        ChatMonitoringModerationConcepts.Families.PlatformAbuse,
        ChatMonitoringModerationConcepts.Families.Discrimination,
        ChatMonitoringModerationConcepts.Families.Misinformation,
    ];

    public const int FamilyCount = 10;

    private static readonly (string Needle, string Family)[] FamilyNeedles =
    [
        ("payment", ChatMonitoringModerationConcepts.Families.Financial),
        ("tip", ChatMonitoringModerationConcepts.Families.Financial),
        ("paypal", ChatMonitoringModerationConcepts.Families.Financial),
        ("cash app", ChatMonitoringModerationConcepts.Families.Financial),
        ("venmo", ChatMonitoringModerationConcepts.Families.Financial),
        ("impersonat", ChatMonitoringModerationConcepts.Families.Fraud),
        ("phish", ChatMonitoringModerationConcepts.Families.Fraud),
        ("scam", ChatMonitoringModerationConcepts.Families.Fraud),
        ("doxx", ChatMonitoringModerationConcepts.Families.Privacy),
        ("personal data", ChatMonitoringModerationConcepts.Families.Privacy),
        ("address", ChatMonitoringModerationConcepts.Families.Privacy),
        ("sexual", ChatMonitoringModerationConcepts.Families.Sexual),
        ("sextort", ChatMonitoringModerationConcepts.Families.Sexual),
        ("nude", ChatMonitoringModerationConcepts.Families.Sexual),
        ("minor", ChatMonitoringModerationConcepts.Families.MinorSafety),
        ("groom", ChatMonitoringModerationConcepts.Families.MinorSafety),
        ("underage", ChatMonitoringModerationConcepts.Families.MinorSafety),
        ("violent", ChatMonitoringModerationConcepts.Families.PhysicalSafety),
        ("weapon", ChatMonitoringModerationConcepts.Families.PhysicalSafety),
        ("stalk", ChatMonitoringModerationConcepts.Families.PhysicalSafety),
        ("extort", ChatMonitoringModerationConcepts.Families.PhysicalSafety),
        ("blackmail", ChatMonitoringModerationConcepts.Families.PhysicalSafety),
        ("malware", ChatMonitoringModerationConcepts.Families.Cybersecurity),
        ("password", ChatMonitoringModerationConcepts.Families.Cybersecurity),
        ("credential", ChatMonitoringModerationConcepts.Families.Cybersecurity),
        ("hack", ChatMonitoringModerationConcepts.Families.Cybersecurity),
        ("brigad", ChatMonitoringModerationConcepts.Families.PlatformAbuse),
        ("sockpuppet", ChatMonitoringModerationConcepts.Families.PlatformAbuse),
        ("false report", ChatMonitoringModerationConcepts.Families.PlatformAbuse),
        ("harass", ChatMonitoringModerationConcepts.Families.Discrimination),
        ("dehumaniz", ChatMonitoringModerationConcepts.Families.Discrimination),
        ("dogpil", ChatMonitoringModerationConcepts.Families.Discrimination),
        ("misinformation", ChatMonitoringModerationConcepts.Families.Misinformation),
        ("disinformation", ChatMonitoringModerationConcepts.Families.Misinformation),
    ];

    public static int FamilyIndex(string family)
    {
        for (int i = 0; i < FamilyOrder.Length; i++)
        {
            if (string.Equals(FamilyOrder[i], family, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public static ModerationConceptSnapshot Resolve(string? reportedConcept, params string?[] texts)
    {
        string? concept = null;
        if (!string.IsNullOrWhiteSpace(reportedConcept))
        {
            string normalized = ChatMonitoringCategoryTaxonomy.NormalizeCategory(
                NeuralModelKindChatMonitoring.Moderation, reportedConcept);
            if (ChatMonitoringModerationConcepts.TryGet(normalized, out _)
                || string.Equals(normalized, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal))
                concept = normalized;
        }

        concept ??= ParseConceptFromTexts(texts);
        IReadOnlyList<string> related = concept is null || string.Equals(concept, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal)
            ? []
            : ChatMonitoringModerationConcepts.RelatedConcepts(concept);
        string? family = concept is null ? null : ChatMonitoringModerationConcepts.FamilyOf(concept);
        float[] familyHot = new float[FamilyCount];
        if (family is not null)
        {
            int index = FamilyIndex(family);
            if (index >= 0) familyHot[index] = 1f;
        }

        float[] textFamily = ScoreFamiliesFromText(string.Join(' ', texts.Where(t => !string.IsNullOrWhiteSpace(t))));
        float exact = 0f;
        float overlap = 0f;
        if (family is not null)
        {
            int familyIdx = FamilyIndex(family);
            if (familyIdx >= 0 && textFamily[familyIdx] >= .5f)
                exact = 1f;
        }

        if (related.Count > 0)
        {
            string haystack = string.Join(' ', texts).ToLowerInvariant();
            int hits = related.Count(slug => haystack.Contains(slug, StringComparison.Ordinal));
            overlap = Math.Clamp(hits / (float)related.Count, 0f, 1f);
        }

        return new ModerationConceptSnapshot(
            concept,
            family,
            related,
            concept is null ? 0f : 1f,
            Math.Clamp(related.Count / 8f, 0f, 1f),
            exact,
            overlap,
            familyHot,
            textFamily);
    }

    public static ModerationConceptSnapshot ResolveFromSynthetic(string category, string requirement, string message) =>
        Resolve(category, requirement, message);

    public static ModerationConceptSnapshot ResolveFromTicketTexts(params string?[] texts) =>
        Resolve(null, texts);

    private static string? ParseConceptFromTexts(IReadOnlyList<string?> texts)
    {
        string haystack = string.Join(' ', texts.Where(t => !string.IsNullOrWhiteSpace(t))).ToLowerInvariant();
        string? best = null;
        int bestLength = -1;
        foreach (string slug in ChatMonitoringModerationConcepts.Slugs)
        {
            if (haystack.Contains(slug, StringComparison.Ordinal) && slug.Length > bestLength)
            {
                best = slug;
                bestLength = slug.Length;
            }
        }

        return best;
    }

    private static float[] ScoreFamiliesFromText(string text)
    {
        float[] scores = new float[FamilyCount];
        if (string.IsNullOrWhiteSpace(text)) return scores;
        string lower = text.ToLowerInvariant();
        foreach ((string needle, string family) in FamilyNeedles)
        {
            if (!lower.Contains(needle, StringComparison.Ordinal)) continue;
            int index = FamilyIndex(family);
            if (index >= 0) scores[index] = Math.Max(scores[index], 1f);
        }

        return scores;
    }
}

public sealed record ModerationConceptSnapshot(
    string? ReportedConcept,
    string? ReportedFamily,
    IReadOnlyList<string> RelatedConcepts,
    float HasHypothesis,
    float RelatedCountNorm,
    float ExactFamilyMatch,
    float RelatedOverlap,
    IReadOnlyList<float> FamilyMultiHot,
    IReadOnlyList<float> TextFamilyScores);
