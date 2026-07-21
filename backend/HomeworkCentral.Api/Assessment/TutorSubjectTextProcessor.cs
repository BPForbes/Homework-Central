using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Deterministic tutor-subject free-text processor: lowercase, alias map to Mask-C generals
/// (biology → Science, rust → ComputerScience), and Levenshtein spell-check against the
/// known subject/expertise vocabulary. Unverified tokens ask the applicant to re-enter.
/// Custom rooms/categories are intentionally unsupported for now.
/// </summary>
public static partial class TutorSubjectTextProcessor
{
    public const string TutorSubjectsQuestionId = "tutor-subjects";

    private static readonly Lazy<Vocabulary> Vocab = new(BuildVocabulary);

    private static readonly Regex TokenSplitter = TokenSplitterPattern();

    /// <summary>Strict intake path: every token must verify or the whole answer is rejected.</summary>
    public static ProcessResult ProcessStrict(string? freeText) =>
        Process(freeText, requireAllVerified: true);

    /// <summary>Lenient parse for monitoring/signals: keep verified generals, skip unknowns.</summary>
    public static ProcessResult ProcessLenient(string? freeText) =>
        Process(freeText, requireAllVerified: false);

    public static ProcessResult Process(string? freeText, bool requireAllVerified)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            return new ProcessResult(
                Ok: false,
                GeneralMasks: [],
                CanonicalDisplay: string.Empty,
                ErrorMessage: "Please enter at least one subject to tutor in.",
                Tokens: []);
        }

        List<string> rawTokens = SplitTokens(freeText);
        if (rawTokens.Count == 0)
        {
            return new ProcessResult(
                Ok: false,
                GeneralMasks: [],
                CanonicalDisplay: string.Empty,
                ErrorMessage: "Please enter at least one subject to tutor in.",
                Tokens: []);
        }

        List<SubjectTokenResult> tokenResults = [];
        List<string> generals = [];
        List<string> failures = [];

        foreach (string raw in rawTokens)
        {
            SubjectTokenResult resolved = ResolveToken(raw);
            tokenResults.Add(resolved);
            if (!resolved.Verified)
            {
                failures.Add(resolved.FailureReason ?? $"Could not verify “{raw}”.");
                continue;
            }

            if (!generals.Contains(resolved.GeneralMask!, StringComparer.OrdinalIgnoreCase))
                generals.Add(resolved.GeneralMask!);
        }

        if (requireAllVerified && failures.Count > 0)
        {
            string detail = string.Join(" ", failures);
            return new ProcessResult(
                Ok: false,
                GeneralMasks: [],
                CanonicalDisplay: string.Empty,
                ErrorMessage:
                    $"{detail} Please re-enter your subject(s) using known topics "
                    + "(for example: Biology, Rust, Mathematics). Separate multiple with commas.",
                Tokens: tokenResults);
        }

        if (generals.Count == 0)
        {
            return new ProcessResult(
                Ok: false,
                GeneralMasks: [],
                CanonicalDisplay: string.Empty,
                ErrorMessage:
                    "None of the entered subjects could be verified. Please re-enter using known topics "
                    + "(for example: Biology, Rust, Mathematics).",
                Tokens: tokenResults);
        }

        string display = string.Join(", ", generals.Select(DisplayNameForMask));
        return new ProcessResult(
            Ok: true,
            GeneralMasks: generals,
            CanonicalDisplay: display,
            ErrorMessage: null,
            Tokens: tokenResults);
    }

    /// <summary>
    /// Lenient extraction for monitoring/signals: verified list tokens plus vocabulary
    /// needles found inside prose (so “Tutor math and science applicant” still maps).
    /// </summary>
    public static IReadOnlyList<string> ExtractGeneralMasks(string? freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return [];

        List<string> found = [];
        foreach (string mask in ProcessLenient(freeText).GeneralMasks)
        {
            if (!found.Contains(mask, StringComparer.OrdinalIgnoreCase))
                found.Add(mask);
        }

        string padded = $" {NormalizeKey(freeText)} ";
        string paddedCompact = $" {Compact(NormalizeKey(freeText))} ";
        foreach (string key in Vocab.Value.KeysLongestFirst)
        {
            if (!Vocab.Value.Exact.TryGetValue(key, out VocabEntry? entry) || entry is null)
                continue;
            if (found.Contains(entry.GeneralMask, StringComparer.OrdinalIgnoreCase))
                continue;

            if (ContainsToken(padded, key) || ContainsToken(paddedCompact, key))
                found.Add(entry.GeneralMask);
        }

        return found;
    }

    private static bool ContainsToken(string paddedLowerHaystack, string needle)
    {
        if (string.IsNullOrEmpty(needle) || paddedLowerHaystack.Length < needle.Length + 2)
            return false;

        int index = 0;
        while ((index = paddedLowerHaystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            char before = paddedLowerHaystack[index - 1];
            char after = index + needle.Length < paddedLowerHaystack.Length
                ? paddedLowerHaystack[index + needle.Length]
                : ' ';
            bool boundaryBefore = !char.IsLetterOrDigit(before);
            bool boundaryAfter = !char.IsLetterOrDigit(after);
            if (boundaryBefore && boundaryAfter)
                return true;
            index += needle.Length;
        }

        return false;
    }

    private static SubjectTokenResult ResolveToken(string raw)
    {
        string normalized = NormalizeKey(raw);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new SubjectTokenResult(
                raw, normalized, null, null, false,
                $"Could not verify “{raw.Trim()}” (empty after normalizing).");
        }

        Vocabulary vocab = Vocab.Value;
        if (vocab.Exact.TryGetValue(normalized, out VocabEntry? exact) && exact is not null)
        {
            return new SubjectTokenResult(raw, normalized, exact.GeneralMask, exact.Label, true, null);
        }

        // Spell-check: case is already folded; Levenshtein covers typos only.
        int maxDistance = MaxEditDistance(normalized.Length);
        if (maxDistance <= 0)
        {
            return new SubjectTokenResult(
                raw, normalized, null, null, false,
                $"Could not verify “{raw.Trim()}”. Please re-enter that subject.");
        }

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
            return new SubjectTokenResult(
                raw, normalized, null, null, false,
                $"Could not verify “{raw.Trim()}”. Please re-enter that subject.");
        }

        int best = candidates.Min(c => c.Distance);
        List<VocabEntry> bestEntries = candidates
            .Where(c => c.Distance == best)
            .Select(c => c.Entry)
            .DistinctBy(e => e.GeneralMask, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bestEntries.Count > 1)
        {
            string options = string.Join(" or ", bestEntries.Select(e => e.Label).Distinct(StringComparer.OrdinalIgnoreCase));
            return new SubjectTokenResult(
                raw, normalized, null, null, false,
                $"Could not verify “{raw.Trim()}” (ambiguous — did you mean {options}?). Please re-enter that subject.");
        }

        VocabEntry match = bestEntries[0];
        return new SubjectTokenResult(raw, normalized, match.GeneralMask, match.Label, true, null);
    }

    private static List<string> SplitTokens(string freeText)
    {
        // Prefer explicit list separators; fall back to whole-string as one token when no separators.
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

    private static string NormalizeKey(string value)
    {
        string lower = value.Trim().ToLowerInvariant();
        // Fold common punctuation / symbols used in subject names.
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

        string collapsed = Regex.Replace(builder.ToString().Trim(), @"\s+", " ");
        // Compact form also registered; keep spaced for multi-word keys.
        return collapsed;
    }

    private static string Compact(string normalized) =>
        normalized.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static int MaxEditDistance(int length) => length switch
    {
        <= 3 => 0, // short tokens (go, art, c) must be exact
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

    private static string DisplayNameForMask(string mask) => mask switch
    {
        SubjectMaskNames.ComputerScience => "Computer Science",
        SubjectMaskNames.Mathematics => "Mathematics",
        SubjectMaskNames.Science => "Science",
        SubjectMaskNames.Languages => "Languages",
        SubjectMaskNames.History => "History",
        SubjectMaskNames.Business => "Business",
        SubjectMaskNames.Art => "Art",
        SubjectMaskNames.Music => "Music",
        SubjectMaskNames.Engineering => "Engineering",
        SubjectMaskNames.Medicine => "Medicine",
        SubjectMaskNames.Finance => "Finance",
        SubjectMaskNames.Economics => "Economics",
        SubjectMaskNames.Education => "Education",
        _ => mask,
    };

    private static Vocabulary BuildVocabulary()
    {
        Dictionary<string, VocabEntry> exact = new(StringComparer.Ordinal);

        void Add(string alias, string generalMask, string label)
        {
            string key = NormalizeKey(alias);
            if (string.IsNullOrWhiteSpace(key))
                return;

            Register(exact, key, generalMask, label);
            string compact = Compact(key);
            if (!string.Equals(compact, key, StringComparison.Ordinal))
                Register(exact, compact, generalMask, label);
        }

        // Mask-C generals + common aliases (case folded via NormalizeKey).
        Add("mathematics", SubjectMaskNames.Mathematics, "Mathematics");
        Add("math", SubjectMaskNames.Mathematics, "Mathematics");
        Add("maths", SubjectMaskNames.Mathematics, "Mathematics");

        Add("science", SubjectMaskNames.Science, "Science");

        Add("computer science", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("computerscience", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("comp sci", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("compsci", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("cs", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("coding", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("programming", SubjectMaskNames.ComputerScience, "Computer Science");

        Add("languages", SubjectMaskNames.Languages, "Languages");
        Add("language", SubjectMaskNames.Languages, "Languages");
        Add("foreign language", SubjectMaskNames.Languages, "Languages");

        Add("history", SubjectMaskNames.History, "History");
        Add("business", SubjectMaskNames.Business, "Business");
        Add("art", SubjectMaskNames.Art, "Art");
        Add("music", SubjectMaskNames.Music, "Music");
        Add("engineering", SubjectMaskNames.Engineering, "Engineering");
        Add("medicine", SubjectMaskNames.Medicine, "Medicine");
        Add("medical", SubjectMaskNames.Medicine, "Medicine");
        Add("finance", SubjectMaskNames.Finance, "Finance");
        Add("economics", SubjectMaskNames.Economics, "Economics");
        Add("education", SubjectMaskNames.Education, "Education");
        Add("teaching", SubjectMaskNames.Education, "Education");

        // Expertise bits → parent Mask-C general (biology → Science, rust → ComputerScience, …).
        foreach ((string generalMask, Type expertiseType) in ExpertiseTypes())
        {
            foreach (FieldInfo field in expertiseType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(short))
                    continue;

                string display = ExpertiseDisplayName(field.Name);
                Add(field.Name, generalMask, display);
                Add(display, generalMask, display);
                Add(ChatRoomCatalog.ToDisplayName(field.Name), generalMask, display);
            }
        }

        // Extra aliases not present as field names.
        Add("js", SubjectMaskNames.ComputerScience, "JavaScript");
        Add("ts", SubjectMaskNames.ComputerScience, "TypeScript");
        Add("cpp", SubjectMaskNames.ComputerScience, "C++");
        Add("c plus plus", SubjectMaskNames.ComputerScience, "C++");
        Add("golang", SubjectMaskNames.ComputerScience, "Go");
        Add("postgres", SubjectMaskNames.ComputerScience, "PostgreSQL");
        Add("k8s", SubjectMaskNames.ComputerScience, "Kubernetes");
        Add("cyber security", SubjectMaskNames.ComputerScience, "Cyber Security");
        Add("cybersecurity", SubjectMaskNames.ComputerScience, "Cyber Security");
        Add("opsys", SubjectMaskNames.ComputerScience, "Operating Systems");
        Add("os", SubjectMaskNames.ComputerScience, "Operating Systems");
        Add("frontend", SubjectMaskNames.ComputerScience, "Frontend");
        Add("front end", SubjectMaskNames.ComputerScience, "Frontend");
        Add("back end", SubjectMaskNames.ComputerScience, "Backend");
        Add("apis", SubjectMaskNames.ComputerScience, "APIs");
        Add("rest", SubjectMaskNames.ComputerScience, "APIs");
        Add("sql", SubjectMaskNames.ComputerScience, "PostgreSQL");
        Add("ml", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("ai", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("machine learning", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("data structures", SubjectMaskNames.ComputerScience, "Computer Science");
        Add("algorithms", SubjectMaskNames.ComputerScience, "Computer Science");

        Add("bio", SubjectMaskNames.Science, "Biology");
        Add("chem", SubjectMaskNames.Science, "Chemistry");
        Add("phys", SubjectMaskNames.Science, "Physics");
        Add("psych", SubjectMaskNames.Science, "Psychology");

        Add("calc", SubjectMaskNames.Mathematics, "Calculus");
        Add("trig", SubjectMaskNames.Mathematics, "Trigonometry");
        Add("stats", SubjectMaskNames.Mathematics, "Statistics");
        Add("lin alg", SubjectMaskNames.Mathematics, "Linear Algebra");
        Add("linear alg", SubjectMaskNames.Mathematics, "Linear Algebra");
        Add("diff eq", SubjectMaskNames.Mathematics, "Differential Equations");
        Add("ode", SubjectMaskNames.Mathematics, "Differential Equations");

        Add("asl", SubjectMaskNames.Languages, "American Sign Language");
        Add("mandarin chinese", SubjectMaskNames.Languages, "Mandarin");
        Add("chinese", SubjectMaskNames.Languages, "Mandarin");

        Add("us history", SubjectMaskNames.History, "US History");
        Add("u.s. history", SubjectMaskNames.History, "US History");
        Add("american history", SubjectMaskNames.History, "US History");
        Add("world hist", SubjectMaskNames.History, "World History");

        Add("mech eng", SubjectMaskNames.Engineering, "Mechanical Engineering");
        Add("electrical eng", SubjectMaskNames.Engineering, "Electrical Engineering");
        Add("civil eng", SubjectMaskNames.Engineering, "Civil Engineering");
        Add("chem eng", SubjectMaskNames.Engineering, "Chemical Engineering");
        Add("aero", SubjectMaskNames.Engineering, "Aerospace Engineering");

        Add("micro econ", SubjectMaskNames.Economics, "Microeconomics");
        Add("macro econ", SubjectMaskNames.Economics, "Macroeconomics");
        Add("micro", SubjectMaskNames.Economics, "Microeconomics");
        Add("macro", SubjectMaskNames.Economics, "Macroeconomics");

        return new Vocabulary(exact);
    }

    private static void Register(
        Dictionary<string, VocabEntry> exact,
        string key,
        string generalMask,
        string label)
    {
        if (exact.TryGetValue(key, out VocabEntry? existing)
            && existing is not null
            && !string.Equals(existing.GeneralMask, generalMask, StringComparison.OrdinalIgnoreCase))
        {
            // Prefer the first registration; aliases are hand-ordered with generals/expertise first.
            return;
        }

        exact[key] = new VocabEntry(generalMask, label);
    }

    private static IEnumerable<(string GeneralMask, Type ExpertiseType)> ExpertiseTypes()
    {
        yield return (SubjectMaskNames.Mathematics, typeof(MathematicsExpertise));
        yield return (SubjectMaskNames.Science, typeof(ScienceExpertise));
        yield return (SubjectMaskNames.ComputerScience, typeof(ComputerScienceExpertise));
        yield return (SubjectMaskNames.Languages, typeof(LanguageExpertise));
        yield return (SubjectMaskNames.History, typeof(HistoryExpertise));
        yield return (SubjectMaskNames.Business, typeof(BusinessExpertise));
        yield return (SubjectMaskNames.Art, typeof(ArtExpertise));
        yield return (SubjectMaskNames.Music, typeof(MusicExpertise));
        yield return (SubjectMaskNames.Engineering, typeof(EngineeringExpertise));
        yield return (SubjectMaskNames.Medicine, typeof(MedicineExpertise));
        yield return (SubjectMaskNames.Finance, typeof(FinanceExpertise));
        yield return (SubjectMaskNames.Economics, typeof(EconomicsExpertise));
        yield return (SubjectMaskNames.Education, typeof(EducationExpertise));
    }

    private static string ExpertiseDisplayName(string fieldName) => fieldName switch
    {
        "CPlusPlus" => "C++",
        "CSharp" => "C#",
        "Css" => "CSS",
        "Html" => "HTML",
        "RestApis" => "APIs",
        "PostgreSql" => "PostgreSQL",
        "MySql" => "MySQL",
        "MongoDb" => "MongoDB",
        "GraphQl" => "GraphQL",
        "Aws" => "AWS",
        "CyberSecurity" => "Cyber Security",
        "OperatingSystems" => "Operating Systems",
        "AmericanSignLanguage" => "American Sign Language",
        "UsHistory" => "US History",
        "DigitalArt" => "Digital Art",
        "ArtHistory" => "Art History",
        "MusicTheory" => "Music Theory",
        "MusicProduction" => "Music Production",
        "PublicHealth" => "Public Health",
        "PersonalFinance" => "Personal Finance",
        "CorporateFinance" => "Corporate Finance",
        "InternationalEconomics" => "International Economics",
        "CurriculumDesign" => "Curriculum Design",
        "SpecialEducation" => "Special Education",
        "EarlyChildhood" => "Early Childhood",
        "EducationalTechnology" => "Educational Technology",
        "LinearAlgebra" => "Linear Algebra",
        "DifferentialEquations" => "Differential Equations",
        "DiscreteMathematics" => "Discrete Mathematics",
        "NumericalMethods" => "Numerical Methods",
        "Mechanical" => "Mechanical Engineering",
        "Electrical" => "Electrical Engineering",
        "Civil" => "Civil Engineering",
        "Chemical" => "Chemical Engineering",
        "Aerospace" => "Aerospace Engineering",
        _ => ChatRoomCatalog.ToDisplayName(fieldName),
    };

    [GeneratedRegex(@"[,;|/]+|\s+&\s+|\s+and\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenSplitterPattern();

    private sealed record VocabEntry(string GeneralMask, string Label);

    private sealed class Vocabulary
    {
        public Vocabulary(Dictionary<string, VocabEntry> exact)
        {
            Exact = exact;
            KeysLongestFirst = exact.Keys
                .OrderByDescending(k => k.Length)
                .ThenBy(k => k, StringComparer.Ordinal)
                .ToArray();
        }

        public Dictionary<string, VocabEntry> Exact { get; }
        public string[] KeysLongestFirst { get; }
    }

    public sealed record SubjectTokenResult(
        string RawToken,
        string NormalizedToken,
        string? GeneralMask,
        string? MatchedLabel,
        bool Verified,
        string? FailureReason);

    public sealed record ProcessResult(
        bool Ok,
        IReadOnlyList<string> GeneralMasks,
        string CanonicalDisplay,
        string? ErrorMessage,
        IReadOnlyList<SubjectTokenResult> Tokens);
}
