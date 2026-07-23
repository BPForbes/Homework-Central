using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tickets.Preface;

/// <summary>
/// Mod-report preface check: maps free-text report reasons onto the 100 fine moderation concepts
/// using the same vocabulary engine as tutor subjects. Lenient — narrative reasons are never
/// rejected for unknown wording; verified concepts feed the moderation cascade hypothesis.
/// </summary>
public sealed class ModerationConceptPrefaceCheck : VocabularyTicketPrefaceCheck
{
    public const string QuestionIdValue = "report-reason";
    public const string CheckIdValue = "moderation-concepts";

    public static ModerationConceptPrefaceCheck Instance { get; } = new();

    public override string CheckId => CheckIdValue;
    public override string QuestionId => QuestionIdValue;
    public override string? FilterName => DefaultTicketPortalPresets.ModFilterName;
    public override TicketPrefaceMode Mode => TicketPrefaceMode.Lenient;
    public override bool RewriteAnswerOnSuccess => false;

    protected override string CategoryNoun => "moderation concept";
    protected override string ReenterExamples => "payment-solicitation, harassment, spam";

    protected override void RegisterVocabulary(VocabularyBuilder builder)
    {
        foreach (ModerationConceptDefinition concept in ChatMonitoringModerationConcepts.All)
        {
            string human = HumanizeSlug(concept.Slug);
            builder.Add(concept.Slug, concept.Slug, human, isSpecific: true);
            builder.Add(human, concept.Slug, human, isSpecific: true);
        }

        builder.Add("moderation general", ChatMonitoringModerationConcepts.CatchAll, "Moderation General");
        builder.Add("general", ChatMonitoringModerationConcepts.CatchAll, "Moderation General");

        // Legacy broad labels → fine concepts (same mapping the taxonomy uses).
        AddLegacy(builder, "spam", "fake-engagement");
        AddLegacy(builder, "flood", "fake-engagement");
        AddLegacy(builder, "profanity", ChatMonitoringModerationConcepts.CatchAll);
        AddLegacy(builder, "cussing", ChatMonitoringModerationConcepts.CatchAll);
        AddLegacy(builder, "threat", "violent-intent");
        AddLegacy(builder, "harassment", "persistent-unwanted-contact");
        AddLegacy(builder, "insult", "persistent-unwanted-contact");
        AddLegacy(builder, "abuse", "persistent-unwanted-contact");
        AddLegacy(builder, "evasion", "security-control-bypass");

        // Common reporter phrases → representative concepts.
        builder.Add("asked for money", "payment-solicitation", "Payment Solicitation", isSpecific: true);
        builder.Add("wants payment", "payment-solicitation", "Payment Solicitation", isSpecific: true);
        builder.Add("pay for help", "paywalled-help", "Paywalled Help", isSpecific: true);
        builder.Add("cash app", "off-platform-payment", "Off Platform Payment", isSpecific: true);
        builder.Add("paypal", "off-platform-payment", "Off Platform Payment", isSpecific: true);
        builder.Add("venmo", "off-platform-payment", "Off Platform Payment", isSpecific: true);
        builder.Add("tip begging", "tip-solicitation", "Tip Solicitation", isSpecific: true);
        builder.Add("impersonating staff", "staff-impersonation", "Staff Impersonation", isSpecific: true);
        builder.Add("fake admin", "staff-impersonation", "Staff Impersonation", isSpecific: true);
        builder.Add("doxxing", "doxxing", "Doxxing", isSpecific: true);
        builder.Add("doxx", "doxxing", "Doxxing", isSpecific: true);
        builder.Add("phishing", "phishing-pretext", "Phishing Pretext", isSpecific: true);
        builder.Add("scam", "trust-building-scam", "Trust Building Scam", isSpecific: true);
        builder.Add("grooming", "grooming-escalation", "Grooming Escalation", isSpecific: true);
        builder.Add("underage", "minor-targeting", "Minor Targeting", isSpecific: true);
        builder.Add("swatting", "swatting", "Swatting", isSpecific: true);
        builder.Add("blackmail", "extortion", "Extortion", isSpecific: true);
        builder.Add("malware", "malware-distribution", HumanizeSlug("malware-distribution"), isSpecific: true);
        builder.Add("hate speech", "dehumanization", HumanizeSlug("dehumanization"), isSpecific: true);
    }

    private static void AddLegacy(VocabularyBuilder builder, string alias, string slug)
    {
        string human = string.Equals(slug, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal)
            ? "Moderation General"
            : HumanizeSlug(slug);
        builder.Add(alias, slug, human, isSpecific: !string.Equals(slug, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal));
    }

    private static string HumanizeSlug(string slug) =>
        string.Join(' ', slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0
                ? part
                : char.ToUpperInvariant(part[0]) + part[1..]));
}
