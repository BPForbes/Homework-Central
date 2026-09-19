namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Heap-spill helpers shared by the training loop and tests. Release traces first,
/// then snapshot compact weights. Same-process continue keeps the live net;
/// resume/restart reloads <c>spill-checkpoint-v1</c> from the run row.
/// </summary>
internal static class TrainingHeapSpill
{
    public const int MaxLiveFeedLines = 8;
    public const int MaxWeightFeedLines = 24;

    public const string HeapElevatedMessage = TrainingPersistencePolicy.HeapElevatedMessage;

    /// <summary>
    /// Drops accumulated traces before allocating a compact snapshot so the GC
    /// can reclaim <c>BuildForwardTrace</c> bags ([runtime#58974] catch-and-allocate).
    /// </summary>
    public static TrainingHeapSpillPrepareResult TryPrepare(
        Action releaseAccumulatedHeap,
        Func<NeuralNetParameterSnapshot?> trySnapshot,
        Guid sessionId,
        NeuralModelKindChatMonitoring kind,
        int ticketsProcessed)
    {
        ArgumentNullException.ThrowIfNull(releaseAccumulatedHeap);
        ArgumentNullException.ThrowIfNull(trySnapshot);

        releaseAccumulatedHeap();
        NeuralNetParameterSnapshot? snapshot = trySnapshot();
        if (snapshot is null)
            return TrainingHeapSpillPrepareResult.Failed();

        string json = TrainingSpillCheckpoint.Serialize(sessionId, kind, ticketsProcessed, snapshot);
        if (!TrainingSpillCheckpoint.TryParse(json, out TrainingSpillCheckpoint? parsed) || parsed is null)
            return TrainingHeapSpillPrepareResult.Failed();

        return new TrainingHeapSpillPrepareResult(true, json, snapshot);
    }

    public static bool TryRestore(string? workerReplayJson, Action<NeuralNetParameterSnapshot> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (!TrainingSpillCheckpoint.TryParse(workerReplayJson, out TrainingSpillCheckpoint? checkpoint)
            || checkpoint is null)
        {
            return false;
        }

        load(checkpoint.Parameters);
        return true;
    }

    /// <summary>
    /// Ticket cursor stored with the weights. Resume must continue from this
    /// count so a paused continuous session does not look empty.
    /// </summary>
    public static int TicketsProcessedFromCheckpoint(string? workerReplayJson)
    {
        if (!TrainingSpillCheckpoint.TryParse(workerReplayJson, out TrainingSpillCheckpoint? checkpoint)
            || checkpoint is null)
        {
            return 0;
        }

        return checkpoint.TicketsProcessed;
    }

    /// <summary>
    /// Continue cursor: leftover in-process progress wins when the spill
    /// write missed; otherwise the highest checkpoint ticket count.
    /// Messages and examples stay leftover (0 when only a checkpoint exists).
    /// </summary>
    public static ContinuousResumeSeed SeedContinuousResume(
        int leftoverTicketsProcessed,
        int leftoverMessagesProcessed,
        int leftoverExamplesPersisted,
        IEnumerable<string?> workerReplayJsons)
    {
        ArgumentNullException.ThrowIfNull(workerReplayJsons);
        int fromCheckpoints = workerReplayJsons
            .Select(TicketsProcessedFromCheckpoint)
            .DefaultIfEmpty(0)
            .Max();
        return new ContinuousResumeSeed(
            Math.Max(leftoverTicketsProcessed, fromCheckpoints),
            leftoverMessagesProcessed,
            leftoverExamplesPersisted);
    }

    public static TrainingStepAfterSpill AfterOutOfMemory(bool spillSucceeded) =>
        spillSucceeded ? TrainingStepAfterSpill.AdvanceWithoutRetry : TrainingStepAfterSpill.Stop;

    public static TrainingStepAfterSpill AfterProactiveAttempt(bool spillSucceeded) =>
        spillSucceeded ? TrainingStepAfterSpill.Continue : TrainingStepAfterSpill.Stop;

    public static bool ShouldKeepSpillCheckpoint(string? workerReplayJson) =>
        TrainingSpillCheckpoint.TryParse(workerReplayJson, out _);

    public static List<string> TrimFeed(IReadOnlyList<string>? feed, int keep = MaxLiveFeedLines)
    {
        if (feed is null || feed.Count == 0)
            return [];

        return feed.Count <= keep ? [.. feed] : [.. feed.Skip(feed.Count - keep)];
    }

    public static List<string> CapFeed(IReadOnlyList<string> lines, int max = MaxWeightFeedLines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (max <= 0 || lines.Count <= max)
            return [.. lines];

        int shown = Math.Max(1, max - 1);
        return [.. lines.Take(shown), $"… {lines.Count - shown} more"];
    }

    public static NeuralNetTrainingLiveProgress BoundAfterSpill(
        NeuralNetTrainingLiveProgress progress,
        int ticketsProcessed) =>
        progress with
        {
            Phase = "Heap spill · checkpoint written",
            TicketsRequested = Math.Max(progress.TicketsRequested, ticketsProcessed),
            LatestTrainingLlmSummary = Truncate(
                $"Heap spilled after ticket {ticketsProcessed}; latest weights kept in the live net.",
                280),
            WeightUpdateFeed = [],
            ActiveNodeIndexes = [],
            ActiveEdgeParameterIndexes = [],
            ActiveLayerIndex = null,
            AuditFeedbackFeed = TrimFeed(progress.AuditFeedbackFeed),
            GeneratorHints = TrimFeed(progress.GeneratorHints),
        };

    public static NeuralNetTrainingLiveProgress BoundAfterCancel(NeuralNetTrainingLiveProgress progress) =>
        progress with
        {
            Phase = "Paused",
            WeightUpdateFeed = [],
            ActiveNodeIndexes = [],
            ActiveEdgeParameterIndexes = [],
            ActiveLayerIndex = null,
            AuditFeedbackFeed = TrimFeed(progress.AuditFeedbackFeed),
            GeneratorHints = TrimFeed(progress.GeneratorHints),
            PathTone = "idle",
        };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

internal readonly record struct ContinuousResumeSeed(
    int TicketsProcessed,
    int MessagesProcessed,
    int ExamplesPersisted);

internal readonly record struct TrainingHeapSpillPrepareResult(
    bool Succeeded,
    string? Json,
    NeuralNetParameterSnapshot? Snapshot)
{
    public static TrainingHeapSpillPrepareResult Failed() => new(false, null, null);
}

internal enum TrainingStepAfterSpill
{
    Continue,
    AdvanceWithoutRetry,
    Stop,
}

/// <summary>Proactive spill failed; the training loop must stop instead of allocating again.</summary>
internal sealed class TrainingHeapSpillFailedException : InvalidOperationException
{
    public TrainingHeapSpillFailedException(string message)
        : base(message)
    {
    }
}
