using System.Text.Json;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

public interface IVectorDocumentStore
{
    Task UpsertAsync(
        string ns,
        string contentText,
        IReadOnlyList<float> embedding,
        string? positionId,
        Guid? canonicalRecordId,
        object? metadata,
        CancellationToken ct = default);

    Task<IReadOnlyList<VectorDocument>> RetrieveSimilarAsync(
        string ns,
        IReadOnlyList<float> queryEmbedding,
        int take = 8,
        string? positionId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Retrieval-only store. Embeddings are cosine-compared in process (JSON float arrays).
/// Never returns or stores authoritative candidate quality scores.
/// </summary>
public sealed class VectorDocumentStore(AppDbContext db) : IVectorDocumentStore
{
    public async Task UpsertAsync(
        string ns,
        string contentText,
        IReadOnlyList<float> embedding,
        string? positionId,
        Guid? canonicalRecordId,
        object? metadata,
        CancellationToken ct = default)
    {
        VectorDocument? existing = canonicalRecordId is Guid id
            ? await db.VectorDocuments.FirstOrDefaultAsync(
                d => d.Namespace == ns && d.CanonicalRecordId == id,
                ct)
            : null;

        string embeddingJson = JsonSerializer.Serialize(embedding);
        string metadataJson = metadata is null
            ? "{}"
            : JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (existing is null)
        {
            db.VectorDocuments.Add(new VectorDocument
            {
                DocumentId = Guid.NewGuid(),
                Namespace = ns,
                PositionId = positionId,
                CanonicalRecordId = canonicalRecordId,
                MetadataJson = metadataJson,
                ContentText = contentText,
                EmbeddingJson = embeddingJson,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.ContentText = contentText;
            existing.EmbeddingJson = embeddingJson;
            existing.MetadataJson = metadataJson;
            existing.PositionId = positionId;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<VectorDocument>> RetrieveSimilarAsync(
        string ns,
        IReadOnlyList<float> queryEmbedding,
        int take = 8,
        string? positionId = null,
        CancellationToken ct = default)
    {
        IQueryable<VectorDocument> query = db.VectorDocuments.AsNoTracking()
            .Where(d => d.Namespace == ns);
        if (!string.IsNullOrWhiteSpace(positionId))
            query = query.Where(d => d.PositionId == positionId);

        List<VectorDocument> docs = await query.Take(200).ToListAsync(ct);
        return docs
            .Select(d => (Doc: d, Score: Cosine(queryEmbedding, Parse(d.EmbeddingJson))))
            .OrderByDescending(x => x.Score)
            .Take(take)
            .Select(x => x.Doc)
            .ToList();
    }

    private static float[] Parse(string json) =>
        JsonSerializer.Deserialize<float[]>(json) ?? [];

    private static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        int n = Math.Min(a.Count, b.Count);
        if (n == 0)
            return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        double denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom <= 0 ? 0 : dot / denom;
    }
}
