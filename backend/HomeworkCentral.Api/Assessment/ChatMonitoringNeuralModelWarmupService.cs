using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Rebuilds the isolated hashed-MLP chat-monitor lineages and retrieval mirror from approved rows.</summary>
public sealed class ChatMonitoringNeuralModelWarmupService(
    IServiceScopeFactory scopeFactory,
    IChatMonitoringNeuralModelFactory chatMonitoringModels,
    ILogger<ChatMonitoringNeuralModelWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Warmup must not block API startup when Postgres/vectors are unavailable.
        await OperationalExceptionGuard.RunAsync(
            () => LoadApprovedExamplesAsync(ct),
            ex =>
            {
                logger.LogWarning(
                    ex,
                    "Chat-monitoring neural-model warmup was skipped; inference will remain low confidence and use the reviewer.");
                return Task.CompletedTask;
            });
    }

    private async Task LoadApprovedExamplesAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IVectorDocumentStore vectors = scope.ServiceProvider.GetRequiredService<IVectorDocumentStore>();
        List<TicketModelTrainingExample> examples = await db.TicketModelTrainingExamples
            .AsNoTracking().OrderBy(x => x.ApprovedAtUtc).Take(2000).ToListAsync(ct);
        Guid[] messageIds = examples.Where(x => x.MessageId.HasValue).Select(x => x.MessageId!.Value).Distinct().ToArray();
        Dictionary<Guid, string> messages = await db.ChatMessages.AsNoTracking()
            .Where(x => messageIds.Contains(x.MessageId))
            .ToDictionaryAsync(x => x.MessageId, x => x.RawContent, ct);

        int loaded = 0;
        foreach (TicketModelTrainingExample row in examples)
        {
            string? message = row.MessageId is Guid id ? messages.GetValueOrDefault(id) : row.BootstrapMessage;
            if (string.IsNullOrWhiteSpace(message))
                continue;
            string threadContext = row.ContextSnapshot ?? string.Empty;
            IChatMonitoringNeuralModel model = chatMonitoringModels.Get(row.ChatMonitoringKind);
            ChatMonitoringNeuralModelInput input = new(row.Requirement, threadContext, message, 0, 1, 0, .5f);
            model.Train(
                input,
                new ChatMonitoringNeuralModelTargets((float)row.TargetScore, (float)row.TargetRelevance),
                row.Source == "Seed" ? 100 : 16);
            await vectors.UpsertAsync(
                VectorNamespaces.TicketTrainingExample,
                message,
                ChatMonitoringFeatureEncoder.EmbedText(message),
                ChatMonitoringVectorKeys.LineagePositionId(row.ChatMonitoringKind),
                row.TrainingExampleId,
                new
                {
                    row.TrainingExampleId,
                    row.MessageId,
                    row.ScoreEventId,
                    row.Category,
                    row.TargetScore,
                    row.TargetRelevance,
                    row.Source,
                    row.ChatMonitoringKind,
                },
                ct);
            loaded++;
        }

        logger.LogInformation(
            "Loaded {Count} approved chat-monitoring neural-model training examples from PostgreSQL.",
            loaded);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
