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

public sealed class NeuralNetVisualizerModelDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NeuralModelKindChatMonitoring ChatMonitoringKind { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public IReadOnlyList<int> LayerWidths { get; set; } = [];
    public IReadOnlyList<string> LayerLabels { get; set; } = [];
    public int ParameterCount { get; set; }
    public int SupportExamples { get; set; }
    public int NodeCount { get; set; }

    /// <summary>Stage-1 router widths for cascade g(f(x)).</summary>
    public IReadOnlyList<int> Stage1LayerWidths { get; set; } = [30, 24, 8];

    /// <summary>Human label for stage-1 (concept-context vs subject-context).</summary>
    public string Stage1Role { get; set; } = "context-router";

    /// <summary>Softmax category vocabulary size (excludes evidence/relevance).</summary>
    public int CategoryCount { get; set; }

    public string CascadeComposition { get; set; } = "g(f(x))";
    public string ChainRuleSummary { get; set; } = "∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f)";
    /// <summary>Checkpoint/runtime lineage id; Math.NET-backed engine keeps HashedMlpV8 packing.</summary>
    public string RuntimeKind { get; set; } = "HashedMlpV8";
}

public sealed class NeuralNetVisualizerDto
{
    public IReadOnlyList<NeuralNetVisualizerModelDto> Models { get; set; } = [];
    public List<string> OutputNodes { get; set; } = ["Evidence score", "Relevance"];
    public int TrainingExamples { get; set; }

    // Legacy single-graph fields kept for older clients; populated from the first model.
    public int InputNodes { get; set; }
    public int HiddenNodes { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
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
    public IReadOnlyList<ChatMonitoringNeuralModelRunDto> ChatMonitoringRuns { get; set; } = [];
    public NeuralNetTrainingLiveProgressDto? LiveProgress { get; set; }
}

public sealed class NeuralNetTrainingLiveProgressDto
{
    public string Phase { get; set; } = string.Empty;
    public int TicketsRequested { get; set; }
    public int TicketsGenerated { get; set; }
    public int TicketsProcessed { get; set; }
    public int MessagesProcessed { get; set; }
    public int ExamplesPersisted { get; set; }
    public int AuditsCompleted { get; set; }
    public string? ActiveChatMonitoringKind { get; set; }
    public string? LatestLlm1Summary { get; set; }
    public string? LatestLlm2Feedback { get; set; }
    public string? LatestLossSummary { get; set; }
    public IReadOnlyList<string> GeneratorHints { get; set; } = [];
    public IReadOnlyList<string> WeightUpdateFeed { get; set; } = [];
    /// <summary>forward | reeval | backprop | accepted | revision | idle</summary>
    public string PathTone { get; set; } = "idle";
    public IReadOnlyList<int> LayerWidths { get; set; } = [];
    public IReadOnlyList<string> LayerLabels { get; set; } = [];
    public IReadOnlyList<int> ActiveNodeIndexes { get; set; } = [];
    public IReadOnlyList<int> ActiveEdgeParameterIndexes { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ChatMonitoringNeuralModelRunDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NeuralModelKindChatMonitoring ChatMonitoringKind { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? CanonicalGeneration { get; set; }
    public bool HasWorkerReplay { get; set; }
    public bool HasPromotionReplay { get; set; }
    public string? FailureReason { get; set; }
}
