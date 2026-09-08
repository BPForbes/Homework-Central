namespace HomeworkCentral.Api.Models;

/// <summary>
/// Lookup row for one trainable ANI lineage. Built-in rows cover moderation and tutoring;
/// additional rows let a custom ticket portal store and train against its own vocabulary.
/// </summary>
public sealed class AIModelLineage
{
    public int LineageId { get; set; }
    public string Slug { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsBuiltIn { get; set; }
    public Guid? PortalChannelId { get; set; }
    public TicketPortalConfig? Portal { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<AICategory> Categories { get; set; } = new List<AICategory>();
    public ICollection<AITrackingSession> Sessions { get; set; } = new List<AITrackingSession>();
}

/// <summary>
/// Lookup row for one category slug on a lineage. Softmax and teacher weights resolve here
/// instead of storing free-text names on fact rows.
/// </summary>
public sealed class AICategory
{
    public int CategoryId { get; set; }
    public int LineageId { get; set; }
    public AIModelLineage Lineage { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsCatchAll { get; set; }
}

/// <summary>
/// Entity row for one labeling pass: a message (usually on a ticket) scored by one lineage
/// and model version. Custom ticket portals use the same table via a non-built-in lineage.
/// </summary>
public sealed class AITrackingSession
{
    public long SessionId { get; set; }

    public int LineageId { get; set; }
    public AIModelLineage Lineage { get; set; } = null!;

    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Origin of the labeled message. Ticket rooms use <c>ticket</c>.</summary>
    public string SourceKind { get; set; } = AITrackingSourceKinds.Ticket;

    public int MessageIndex { get; set; }
    public string ModelVersion { get; set; } = null!;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<AITrackingCategoryWeight> CategoryWeights { get; set; } = new List<AITrackingCategoryWeight>();
    public ICollection<AITrackingPrediction> Predictions { get; set; } = new List<AITrackingPrediction>();
}

/// <summary>
/// Junction of a tracking session to a category lookup row with the teacher weight.
/// Sparse: only categories with a positive weight are stored.
/// </summary>
public sealed class AITrackingCategoryWeight
{
    public long CategoryWeightId { get; set; }

    public long SessionId { get; set; }
    public AITrackingSession TrackingSession { get; set; } = null!;

    public int CategoryId { get; set; }
    public AICategory Category { get; set; } = null!;

    public double Weight { get; set; }
    public bool IsHumanCorrected { get; set; }
    public int? HumanOverrideCategoryId { get; set; }
    public AICategory? HumanOverrideCategory { get; set; }
    public Guid? HumanCorrectedByUserId { get; set; }
    public DateTime? HumanCorrectionAtUtc { get; set; }
}

/// <summary>
/// Entity row for a trained-model prediction against a session, linked to category lookups
/// rather than free-text labels.
/// </summary>
public sealed class AITrackingPrediction
{
    public long PredictionId { get; set; }

    public long SessionId { get; set; }
    public AITrackingSession TrackingSession { get; set; } = null!;

    public int PredictedCategoryId { get; set; }
    public AICategory PredictedCategory { get; set; } = null!;

    public float PredictedScore { get; set; }

    public int? ActualCategoryId { get; set; }
    public AICategory? ActualCategory { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public static class AITrackingSourceKinds
{
    public const string Ticket = "ticket";
}
