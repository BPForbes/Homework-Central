using System.Text;
using System.Text.RegularExpressions;

namespace HomeworkCentral.Api.Tickets.Preface;

/// <summary>
/// Shared deterministic preface engine: tokenize, lowercase, alias map, Levenshtein spell-check.
/// Tutor and mod specializations only supply vocabulary + policy (strict vs lenient, rewrite).
/// Custom ticket types inherit this class and register via DI.
/// </summary>
public abstract partial class VocabularyTicketPrefaceCheck : ITicketPrefaceCheck
{
    private readonly Lazy<Vocabulary> _vocab;
    private static readonly Regex TokenSplitter = TokenSplitterPattern();

    protected VocabularyTicketPrefaceCheck()
    {
        _vocab = new Lazy<Vocabulary>(BuildVocabulary);
    }

    public abstract string CheckId { get; }
    public abstract string QuestionId { get; }
    public abstract string? FilterName { get; }
    public abstract TicketPrefaceMode Mode { get; }
    public abstract bool RewriteAnswerOnSuccess { get; }

    /// <summary>User-facing noun for errors (e.g. "subject", "moderation concept").</summary>
    protected abstract string CategoryNoun { get; }

    /// <summary>Examples shown when asking the user to re-enter.</summary>
    protected abstract string ReenterExamples { get; }

    /// <summary>When categories are general-only, how to display them in CanonicalDisplay.</summary>
    protected virtual string DisplayNameForCategory(string category) => category;

    public TicketPrefaceResult Process(string? freeText) =>
        ProcessCore(freeText, requireAllVerified: Mode == TicketPrefaceMode.Strict);

    public TicketPrefaceResult ProcessStrict(string? freeText) =>
        ProcessCore(freeText, requireAllVerified: true);

    public TicketPrefaceResult ProcessLenient(string? freeText) =>
        ProcessCore(freeText, requireAllVerified: false);

    public TicketPrefaceExtraction Extract(string? freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return TicketPrefaceExtraction.Empty;

        PrefaceExtractionBuilder builder = new();
        AddVerifiedTokenHits(freeText, builder);
        AddPhraseHits(freeText, builder);
        return builder.Build();
    }

    private void AddVerifiedTokenHits(string freeText, PrefaceExtractionBuilder builder)
    {
        foreach (string raw in SplitTokens(freeText))
        {
            TicketPrefaceTokenResult token = ResolveToken(raw);
            if (!token.Verified
                || string.IsNullOrWhiteSpace(token.Category)
                || string.IsNullOrWhiteSpace(token.Label))
            {
                continue;
            }

            builder.Add(
                new VocabEntry(token.Category, token.Label, token.IsSpecific),
                token.NormalizedToken,
                token.RawToken);
        }
    }

    private void AddPhraseHits(string freeText, PrefaceExtractionBuilder builder)
    {
        string normalized = NormalizeKey(freeText);
        if (normalized.Length == 0)
            return;

        Dictionary<string, VocabEntry> exact = _vocab.Value.Exact;
        Dictionary<string, VocabEntry> phraseHits = new(StringComparer.Ordinal);

        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int windowSize = words.Length; windowSize >= 1; windowSize--)
        {
            for (int start = 0; start <= words.Length - windowSize; start++)
            {
                string phrase = windowSize == 1
                    ? words[start]
                    : string.Join(' ', words.AsSpan(start, windowSize));
                if (phrase.Length < 3)
                    continue;

                if (exact.TryGetValue(phrase, out VocabEntry? spacedEntry) && spacedEntry is not null)
                    phraseHits.TryAdd(phrase, spacedEntry);
            }
        }

        string compact = Compact(normalized);
        if (compact.Length >= 3
            && exact.TryGetValue(compact, out VocabEntry? compactEntry)
            && compactEntry is not null)
        {
            phraseHits.TryAdd(compact, compactEntry);
        }

