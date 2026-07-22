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
    float PriorScore,
    float AppliedSubjectCountNorm = 0f,
    float ExactSubjectMatch = 0f,
    float RelatedSubjectMatch = 0f,
    float CrossSubjectSupport = 0f,
    IReadOnlyList<float>? AppliedSubjectMultiHot = null,
    IReadOnlyList<float>? ChannelSubjectMultiHot = null,
    IReadOnlyList<float>? CascadeContext = null)
{
    public static ChatMonitoringNeuralModelInput Create(
        string requirement,
        string threadContext,
        string message,
        float communityVote,
        float threadContinuity,
        float priorScore,
        SubjectSignalSnapshot subjects,
        IReadOnlyList<float>? cascadeContext = null) =>
        new(
            requirement,
            threadContext,
            message,
            communityVote,
            subjects.EffectiveChannelRelevance,
            threadContinuity,
            priorScore,
            subjects.AppliedCountNorm,
            subjects.ExactMatch,
            subjects.RelatedMatch,
            subjects.CrossSubjectSupport,
            ToMultiHot(subjects.AppliedGenerals),
            ToMultiHot(subjects.ChannelGeneral is null ? [] : [subjects.ChannelGeneral]),
            cascadeContext);

    private static float[] ToMultiHot(IReadOnlyList<string> generals)
    {
        float[] hot = new float[ChatMonitoringSubjectSignals.GeneralSubjectCount];
        foreach (string general in generals)
        {
            int index = ChatMonitoringSubjectSignals.GeneralIndex(general);
            if (index >= 0) hot[index] = 1f;
        }

        return hot;
    }
}

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

/// <summary>
/// In-process chat-monitoring student model. Predictions and training mutate process-local
/// weights only; canonical promotion remains outside this contract.
/// </summary>
public interface IChatMonitoringNeuralModel
{
    NeuralModelKindChatMonitoring Kind { get; }
    ChatMonitoringNeuralModelPrediction Predict(ChatMonitoringNeuralModelInput input);
    void Train(ChatMonitoringNeuralModelInput input, ChatMonitoringNeuralModelTargets targets, int epochs = 12);
}

/// <summary>
/// Telemetry-capable student model used for replay capture and admin training sessions.
/// Callers own disposal; parameter snapshots are process-local until promotion persists them.
/// </summary>
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

/// <summary>
/// Resolves the active moderation/tutoring student models for live scoring and training modes.
/// </summary>
public interface IChatMonitoringNeuralModelFactory
{
    IChatMonitoringNeuralModel Get(NeuralModelKindChatMonitoring kind);
    IReadOnlyList<IChatMonitoringNeuralModel> Resolve(NeuralTrainingMode mode);
}
