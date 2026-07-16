namespace HomeworkCentral.Api.Models;

public class TicketPortalConfig
{
    public Guid ChannelId { get; set; }
    public CustomChannel Channel { get; set; } = null!;
    public string CtaLabel { get; set; } = "Open Ticket";
    public string Description { get; set; } = string.Empty;
    /// <summary>Human-readable purpose / portal label.</summary>
    public string Purpose { get; set; } = "General";
    /// <summary>Short key used in ticket room titles (e.g. Tutor, Mod-Mail).</summary>
    public string FilterName { get; set; } = "General";
    public int NextDisplayNumber { get; set; } = 1;
    public string TrackingMode { get; set; } = "None";
    public string? TrackingInstructions { get; set; }
    public string DecisionLabelsJson { get; set; } = "[]";
    public string MentionRoleRulesJson { get; set; } = "[]";
    public string StaffAccessRulesJson { get; set; } = "[]";
    public string IntakeSchemaJson { get; set; } = "[]";
    public DateTime UpdatedAtUtc { get; set; }
}
