using HomeworkCentral.Api.Assessment;
using System.Text.Json.Serialization;

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

public sealed class StartNeuralNetTrainingRequest
{
    public int TicketCount { get; set; } = 3;
    public int MaxPassesPerTicket { get; set; } = 3;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NeuralTrainingMode Mode { get; set; } = NeuralTrainingMode.Both;
}

public sealed class NeuralNetTrainingSessionDto
{
    public Guid SessionId { get; set; }
    public int RequestedTicketCount { get; set; }
    public int MaxPassesPerTicket { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NeuralTrainingMode Mode { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public bool HasReport { get; set; }
}
