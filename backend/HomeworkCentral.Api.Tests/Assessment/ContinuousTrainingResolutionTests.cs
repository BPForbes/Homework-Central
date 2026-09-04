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
}
