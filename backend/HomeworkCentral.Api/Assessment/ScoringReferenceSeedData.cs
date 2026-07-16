using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Assessment;

public static class ScoringReferenceSeedData
{
    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (await db.VectorDocuments.AnyAsync(d => d.Namespace == VectorNamespaces.ScoringReference, ct))
            return;

        DateTime now = DateTime.UtcNow;
        db.VectorDocuments.AddRange(
            Doc("tutor", "calculus", "Rubric: correctness and bound-handling for calculus tutoring answers.", now),
            Doc("tutor", "pedagogy", "Rubric: clear step-by-step teaching, check understanding, avoid dumping answers.", now),
            Doc("tutor", "algebra", "Rubric: algebraic manipulation accuracy and explanation quality.", now),
            Doc("tutor", "professionalism", "Rubric: respectful tone, no academic dishonesty facilitation.", now),
            Doc("mod_report", "conduct", "Moderation rubric: match reported reason to message evidence; escalate critical harm.", now),
            Doc("mod_report", "proof", "Use forwarded messages, images, and links as proof when cross-examining reports.", now));

        await db.SaveChangesAsync(ct);
        logger?.LogInformation("Seeded scoring_reference vector documents.");
    }

    private static VectorDocument Doc(string positionId, string subject, string content, DateTime now) =>
        new()
        {
            DocumentId = Guid.NewGuid(),
            Namespace = VectorNamespaces.ScoringReference,
            PositionId = positionId,
            CanonicalRecordId = null,
            MetadataJson = $"{{\"documentType\":\"rubric\",\"position\":\"{positionId}\",\"subject\":\"{subject}\",\"rubricVersion\":\"1.0\",\"active\":true}}",
            ContentText = content,
            EmbeddingJson = "[]",
            CreatedAtUtc = now,
        };
}
