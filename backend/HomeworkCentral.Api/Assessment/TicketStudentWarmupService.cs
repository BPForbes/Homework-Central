using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Rebuilds the tiny in-memory model and its retrieval mirror from approved DB rows.</summary>
public sealed class TicketStudentWarmupService(
    IServiceScopeFactory scopeFactory,
    ITicketStudentModel student,
    ILogger<TicketStudentWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
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
                if (string.IsNullOrWhiteSpace(message)) continue;
                string modelMessage = string.IsNullOrWhiteSpace(row.ContextSnapshot) ? message : $"{row.ContextSnapshot}\n<current_message>\n{message}\n</current_message>";
                student.Train(new(row.Requirement, modelMessage, row.TargetScore, row.TargetRelevance, row.Category), row.Source == "Seed" ? 100 : 16);
                await vectors.UpsertAsync(
                    VectorNamespaces.TicketTrainingExample, message, student.Embed(message), row.Category,
                    row.TrainingExampleId,
                    new { row.TrainingExampleId, row.MessageId, row.ScoreEventId, row.Category, row.TargetScore, row.TargetRelevance, row.Source }, ct);
                loaded++;
            }
            logger.LogInformation("Loaded {Count} approved ticket student training examples from PostgreSQL.", loaded);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ticket student warmup was skipped; inference will remain low confidence and use the reviewer.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
