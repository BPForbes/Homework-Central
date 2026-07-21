using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Tickets;

/// <summary>
/// Canonical default ticket portals seeded on the master database once per
/// <see cref="Authorization.AccountClass"/> (RealAccount and DeveloperAccount).
/// </summary>
public static class DefaultTicketPortalPresets
{
    public const string TutorDisplayName = "Apply for Tutor Positions";
    public const string TutorFilterName = "Tutor";
    public const string TutorRoomKey = "ticket-apply-tutor";

    public const string ModDisplayName = "Notify Mods";
    public const string ModFilterName = "Mod-Mail";
    public const string ModRoomKey = "ticket-notify-mods";

    public static readonly string[] TutorDecisionLabels = ["Approve", "Reject", "Needs Review"];
    public static readonly string[] ModDecisionLabels = ["Action Taken", "No Action", "Needs Review"];

    public static List<TicketIntakeQuestionDto> TutorIntakeQuestions() =>
    [
        new()
        {
            Id = "why-consider",
            Type = "longText",
            Prompt = "Why should we consider you?",
            Required = false,
        },
        new()
        {
            Id = "tutor-subjects",
            Type = "shortText",
            Prompt =
                "What do you want to tutor in? Use known subjects (e.g. Biology, Rust, Mathematics). "
                + "Separate multiple with commas. Unknown topics will ask you to re-enter.",
            Required = true,
        },
        new()
        {
            Id = "work-samples",
            Type = "mixed",
            Prompt = "Upload or link any work that may help in decision-making.",
            Required = false,
            AllowedResponseKinds = ["file", "link"],
        },
        new()
        {
            Id = "unpaid-ack",
            Type = "shortText",
            Prompt =
                "Please sign your username to understand this will not be a paid position. "
                + "If you have any questions feel free to ask the head tutors in this ticket.",
            Required = true,
        },
        new()
        {
            Id = "ai-opt-out",
            Type = "checkbox",
            Prompt =
                "We use AI by default in decision-making for tickets. If you want to opt out of AI "
                + "decision-making for your application, please click below.",
            Required = false,
            AiOptOut = true,
        },
    ];

    public static List<TicketIntakeQuestionDto> ModIntakeQuestions() =>
    [
        new()
        {
            Id = "report-reason",
            Type = "longText",
            Prompt = "Why is this being reported?",
            Required = true,
        },
        new()
        {
            Id = "reported-user",
            Type = "shortText",
            Prompt = "What user is being reported?",
            Required = true,
            TracksUser = true,
        },
        new()
        {
            Id = "proof",
            Type = "mixed",
            Prompt = "Provide proof of your claim (files, forwarded messages, and/or links).",
            Required = true,
            AllowedResponseKinds = ["file", "forward", "link"],
        },
        new()
        {
            Id = "ai-opt-out",
            Type = "checkbox",
            Prompt =
                "We use AI by default in decision-making for tickets. If you want to opt out of AI "
                + "decision-making for this report, please click below.",
            Required = false,
            AiOptOut = true,
        },
    ];

    public static List<CustomChannelAccessRuleInput> TutorStaffRules() =>
    [
        PlatformRule(PlatformRoles.HeadTutor),
        PlatformRule(PlatformRoles.SeniorTutor),
        PlatformRule(PlatformRoles.Tutor),
        PlatformRule(PlatformRoles.Administrator),
        PlatformRule(PlatformRoles.Owner),
    ];

    public static List<CustomChannelAccessRuleInput> TutorMentionRules() =>
    [
        PlatformRule(PlatformRoles.HeadTutor),
        PlatformRule(PlatformRoles.SeniorTutor),
        PlatformRule(PlatformRoles.Tutor),
        PlatformRule(PlatformRoles.Administrator),
        PlatformRule(PlatformRoles.Owner),
    ];

    public static List<CustomChannelAccessRuleInput> ModStaffRules() =>
    [
        PlatformRule(PlatformRoles.Moderator),
        PlatformRule(PlatformRoles.SeniorModerator),
        PlatformRule(PlatformRoles.Administrator),
        PlatformRule(PlatformRoles.SystemAdministrator),
        PlatformRule(PlatformRoles.Owner),
    ];

    public static List<CustomChannelAccessRuleInput> ModMentionRules() => ModStaffRules();

    private static CustomChannelAccessRuleInput PlatformRule(short bit) =>
        new() { PlatformRoleBit = bit };
}
