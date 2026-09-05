using HomeworkCentral.Api.Assessment;
using Xunit;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class TrainingPersistencePolicyTests
{
    [Theory]
    [InlineData("Cancelled", 0, true)]
    [InlineData("cancelled", 0, true)]
    [InlineData("Cancelled", 2, false)]
    [InlineData("Running", 0, false)]
    [InlineData("Queued", 0, false)]
    [InlineData("Completed", 0, false)]
    [InlineData("Failed", 0, false)]
    public void CanResumeContinuousTraining_requires_cancelled_continuous(
        string status,
        int requestedTicketCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            TrainingPersistencePolicy.CanResumeContinuousTraining(status, requestedTicketCount));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Queued", true)]
    [InlineData("Continuous training", true)]
    [InlineData("Running", true)]
    [InlineData("Completed", false)]
    [InlineData("completed", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Stop cancelled", false)]
    [InlineData("Failed", false)]
    [InlineData("Worker failed", false)]
    public void IsActiveLivePhase_matches_in_memory_progress_contract(string? phase, bool expected)
    {
        Assert.Equal(expected, TrainingPersistencePolicy.IsActiveLivePhase(phase));
    }
}
