namespace HomeworkCentral.Api.Assessment;

public enum NeuralModelKindChatMonitoring
{
    Moderation,
    Tutoring,
}

public sealed record ChatMonitoringNeuralModelInput(
    string Requirement,
    string ThreadContext,
    string Message,
    float CommunityVote,
    float ChannelRelevance,
    float ThreadContinuity,
    float PriorScore);

/// <summary>
/// Supervised targets. Evidence/relevance use sigmoid+BCE; <see cref="CategoryIndex"/> uses
/// softmax + categorical cross-entropy (3Blue1Brown multi-class cost). Use -1 to derive the
/// index from the training example category string.
/// </summary>
public sealed record ChatMonitoringNeuralModelTargets(float Evidence, float Relevance, int CategoryIndex = -1);

public sealed record ChatMonitoringNeuralModelPrediction(
    float Evidence,
    float Relevance,
    float Confidence,
    NeuralModelKindChatMonitoring ChatMonitoringKind,
    string ModelVersion,
    string Category,
    string Reasoning,
    float CategoryConfidence = 0f);

public sealed record ChatMonitoringNeuralModelTrainingExample(
    ChatMonitoringNeuralModelInput Input,
    ChatMonitoringNeuralModelTargets Targets,
    string Category);

public sealed record ChatMonitoringNeuralModelInferenceTrace(
    ChatMonitoringNeuralModelPrediction Prediction,
    ForwardPropagationTrace Forward);

public sealed record ChatMonitoringNeuralModelStateSnapshot(
    NeuralModelKindChatMonitoring ChatMonitoringKind,
    string ModelVersion,
    IReadOnlyList<int> LayerWidths,
    IReadOnlyList<string> LayerLabels,
    int ParameterCount,
    float ParameterL2Norm,
    int SupportExamples);

public interface IChatMonitoringNeuralModel
{
    NeuralModelKindChatMonitoring Kind { get; }
    ChatMonitoringNeuralModelPrediction Predict(ChatMonitoringNeuralModelInput input);
    void Train(ChatMonitoringNeuralModelInput input, ChatMonitoringNeuralModelTargets targets, int epochs = 12);
}

public interface IChatMonitoringNeuralModelTelemetry : IChatMonitoringNeuralModel, IDisposable
{
    ChatMonitoringNeuralModelInferenceTrace PredictWithTrace(ChatMonitoringNeuralModelInput input);
    TrainingPassTrace TrainWithTrace(
        ChatMonitoringNeuralModelTrainingExample example,
        int epochs = 12,
        NeuralTrainingTraceDetail detail = NeuralTrainingTraceDetail.Full,
        float evidenceTolerance = 0f,
        float relevanceTolerance = 0f,
        float lossStopThreshold = 0f);
    /// <summary>
    /// Mini-batch SGD as in 3Blue1Brown: C = (1/n) Σ C_x and one update along −∇C.
    /// </summary>
    TrainingPassTrace TrainMiniBatchWithTrace(
        IReadOnlyList<ChatMonitoringNeuralModelTrainingExample> examples,
        int epochs = 12,
        NeuralTrainingTraceDetail detail = NeuralTrainingTraceDetail.Full,
        float evidenceTolerance = 0f,
        float relevanceTolerance = 0f,
        float lossStopThreshold = 0f);
    NeuralNetTopologySnapshot GetTopologySnapshot();
    NeuralNetParameterSnapshot GetParameterSnapshot(long? canonicalGeneration, int localRevision);
    ChatMonitoringNeuralModelStateSnapshot GetStateSnapshot();
    void LoadParameterSnapshot(NeuralNetParameterSnapshot snapshot);
}

public interface IChatMonitoringNeuralModelFactory
{
    IChatMonitoringNeuralModel Get(NeuralModelKindChatMonitoring kind);
    IReadOnlyList<IChatMonitoringNeuralModel> Resolve(NeuralTrainingMode mode);
}
