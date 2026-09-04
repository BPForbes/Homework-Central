using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Rebuilds live hashed-MLP chat monitors from approved rows after Kestrel listens.
/// Caps example/epoch work and uses silent <see cref="IChatMonitoringNeuralModel.Train"/> so startup
/// cannot OOM the host the way synchronous Full-trace Seed replay did.
/// </summary>
public sealed class ChatMonitoringNeuralModelWarmupService(
    IServiceScopeFactory scopeFactory,
    IChatMonitoringNeuralModelFactory chatMonitoringModels,
    ILogger<ChatMonitoringNeuralModelWarmupService> logger) : BackgroundService
{
    // Seed rows historically used 100 epochs × Full replay; that OuterProduct + FlattenParameters path OOMs on modest hosts.
    private const int MaxExamples = 256;
    private const int SeedEpochs = 8;
    private const int ApprovedEpochs = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield so Host.StartAsync (and Kestrel listen) finishes before training begins.
        await Task.Yield();

        try
        {
            await OperationalExceptionGuard.RunAsync(
                () => LoadApprovedExamplesAsync(stoppingToken),
                ex =>
                {
                    logger.LogWarning(
                        ex,
                        "Chat-monitoring neural-model warmup was skipped; inference will remain low confidence and use the reviewer.");
                    return Task.CompletedTask;
                });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown during warmup.
        }
        catch (OutOfMemoryException ex)
        {
            // Not in OperationalExceptionGuard's closed set; keep the host alive.
            logger.LogWarning(
                ex,
                "Chat-monitoring neural-model warmup aborted under memory pressure; inference will remain low confidence and use the reviewer.");
        }
    }

    private async Task LoadApprovedExamplesAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IVectorDocumentStore vectors = scope.ServiceProvider.GetRequiredService<IVectorDocumentStore>();
        ILlmClient llm = scope.ServiceProvider.GetRequiredService<ILlmClient>();
        List<TicketModelTrainingExample> examples = await db.TicketModelTrainingExamples
            .AsNoTracking().OrderBy(x => x.ApprovedAtUtc).Take(MaxExamples).ToListAsync(ct);
        Guid[] messageIds = examples.Where(x => x.MessageId.HasValue).Select(x => x.MessageId!.Value).Distinct().ToArray();
        Dictionary<Guid, string> messages = await db.ChatMessages.AsNoTracking()
            .Where(x => messageIds.Contains(x.MessageId))
            .ToDictionaryAsync(x => x.MessageId, x => x.RawContent, ct);

        int loaded = 0;
        foreach (TicketModelTrainingExample row in examples)
        {
            if (ct.IsCancellationRequested)
                break;

            string? message = row.MessageId is Guid id ? messages.GetValueOrDefault(id) : row.BootstrapMessage;
            if (string.IsNullOrWhiteSpace(message))
                continue;

            string threadContext = row.ContextSnapshot ?? string.Empty;
            IChatMonitoringNeuralModel model = chatMonitoringModels.Get(row.ChatMonitoringKind);
            ChatMonitoringNeuralModelInput input = new(
                row.Requirement,
                threadContext,
                message,
                0,
                1,
                0,
                .5f,
                TextEmbedding: await llm.EmbedAsync(message, ct));
            int epochs = row.Source == "Seed" ? SeedEpochs : ApprovedEpochs;
            try
            {
                model.Train(
                    input,
                    new ChatMonitoringNeuralModelTargets((float)row.TargetScore, (float)row.TargetRelevance),
                    epochs);
            }
            catch (OutOfMemoryException ex)
            {
                // OutOfMemoryException is not an operational DB/IO failure; stop replay and keep the API up.
                logger.LogWarning(
                    ex,
                    "Chat-monitoring neural-model warmup stopped after {Count} examples due to memory pressure.",
                    loaded);
                break;
            }

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
}
