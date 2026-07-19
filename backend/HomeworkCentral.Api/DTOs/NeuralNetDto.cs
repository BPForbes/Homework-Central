namespace HomeworkCentral.Api.DTOs;

public sealed class NeuralNetTrainingFeedbackDto
{
    public Guid ScoreEventId { get; set; }
    public Guid TicketId { get; set; }
    public Guid MessageId { get; set; }
    public string MessagePreview { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double StudentScore { get; set; }
    public double StudentConfidence { get; set; }
    public double ReviewerScore { get; set; }
    public double ReviewerConfidence { get; set; }
    public bool CorrectionNeeded { get; set; }
    public string? Explanation { get; set; }
    public string? Guidance { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class NeuralNetDataManagementDto
{
    public int PendingFeedback { get; set; }
    public int ApprovedFeedback { get; set; }
    public int RejectedFeedback { get; set; }
    public int TrainingExamples { get; set; }
    public int VectorExamples { get; set; }
    public Dictionary<string, int> CategoryCounts { get; set; } = [];
}

public sealed class NeuralNetVisualizerDto
{
    public int InputNodes { get; set; } = 256;
    public int HiddenNodes { get; set; } = 8;
    public List<string> OutputNodes { get; set; } = ["Evidence score", "Relevance"];
    public string ModelVersion { get; set; } = "hc-student-mlp-v1";
    public int TrainingExamples { get; set; }
}
