using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Serially publishes canonical generations per chat-monitor kind. Worker candidates are diagnostic only;
/// promotion always retrains a fresh session-local hashed-MLP model from the approved examples.
/// </summary>
public sealed class NeuralNetTrainingPromoter(AppDbContext db, NeuralNetCheckpointStore checkpoints)
{
    public async Task QueueSessionAsync(Guid sessionId, CancellationToken ct)
    {
        List<NeuralModelKindChatMonitoring> kinds = await db.ChatMonitoringNeuralModelRuns.AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.Status == "Completed")
            .Select(x => x.ChatMonitoringKind).ToListAsync(ct);
        foreach (NeuralModelKindChatMonitoring chatMonitoringKind in kinds)
            await QueueSessionAsync(sessionId, chatMonitoringKind, ct);
    }

    public async Task QueueSessionAsync(Guid sessionId, NeuralModelKindChatMonitoring chatMonitoringKind, CancellationToken ct)
    {
        if (await db.NeuralNetTrainingPromotions.AnyAsync(x => x.SessionId == sessionId && x.ChatMonitoringKind == chatMonitoringKind, ct)) return;
        long sequence = (await db.NeuralNetTrainingPromotions.Where(x => x.ChatMonitoringKind == chatMonitoringKind)
            .MaxAsync(x => (long?)x.PromotionSequence, ct) ?? 0) + 1;
        db.NeuralNetTrainingPromotions.Add(new NeuralNetTrainingPromotion
        {
            PromotionId = Guid.NewGuid(),
            SessionId = sessionId,
            ChatMonitoringKind = chatMonitoringKind,
            PromotionSequence = sequence,
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> PromoteNextAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        Guid lease = Guid.NewGuid();
        NeuralNetTrainingPromotion? candidate = await db.NeuralNetTrainingPromotions
            .Where(x => x.Status != "Promoted" && x.Status != "Rejected")
            .OrderBy(x => x.ChatMonitoringKind).ThenBy(x => x.PromotionSequence).FirstOrDefaultAsync(ct);
        if (candidate is null || !CanClaim(candidate.Status, candidate.LeaseExpiresAtUtc, now)) return false;

        int claimed = await db.NeuralNetTrainingPromotions.Where(x => x.PromotionId == candidate.PromotionId
                && (x.Status == "Pending" || (x.Status == "RetryPending" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now)) || (x.Status == "Promoting" && x.LeaseExpiresAtUtc < now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Promoting")
                .SetProperty(x => x.LeaseId, lease)
                .SetProperty(x => x.LeaseExpiresAtUtc, now.AddMinutes(10))
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
        if (claimed == 0) return false;

        try
        {
            List<TicketModelTrainingExample> examples = await db.TicketModelTrainingExamples
                .Where(x => x.NeuralNetTrainingSessionId == candidate.SessionId
                            && x.ChatMonitoringKind == candidate.ChatMonitoringKind
                            && x.CanonicalGenerationApplied == null)
                .OrderBy(x => x.ApprovedAtUtc).ThenBy(x => x.TrainingExampleId).ToListAsync(ct);
            IChatMonitoringNeuralModelTelemetry model = CreateSessionLocalModel(candidate.ChatMonitoringKind);
            NeuralNetCanonicalCheckpoint? current = await checkpoints.GetCurrentAsync(candidate.ChatMonitoringKind, ct);
            NeuralNetParameterSnapshot initial = current is null
                ? model.GetParameterSnapshot(null, 0)
                : new NeuralNetParameterSnapshot(current.Generation, 0, "ieee754-float32-le", "dense-base64", model.GetTopologySnapshot().Parameters.Count, current.ParametersBase64, current.Checksum);
            if (current is not null) model.LoadParameterSnapshot(initial);
            foreach (TicketModelTrainingExample example in examples)
            {
                ChatMonitoringNeuralModelInput input = new(example.Requirement, example.ContextSnapshot ?? string.Empty,
                    example.BootstrapMessage ?? string.Empty, 0, 1, 0, .5f);
                model.Train(input, new ChatMonitoringNeuralModelTargets((float)example.TargetScore, (float)example.TargetRelevance));
            }
            NeuralNetParameterSnapshot snapshot = model.GetParameterSnapshot(null, examples.Count);
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            long generation = await checkpoints.PublishAsync(candidate.ChatMonitoringKind, model.GetStateSnapshot().ModelVersion, snapshot, ct);
            foreach (TicketModelTrainingExample example in examples) example.CanonicalGenerationApplied = generation;
            NeuralNetTrainingPromotion promotion = await db.NeuralNetTrainingPromotions.SingleAsync(x => x.PromotionId == candidate.PromotionId && x.LeaseId == lease, ct);
            promotion.Status = "Promoted";
            promotion.PromotedGeneration = generation;
            promotion.CompletedAtUtc = DateTime.UtcNow;
            promotion.LeaseExpiresAtUtc = null;
            ChatMonitoringNeuralModelRun? sourceRun = await db.ChatMonitoringNeuralModelRuns
                .SingleOrDefaultAsync(x => x.SessionId == candidate.SessionId && x.ChatMonitoringKind == candidate.ChatMonitoringKind, ct);
            string sourceChecksum = NeuralNetReplaySerializer.ComputeSha256(sourceRun?.WorkerReplayJson ?? string.Empty);
            PromotionValidationResult validation = new(true, ["Ordered lease held", "Examples not previously promoted", "Hashed MLP replayed approved examples"], [], current?.Checksum ?? initial.Checksum, snapshot.Checksum, examples.Count, examples.Count * 12);
            ReplayIntegrity integrity = new("hc-replay-canonical-json-v1", "sha-256", string.Empty, initial.Checksum, snapshot.Checksum, string.Empty);
            ModelPromotionReplay replay = new(promotion.PromotionId, promotion.SessionId, sourceChecksum, new([]), initial, examples.Select(x => x.TrainingExampleId).ToList(), snapshot, validation, integrity);
            promotion.PromotionReportJson = JsonSerializer.Serialize(replay);
            if (sourceRun is not null)
            {
                sourceRun.PromotionReplayJson = promotion.PromotionReportJson;
                sourceRun.CanonicalGeneration = generation;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            model.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NeuralNetTrainingPromotion item = await db.NeuralNetTrainingPromotions.SingleAsync(x => x.PromotionId == candidate.PromotionId, CancellationToken.None);
            item.Status = item.AttemptCount >= 3 ? "Rejected" : "RetryPending";
            item.FailureReason = ex.Message[..Math.Min(1000, ex.Message.Length)];
            item.LeaseExpiresAtUtc = null;
            await db.SaveChangesAsync(CancellationToken.None);
            return false;
        }
    }

    public static bool CanClaim(string status, DateTime? leaseExpiresAtUtc, DateTime now)
    {
        bool leaseExpired = leaseExpiresAtUtc is not null && leaseExpiresAtUtc < now;
        return status == "Pending" || (status == "RetryPending" && (leaseExpiresAtUtc is null || leaseExpired)) || (status == "Promoting" && leaseExpired);
    }

    private static IChatMonitoringNeuralModelTelemetry CreateSessionLocalModel(NeuralModelKindChatMonitoring chatMonitoringKind) => chatMonitoringKind switch
    {
        NeuralModelKindChatMonitoring.Moderation => new ModerationChatMonitorNeuralNet(),
        NeuralModelKindChatMonitoring.Tutoring => new TutoringChatMonitorNeuralNet(),
        _ => throw new ArgumentOutOfRangeException(nameof(chatMonitoringKind), chatMonitoringKind, null),
    };
}
