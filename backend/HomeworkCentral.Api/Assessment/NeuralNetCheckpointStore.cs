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
        return generation;
    }
}
