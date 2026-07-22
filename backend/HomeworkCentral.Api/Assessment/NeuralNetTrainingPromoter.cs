using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Serially publishes canonical chat-monitor generations under a database lease.
/// Worker candidates are diagnostic only; promotion retrains a fresh session-local
/// hashed-MLP model from approved examples before replacing the canonical checkpoint.
/// </summary>
public sealed class NeuralNetTrainingPromoter(AppDbContext db, NeuralNetCheckpointStore checkpoints)
{
    private const string StatusPending = "Pending";
    private const string StatusRetryPending = "RetryPending";
    private const string StatusPromoting = "Promoting";
    private const string StatusPromoted = "Promoted";
    private const string StatusRejected = "Rejected";
    private static readonly TimeSpan PromotionLeaseDuration = TimeSpan.FromMinutes(10);

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
            Status = StatusPending,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Claims the oldest eligible promotion lease and publishes one canonical
    /// generation. A false return means no pending, expired retry, or expired
    /// promoting lease was available to the worker.
    /// </summary>
    public async Task<bool> PromoteNextAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        PromotionLease? promotionLease = await TryClaimPromotionAsync(now, ct);
        if (promotionLease is null) return false;

        try
        {
            List<TicketModelTrainingExample> examples = await LoadUnpromotedExamplesAsync(promotionLease, ct);
            RetrainedSessionModel retrainedModel = await RetrainSessionLocalModelAsync(promotionLease, examples, ct);
            await PublishAndMarkExamplesAsync(promotionLease, examples, retrainedModel, ct);
            retrainedModel.Model.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkPromotionFailedAsync(promotionLease, ex);
            return false;
        }
    }

    /// <summary>
    /// Determines whether a promotion row can be claimed at the supplied instant.
    /// Expired Promoting leases are recoverable because a worker may have crashed
    /// after the claim update and before the checkpoint transaction committed.
    /// </summary>
    public static bool CanClaim(string status, DateTime? leaseExpiresAtUtc, DateTime now)
    {
        bool leaseExpired = leaseExpiresAtUtc is not null && leaseExpiresAtUtc < now;
        return status switch
        {
            StatusPending => true,
            StatusRetryPending => leaseExpiresAtUtc is null || leaseExpired,
            StatusPromoting => leaseExpired,
            _ => false,
        };
    }

    private async Task<PromotionLease?> TryClaimPromotionAsync(DateTime now, CancellationToken ct)
    {
        NeuralNetTrainingPromotion? candidate = await db.NeuralNetTrainingPromotions
            .AsNoTracking()
            .Where(x => x.Status != StatusPromoted && x.Status != StatusRejected)
            .OrderBy(x => x.ChatMonitoringKind)
            .ThenBy(x => x.PromotionSequence)
            .FirstOrDefaultAsync(ct);
        if (candidate is null || !CanClaim(candidate.Status, candidate.LeaseExpiresAtUtc, now))
            return null;

        Guid leaseId = Guid.NewGuid();
        DateTime leaseExpiresAtUtc = now.Add(PromotionLeaseDuration);
        // Pending -> Promoting is an atomic lease claim; concurrent workers lose
        // by observing zero updated rows rather than sharing a canonical write.
        int claimed = await db.NeuralNetTrainingPromotions
            .Where(x => x.PromotionId == candidate.PromotionId
                        && (x.Status == StatusPending
                            || (x.Status == StatusRetryPending
                                && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
                            || (x.Status == StatusPromoting && x.LeaseExpiresAtUtc < now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, StatusPromoting)
                .SetProperty(x => x.LeaseId, leaseId)
                .SetProperty(x => x.LeaseExpiresAtUtc, leaseExpiresAtUtc)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);

        return claimed == 0
            ? null
            : new PromotionLease(candidate.PromotionId, candidate.SessionId, candidate.ChatMonitoringKind, leaseId);
    }

    private async Task<List<TicketModelTrainingExample>> LoadUnpromotedExamplesAsync(
        PromotionLease promotionLease,
        CancellationToken ct) =>
        await db.TicketModelTrainingExamples
            .Where(x => x.NeuralNetTrainingSessionId == promotionLease.SessionId
                        && x.ChatMonitoringKind == promotionLease.ChatMonitoringKind
                        && x.CanonicalGenerationApplied == null)
            .OrderBy(x => x.ApprovedAtUtc)
            .ThenBy(x => x.TrainingExampleId)
            .ToListAsync(ct);

    private async Task<RetrainedSessionModel> RetrainSessionLocalModelAsync(
        PromotionLease promotionLease,
        IReadOnlyList<TicketModelTrainingExample> examples,
        CancellationToken ct)
    {
        IChatMonitoringNeuralModelTelemetry model = CreateSessionLocalModel(promotionLease.ChatMonitoringKind);
        NeuralNetCanonicalCheckpoint? current = await checkpoints.GetCurrentAsync(promotionLease.ChatMonitoringKind, ct);
        NeuralNetParameterSnapshot initial = current is null
            ? model.GetParameterSnapshot(null, 0)
            : new NeuralNetParameterSnapshot(
                current.Generation,
                0,
                "ieee754-float32-le",
                "dense-base64",
                model.GetTopologySnapshot().Parameters.Count,
                current.ParametersBase64,
                current.Checksum);

        if (current is not null)
            model.LoadParameterSnapshot(initial);

        foreach (TicketModelTrainingExample example in examples)
        {
            ChatMonitoringNeuralModelInput input = new(
                example.Requirement,
                example.ContextSnapshot ?? string.Empty,
                example.BootstrapMessage ?? string.Empty,
                0,
                1,
                0,
                .5f);
            ChatMonitoringNeuralModelTargets targets = new(
                (float)example.TargetScore,
                (float)example.TargetRelevance);
            model.Train(input, targets);
        }

        NeuralNetParameterSnapshot snapshot = model.GetParameterSnapshot(null, examples.Count);
        return new RetrainedSessionModel(model, current, initial, snapshot);
    }

    private async Task<long> PublishAndMarkExamplesAsync(
        PromotionLease promotionLease,
        IReadOnlyList<TicketModelTrainingExample> examples,
        RetrainedSessionModel retrainedModel,
        CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        long generation = await checkpoints.PublishAsync(
            promotionLease.ChatMonitoringKind,
            retrainedModel.Model.GetStateSnapshot().ModelVersion,
            retrainedModel.Snapshot,
            ct);

        foreach (TicketModelTrainingExample example in examples)
            example.CanonicalGenerationApplied = generation;

        NeuralNetTrainingPromotion promotion = await db.NeuralNetTrainingPromotions
            .SingleAsync(x => x.PromotionId == promotionLease.PromotionId
                              && x.LeaseId == promotionLease.LeaseId, ct);
        // Promoting -> Promoted is committed with the checkpoint and example marks
        // so a canonical generation never points at unreconciled training rows.
        promotion.Status = StatusPromoted;
        promotion.PromotedGeneration = generation;
        promotion.CompletedAtUtc = DateTime.UtcNow;
        promotion.LeaseExpiresAtUtc = null;

        ChatMonitoringNeuralModelRun? sourceRun = await db.ChatMonitoringNeuralModelRuns
            .SingleOrDefaultAsync(x => x.SessionId == promotionLease.SessionId
                                       && x.ChatMonitoringKind == promotionLease.ChatMonitoringKind, ct);
        string sourceChecksum = NeuralNetReplaySerializer.ComputeSha256(sourceRun?.WorkerReplayJson ?? string.Empty);
        PromotionValidationResult validation = new(
            true,
            ["Ordered lease held", "Examples not previously promoted", "Hashed MLP replayed approved examples"],
            [],
            retrainedModel.CurrentCheckpoint?.Checksum ?? retrainedModel.Initial.Checksum,
            retrainedModel.Snapshot.Checksum,
            examples.Count,
            examples.Count * 12);
        ReplayIntegrity integrity = new(
            "hc-replay-canonical-json-v1",
            "sha-256",
            string.Empty,
            retrainedModel.Initial.Checksum,
            retrainedModel.Snapshot.Checksum,
            string.Empty);
        ModelPromotionReplay replay = new(
            promotion.PromotionId,
            promotion.SessionId,
            sourceChecksum,
            new([]),
            retrainedModel.Initial,
            examples.Select(x => x.TrainingExampleId).ToList(),
            retrainedModel.Snapshot,
            validation,
            integrity);
        promotion.PromotionReportJson = JsonSerializer.Serialize(replay);

        if (sourceRun is not null)
        {
            sourceRun.PromotionReplayJson = promotion.PromotionReportJson;
            sourceRun.CanonicalGeneration = generation;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return generation;
    }

    private async Task MarkPromotionFailedAsync(PromotionLease promotionLease, Exception ex)
    {
        NeuralNetTrainingPromotion promotion = await db.NeuralNetTrainingPromotions
            .SingleAsync(x => x.PromotionId == promotionLease.PromotionId, CancellationToken.None);
        // Promoting -> RetryPending/Rejected releases the lease without hiding
        // failed attempts from operators inspecting promotion history.
        promotion.Status = promotion.AttemptCount >= 3 ? StatusRejected : StatusRetryPending;
        promotion.FailureReason = ex.Message[..Math.Min(1000, ex.Message.Length)];
        promotion.LeaseExpiresAtUtc = null;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static IChatMonitoringNeuralModelTelemetry CreateSessionLocalModel(NeuralModelKindChatMonitoring chatMonitoringKind) => chatMonitoringKind switch
    {
        NeuralModelKindChatMonitoring.Moderation => new ModerationChatMonitorNeuralNet(),
        NeuralModelKindChatMonitoring.Tutoring => new TutoringChatMonitorNeuralNet(),
        _ => throw new ArgumentOutOfRangeException(nameof(chatMonitoringKind), chatMonitoringKind, null),
    };

    private sealed record PromotionLease(
        Guid PromotionId,
        Guid SessionId,
        NeuralModelKindChatMonitoring ChatMonitoringKind,
        Guid LeaseId);

    private sealed record RetrainedSessionModel(
        IChatMonitoringNeuralModelTelemetry Model,
        NeuralNetCanonicalCheckpoint? CurrentCheckpoint,
        NeuralNetParameterSnapshot Initial,
        NeuralNetParameterSnapshot Snapshot);
}
