using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Live mid-run training progress for the Server Neural Net UI (in-process only).</summary>
public sealed record NeuralNetTrainingLiveProgress(
    Guid SessionId,
    string Phase,
    int TicketsRequested,
    int TicketsGenerated,
    int TicketsProcessed,
    int MessagesProcessed,
    int ExamplesPersisted,
    int AuditsCompleted,
    string? ActiveChatMonitoringKind,
    string? LatestLlm1Summary,
    string? LatestLlm2Feedback,
    string? LatestLossSummary,
    IReadOnlyList<string> GeneratorHints,
    IReadOnlyList<string> WeightUpdateFeed,
    /// <summary>forward | reeval | backprop | accepted | revision | idle</summary>
    string PathTone,
    IReadOnlyList<int> LayerWidths,
    IReadOnlyList<string> LayerLabels,
    IReadOnlyList<int> ActiveNodeIndexes,
    IReadOnlyList<int> ActiveEdgeParameterIndexes,
    DateTime UpdatedAtUtc);

public interface INeuralNetTrainingProgressStore
{
    void Upsert(NeuralNetTrainingLiveProgress progress);
    NeuralNetTrainingLiveProgress? Get(Guid sessionId);
    void Clear(Guid sessionId);
}

public sealed class NeuralNetTrainingProgressStore : INeuralNetTrainingProgressStore
{
    private readonly ConcurrentDictionary<Guid, NeuralNetTrainingLiveProgress> _bySession = new();

    public void Upsert(NeuralNetTrainingLiveProgress progress) =>
        _bySession[progress.SessionId] = progress with { UpdatedAtUtc = DateTime.UtcNow };

    public NeuralNetTrainingLiveProgress? Get(Guid sessionId) =>
        _bySession.TryGetValue(sessionId, out NeuralNetTrainingLiveProgress? progress) ? progress : null;

    public void Clear(Guid sessionId) => _bySession.TryRemove(sessionId, out _);
}
