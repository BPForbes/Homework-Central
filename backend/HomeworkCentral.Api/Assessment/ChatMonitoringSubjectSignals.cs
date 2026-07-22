using System.Text.Json;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Multi-subject tutor application signals: which Mask-C subjects were applied for,
/// which subject channel the message arrived in, and relatedness (e.g. Physics/Science
/// strongly related to Mathematics) used for channel relevance / reward scaling.
/// </summary>
public static class ChatMonitoringSubjectSignals
{
    public const int GeneralSubjectCount = 13;

    private static readonly string[] GeneralOrder =
    [
        SubjectMaskNames.Mathematics,
        SubjectMaskNames.Science,
        SubjectMaskNames.ComputerScience,
        SubjectMaskNames.Languages,
        SubjectMaskNames.History,
        SubjectMaskNames.Business,
        SubjectMaskNames.Art,
        SubjectMaskNames.Music,
        SubjectMaskNames.Engineering,
        SubjectMaskNames.Medicine,
        SubjectMaskNames.Finance,
        SubjectMaskNames.Economics,
        SubjectMaskNames.Education,
    ];

    /// <summary>Symmetric relatedness on Mask-C generals (0 = none, 1 = same subject).</summary>
    private static readonly Dictionary<(string, string), float> Relatedness = new()
    {
        { Key(SubjectMaskNames.Mathematics, SubjectMaskNames.Science), .85f },
        { Key(SubjectMaskNames.Mathematics, SubjectMaskNames.Engineering), .8f },
        { Key(SubjectMaskNames.Mathematics, SubjectMaskNames.ComputerScience), .75f },
        { Key(SubjectMaskNames.Mathematics, SubjectMaskNames.Economics), .55f },
        { Key(SubjectMaskNames.Mathematics, SubjectMaskNames.Finance), .5f },
        { Key(SubjectMaskNames.Science, SubjectMaskNames.Engineering), .65f },
        { Key(SubjectMaskNames.Science, SubjectMaskNames.Medicine), .7f },
        { Key(SubjectMaskNames.Science, SubjectMaskNames.ComputerScience), .45f },
        { Key(SubjectMaskNames.ComputerScience, SubjectMaskNames.Engineering), .7f },
        { Key(SubjectMaskNames.Business, SubjectMaskNames.Economics), .8f },
        { Key(SubjectMaskNames.Business, SubjectMaskNames.Finance), .85f },
        { Key(SubjectMaskNames.Economics, SubjectMaskNames.Finance), .85f },
        { Key(SubjectMaskNames.Art, SubjectMaskNames.Music), .55f },
        { Key(SubjectMaskNames.Education, SubjectMaskNames.Languages), .4f },
        { Key(SubjectMaskNames.Education, SubjectMaskNames.Mathematics), .35f },
        { Key(SubjectMaskNames.History, SubjectMaskNames.Languages), .35f },
        { Key(SubjectMaskNames.History, SubjectMaskNames.Education), .4f },
        { Key(SubjectMaskNames.Medicine, SubjectMaskNames.Education), .3f },
    };

    /// <summary>
    /// Channel-id needles only (ordered longest-first). Free-text subject parsing uses
    /// <see cref="TutorSubjectTextProcessor"/> so aliases like biology→Science and rust→ComputerScience apply.
    /// </summary>
    private static readonly (string Needle, string Mask)[] ChannelNeedles =
    [
        ("computer science", SubjectMaskNames.ComputerScience),
        ("computerscience", SubjectMaskNames.ComputerScience),
        ("comp sci", SubjectMaskNames.ComputerScience),
        ("mathematics", SubjectMaskNames.Mathematics),
        ("engineering", SubjectMaskNames.Engineering),
        ("economics", SubjectMaskNames.Economics),
        ("education", SubjectMaskNames.Education),
        ("medicine", SubjectMaskNames.Medicine),
        ("languages", SubjectMaskNames.Languages),
        ("language", SubjectMaskNames.Languages),
        ("business", SubjectMaskNames.Business),
        ("finance", SubjectMaskNames.Finance),
        ("history", SubjectMaskNames.History),
        ("science", SubjectMaskNames.Science),
        ("physics", SubjectMaskNames.Science),
        ("chemistry", SubjectMaskNames.Science),
        ("biology", SubjectMaskNames.Science),
        ("english", SubjectMaskNames.Languages),
        ("spanish", SubjectMaskNames.Languages),
        ("music", SubjectMaskNames.Music),
        ("math", SubjectMaskNames.Mathematics),
        ("art", SubjectMaskNames.Art),
    ];

