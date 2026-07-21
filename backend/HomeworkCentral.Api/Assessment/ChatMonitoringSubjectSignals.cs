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

    private static readonly (string Needle, string Mask)[] SubjectNeedles =
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
        float fallbackChannelRelevance = .5f)
    {
        List<string> applied = appliedGenerals
            .Where(s => GeneralIndex(s) >= 0)
            .Select(CanonicalMask)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        float countNorm = Math.Clamp(applied.Count / 6f, 0f, 1f);
        if (string.IsNullOrWhiteSpace(channelGeneral) || GeneralIndex(channelGeneral) < 0)
        {
            return new SubjectSignalSnapshot(applied, null, 0f, 0f, 0f, countNorm,
                Math.Clamp(fallbackChannelRelevance, 0f, 1f), 1f);
        }

        string channel = CanonicalMask(channelGeneral);
        bool exact = applied.Any(s => string.Equals(s, channel, StringComparison.OrdinalIgnoreCase));
        float maxRelated = 0f;
        float crossSupport = 0f;
        foreach (string subject in applied)
        {
            float rel = PairRelatedness(subject, channel);
            if (string.Equals(subject, channel, StringComparison.OrdinalIgnoreCase))
                continue;
            if (rel > crossSupport) crossSupport = rel;
            if (rel > maxRelated) maxRelated = rel;
        }

        float exactMatch = exact ? 1f : 0f;
        float relatedMatch = exact ? crossSupport : maxRelated;
        // Exact channel match gets a cross-subject boost (Math+Science applicant in Physics/Science).
        float effective = exact
            ? Math.Clamp(.85f + .15f * crossSupport, 0f, 1f)
            : relatedMatch >= .7f ? .55f + .3f * relatedMatch
            : relatedMatch >= .35f ? .3f + .25f * relatedMatch
            : .1f;
        float rewardScale = exact
            ? Math.Clamp(.9f + .2f * crossSupport, 0f, 1.15f)
            : relatedMatch >= .7f ? .65f
            : relatedMatch >= .35f ? .4f
            : .15f;

        return new SubjectSignalSnapshot(applied, channel, exactMatch, relatedMatch, crossSupport, countNorm, effective, rewardScale);
    }

    public static SubjectSignalSnapshot ResolveFromTicket(TicketUserWatch watch, string? roomId, float fallbackChannelRelevance = .5f)
    {
        IReadOnlyList<string> applied = ParseAppliedSubjects(
            watch.Ticket.FilterName,
            watch.Ticket.TrackingTemplateJson,
            watch.Ticket.Portal.TrackingInstructions,
            watch.ContextLabel);
        string? channel = ResolveChannelSubject(roomId);
        return Resolve(applied, channel, fallbackChannelRelevance);
    }

    public static SubjectSignalSnapshot ResolveFromSynthetic(
        string category,
        string requirement,
        string channel,
        float messageChannelRelevance)
    {
        List<string> applied = ParseSubjectsFromText($"{category} {requirement}").ToList();
        if (applied.Count == 0)
        {
            string fromCategory = ChatMonitoringCategoryTaxonomy.NormalizeCategory(NeuralModelKindChatMonitoring.Tutoring, category);
            string? mask = TutoringSlugToMask(fromCategory);
            if (mask is not null) applied.Add(mask);
        }

        string? channelGeneral = ResolveChannelSubject(channel);
        return Resolve(applied, channelGeneral, messageChannelRelevance);
    }

    public static IReadOnlyList<string> ParseAppliedSubjects(params string?[] texts)
    {
        List<string> found = [];
        foreach (string? text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            foreach (string subject in ParseSubjectsFromText(text))
            {
                if (!found.Contains(subject, StringComparer.OrdinalIgnoreCase))
                    found.Add(subject);
            }

            if (text.Contains("tutor-subjects", StringComparison.OrdinalIgnoreCase)
                || text.Contains("\"intake\"", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(text);
                    foreach (string subject in ExtractTutorSubjectsFromTemplate(document.RootElement))
                    {
                        if (!found.Contains(subject, StringComparer.OrdinalIgnoreCase))
                            found.Add(subject);
                    }
                }
                catch (JsonException)
                {
                    // free-text parse already ran
                }
            }
        }

        return found;
    }

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
        foreach ((string needle, string mask) in SubjectNeedles)
        {
            if (lower.Contains(needle, StringComparison.Ordinal))
                return mask;
        }

        if (lower.Contains("lounge", StringComparison.Ordinal) || lower is "general")
            return null;
        return null;
    }

    private static IEnumerable<string> ExtractTutorSubjectsFromTemplate(JsonElement root)
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
            string text = answer.ValueKind switch
            {
                JsonValueKind.String => answer.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(" ", answer.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())),
                _ => answer.ToString(),
            };
            foreach (string subject in ParseSubjectsFromText(text))
                yield return subject;
        }
    }

    private static IEnumerable<string> ParseSubjectsFromText(string text)
    {
        string lower = $" {text.ToLowerInvariant()} ";
        HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string needle, string mask) in SubjectNeedles)
        {
            if (found.Contains(SubjectMaskNames.ComputerScience) && mask == SubjectMaskNames.Science && needle == "science")
                continue;
            if (ContainsToken(lower, needle))
                found.Add(mask);
        }

        return found;
    }

    private static bool ContainsToken(string paddedLowerHaystack, string needle)
    {
        int index = 0;
        while ((index = paddedLowerHaystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            char before = paddedLowerHaystack[index - 1];
            char after = paddedLowerHaystack[index + needle.Length];
            bool boundaryBefore = !char.IsLetterOrDigit(before);
            bool boundaryAfter = !char.IsLetterOrDigit(after);
            // "math" must not match inside "mathematics" — require boundary after unless needle is a prefix phrase.
            if (boundaryBefore && (boundaryAfter || needle.Length > 4))
                return true;
            index += needle.Length;
        }

        return false;
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
}

public sealed record SubjectSignalSnapshot(
    IReadOnlyList<string> AppliedGenerals,
    string? ChannelGeneral,
    float ExactMatch,
    float RelatedMatch,
    float CrossSubjectSupport,
    float AppliedCountNorm,
    float EffectiveChannelRelevance,
    float RewardScale);
