namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Stable slugs for built-in ANI lineages. Custom ticket portals register additional slugs
/// in <c>AIModelLineages</c>; those rows are not listed here.
/// </summary>
public static class AITrackingCatalog
{
    public const string ModerationSlug = "moderation";
    public const string TutoringSlug = "tutoring";

    public static string SlugFor(NeuralModelKindChatMonitoring kind) => kind switch
    {
        NeuralModelKindChatMonitoring.Tutoring => TutoringSlug,
        NeuralModelKindChatMonitoring.Moderation => ModerationSlug,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Built-in lineage slugs exist only for moderation and tutoring."),
    };

    public static bool TryParseBuiltInKind(string? slug, out NeuralModelKindChatMonitoring kind)
    {
        switch (slug?.Trim().ToLowerInvariant())
        {
            case ModerationSlug:
                kind = NeuralModelKindChatMonitoring.Moderation;
                return true;
            case TutoringSlug:
                kind = NeuralModelKindChatMonitoring.Tutoring;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
