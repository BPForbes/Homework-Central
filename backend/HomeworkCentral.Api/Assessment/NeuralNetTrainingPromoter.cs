using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
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
    /// generation. A false return means either that no pending, expired retry, or expired
    /// promoting lease was available to the worker, or that a claimed candidate was rejected
    /// for regressing against the held-out set.
    /// </summary>
    public async Task<bool> PromoteNextAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        PromotionLease? promotionLease = await TryClaimPromotionAsync(now, ct);
        if (promotionLease is null) return false;

        return await OperationalExceptionGuard.RunAsync(
            async () =>
            {
                List<TicketModelTrainingExample> examples =
                    await LoadUnpromotedExamplesAsync(promotionLease, ct);
                List<ChatMonitoringEvaluationExample> heldOut =
                    await LoadHeldOutExamplesAsync(promotionLease.ChatMonitoringKind, ct);
                RetrainedSessionModel retrainedModel =
                    await RetrainSessionLocalModelAsync(promotionLease, examples, ct);

                ChatMonitoringEvaluation candidate =
                    ChatMonitoringModelEvaluator.Evaluate(retrainedModel.Model, heldOut);
                ChatMonitoringEvaluation incumbent =
                    await EvaluateIncumbentAsync(promotionLease.ChatMonitoringKind, heldOut, ct);

                if (ChatMonitoringModelEvaluator.IsRegression(candidate, incumbent))
                {
                    // Rejected, not retried: the candidate is a finished model that measured worse
                    // than the one in place, so running the same promotion again cannot help.
                    await MarkPromotionRejectedAsync(promotionLease, candidate, incumbent, ct);
                    retrainedModel.Model.Dispose();
                    return false;
                }

                await PublishAndMarkExamplesAsync(
                    promotionLease, examples, retrainedModel, candidate, incumbent, ct);
                retrainedModel.Model.Dispose();
                return true;
            },
            async ex =>
            {
                await MarkPromotionFailedAsync(promotionLease, ex);
                return false;
            });
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
        PromotionLease lease = new(
            candidate.PromotionId,
            candidate.SessionId,
            candidate.ChatMonitoringKind,
            leaseId);

        // Split status predicates so each atomic claim update stays a simple filter.
        // Concurrent workers lose by observing zero updated rows. Return on first claim.
        if (await ClaimPendingLeaseAsync(candidate.PromotionId, leaseId, leaseExpiresAtUtc, ct) > 0)
            return lease;
        if (await ClaimRetryPendingWithoutLeaseAsync(candidate.PromotionId, leaseId, leaseExpiresAtUtc, ct) > 0)
            return lease;
        if (await ClaimRetryPendingWithExpiredLeaseAsync(candidate.PromotionId, leaseId, leaseExpiresAtUtc, now, ct) > 0)
            return lease;
        if (await ClaimExpiredPromotingLeaseAsync(candidate.PromotionId, leaseId, leaseExpiresAtUtc, now, ct) > 0)
            return lease;

        return null;
    }

    private Task<int> ClaimPendingLeaseAsync(
        Guid promotionId,
        Guid leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken ct) =>
        ApplyPromotionLeaseAsync(
            db.NeuralNetTrainingPromotions.Where(x =>
                x.PromotionId == promotionId && x.Status == StatusPending),
            leaseId,
            leaseExpiresAtUtc,
            ct);

    private Task<int> ClaimRetryPendingWithoutLeaseAsync(
        Guid promotionId,
        Guid leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken ct) =>
        ApplyPromotionLeaseAsync(
            db.NeuralNetTrainingPromotions.Where(x =>
                x.PromotionId == promotionId
                && x.Status == StatusRetryPending
                && x.LeaseExpiresAtUtc == null),
            leaseId,
            leaseExpiresAtUtc,
            ct);

    private Task<int> ClaimRetryPendingWithExpiredLeaseAsync(
        Guid promotionId,
        Guid leaseId,
        DateTime leaseExpiresAtUtc,
        DateTime now,
        CancellationToken ct) =>
        ApplyPromotionLeaseAsync(
            db.NeuralNetTrainingPromotions.Where(x =>
                x.PromotionId == promotionId
                && x.Status == StatusRetryPending
                && x.LeaseExpiresAtUtc < now),
            leaseId,
            leaseExpiresAtUtc,
            ct);

    private Task<int> ClaimExpiredPromotingLeaseAsync(
        Guid promotionId,
        Guid leaseId,
        DateTime leaseExpiresAtUtc,
        DateTime now,
        CancellationToken ct) =>
        ApplyPromotionLeaseAsync(
            db.NeuralNetTrainingPromotions.Where(x =>
                x.PromotionId == promotionId
                && x.Status == StatusPromoting
                && x.LeaseExpiresAtUtc < now),
            leaseId,
            leaseExpiresAtUtc,
            ct);

    private Task<int> ApplyPromotionLeaseAsync(
        IQueryable<NeuralNetTrainingPromotion> claimable,
        Guid leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken ct) =>
        claimable.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(x => x.Status, StatusPromoting)
                .SetProperty(x => x.LeaseId, leaseId)
                .SetProperty(x => x.LeaseExpiresAtUtc, leaseExpiresAtUtc)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1),
            ct);

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

        int trained = 0;
        foreach (TicketModelTrainingExample example in examples)
        {
            // Held-out rows are skipped here and only ever used for scoring. Training on them
            // would make the evaluation measure memorisation instead of generalisation.
            if (ChatMonitoringHoldout.IsHeldOut(example.TrainingExampleId))
                continue;

            model.Train(BuildModelInput(example), BuildTargets(example));
            trained++;
        }

        NeuralNetParameterSnapshot snapshot = model.GetParameterSnapshot(null, trained);
        return new RetrainedSessionModel(model, current, initial, snapshot);
    }

    private async Task<long> PublishAndMarkExamplesAsync(
        PromotionLease promotionLease,
        IReadOnlyList<TicketModelTrainingExample> examples,
        RetrainedSessionModel retrainedModel,
        ChatMonitoringEvaluation candidate,
        ChatMonitoringEvaluation incumbent,
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
        // Provenance rules answer "was this produced correctly"; the held-out comparison answers
        // "is it any good". Both are recorded so a promotion's history shows what was actually
        // checked rather than an unconditional pass.
        List<string> passedRules =
        [
            "Ordered lease held",
            "Examples not previously promoted",
            "Hashed MLP replayed approved examples",
            candidate.IsInconclusive
                ? $"Held-out set too small to gate ({candidate.ExampleCount} < {ChatMonitoringHoldout.MinimumForGating})"
                : $"Held-out fitness {candidate.Fitness:F4} vs canonical {incumbent.Fitness:F4} "
                  + $"over {candidate.ExampleCount} examples",
        ];

        PromotionValidationResult validation = new(
            true,
            passedRules,
            [],
            retrainedModel.CurrentCheckpoint?.Checksum ?? retrainedModel.Initial.Checksum,
            retrainedModel.Snapshot.Checksum,
            // Trained count, not loaded count: held-out rows are loaded and marked but never
            // trained on. LocalRevision carries it out of RetrainSessionLocalModelAsync.
            retrainedModel.Snapshot.LocalRevision,
            retrainedModel.Snapshot.LocalRevision * 12);
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

    /// <summary>
    /// The held-out slice across every approved example for this lineage, not just this session's.
    /// A session can contribute only a handful of rows; scoring against the whole accumulated
    /// holdout is what makes successive generations comparable to each other.
    /// </summary>
    private async Task<List<ChatMonitoringEvaluationExample>> LoadHeldOutExamplesAsync(
        NeuralModelKindChatMonitoring chatMonitoringKind,
        CancellationToken ct)
    {
        List<TicketModelTrainingExample> approved = await db.TicketModelTrainingExamples
            .AsNoTracking()
            .Where(x => x.ChatMonitoringKind == chatMonitoringKind)
            .OrderBy(x => x.ApprovedAtUtc)
            .ThenBy(x => x.TrainingExampleId)
            .ToListAsync(ct);

        // Bucketing is a local hash of the id, so it cannot be pushed into SQL.
        return approved
            .Where(example => ChatMonitoringHoldout.IsHeldOut(example.TrainingExampleId))
            .Select(example => new ChatMonitoringEvaluationExample(
                BuildModelInput(example),
                (float)example.TargetScore,
                (float)example.TargetRelevance))
            .ToList();
    }

    /// <summary>
    /// Scores the generation currently in place on the same held-out examples, so the comparison
    /// is like for like. Returns an empty evaluation when there is no incumbent yet, which
    /// <see cref="ChatMonitoringModelEvaluator.IsRegression"/> treats as "nothing to regress from".
    /// </summary>
    private async Task<ChatMonitoringEvaluation> EvaluateIncumbentAsync(
        NeuralModelKindChatMonitoring chatMonitoringKind,
        IReadOnlyList<ChatMonitoringEvaluationExample> heldOut,
        CancellationToken ct)
    {
        if (heldOut.Count == 0)
            return ChatMonitoringEvaluation.Empty;

        NeuralNetCanonicalCheckpoint? current = await checkpoints.GetCurrentAsync(chatMonitoringKind, ct);
        if (current is null)
            return ChatMonitoringEvaluation.Empty;

        using IChatMonitoringNeuralModelTelemetry incumbent = CreateSessionLocalModel(chatMonitoringKind);
        incumbent.LoadParameterSnapshot(new NeuralNetParameterSnapshot(
            current.Generation,
            0,
            "ieee754-float32-le",
            "dense-base64",
            incumbent.GetTopologySnapshot().Parameters.Count,
            current.ParametersBase64,
            current.Checksum));

        return ChatMonitoringModelEvaluator.Evaluate(incumbent, heldOut);
    }

    private async Task MarkPromotionRejectedAsync(
        PromotionLease promotionLease,
        ChatMonitoringEvaluation candidate,
        ChatMonitoringEvaluation incumbent,
        CancellationToken ct)
    {
        NeuralNetTrainingPromotion promotion = await db.NeuralNetTrainingPromotions
            .SingleAsync(x => x.PromotionId == promotionLease.PromotionId, ct);
        promotion.Status = StatusRejected;
        promotion.FailureReason =
            $"Held-out fitness regressed: candidate {candidate.Fitness:F4} vs canonical "
            + $"{incumbent.Fitness:F4} over {candidate.ExampleCount} held-out examples.";
        promotion.LeaseExpiresAtUtc = null;
        promotion.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static ChatMonitoringNeuralModelInput BuildModelInput(TicketModelTrainingExample example) =>
        new(
            example.Requirement,
            example.ContextSnapshot ?? string.Empty,
            example.BootstrapMessage ?? string.Empty,
            0,
            1,
            0,
            .5f);

    private static ChatMonitoringNeuralModelTargets BuildTargets(TicketModelTrainingExample example) =>
        new((float)example.TargetScore, (float)example.TargetRelevance);

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
