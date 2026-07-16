using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.DTOs;

public class TicketPortalConfigDto
{
    public Guid ChannelId { get; set; }
    public string RoomId { get; set; } = null!;
    public string CtaLabel { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public string FilterName { get; set; } = null!;
    public int NextDisplayNumber { get; set; }
    public string TrackingMode { get; set; } = null!;
    public string? TrackingInstructions { get; set; }
    public List<string> DecisionLabels { get; set; } = [];
    public List<CustomChannelAccessRuleDto> MentionRoleRules { get; set; } = [];
    public List<CustomChannelAccessRuleDto> StaffAccessRules { get; set; } = [];
    public List<TicketIntakeQuestionDto> IntakeQuestions { get; set; } = [];
}

public class UpdateTicketPortalConfigRequest
{
    public string CtaLabel { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public string FilterName { get; set; } = null!;
    public string TrackingMode { get; set; } = null!;
    public string? TrackingInstructions { get; set; }
    public List<string> DecisionLabels { get; set; } = [];
    public List<CustomChannelAccessRuleInput> MentionRoleRules { get; set; } = [];
    public List<CustomChannelAccessRuleInput> StaffAccessRules { get; set; } = [];
    public List<TicketIntakeQuestionDto> IntakeQuestions { get; set; } = [];
}

public class TicketIntakeQuestionDto
{
    public string Id { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Prompt { get; set; } = null!;
    public bool Required { get; set; }
    public bool TracksUser { get; set; }
    public bool AiOptOut { get; set; }
    public List<string>? AllowedResponseKinds { get; set; }
    public List<string>? Options { get; set; }
}

public class OpenTicketRequest
{
    public Dictionary<string, System.Text.Json.JsonElement> Answers { get; set; } = [];
}

public class TicketDto
{
    public Guid TicketId { get; set; }
    public Guid PortalChannelId { get; set; }
    public string PortalRoomId { get; set; } = null!;
    public Guid ChatChannelId { get; set; }
    public string RoomId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public string FilterName { get; set; } = null!;
    public int DisplayNumber { get; set; }
    public string Status { get; set; } = null!;
    public Guid OpenedByUserId { get; set; }
    public string OpenedByUsername { get; set; } = null!;
    public bool CanManage { get; set; }
    public bool AiTrackingOptOut { get; set; }
    public string? ApprovedDecision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public List<TicketIntakeAnswerDto> IntakeAnswers { get; set; } = [];
    public List<TicketUserWatchDto> Watches { get; set; } = [];
}

public class ApproveTicketDecisionRequest
{
    public string Decision { get; set; } = null!;
    public string? Reason { get; set; }
}

public class TicketIntakeAnswerDto
{
    public string QuestionId { get; set; } = null!;
    public string Prompt { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string ValueDisplay { get; set; } = null!;
}

public class TicketUserWatchDto
{
    public Guid WatchId { get; set; }
    public Guid TrackedUserId { get; set; }
    public string TrackedUsername { get; set; } = null!;
    public string ContextLabel { get; set; } = null!;
    public bool IsActive { get; set; }
    public string Source { get; set; } = null!;
    public DateTime UpdatedAtUtc { get; set; }
}

public class UpsertTicketWatchRequest
{
    public Guid TrackedUserId { get; set; }
    public bool IsActive { get; set; }
    public string? ContextLabel { get; set; }
}

public class TicketAnalyzeResultDto
{
    public bool Available { get; set; }
    public string? Decision { get; set; }
    public string? Summary { get; set; }
    public List<TicketUserWatchDto> Watches { get; set; } = [];
    public int InboxRecipientsNotified { get; set; }
}

public class TicketDecisionPayloadDto
{
    public string? Decision { get; set; }
    public string? Summary { get; set; }
}

public class TicketOpenedPayloadDto
{
    public List<TicketIntakeAnswerDto> IntakeAnswers { get; set; } = [];
}
