namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Training SQL is written at session start (Queued row) and again when a run
/// stops, completes, or fails — not after every continuous step.
/// </summary>
public static class TrainingPersistencePolicy
{
    public static bool CanResumeContinuousTraining(string status, int requestedTicketCount) =>
        requestedTicketCount == 0 &&
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

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
