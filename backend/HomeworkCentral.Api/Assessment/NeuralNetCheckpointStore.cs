using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

public sealed class NeuralNetCheckpointStore(AppDbContext db)
{
    public Task<NeuralNetCanonicalCheckpoint?> GetCurrentAsync(CancellationToken ct) =>
        db.NeuralNetCanonicalCheckpoints.OrderByDescending(x => x.Generation).FirstOrDefaultAsync(ct);

    public async Task<long> PublishAsync(NeuralNetParameterSnapshot snapshot, CancellationToken ct)
    {
        long generation = (await db.NeuralNetCanonicalCheckpoints.MaxAsync(x => (long?)x.Generation, ct) ?? 0) + 1;
        db.NeuralNetCanonicalCheckpoints.Add(new NeuralNetCanonicalCheckpoint { Generation = generation, ModelVersion = "hc-student-mlp-v2", ParametersBase64 = snapshot.PackedValues, Checksum = snapshot.Checksum, CreatedAtUtc = DateTime.UtcNow });
        return generation;
    }
}
