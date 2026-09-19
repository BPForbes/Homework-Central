using HomeworkCentral.Api.Assessment;
using Xunit;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class ContinuousTrainingResolutionTests
{
    [Theory]
    [InlineData(true, 3, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, true)]
    [InlineData(false, 0, true)]
    [InlineData(false, -1, true)]
    [InlineData(false, 1, false)]
    [InlineData(false, 10, false)]
    public void ResolveContinuousTraining_matches_train_until_stop_contract(
        bool continuousFlag,
        int ticketCount,
        bool expectedContinuous)
    {
        Assert.Equal(
            expectedContinuous,
            NeuralNetTrainingService.ResolveContinuousTraining(continuousFlag, ticketCount));
    }

    [Theory]
    [InlineData("Queued", true)]
    [InlineData("queued", true)]
    [InlineData("Running", true)]
    [InlineData("RUNNING", true)]
    [InlineData("Cancelled", false)]
    [InlineData("Completed", false)]
    [InlineData(null, false)]
    public void IsActiveTrainingStatus_matches_stop_path(string? status, bool expected)
    {
        Assert.Equal(expected, NeuralNetTrainingService.IsActiveTrainingStatus(status));
    }

    [Fact]
    public async Task ApplyStopsAsync_calls_stop_for_each_session_id()
    {
        List<Guid> sessionIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        List<Guid> seen = [];

        int paused = await NeuralNetTrainingService.ApplyStopsAsync(
            sessionIds,
            sessionId =>
            {
                seen.Add(sessionId);
                return Task.FromResult(sessionId != sessionIds[1]);
            });

        Assert.Equal(2, paused);
        Assert.Equal(sessionIds, seen);
    }
}
