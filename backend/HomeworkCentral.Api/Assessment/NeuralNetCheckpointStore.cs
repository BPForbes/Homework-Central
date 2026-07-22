using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Persists canonical hashed-MLP checkpoints for chat monitors. Each publish appends a new
/// generation; in-process models reload via <see cref="NeuralNetCheckpointRefreshService"/>.
/// Promotion remains outside this store (see <see cref="NeuralNetTrainingPromoter"/>).
/// </summary>
public sealed class NeuralNetCheckpointStore(AppDbContext db)
{
    /// <summary>Latest published checkpoint for <paramref name="chatMonitoringKind"/> and the hashed-MLP runtime.</summary>
    public Task<NeuralNetCanonicalCheckpoint?> GetCurrentAsync(NeuralModelKindChatMonitoring chatMonitoringKind, CancellationToken ct) =>
        db.NeuralNetCanonicalCheckpoints.Where(x => x.ChatMonitoringKind == chatMonitoringKind && x.RuntimeKind == ChatMonitoringNeuralModelHashedMlp.RuntimeKind)
            .OrderByDescending(x => x.Generation).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Appends a new generation from <paramref name="snapshot"/> packed weights/checksum.
    /// Returns the assigned generation number; callers must <c>SaveChanges</c> with the surrounding unit of work.
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
        return generation;
    }
}
