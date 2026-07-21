namespace HomeworkCentral.Api.Models;

public class Ticket
{
    public Guid TicketId { get; set; }
    public Guid PortalChannelId { get; set; }
    public TicketPortalConfig Portal { get; set; } = null!;
    public Guid ChatChannelId { get; set; }
    public CustomChannel ChatChannel { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public int DisplayNumber { get; set; }
    public string Purpose { get; set; } = null!;
    /// <summary>Filter key snapped from the portal at open time for room naming.</summary>
    public string FilterName { get; set; } = null!;
    public Guid OpenedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string IntakeAnswersJson { get; set; } = "[]";
    public bool AiTrackingOptOut { get; set; }
    public string? TrackingTemplateJson { get; set; }
    public string? ApprovedDecision { get; set; }
    public DateTime? DecisionApprovedAtUtc { get; set; }
    public Guid? DecisionApprovedByUserId { get; set; }

    public ICollection<TicketUserWatch> Watches { get; set; } = [];
    public ICollection<TicketMessageScore> MessageScores { get; set; } = [];
}

public class TicketUserWatch
{
    public Guid WatchId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid TrackedUserId { get; set; }
    public string ContextLabel { get; set; } = null!;
    public bool IsActive { get; set; }
    public Guid SetByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Source { get; set; } = "Staff";
}

/// <summary>
/// Authoritative, append-only confidence update for one watched user's message.
/// Vector documents mirror these events for contextual retrieval but never replace
/// this relational audit history.
/// </summary>
public class TicketMessageScore
{
    public Guid ScoreEventId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid MessageId { get; set; }
    public Guid TrackedUserId { get; set; }
    public double PreviousScore { get; set; }
    public double ScoreDelta { get; set; }
    public double CurrentScore { get; set; }
    public double EvidenceConfidence { get; set; }
    public double Relevance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string EvaluatorModelVersion { get; set; } = string.Empty;
    public string RawEvaluationJson { get; set; } = "{}";
    public double StudentScore { get; set; }
    public double StudentConfidence { get; set; }
    public double StudentRelevance { get; set; }
    public string StudentCategory { get; set; } = "general";
    public string StudentReasoning { get; set; } = string.Empty;
    public string ContextSnapshot { get; set; } = string.Empty;
    public bool ReviewerInvoked { get; set; }
    public double? ReviewerScore { get; set; }
    public double? ReviewerConfidence { get; set; }
    public double? ReviewerRelevance { get; set; }
    public bool CorrectionNeeded { get; set; }
    public string? ReviewerExplanation { get; set; }
    public string? ReviewerGuidance { get; set; }
    public DateTime? TrainingApprovedAtUtc { get; set; }
    public Guid? TrainingApprovedByUserId { get; set; }
    public DateTime? TrainingRejectedAtUtc { get; set; }
    public Guid? TrainingRejectedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Approved relational source for the in-memory student model.</summary>
public class TicketModelTrainingExample
{
    public Guid TrainingExampleId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? ScoreEventId { get; set; }
    public string Requirement { get; set; } = string.Empty;
    public string? BootstrapMessage { get; set; }
    public string? ContextSnapshot { get; set; }
    public double TargetScore { get; set; }
    public double TargetRelevance { get; set; }
    public string Category { get; set; } = "general";
    public string Source { get; set; } = "Seed";
    public DateTime ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? NeuralNetTrainingSessionId { get; set; }
    public HomeworkCentral.Api.Assessment.NeuralModelKindChatMonitoring ChatMonitoringKind { get; set; } = HomeworkCentral.Api.Assessment.NeuralModelKindChatMonitoring.Moderation;
    public long? CanonicalGenerationApplied { get; set; }
}

/// <summary>Auditable, synthetic-only teacher/student training run initiated by an administrator.</summary>
public class NeuralNetTrainingSession
{
    public Guid SessionId { get; set; }
    public Guid StartedByUserId { get; set; }
    public int RequestedTicketCount { get; set; }
    public int MaxPassesPerTicket { get; set; }
    public HomeworkCentral.Api.Assessment.NeuralTrainingMode Mode { get; set; } = HomeworkCentral.Api.Assessment.NeuralTrainingMode.Both;
    public string Status { get; set; } = "Queued";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public string? ReportJson { get; set; }
}

/// <summary>Immutable canonical parameter snapshot for one chat-monitoring model lineage.</summary>
public class NeuralNetCanonicalCheckpoint
{
    public HomeworkCentral.Api.Assessment.NeuralModelKindChatMonitoring ChatMonitoringKind { get; set; } = HomeworkCentral.Api.Assessment.NeuralModelKindChatMonitoring.Moderation;
    public long Generation { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string ArchitectureVersion { get; set; } = string.Empty;
    public string RuntimeKind { get; set; } = "HashedMlp";
    public string ParametersBase64 { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Serialized promotion work item; worker candidate weights are never installed directly.</summary>
public class NeuralNetTrainingPromotion
{
    public Guid PromotionId { get; set; }
    public Guid SessionId { get; set; }
    public HomeworkCentral.Api.Assessment.NeuralModelKindChatMonitoring ChatMonitoringKind { get; set; } = HomeworkCentral.Api.Assessment.NeuralModelKindChatMonitoring.Moderation;
    public long PromotionSequence { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public long? PromotedGeneration { get; set; }
    public string? PromotionReportJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>Per chat-monitor training run within a synthetic training session.</summary>
public class ChatMonitoringNeuralModelRun
{
    public Guid RunId { get; set; }
    public Guid SessionId { get; set; }
    public HomeworkCentral.Api.Assessment.NeuralModelKindChatMonitoring ChatMonitoringKind { get; set; }
    public string Status { get; set; } = "Queued";
    public string? WorkerReplayJson { get; set; }
    public string? PromotionReplayJson { get; set; }
    public long? CanonicalGeneration { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
}
