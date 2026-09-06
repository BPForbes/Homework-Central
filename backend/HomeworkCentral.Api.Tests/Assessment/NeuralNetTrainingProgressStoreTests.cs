using HomeworkCentral.Api.Assessment;
using Xunit;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class NeuralNetTrainingProgressStoreTests
{
    [Fact]
    public void HasActiveTraining_is_true_for_live_continuous_phase()
    {
        NeuralNetTrainingProgressStore store = new();
        Guid sessionId = Guid.NewGuid();
        store.Upsert(Progress(sessionId, "Continuous training"));

        Assert.True(store.HasActiveTraining());
        Assert.Single(store.GetAll());
        Assert.Equal("Continuous training", store.Get(sessionId)?.Phase);
    }

    [Fact]
    public void HasActiveTraining_is_false_after_clear_or_terminal_phase()
    {
        NeuralNetTrainingProgressStore store = new();
        Guid sessionId = Guid.NewGuid();
        store.Upsert(Progress(sessionId, "Queued"));
        Assert.True(store.HasActiveTraining());

        store.Upsert(Progress(sessionId, "Cancelled"));
        Assert.False(store.HasActiveTraining());

        store.Upsert(Progress(sessionId, "Running"));
        store.Clear(sessionId);
        Assert.False(store.HasActiveTraining());
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void HasActiveTraining_is_false_after_bound_cancel_phase()
    {
        NeuralNetTrainingProgressStore store = new();
        Guid sessionId = Guid.NewGuid();
        store.Upsert(Progress(sessionId, "Continuous training"));
        store.Upsert(TrainingHeapSpill.BoundAfterCancel(store.Get(sessionId)!));

        Assert.False(store.HasActiveTraining());
        Assert.Equal("Cancelled", store.Get(sessionId)?.Phase);
        Assert.Empty(store.Get(sessionId)!.WeightUpdateFeed);
    }

    private static NeuralNetTrainingLiveProgress Progress(Guid sessionId, string phase) =>
        new(
            sessionId,
            phase,
            TicketsRequested: 0,
            TicketsGenerated: 0,
            TicketsProcessed: 0,
            MessagesProcessed: 0,
            ExamplesPersisted: 0,
            AuditsCompleted: 0,
            ActiveChatMonitoringKind: null,
            LatestTrainingLlmSummary: null,
            LatestAuditFeedback: null,
            LatestLossSummary: null,
            GeneratorHints: Array.Empty<string>(),
            AuditFeedbackFeed: Array.Empty<string>(),
            CurrentEvaluationData: null,
            WeightUpdateFeed: Array.Empty<string>(),
            PathTone: "idle",
            LayerWidths: Array.Empty<int>(),
            LayerLabels: Array.Empty<string>(),
            ActiveNodeIndexes: Array.Empty<int>(),
            ActiveEdgeParameterIndexes: Array.Empty<int>(),
            UpdatedAtUtc: DateTime.UtcNow);
}
