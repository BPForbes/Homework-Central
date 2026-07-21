namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Fixed category vocabularies for each chat-monitor lineage. Softmax over these classes
/// is the 3Blue1Brown multi-class path; keyword heuristics remain only as a bootstrap labeler
/// when no trained category target is supplied.
/// </summary>
public static class ChatMonitoringCategoryTaxonomy
{
    public static readonly string[] Moderation =
    [
        "spam",
        "profanity",
        "threat",
        "harassment",
        "evasion",
        "moderation-general",
    ];

    public static readonly string[] Tutoring =
    [
        "tutoring-math",
        "tutoring-science",
        "tutoring-english",
        "tutoring-competency",
    ];

    public static IReadOnlyList<string> For(NeuralModelKindChatMonitoring kind) =>
        kind == NeuralModelKindChatMonitoring.Tutoring ? Tutoring : Moderation;

    public static int IndexOf(NeuralModelKindChatMonitoring kind, string? category)
    {
        IReadOnlyList<string> labels = For(kind);
        if (string.IsNullOrWhiteSpace(category))
            return labels.Count - 1;
        for (int i = 0; i < labels.Count; i++)
        {
            if (string.Equals(labels[i], category, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        string value = category.ToLowerInvariant();
        for (int i = 0; i < labels.Count; i++)
        {
            string stem = labels[i].Contains('-', StringComparison.Ordinal)
                ? labels[i][(labels[i].IndexOf('-') + 1)..]
                : labels[i];
            if (value.Contains(stem, StringComparison.Ordinal))
                return i;
        }

        return labels.Count - 1;
    }

    public static string Label(NeuralModelKindChatMonitoring kind, int index)
    {
        IReadOnlyList<string> labels = For(kind);
        if (index < 0 || index >= labels.Count)
            return labels[^1];
        return labels[index];
    }
}
