using System.Text.Json;
using HomeworkCentral.Api.Tickets.Preface;

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
        string? concept = ResolveReportedConcept(reportedConcept)
                          ?? ResolveConceptFromTexts(texts)
                          ?? ParseConceptFromTexts(texts);
        IReadOnlyList<string> related = concept is null || string.Equals(concept, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal)
            ? []
            : ChatMonitoringModerationConcepts.RelatedConcepts(concept);
        string? family = concept is null ? null : ChatMonitoringModerationConcepts.FamilyOf(concept);
        float[] familyHot = BuildFamilyMultiHot(family);
        float[] textFamily = ScoreFamiliesFromText(string.Join(' ', texts.Where(t => !string.IsNullOrWhiteSpace(t))));
        float exact = CalculateExactFamilyMatch(family, textFamily);
        float overlap = CalculateRelatedOverlap(related, texts);

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

    private static string? ResolveReportedConcept(string? reportedConcept)
    {
        if (string.IsNullOrWhiteSpace(reportedConcept))
            return null;

        string normalized = ChatMonitoringCategoryTaxonomy.NormalizeCategory(
            NeuralModelKindChatMonitoring.Moderation,
            reportedConcept);

        return IsKnownConcept(normalized) ? normalized : null;
    }

    private static string? ResolveConceptFromTexts(IReadOnlyList<string?> texts)
    {
        // Preface checks encode verified intake concepts before broad slug fallback.
        foreach (string? text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            string? extractedConcept = ExtractPrefacePrimaryCategory(text);
            if (extractedConcept is not null)
                return extractedConcept;

            string? templateConcept = TryParseTemplatePrefaceCategory(text);
            if (templateConcept is not null)
                return templateConcept;
        }

        return null;
    }

    private static string? ExtractPrefacePrimaryCategory(string text)
    {
        TicketPrefaceExtraction extraction = ModerationConceptPrefaceCheck.Instance.Extract(text);
        return string.IsNullOrWhiteSpace(extraction.PrimaryCategory)
            ? null
            : extraction.PrimaryCategory;
    }

    private static string? TryParseTemplatePrefaceCategory(string text)
    {
        if (!text.Contains("prefaceCategory", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("prefaceCategory", out JsonElement categoryElement)
                || categoryElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(categoryElement.GetString()))
            {
                return null;
            }

            string fromTemplate = ChatMonitoringCategoryTaxonomy.NormalizeCategory(
                NeuralModelKindChatMonitoring.Moderation,
                categoryElement.GetString()!);
            return IsKnownConcept(fromTemplate) ? fromTemplate : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsKnownConcept(string concept) =>
        ChatMonitoringModerationConcepts.TryGet(concept, out _)
        || string.Equals(concept, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal);

    private static string? ParseConceptFromTexts(IReadOnlyList<string?> texts)
    {
        string haystack = string.Join(' ', texts.Where(t => !string.IsNullOrWhiteSpace(t))).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(haystack))
            return null;

        string[] atoms = haystack
            .Split([' ', '\t', '\r', '\n', '/', '_', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(token => token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(atom => atom.Length > 0)
            .ToArray();

        if (atoms.Length == 0)
            return null;

        string? best = null;
        int bestLength = -1;
        const int maxSlugParts = 8;
        int maxWindow = Math.Min(maxSlugParts, atoms.Length);

        for (int windowSize = maxWindow; windowSize >= 1; windowSize--)
        {
            for (int start = 0; start <= atoms.Length - windowSize; start++)
            {
                string candidate = windowSize == 1
                    ? atoms[start]
                    : string.Join('-', atoms.AsSpan(start, windowSize));
                if (candidate.Length <= bestLength)
                    continue;

                if (!ChatMonitoringModerationConcepts.TryGet(candidate, out _))
                    continue;

                best = candidate;
                bestLength = candidate.Length;
            }
        }

        return best;
    }

    private static float[] BuildFamilyMultiHot(string? family)
    {
        float[] familyHot = new float[FamilyCount];
        if (family is null)
            return familyHot;

        int index = FamilyIndex(family);
        if (index >= 0)
            familyHot[index] = 1f;

        return familyHot;
    }

    private static float CalculateExactFamilyMatch(string? family, IReadOnlyList<float> textFamily)
    {
        if (family is null)
            return 0f;

        int familyIndex = FamilyIndex(family);
        return familyIndex >= 0 && textFamily[familyIndex] >= .5f ? 1f : 0f;
    }

    private static float CalculateRelatedOverlap(IReadOnlyList<string> related, IReadOnlyList<string?> texts)
    {
        if (related.Count == 0)
            return 0f;

        string haystack = string.Join(' ', texts).ToLowerInvariant();
        int hits = related.Count(slug => haystack.Contains(slug, StringComparison.Ordinal));
        return Math.Clamp(hits / (float)related.Count, 0f, 1f);
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
