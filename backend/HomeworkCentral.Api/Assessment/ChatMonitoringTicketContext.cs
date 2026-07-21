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
            if (value.Contains("math") || value.Contains("algebra") || value.Contains("quadratic")) return "tutoring-math";
            if (value.Contains("science") || value.Contains("biology") || value.Contains("chemistry")) return "tutoring-science";
            if (value.Contains("english") || value.Contains("writing") || value.Contains("essay")) return "tutoring-english";
            return "tutoring-competency";
        }

        if (value.Contains("spam") || value.Contains("flood")) return "spam";
        if (value.Contains("profan") || value.Contains("cuss")) return "profanity";
        if (value.Contains("threat") || value.Contains("harm")) return "threat";
        if (value.Contains("harass") || value.Contains("insult") || value.Contains("abuse")) return "harassment";
        if (value.Contains("evad") || value.Contains("filter")) return "evasion";
        return "moderation-general";
    }

    /// <summary>Maps a free-text / heuristic category onto the softmax vocabulary index.</summary>
    public static int CategoryIndex(string? category, NeuralModelKindChatMonitoring kind) =>
        ChatMonitoringCategoryTaxonomy.IndexOf(kind, category);
}

