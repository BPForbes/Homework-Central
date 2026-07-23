using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Assessment;

public sealed class AssessmentWorker(
    IAssessmentQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AssessmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (AssessmentMessageJob job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IAssessmentPipelineService pipeline =
                    scope.ServiceProvider.GetRequiredService<IAssessmentPipelineService>();
                await pipeline.ProcessMessageAsync(job, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Assessment worker failed for message {MessageId}", job.MessageId);
            }
        }
    }
}
