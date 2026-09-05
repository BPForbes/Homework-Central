using HomeworkCentral.Api.Services;

namespace HomeworkCentral.Api.Tests.Services;

public class ApplicationReadinessTests
{
    [Fact]
    public async Task WaitUntilReadyAsync_returns_true_when_already_ready()
    {
        ApplicationReadiness readiness = new();
        readiness.MarkReady();

        bool ready = await readiness.WaitUntilReadyAsync(CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_returns_false_when_startup_failed()
    {
        ApplicationReadiness readiness = new();
        readiness.MarkFailed("migrate failed");

        bool ready = await readiness.WaitUntilReadyAsync(CancellationToken.None);

        Assert.False(ready);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_waits_until_ready()
    {
        ApplicationReadiness readiness = new();
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            readiness.MarkReady();
        });

        bool ready = await readiness.WaitUntilReadyAsync(CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_throws_when_cancelled()
    {
        ApplicationReadiness readiness = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            readiness.WaitUntilReadyAsync(cts.Token));
    }
}