    public static IReadOnlyList<string> GeneralSubjectsInOrder => GeneralOrder;

    public static int GeneralIndex(string subjectMaskName)
    {
        for (int i = 0; i < GeneralOrder.Length; i++)
        {
            if (string.Equals(GeneralOrder[i], subjectMaskName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public static float PairRelatedness(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return 1f;
        return Relatedness.TryGetValue(Key(left, right), out float value) ? value : 0f;
    }

    public static SubjectSignalSnapshot Resolve(
        IReadOnlyList<string> appliedGenerals,
        string? channelGeneral,
        float fallbackChannelRelevance = .5f,
        IReadOnlyList<string>? appliedExpertise = null)
    {
        List<string> applied = NormalizeAppliedGenerals(appliedGenerals);
        List<string> expertise = NormalizeAppliedExpertise(appliedExpertise);
        float countNorm = Math.Clamp(applied.Count / 6f, 0f, 1f);
        if (string.IsNullOrWhiteSpace(channelGeneral) || GeneralIndex(channelGeneral) < 0)
            return BuildNoChannelSnapshot(applied, expertise, countNorm, fallbackChannelRelevance);

        string channel = CanonicalMask(channelGeneral);
        SubjectChannelMatch channelMatch = CalculateChannelMatch(applied, channel);
        return new SubjectSignalSnapshot(
            applied,
            expertise,
            channel,
            channelMatch.ExactMatch,
            channelMatch.RelatedMatch,
            channelMatch.CrossSubjectSupport,
            countNorm,
            channelMatch.EffectiveChannelRelevance,
            channelMatch.RewardScale);
    }

    private static List<string> NormalizeAppliedGenerals(IReadOnlyList<string> appliedGenerals) =>
        appliedGenerals
            .Where(subject => GeneralIndex(subject) >= 0)
            .Select(CanonicalMask)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> NormalizeAppliedExpertise(IReadOnlyList<string>? appliedExpertise) =>
        (appliedExpertise ?? [])
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static SubjectSignalSnapshot BuildNoChannelSnapshot(
        IReadOnlyList<string> applied,
        IReadOnlyList<string> expertise,
        float countNorm,
        float fallbackChannelRelevance) =>
        new(
            applied,
            expertise,
            null,
            0f,
            0f,
            0f,
            countNorm,
            Math.Clamp(fallbackChannelRelevance, 0f, 1f),
            1f);

    private static SubjectChannelMatch CalculateChannelMatch(IReadOnlyList<string> applied, string channel)
    {
        bool exact = applied.Any(subject => string.Equals(subject, channel, StringComparison.OrdinalIgnoreCase));
        float maxRelated = 0f;
        float crossSupport = 0f;
        foreach (string subject in applied)
        {
            float relatedness = PairRelatedness(subject, channel);
            if (string.Equals(subject, channel, StringComparison.OrdinalIgnoreCase))
                continue;

            if (relatedness > crossSupport)
                crossSupport = relatedness;
            if (relatedness > maxRelated)
                maxRelated = relatedness;
        }

        float relatedMatch = exact ? crossSupport : maxRelated;
        return new SubjectChannelMatch(
            exact ? 1f : 0f,
            relatedMatch,
            crossSupport,
            CalculateEffectiveChannelRelevance(exact, relatedMatch, crossSupport),
            CalculateRewardScale(exact, relatedMatch, crossSupport));
    }

    private static float CalculateEffectiveChannelRelevance(bool exact, float relatedMatch, float crossSupport)
    {
        if (exact)
            return Math.Clamp(.85f + .15f * crossSupport, 0f, 1f);
        if (relatedMatch >= .7f)
            return .55f + .3f * relatedMatch;
        if (relatedMatch >= .35f)
            return .3f + .25f * relatedMatch;
        return .1f;
    }

    private static float CalculateRewardScale(bool exact, float relatedMatch, float crossSupport)
    {
        if (exact)
            return Math.Clamp(.9f + .2f * crossSupport, 0f, 1.15f);
        if (relatedMatch >= .7f)
            return .65f;
        if (relatedMatch >= .35f)
            return .4f;
        return .15f;
    }

    public static SubjectSignalSnapshot ResolveFromTicket(TicketUserWatch watch, string? roomId, float fallbackChannelRelevance = .5f)
    {
        TutorSubjectTextProcessor.SubjectExtraction extraction = ParseAppliedExtraction(
            watch.Ticket.FilterName,
            watch.Ticket.TrackingTemplateJson,
            watch.Ticket.Portal.TrackingInstructions,
            watch.ContextLabel);
        string? channel = ResolveChannelSubject(roomId);
        return Resolve(extraction.GeneralMasks, channel, fallbackChannelRelevance, extraction.ExpertiseLabels);
    }

    public static SubjectSignalSnapshot ResolveFromSynthetic(
        string category,
        string requirement,
        string channel,
        float messageChannelRelevance)
    {
        TutorSubjectTextProcessor.SubjectExtraction extraction =
            TutorSubjectTextProcessor.ExtractSubjects($"{category} {requirement}");
        List<string> applied = extraction.GeneralMasks.ToList();
        List<string> expertise = extraction.ExpertiseLabels.ToList();
        if (applied.Count == 0)
        {
            string fromCategory = ChatMonitoringCategoryTaxonomy.NormalizeCategory(NeuralModelKindChatMonitoring.Tutoring, category);
            string? mask = TutoringSlugToMask(fromCategory);
            if (mask is not null) applied.Add(mask);
        }

        string? channelGeneral = ResolveChannelSubject(channel);
        return Resolve(applied, channelGeneral, messageChannelRelevance, expertise);
    }

    public static IReadOnlyList<string> ParseAppliedSubjects(params string?[] texts) =>
        ParseAppliedExtraction(texts).GeneralMasks;

    public static TutorSubjectTextProcessor.SubjectExtraction ParseAppliedExtraction(params string?[] texts)
    {
        SubjectExtractionBuilder builder = new();

        foreach (string? text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            builder.Merge(TutorSubjectTextProcessor.ExtractSubjects(text));
            MergeTemplateSubjectTexts(text, builder);
        }

        return builder.Build();
    }

    private static void MergeTemplateSubjectTexts(string text, SubjectExtractionBuilder builder)
    {
        if (!LooksLikeTrackingTemplate(text))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            foreach (string subjectText in ExtractTutorSubjectTextsFromTemplate(document.RootElement))
                builder.Merge(TutorSubjectTextProcessor.ExtractSubjects(subjectText));
        }
        catch (JsonException)
        {
            // Free-text parsing already captured any readable subject hints.
        }
    }

    private static bool LooksLikeTrackingTemplate(string text) =>
        text.Contains("tutor-subjects", StringComparison.OrdinalIgnoreCase)
        || text.Contains("\"intake\"", StringComparison.OrdinalIgnoreCase);

    public static string? ResolveChannelSubject(string? roomOrChannel)
    {
        if (string.IsNullOrWhiteSpace(roomOrChannel)) return null;
        string value = roomOrChannel.Trim();

        if (value.StartsWith("subject:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = value.Split(':', 3, StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && GeneralIndex(parts[1]) >= 0)
                return CanonicalMask(parts[1]);
        }

        string lower = value.ToLowerInvariant();
        foreach ((string needle, string mask) in ChannelNeedles)
        {
            if (lower.Contains(needle, StringComparison.Ordinal))
                return mask;
        }

        if (lower.Contains("lounge", StringComparison.Ordinal) || lower is "general")
            return null;
        return null;
    }

    private static IEnumerable<string> ExtractTutorSubjectTextsFromTemplate(JsonElement root)
    {
        if (!root.TryGetProperty("intake", out JsonElement intake) || intake.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (JsonElement item in intake.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? string.Empty
                : string.Empty;
            if (!string.Equals(id, "tutor-subjects", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!item.TryGetProperty("answer", out JsonElement answer))
                continue;
            yield return answer.ValueKind switch
            {
                JsonValueKind.String => answer.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(" ", answer.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())),
                _ => answer.ToString(),
            };
        }
    }

    private static string? TutoringSlugToMask(string slug) => slug switch
    {
        "tutoring-mathematics" => SubjectMaskNames.Mathematics,
        "tutoring-science" => SubjectMaskNames.Science,
        "tutoring-computer-science" => SubjectMaskNames.ComputerScience,
        "tutoring-languages" => SubjectMaskNames.Languages,
        "tutoring-history" => SubjectMaskNames.History,
        "tutoring-business" => SubjectMaskNames.Business,
        "tutoring-art" => SubjectMaskNames.Art,
        "tutoring-music" => SubjectMaskNames.Music,
        "tutoring-engineering" => SubjectMaskNames.Engineering,
        "tutoring-medicine" => SubjectMaskNames.Medicine,
        "tutoring-finance" => SubjectMaskNames.Finance,
        "tutoring-economics" => SubjectMaskNames.Economics,
        "tutoring-education" => SubjectMaskNames.Education,
        _ => null,
    };

    private static string CanonicalMask(string value)
    {
        foreach (string name in GeneralOrder)
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return value;
    }

    private static (string, string) Key(string left, string right)
    {
        string a = left.ToLowerInvariant();
        string b = right.ToLowerInvariant();
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }

    private sealed record SubjectChannelMatch(
        float ExactMatch,
        float RelatedMatch,
        float CrossSubjectSupport,
        float EffectiveChannelRelevance,
        float RewardScale);

    private sealed class SubjectExtractionBuilder
    {
        private readonly List<string> _generals = [];
        private readonly List<string> _expertise = [];
        private readonly List<TutorSubjectTextProcessor.SubjectHit> _hits = [];

        public void Merge(TutorSubjectTextProcessor.SubjectExtraction extraction)
        {
            foreach (string general in extraction.GeneralMasks)
            {
                if (!_generals.Contains(general, StringComparer.OrdinalIgnoreCase))
                    _generals.Add(general);
            }

            foreach (string label in extraction.ExpertiseLabels)
            {
                if (!_expertise.Contains(label, StringComparer.OrdinalIgnoreCase))
                    _expertise.Add(label);
            }

            foreach (TutorSubjectTextProcessor.SubjectHit hit in extraction.Hits)
            {
                if (!ContainsHit(hit))
                    _hits.Add(hit);
            }
        }

        public TutorSubjectTextProcessor.SubjectExtraction Build() =>
            new(_generals, _expertise, _hits);

        private bool ContainsHit(TutorSubjectTextProcessor.SubjectHit hit) =>
            _hits.Any(existing =>
                string.Equals(existing.GeneralMask, hit.GeneralMask, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Label, hit.Label, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record SubjectSignalSnapshot(
    IReadOnlyList<string> AppliedGenerals,
    IReadOnlyList<string> AppliedExpertise,
    string? ChannelGeneral,
    float ExactMatch,
    float RelatedMatch,
    float CrossSubjectSupport,
    float AppliedCountNorm,
    float EffectiveChannelRelevance,
    float RewardScale);
