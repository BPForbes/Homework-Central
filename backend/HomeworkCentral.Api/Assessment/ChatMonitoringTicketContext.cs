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
            || value.Contains("application")
            || value.Contains("subject-help")
            || value.Contains("learning");
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

        if (value.Contains("spam") || value.Contains("flood")) return "spam";
        if (value.Contains("profan") || value.Contains("cuss")) return "profanity";
        if (value.Contains("threat") || value.Contains("harm")) return "threat";
        if (value.Contains("harass") || value.Contains("insult") || value.Contains("abuse")) return "harassment";
        if (value.Contains("evad") || value.Contains("filter")) return "evasion";
        return "moderation-general";
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

