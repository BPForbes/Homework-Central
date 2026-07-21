using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Builds bounded ticket context and resolves which chat-monitor lineage to run.</summary>
public static class ChatMonitoringTicketContext
{
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
        if (kind == NeuralModelKindChatMonitoring.Tutoring)
        {
            if (ContainsAny(value, "computer science", "computerscience", "programming", "coding", "python", "java", "software"))
                return "tutoring-computer-science";
            if (ContainsAny(value, "math", "algebra", "calculus", "geometry", "trigonometry", "statistics"))
                return "tutoring-mathematics";
            if (ContainsAny(value, "biology", "chemistry", "physics", "science", "psychology", "philosophy"))
                return "tutoring-science";
            if (ContainsAny(value, "english", "spanish", "french", "german", "language", "writing", "essay", "asl"))
                return "tutoring-languages";
            if (ContainsAny(value, "history", "civics", "geography"))
                return "tutoring-history";
            if (ContainsAny(value, "business", "marketing", "management", "entrepreneur"))
                return "tutoring-business";
            if (ContainsAny(value, "art", "drawing", "painting", "design"))
                return "tutoring-art";
            if (ContainsAny(value, "music", "band", "choir", "orchestra"))
                return "tutoring-music";
            if (ContainsAny(value, "engineering", "mechanical", "electrical", "civil"))
                return "tutoring-engineering";
            if (ContainsAny(value, "medicine", "anatomy", "medical", "nursing", "health"))
                return "tutoring-medicine";
            if (ContainsAny(value, "finance", "investing", "accounting", "banking"))
                return "tutoring-finance";
            if (ContainsAny(value, "economics", "microeconomics", "macroeconomics"))
                return "tutoring-economics";
            if (ContainsAny(value, "education", "curriculum", "pedagogy", "teaching"))
                return "tutoring-education";
            return "tutoring-competency";
        }

        // Prefer an explicit fine-grained slug mentioned in the ticket text.
        string? bestSlug = null;
        int bestLength = -1;
        foreach (string slug in ChatMonitoringModerationConcepts.Slugs)
        {
            if (value.Contains(slug, StringComparison.Ordinal) && slug.Length > bestLength)
            {
                bestSlug = slug;
                bestLength = slug.Length;
            }
        }

        if (bestSlug is not null)
            return bestSlug;

        if (ContainsAny(value, "tip pressure", "tip-pressure", "guilt tip")) return "tip-pressure";
        if (ContainsAny(value, "tip solicit", "tip-solicit", "tips expected", "send tip")) return "tip-solicitation";
        if (ContainsAny(value, "pay me", "payment solicit", "pay for help", "paywalled")) return "payment-solicitation";
        if (ContainsAny(value, "cash app", "paypal", "venmo", "crypto", "gift card")) return "off-platform-payment";
        if (ContainsAny(value, "impersonat", "pretend to be staff", "fake mod")) return "staff-impersonation";
        if (ContainsAny(value, "doxx", "doxing", "home address", "phone number leak")) return "doxxing";
        if (ContainsAny(value, "groom", "minor", "underage")) return "grooming-escalation";
        if (ContainsAny(value, "threat", "kill you", "hurt you", "violent")) return "violent-intent";
        if (ContainsAny(value, "malware", "password", "account takeover", "phishing")) return "credential-theft";
        if (ContainsAny(value, "brigad", "mass report", "sockpuppet", "vote manip")) return "coordinated-brigading";
        if (ContainsAny(value, "harass", "insult", "dogpil", "unwanted contact")) return "persistent-unwanted-contact";
        if (ContainsAny(value, "misinfo", "fake news", "fabricated source")) return "fabricated-source";
        if (ContainsAny(value, "spam", "flood")) return "fake-engagement";
        if (ContainsAny(value, "profan", "cuss")) return ChatMonitoringModerationConcepts.CatchAll;
        if (ContainsAny(value, "evad", "filter bypass")) return "security-control-bypass";
        return ChatMonitoringModerationConcepts.CatchAll;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Maps a free-text / heuristic category onto the softmax vocabulary index.</summary>
    public static int CategoryIndex(string? category, NeuralModelKindChatMonitoring kind) =>
        ChatMonitoringCategoryTaxonomy.IndexOf(kind, category);
}