        foreach ((string key, VocabEntry entry) in phraseHits
                     .OrderByDescending(pair => pair.Key.Length)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Add(entry, key);
        }
    }

    protected abstract void RegisterVocabulary(VocabularyBuilder builder);

    private Vocabulary BuildVocabulary()
    {
        VocabularyBuilder builder = new();
        RegisterVocabulary(builder);
        return new Vocabulary(builder.Entries);
    }

    private TicketPrefaceResult ProcessCore(string? freeText, bool requireAllVerified)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            return FailEmpty(requireAllVerified);
        }

        List<string> rawTokens = SplitTokens(freeText);
        if (rawTokens.Count == 0)
            return FailEmpty(requireAllVerified);

        return requireAllVerified switch
        {
            true => ProcessStrictTokens(rawTokens),
            false => ProcessLenientNarrative(freeText, rawTokens),
        };
    }

    private TicketPrefaceResult ProcessStrictTokens(IReadOnlyList<string> rawTokens)
    {
        // Strict intake fields accept only verified vocabulary tokens; unknown
        // subjects reject intake instead of becoming ambiguous tracking context.
        // See docs/tickets.md#intake-validation-and-preface-checks.
        TokenResolutionSummary summary = ResolveTokens(rawTokens);
        if (summary.Failures.Count > 0)
        {
            string detail = string.Join(" ", summary.Failures);
            return new TicketPrefaceResult(
                Ok: false,
                Categories: [],
                SpecificLabels: [],
                PrimaryCategory: null,
                CanonicalDisplay: string.Empty,
                ErrorMessage:
                    $"{detail} Please re-enter your {CategoryNoun}(s) using known topics "
                    + $"(for example: {ReenterExamples}). Separate multiple with commas.",
                Tokens: summary.TokenResults);
        }

        if (summary.Categories.Count == 0)
        {
            return new TicketPrefaceResult(
                Ok: false,
                Categories: [],
                SpecificLabels: [],
                PrimaryCategory: null,
                CanonicalDisplay: string.Empty,
                ErrorMessage:
                    $"None of the entered {CategoryNoun}s could be verified. Please re-enter using known topics "
                    + $"(for example: {ReenterExamples}).",
                Tokens: summary.TokenResults);
        }

        string display = summary.DisplayLabels.Count > 0
            ? string.Join(", ", summary.DisplayLabels)
            : string.Join(", ", summary.Categories.Select(DisplayNameForCategory));

        return BuildSuccessfulResult(summary, display);
    }

    private TicketPrefaceResult ProcessLenientNarrative(string freeText, IReadOnlyList<string> rawTokens)
    {
        // Lenient intake fields preserve the user's narrative while extracting
        // recognized moderation concepts for tracking. See
        // docs/tickets.md#intake-validation-and-preface-checks.
        TokenResolutionSummary summary = ResolveTokens(rawTokens);
        if (summary.Categories.Count == 0)
        {
            TicketPrefaceExtraction extraction = Extract(freeText);
            if (extraction.Categories.Count > 0)
            {
                return new TicketPrefaceResult(
                    Ok: true,
                    Categories: extraction.Categories,
                    SpecificLabels: extraction.SpecificLabels,
                    PrimaryCategory: extraction.PrimaryCategory,
                    CanonicalDisplay: freeText.Trim(),
                    ErrorMessage: null,
                    Tokens: summary.TokenResults);
            }

            return new TicketPrefaceResult(
                Ok: true,
                Categories: [],
                SpecificLabels: [],
                PrimaryCategory: null,
                CanonicalDisplay: freeText.Trim(),
                ErrorMessage: null,
                Tokens: summary.TokenResults);
        }

        MergeNarrativeHits(freeText, summary.Categories, summary.Specifics);
        return BuildSuccessfulResult(summary, freeText.Trim());
    }

    private TokenResolutionSummary ResolveTokens(IReadOnlyList<string> rawTokens)
    {
        List<TicketPrefaceTokenResult> tokenResults = [];
        List<string> categories = [];
        List<string> specifics = [];
        List<string> displayLabels = [];
        List<string> failures = [];

        foreach (string raw in rawTokens)
        {
            TicketPrefaceTokenResult resolved = ResolveToken(raw);
            tokenResults.Add(resolved);
            if (!resolved.Verified)
            {
                failures.Add(resolved.FailureReason ?? $"Could not verify “{raw}”.");
                continue;
            }

            if (!categories.Contains(resolved.Category!, StringComparer.OrdinalIgnoreCase))
                categories.Add(resolved.Category!);
            if (!string.IsNullOrWhiteSpace(resolved.Label)
                && !displayLabels.Contains(resolved.Label, StringComparer.OrdinalIgnoreCase))
            {
                displayLabels.Add(resolved.Label);
            }

            if (resolved.IsSpecific
                && !string.IsNullOrWhiteSpace(resolved.Label)
                && !specifics.Contains(resolved.Label, StringComparer.OrdinalIgnoreCase))
            {
                specifics.Add(resolved.Label);
            }
        }

        return new TokenResolutionSummary(tokenResults, categories, specifics, displayLabels, failures);
    }

    private void MergeNarrativeHits(string freeText, List<string> categories, List<string> specifics)
    {
        TicketPrefaceExtraction extraction = Extract(freeText);
        foreach (string category in extraction.Categories)
        {
            if (!categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                categories.Add(category);
        }

        foreach (string label in extraction.SpecificLabels)
        {
            if (!specifics.Contains(label, StringComparer.OrdinalIgnoreCase))
                specifics.Add(label);
        }
    }

    private static TicketPrefaceResult BuildSuccessfulResult(TokenResolutionSummary summary, string display)
    {
        return new TicketPrefaceResult(
            Ok: true,
            Categories: summary.Categories,
            SpecificLabels: summary.Specifics,
            PrimaryCategory: summary.Categories[0],
            CanonicalDisplay: display,
            ErrorMessage: null,
            Tokens: summary.TokenResults);
    }

    private TicketPrefaceResult FailEmpty(bool requireAllVerified) =>
        new(
            Ok: !requireAllVerified,
            Categories: [],
            SpecificLabels: [],
            PrimaryCategory: null,
            CanonicalDisplay: string.Empty,
            ErrorMessage: requireAllVerified
                ? $"Please enter at least one {CategoryNoun}."
                : null,
            Tokens: []);

    private TicketPrefaceTokenResult ResolveToken(string raw)
    {
        string normalized = NormalizeKey(raw);
        if (normalized is not { Length: > 0 })
        {
            return CreateUnverifiedTokenResult(
                raw,
                normalized,
                $"Could not verify “{raw.Trim()}” (empty after normalizing).");
        }

        Vocabulary vocab = _vocab.Value;
        if (TryResolveExactToken(raw, normalized, vocab) is TicketPrefaceTokenResult exactResult)
            return exactResult;

        int maxDistance = MaxEditDistance(normalized.Length);
        return maxDistance switch
        {
            <= 0 => CreateUnverifiedTokenResult(
                raw,
                normalized,
                $"Could not verify “{raw.Trim()}”. Please re-enter that {CategoryNoun}."),
            _ => ResolveFuzzyToken(raw, normalized, vocab, maxDistance),
        };
    }

    private static TicketPrefaceTokenResult? TryResolveExactToken(string raw, string normalized, Vocabulary vocab)
    {
        return vocab.Exact.TryGetValue(normalized, out VocabEntry? exact) && exact is not null
            ? new TicketPrefaceTokenResult(
                raw,
                normalized,
                exact.Category,
                exact.Label,
                true,
                exact.IsSpecific,
                null)
            : null;
    }

    private TicketPrefaceTokenResult ResolveFuzzyToken(
        string raw,
        string normalized,
        Vocabulary vocab,
        int maxDistance)
    {
        List<(VocabEntry Entry, int Distance)> candidates = [];
        foreach ((string key, VocabEntry entry) in vocab.Exact)
        {
            if (Math.Abs(key.Length - normalized.Length) > maxDistance)
                continue;
            int distance = Levenshtein(normalized, key);
            if (distance > 0 && distance <= maxDistance)
                candidates.Add((entry, distance));
        }

        if (candidates.Count == 0)
        {
            return CreateUnverifiedTokenResult(
                raw,
                normalized,
                $"Could not verify “{raw.Trim()}”. Please re-enter that {CategoryNoun}.");
        }

        int best = candidates.Min(c => c.Distance);
        List<VocabEntry> bestEntries = candidates
            .Where(c => c.Distance == best)
            .Select(c => c.Entry)
            .DistinctBy(e => $"{e.Category}:{e.Label}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> distinctCategories = bestEntries
            .Select(e => e.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctCategories.Count > 1)
        {
            string options = string.Join(" or ", bestEntries.Select(e => e.Label).Distinct(StringComparer.OrdinalIgnoreCase));
            return CreateUnverifiedTokenResult(
                raw,
                normalized,
                $"Could not verify “{raw.Trim()}” (ambiguous — did you mean {options}?). Please re-enter that {CategoryNoun}.");
        }

        VocabEntry match = bestEntries.OrderByDescending(e => e.IsSpecific).First();
        return new TicketPrefaceTokenResult(
            raw, normalized, match.Category, match.Label, true, match.IsSpecific, null);
    }

    private static TicketPrefaceTokenResult CreateUnverifiedTokenResult(
        string raw,
        string normalized,
        string failureReason) =>
        new(raw, normalized, null, null, false, false, failureReason);

    private static List<string> SplitTokens(string freeText)
    {
        string[] parts = TokenSplitter.Split(freeText);
        List<string> tokens = parts
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (tokens.Count > 0)
            return tokens;

        string single = freeText.Trim();
        return string.IsNullOrEmpty(single) ? [] : [single];
    }

    protected static string NormalizeKey(string value)
    {
        string lower = value.Trim().ToLowerInvariant();
        lower = lower
            .Replace("c++", "cplusplus", StringComparison.Ordinal)
            .Replace("c#", "csharp", StringComparison.Ordinal)
            .Replace(".net", "dotnet", StringComparison.Ordinal)
            .Replace("node.js", "nodejs", StringComparison.Ordinal);

        StringBuilder builder = new(lower.Length);
        char prev = '\0';
        foreach (char ch in lower.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                prev = ch;
            }
            else if (ch is ' ' or '-' or '_' or '/' or '\\' or '&' or '+')
            {
                if (prev != ' ' && builder.Length > 0)
                {
                    builder.Append(' ');
                    prev = ' ';
                }
            }
        }

        return Regex.Replace(builder.ToString().Trim(), @"\s+", " ");
    }

    protected static string Compact(string normalized) =>
        normalized.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static int MaxEditDistance(int length) => length switch
    {
        <= 3 => 0,
        <= 5 => 1,
        _ => 2,
    };

    private static int Levenshtein(string a, string b)
    {
        int n = a.Length;
        int m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        Span<int> prev = stackalloc int[m + 1];
        Span<int> curr = stackalloc int[m + 1];
        for (int j = 0; j <= m; j++)
            prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            Span<int> swap = prev;
            prev = curr;
            curr = swap;
        }

        return prev[m];
    }

    [GeneratedRegex(@"[,;|/]+|\s+&\s+|\s+and\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenSplitterPattern();

    private sealed record TokenResolutionSummary(
        List<TicketPrefaceTokenResult> TokenResults,
        List<string> Categories,
        List<string> Specifics,
        List<string> DisplayLabels,
        List<string> Failures);

    protected sealed record VocabEntry(string Category, string Label, bool IsSpecific);

    private sealed class PrefaceExtractionBuilder
    {
        private readonly List<string> _categories = [];
        private readonly List<string> _specifics = [];
        private readonly List<TicketPrefaceHit> _hits = [];

        public void Add(VocabEntry entry, string matchedKey, string? rawToken = null)
        {
            if (!_categories.Contains(entry.Category, StringComparer.OrdinalIgnoreCase))
                _categories.Add(entry.Category);

            if (entry.IsSpecific
                && !_specifics.Contains(entry.Label, StringComparer.OrdinalIgnoreCase))
            {
                _specifics.Add(entry.Label);
            }

            if (ContainsHit(entry))
                return;

            _hits.Add(new TicketPrefaceHit(entry.Category, entry.Label, entry.IsSpecific, matchedKey, rawToken));
        }

        public TicketPrefaceExtraction Build()
        {
            string? primary = _categories.Count > 0 ? _categories[0] : null;
            return new TicketPrefaceExtraction(_categories, _specifics, _hits, primary);
        }

        private bool ContainsHit(VocabEntry entry) =>
            _hits.Any(hit =>
                string.Equals(hit.Category, entry.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(hit.Label, entry.Label, StringComparison.OrdinalIgnoreCase));
    }

    protected sealed class VocabularyBuilder
    {
        private readonly Dictionary<string, VocabEntry> _exact = new(StringComparer.Ordinal);

        public void Add(string alias, string category, string label, bool isSpecific = false)
        {
            string key = NormalizeKey(alias);
            if (string.IsNullOrWhiteSpace(key))
                return;

            Register(key, category, label, isSpecific);
            string compact = Compact(key);
            if (!string.Equals(compact, key, StringComparison.Ordinal))
                Register(compact, category, label, isSpecific);
        }

        private void Register(string key, string category, string label, bool isSpecific)
        {
            if (_exact.TryGetValue(key, out VocabEntry? existing) && existing is not null)
            {
                if (!string.Equals(existing.Category, category, StringComparison.OrdinalIgnoreCase))
                    return;
                if (!existing.IsSpecific && isSpecific)
                    _exact[key] = new VocabEntry(category, label, true);
                return;
            }

            _exact[key] = new VocabEntry(category, label, isSpecific);
        }

        internal IReadOnlyDictionary<string, VocabEntry> Entries => _exact;
    }

    private sealed class Vocabulary
    {
        public Vocabulary(IReadOnlyDictionary<string, VocabEntry> exact)
        {
            Exact = exact.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        }

        public Dictionary<string, VocabEntry> Exact { get; }
    }
}
