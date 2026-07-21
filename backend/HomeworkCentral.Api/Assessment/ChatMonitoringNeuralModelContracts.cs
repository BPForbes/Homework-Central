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

public sealed record ChatMonitoringNeuralModelTargets(float Evidence, float Relevance);

public sealed record ChatMonitoringNeuralModelPrediction(
    float Evidence,
    float Relevance,
    float Confidence,
    NeuralModelKindChatMonitoring ChatMonitoringKind,
    string ModelVersion,
    string Category,
    string Reasoning);

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
    TrainingPassTrace TrainWithTrace(ChatMonitoringNeuralModelTrainingExample example, int epochs = 12);
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
