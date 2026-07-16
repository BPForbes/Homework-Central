namespace HomeworkCentral.Api.Models;

public static class CandidateApplicationStatuses
{
    public const string InsufficientEvidence = "INSUFFICIENT_EVIDENCE";
    public const string Developing = "DEVELOPING";
    public const string ReviewRecommended = "REVIEW_RECOMMENDED";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string CriticalConcern = "CRITICAL_CONCERN";
}

public class CandidateApplication
{
    public Guid CandidateApplicationId { get; set; }
    public Guid UserId { get; set; }
    public string PositionId { get; set; } = null!;
    public string Status { get; set; } = CandidateApplicationStatuses.InsufficientEvidence;
    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public bool AiOptOut { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    public ICollection<AssessmentEvent> Events { get; set; } = [];
    public ICollection<CandidateCompetencyState> CompetencyStates { get; set; } = [];
    public ICollection<CandidateDecision> Decisions { get; set; } = [];
}

public class AssessmentEvent
{
    public Guid AssessmentEventId { get; set; }
    public Guid CandidateApplicationId { get; set; }
    public CandidateApplication Application { get; set; } = null!;
    public Guid? MessageId { get; set; }
    public string RubricVersion { get; set; } = "1.0";
    public string EvaluatorModelVersion { get; set; } = string.Empty;
    public string RawEvaluationJson { get; set; } = "{}";
    public double LlmScore { get; set; }
    public double? CommunityScore { get; set; }
    public double CombinedScore { get; set; }
    public double EvidenceWeight { get; set; }
    public double Difficulty { get; set; }
    public double Relevance { get; set; }
    public double Confidence { get; set; }
    public double AuthenticityConfidence { get; set; }
    public bool IsAdjustment { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<AssessmentCompetencyEvidence> CompetencyEvidence { get; set; } = [];
}

public class AssessmentCompetencyEvidence
{
    public Guid AssessmentEventId { get; set; }
    public AssessmentEvent Event { get; set; } = null!;
    public string CompetencyId { get; set; } = null!;
    public double MembershipWeight { get; set; }
    public double EffectiveEvidenceWeight { get; set; }
}

public class CandidateCompetencyState
{
    public Guid CandidateApplicationId { get; set; }
    public CandidateApplication Application { get; set; } = null!;
    public string CompetencyId { get; set; } = null!;
    public double Alpha { get; set; } = 1;
    public double Beta { get; set; } = 1;
    public double MeanScore { get; set; }
    public double EvidenceVolume { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }
}

public class CandidateDecision
{
    public Guid CandidateDecisionId { get; set; }
    public Guid CandidateApplicationId { get; set; }
    public CandidateApplication Application { get; set; } = null!;
    public string Decision { get; set; } = null!;
    public string TriggeredBy { get; set; } = null!;
    public Guid? ReviewerId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>pgvector-backed retrieval document. Never authoritative for scores.</summary>
public class VectorDocument
{
    public Guid DocumentId { get; set; }
    /// <summary>scoring_reference | candidate_evidence | assessment_ticket_memory</summary>
    public string Namespace { get; set; } = null!;
    public string? PositionId { get; set; }
    public Guid? CanonicalRecordId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public string ContentText { get; set; } = string.Empty;
    /// <summary>Serialized embedding floats (pgvector column mapped as text/json for portability when extension unavailable).</summary>
    public string EmbeddingJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
}

public static class VectorNamespaces
{
    public const string ScoringReference = "scoring_reference";
    public const string CandidateEvidence = "candidate_evidence";
    public const string AssessmentTicketMemory = "assessment_ticket_memory";
}
