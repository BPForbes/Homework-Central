namespace HomeworkCentral.Api.Models;

/// <summary>
/// Session-level record of AI category labeling for a message in a ticket.
/// Tracks which neural model version generated the labels and when.
/// </summary>
public sealed class AITrackingSession
{
    public long Id { get; set; }

    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    /// <summary>Message index within the ticket thread (0-based).</summary>
    public int MessageIndex { get; set; }

    /// <summary>Which model lineage produced these labels (Moderation or Tutoring).</summary>
    public string NeuralModelKind { get; set; } = null!;

    /// <summary>Model version that generated the labels.</summary>
    public string ModelVersion { get; set; } = null!;

    /// <summary>When these labels were generated.</summary>
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<AITrackingCategoryWeight> CategoryWeights { get; set; } = new List<AITrackingCategoryWeight>();
}

/// <summary>
/// Per-category weight assignment from the LLM for a message.
/// Sparse: only categories with non-zero weight are stored.
/// </summary>
public sealed class AITrackingCategoryWeight
{
    public long Id { get; set; }

    public long TrackingSessionId { get; set; }
    public AITrackingSession TrackingSession { get; set; } = null!;

    /// <summary>Category slug (e.g., "harassment", "tutoring-mathematics").</summary>
    public string CategoryName { get; set; } = null!;

    /// <summary>Weight assigned by the LLM (0..1, typically sums to ~1 across all categories for a message).</summary>
    public double Weight { get; set; }

    /// <summary>If true, a human reviewer corrected this category assignment.</summary>
    public bool IsHumanCorrected { get; set; }

    /// <summary>If IsHumanCorrected, the category the human chose instead (nullable if human disagreed on weight distribution).</summary>
    public string? HumanCategoryOverride { get; set; }

    /// <summary>When the human correction was recorded (nullable if not corrected).</summary>
    public DateTime? HumanCorrectionAtUtc { get; set; }
}

/// <summary>
/// Optional: track what the model actually predicted vs. what it was trained on.
/// Useful for analyzing model performance on training examples.
/// </summary>
public sealed class AITrackingPrediction
{
    public long Id { get; set; }

    public long TrackingSessionId { get; set; }
    public AITrackingSession TrackingSession { get; set; } = null!;

    /// <summary>Category the model predicted (after training).</summary>
    public string PredictedCategory { get; set; } = null!;

    /// <summary>Model's confidence score for the prediction.</summary>
    public float PredictedScore { get; set; }

    /// <summary>Actual outcome if reviewable (e.g., was this message actually problematic?).</summary>
    public string? ActualOutcome { get; set; }

    /// <summary>When the prediction was recorded.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
