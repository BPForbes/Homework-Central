using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Serially publishes canonical generations. Worker candidate weights are never installed.</summary>
public sealed class NeuralNetTrainingPromoter(AppDbContext db, NeuralNetCheckpointStore checkpoints)
{
    public async Task QueueSessionAsync(Guid sessionId, CancellationToken ct)
    {
        if (await db.NeuralNetTrainingPromotions.AnyAsync(x => x.SessionId == sessionId, ct)) return;
        long sequence = (await db.NeuralNetTrainingPromotions.MaxAsync(x => (long?)x.PromotionSequence, ct) ?? 0) + 1;
        db.NeuralNetTrainingPromotions.Add(new() { PromotionId = Guid.NewGuid(), SessionId = sessionId, PromotionSequence = sequence, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> PromoteNextAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow; Guid lease = Guid.NewGuid();
        NeuralNetTrainingPromotion? candidate = await db.NeuralNetTrainingPromotions.Where(x => x.Status != "Promoted" && x.Status != "Rejected").OrderBy(x => x.PromotionSequence).FirstOrDefaultAsync(ct);
        if (candidate is null) return false;
        bool eligible = CanClaim(candidate.Status, candidate.LeaseExpiresAtUtc, now);
        if (!eligible) return false;
        int claimed = await db.NeuralNetTrainingPromotions.Where(x => x.PromotionId == candidate.PromotionId && (x.Status == "Pending" || (x.Status == "RetryPending" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now)) || (x.Status == "Promoting" && x.LeaseExpiresAtUtc < now))).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Promoting").SetProperty(x => x.LeaseId, lease).SetProperty(x => x.LeaseExpiresAtUtc, now.AddMinutes(10)).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
        if (claimed == 0) return false;
        try
        {
            List<TicketModelTrainingExample> examples = await db.TicketModelTrainingExamples.Where(x => x.NeuralNetTrainingSessionId == candidate.SessionId && x.CanonicalGenerationApplied == null).OrderBy(x => x.ApprovedAtUtc).ThenBy(x => x.TrainingExampleId).ToListAsync(ct);
            TicketStudentModel model = new TicketStudentModel(); NeuralNetCanonicalCheckpoint? current = await checkpoints.GetCurrentAsync(ct);
            NeuralNetParameterSnapshot initial = current is null
                ? model.GetParameterSnapshot(0, 0)
                : new(current.Generation, 0, "ieee754-float32-le", "dense-base64", 2074, current.ParametersBase64, current.Checksum);
            if (current is not null) model.LoadParameterSnapshot(initial);
            foreach (TicketModelTrainingExample example in examples) model.Train(new(example.Requirement, example.BootstrapMessage ?? string.Empty, example.TargetScore, example.TargetRelevance, example.Category));
            NeuralNetParameterSnapshot snapshot = model.GetParameterSnapshot(null, examples.Count);
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            long generation = await checkpoints.PublishAsync(snapshot, ct);
            foreach (TicketModelTrainingExample example in examples) example.CanonicalGenerationApplied = generation;
            candidate = await db.NeuralNetTrainingPromotions.SingleAsync(x => x.PromotionId == candidate.PromotionId && x.LeaseId == lease, ct);
            candidate.Status = "Promoted"; candidate.PromotedGeneration = generation; candidate.CompletedAtUtc = DateTime.UtcNow; candidate.LeaseExpiresAtUtc = null;
            NeuralNetTrainingSession sourceSession = await db.NeuralNetTrainingSessions.AsNoTracking().SingleAsync(x => x.SessionId == candidate.SessionId, ct);
            string sourceChecksum = NeuralNetReplaySerializer.ComputeSha256(sourceSession.ReportJson ?? string.Empty);
            PromotionValidationResult validation = new(true, ["Ordered lease held", "Examples not previously promoted"], [], current?.Checksum ?? initial.Checksum, snapshot.Checksum, examples.Count, examples.Count * 12);
            ReplayIntegrity integrity = new("hc-replay-canonical-json-v1", "sha-256", string.Empty, initial.Checksum, snapshot.Checksum, string.Empty);
            ModelPromotionReplay replay = new(candidate.PromotionId, candidate.SessionId, sourceChecksum, new([]), initial, examples.Select(x => x.TrainingExampleId).ToList(), snapshot, validation, integrity);
            candidate.PromotionReportJson = JsonSerializer.Serialize(replay);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NeuralNetTrainingPromotion item = await db.NeuralNetTrainingPromotions.SingleAsync(x => x.PromotionId == candidate.PromotionId, CancellationToken.None);
            item.Status = item.AttemptCount >= 3 ? "Rejected" : "RetryPending"; item.FailureReason = ex.Message[..Math.Min(1000, ex.Message.Length)]; item.LeaseExpiresAtUtc = null; await db.SaveChangesAsync(CancellationToken.None); return false;
        }
    }

    public static bool CanClaim(string status, DateTime? leaseExpiresAtUtc, DateTime now)
    {
        bool leaseExpired = leaseExpiresAtUtc is not null && leaseExpiresAtUtc < now;
        return status == "Pending" || (status == "RetryPending" && (leaseExpiresAtUtc is null || leaseExpired)) || (status == "Promoting" && leaseExpired);
    }
}
