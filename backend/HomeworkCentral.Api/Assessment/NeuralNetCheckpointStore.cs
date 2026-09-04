using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Canonical hashed-MLP checkpoints. Each publish appends a generation; in-process models
/// reload via <see cref="NeuralNetCheckpointRefreshService"/>. Promotion validation lives in
/// <see cref="NeuralNetTrainingPromoter"/>.
/// </summary>
public sealed class NeuralNetCheckpointStore(AppDbContext db)
{
    /// <summary>
    /// Generations kept per lineage. Only the newest is ever read (see <see cref="GetCurrentAsync"/>);
    /// the rest exist so a bad promotion can be inspected or rolled back by hand. Without a bound
    /// this table grows without limit, and each row carries a full base64-packed parameter
    /// snapshot — base64 being a third larger again than the bytes it encodes — so continuous
    /// training turns every publish into permanent disk. Nothing references a generation by value
    /// (promotions record <c>PromotedGeneration</c> as a plain number, with no foreign key), so
    /// trimming the tail is safe.
    /// </summary>
    public const int RetainedGenerations = 10;

    public Task<NeuralNetCanonicalCheckpoint?> GetCurrentAsync(NeuralModelKindChatMonitoring chatMonitoringKind, CancellationToken ct) =>
        db.NeuralNetCanonicalCheckpoints.Where(x => x.ChatMonitoringKind == chatMonitoringKind && x.RuntimeKind == ChatMonitoringNeuralModelHashedMlp.RuntimeKind)
            .OrderByDescending(x => x.Generation).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Appends a generation row; callers must <c>SaveChanges</c> with the surrounding unit of work.
    /// </summary>
    public async Task<long> PublishAsync(
        NeuralModelKindChatMonitoring chatMonitoringKind,
        string modelVersion,
        NeuralNetParameterSnapshot snapshot,
        CancellationToken ct)
    {
        long generation = (await db.NeuralNetCanonicalCheckpoints.Where(x => x.ChatMonitoringKind == chatMonitoringKind)
            .MaxAsync(x => (long?)x.Generation, ct) ?? 0) + 1;
        db.NeuralNetCanonicalCheckpoints.Add(new NeuralNetCanonicalCheckpoint
        {
            ChatMonitoringKind = chatMonitoringKind,
            Generation = generation,
            ModelVersion = modelVersion,
            ArchitectureVersion = modelVersion,
            RuntimeKind = ChatMonitoringNeuralModelHashedMlp.RuntimeKind,
            ParametersBase64 = snapshot.PackedValues,
            Checksum = snapshot.Checksum,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await TrimSupersededGenerationsAsync(chatMonitoringKind, generation, ct);
        return generation;
    }

    /// <summary>
    /// Marks generations older than the newest <see cref="RetainedGenerations"/> for deletion.
    /// Staged into the caller's unit of work rather than executed immediately, so the trim commits
    /// in the same transaction as the publish above — an <c>ExecuteDelete</c> here would drop the
    /// old checkpoints even if the surrounding transaction then rolled the new one back, leaving
    /// the lineage shorter and the publish lost. Only generation numbers are read back, never the
    /// parameter blobs, so trimming does not pull the rows it is deleting into memory.
    /// </summary>
    private async Task TrimSupersededGenerationsAsync(
        NeuralModelKindChatMonitoring chatMonitoringKind,
        long newestGeneration,
        CancellationToken ct)
    {
        long oldestKept = newestGeneration - RetainedGenerations + 1;
        if (oldestKept <= 1)
            return;

        List<long> superseded = await db.NeuralNetCanonicalCheckpoints
            .AsNoTracking()
            .Where(x => x.ChatMonitoringKind == chatMonitoringKind && x.Generation < oldestKept)
            .Select(x => x.Generation)
            .ToListAsync(ct);

        foreach (long generation in superseded)
        {
            // Key-only stub: the composite primary key (ChatMonitoringKind, Generation) is all the
            // DELETE needs, and there is no concurrency token to satisfy.
            db.NeuralNetCanonicalCheckpoints.Remove(new NeuralNetCanonicalCheckpoint
            {
                ChatMonitoringKind = chatMonitoringKind,
                Generation = generation,
            });
        }
    }
}
