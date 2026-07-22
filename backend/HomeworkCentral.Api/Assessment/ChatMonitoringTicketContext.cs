using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Builds bounded ticket context and resolves which chat-monitor lineage to run.</summary>
public static class ChatMonitoringTicketContext
{
    private static readonly (string Category, string[] Needles)[] TutoringCategoryNeedles =
    [
        ("tutoring-computer-science", ["computer science", "computerscience", "programming", "coding", "python", "java", "software"]),
        ("tutoring-mathematics", ["math", "algebra", "calculus", "geometry", "trigonometry", "statistics"]),
        ("tutoring-science", ["biology", "chemistry", "physics", "science", "psychology", "philosophy"]),
        ("tutoring-languages", ["english", "spanish", "french", "german", "language", "writing", "essay", "asl"]),
        ("tutoring-history", ["history", "civics", "geography"]),
        ("tutoring-business", ["business", "marketing", "management", "entrepreneur"]),
        ("tutoring-art", ["art", "drawing", "painting", "design"]),
        ("tutoring-music", ["music", "band", "choir", "orchestra"]),
        ("tutoring-engineering", ["engineering", "mechanical", "electrical", "civil"]),
        ("tutoring-medicine", ["medicine", "anatomy", "medical", "nursing", "health"]),
        ("tutoring-finance", ["finance", "investing", "accounting", "banking"]),
        ("tutoring-economics", ["economics", "microeconomics", "macroeconomics"]),
        ("tutoring-education", ["education", "curriculum", "pedagogy", "teaching"]),
    ];

    private static readonly (string Category, string[] Needles)[] ModerationCategoryNeedles =
    [
        ("tip-pressure", ["tip pressure", "tip-pressure", "guilt tip"]),
        ("tip-solicitation", ["tip solicit", "tip-solicit", "tips expected", "send tip"]),
        ("payment-solicitation", ["pay me", "payment solicit", "pay for help", "paywalled"]),
        ("off-platform-payment", ["cash app", "paypal", "venmo", "crypto", "gift card"]),
        ("staff-impersonation", ["impersonat", "pretend to be staff", "fake mod"]),
        ("doxxing", ["doxx", "doxing", "home address", "phone number leak"]),
        ("grooming-escalation", ["groom", "minor", "underage"]),
        ("violent-intent", ["threat", "kill you", "hurt you", "violent"]),
        ("credential-theft", ["malware", "password", "account takeover", "phishing"]),
        ("coordinated-brigading", ["brigad", "mass report", "sockpuppet", "vote manip"]),
        ("persistent-unwanted-contact", ["harass", "insult", "dogpil", "unwanted contact"]),
        ("fabricated-source", ["misinfo", "fake news", "fabricated source"]),
        ("fake-engagement", ["spam", "flood"]),
        (ChatMonitoringModerationConcepts.CatchAll, ["profan", "cuss"]),
        ("security-control-bypass", ["evad", "filter bypass"]),
    ];

    public static string BuildRequirement(TicketUserWatch watch, int maxCharacters)
    {
        string value = $"Filter: {watch.Ticket.FilterName}. Watch context: {watch.ContextLabel}. "
            + $"Instructions: {watch.Ticket.Portal.TrackingInstructions ?? "none"}. "
            + $"Frozen template: {watch.Ticket.TrackingTemplateJson ?? "none"}.";
        return value.Length <= maxCharacters ? value : value[..maxCharacters];
    }

    /// <summary>
    /// Routes live inference to the Tutoring monitor for tutor-application style tickets;
    /// everything else uses the Moderation monitor.
    /// </summary>
    public static NeuralModelKindChatMonitoring ResolveKind(TicketUserWatch watch)
    {
        string haystack = $"{watch.Ticket.FilterName} {watch.ContextLabel} {watch.Ticket.Portal.TrackingInstructions} {watch.Ticket.TrackingTemplateJson}";
        return IsTutoringDomain(haystack)
            ? NeuralModelKindChatMonitoring.Tutoring
            : NeuralModelKindChatMonitoring.Moderation;
    }

    public static NeuralModelKindChatMonitoring ResolveKind(string requirement, string? category = null)
    {
        string haystack = $"{requirement} {category}";
        return IsTutoringDomain(haystack)
            ? NeuralModelKindChatMonitoring.Tutoring
            : NeuralModelKindChatMonitoring.Moderation;
    }

    public static bool IsTutoringDomain(string text)
    {
        string value = text.ToLowerInvariant();
        return value.Contains("tutor")
            || value.Contains("tutoring")
            || value.Contains("competency")
            || value.Contains("tutor application")
            || value.Contains("tutoring application")
            || value.Contains("subject-help")
            || value.Contains("subject help")
            || value.Contains("learning thread")
            || value.Contains("learning monitor");
    }

    public static string DetectCategory(string requirement, NeuralModelKindChatMonitoring kind)
    {
        string value = requirement.ToLowerInvariant();
        return kind switch
        {
            NeuralModelKindChatMonitoring.Tutoring => DetectTutoringCategory(value),
            _ => DetectModerationCategory(value),
        };
    }

    private static string DetectTutoringCategory(string value)
    {
        foreach ((string category, string[] needles) in TutoringCategoryNeedles)
        {
            if (ContainsAny(value, needles))
                return category;
        }

        return "tutoring-competency";
    }

    private static string DetectModerationCategory(string value)
    {
        // Prefer an explicit fine-grained slug mentioned in the ticket text.
        string? explicitSlug = FindLongestModerationSlug(value);
        if (explicitSlug is not null)
            return explicitSlug;

        foreach ((string category, string[] needles) in ModerationCategoryNeedles)
        {
            if (ContainsAny(value, needles))
                return category;
        }

        return ChatMonitoringModerationConcepts.CatchAll;
    }

    private static string? FindLongestModerationSlug(string value) =>
        ChatMonitoringModerationConcepts.Slugs
            .Where(slug => value.Contains(slug, StringComparison.Ordinal))
            .OrderByDescending(slug => slug.Length)
            .FirstOrDefault();

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));

    /// <summary>Maps a free-text / heuristic category onto the softmax vocabulary index.</summary>
    public static int CategoryIndex(string? category, NeuralModelKindChatMonitoring kind) =>
        ChatMonitoringCategoryTaxonomy.IndexOf(kind, category);
}
