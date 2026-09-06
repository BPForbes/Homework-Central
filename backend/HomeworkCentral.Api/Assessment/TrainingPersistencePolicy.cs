namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Training SQL is written at session start (Queued row) and again when a run
/// stops, completes, or fails — not after every continuous step.
/// Heap-pressure spill is the only mid-run SQL exception: persist compact
/// weights/bias (<c>spill-checkpoint-v1</c>) only — no example <c>AddRange</c>
/// or vector upsert. Empty in-memory traces first, then snapshot.
/// </summary>
public static class TrainingPersistencePolicy
{
    public const string HeapElevatedMessage =
        "The training heap is elevated. Stop the running session before starting or resuming another.";

    public static bool CanResumeContinuousTraining(string status, int requestedTicketCount) =>
        requestedTicketCount == 0 &&
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mid-run <c>SaveChanges</c> is allowed only for the emergency heap-spill
    /// persist. Normal continuous/finite steps stay in memory (persist-on-stop).
    /// </summary>
    public static bool AllowsMidRunSql(bool isEmergencyHeapSpill) => isEmergencyHeapSpill;

    public static bool IsTrainingStartBlocked(bool hasActiveTraining, bool heapElevated) =>
        hasActiveTraining && heapElevated;

    public static bool IsActiveLivePhase(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return false;
        }

        return !phase.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            && !phase.Contains("Cancelled", StringComparison.OrdinalIgnoreCase)
            && !phase.Contains("Failed", StringComparison.OrdinalIgnoreCase);
    }
}
