namespace HomeworkCentral.Api.Assessment;

public sealed class ChatMonitoringNeuralModelFactory(
    ModerationChatMonitorNeuralNet moderation,
    TutoringChatMonitorNeuralNet tutoring) : IChatMonitoringNeuralModelFactory
{
    public IChatMonitoringNeuralModel Get(NeuralModelKindChatMonitoring kind) => kind switch
    {
        NeuralModelKindChatMonitoring.Moderation => moderation,
        NeuralModelKindChatMonitoring.Tutoring => tutoring,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown chat-monitoring neural model."),
    };

    public IReadOnlyList<IChatMonitoringNeuralModel> Resolve(NeuralTrainingMode mode) => mode switch
    {
        NeuralTrainingMode.Moderation => [moderation],
        NeuralTrainingMode.Tutoring => [tutoring],
        NeuralTrainingMode.Both => [moderation, tutoring],
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown neural training mode."),
    };
}
