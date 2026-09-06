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
/// Portable twins live in <c>rust/hc-vector-cosine</c> (single and batch JSON);
/// keep both in lockstep.
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
        if (RustKernels.TryBatchCosineJson(
                queryEmbedding,
                docs.Select(static document => document.EmbeddingJson),
                out double[] rustScores)
            && rustScores.Length == docs.Count)
        {
            return docs
                .Select((document, index) => (Doc: document, Score: rustScores[index]))
                .OrderByDescending(pair => pair.Score)
                .Take(take)
                .Select(pair => pair.Doc)
                .ToList();
        }

        return docs
            .Select(document => (Doc: document, Score: Cosine(queryEmbedding, ParseEmbeddingJson(document.EmbeddingJson))))
            .OrderByDescending(pair => pair.Score)
            .Take(take)
            .Select(pair => pair.Doc)
            .ToList();
    }

    internal static float[] ParseEmbeddingJson(string json)
    {
        if (VectorRetrievalMemo.TryParse(json, out float[]? cached) && cached is not null)
            return (float[])cached.Clone();

        float[] parsed = JsonSerializer.Deserialize<float[]>(json) ?? [];
        VectorRetrievalMemo.PutParse(json, (float[])parsed.Clone());
        return parsed;
    }

    internal static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (RustKernels.TryCosine(a, b, out double rustScore))
            return rustScore;

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
