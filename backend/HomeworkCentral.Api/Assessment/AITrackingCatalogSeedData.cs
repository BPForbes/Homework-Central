using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Upserts built-in moderation and tutoring lineage/category lookup rows from the
/// in-code taxonomies. Custom lineages are created through <see cref="IAITrackingService"/>
/// and are left untouched here.
/// </summary>
public static class AITrackingCatalogSeedData
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await EnsureBuiltInLineageAsync(
            db,
            AITrackingCatalog.ModerationSlug,
            "Moderation",
            ChatMonitoringCategoryTaxonomy.Moderation,
            ct);
        await EnsureBuiltInLineageAsync(
            db,
            AITrackingCatalog.TutoringSlug,
            "Tutoring",
            ChatMonitoringCategoryTaxonomy.Tutoring,
            ct);
    }

    private static async Task EnsureBuiltInLineageAsync(
        AppDbContext db,
        string slug,
        string displayName,
        IReadOnlyList<string> categorySlugs,
        CancellationToken ct)
    {
        AIModelLineage? lineage = await db.AIModelLineages
            .Include(row => row.Categories)
            .FirstOrDefaultAsync(row => row.Slug == slug, ct);

        if (lineage is null)
        {
            lineage = new AIModelLineage
            {
                Slug = slug,
                DisplayName = displayName,
                IsBuiltIn = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.AIModelLineages.Add(lineage);
            await db.SaveChangesAsync(ct);
        }

        HashSet<string> existing = lineage.Categories
            .Select(category => category.Slug)
            .ToHashSet(StringComparer.Ordinal);

        string catchAll = categorySlugs[^1];
        List<AICategory> missing = categorySlugs
            .Select((categorySlug, index) => (Slug: categorySlug, Index: index))
            .Where(candidate => !existing.Contains(candidate.Slug))
            .Select(candidate => new AICategory
            {
                LineageId = lineage.LineageId,
                Slug = candidate.Slug,
                DisplayName = candidate.Slug,
                SortOrder = candidate.Index,
                IsCatchAll = string.Equals(candidate.Slug, catchAll, StringComparison.Ordinal),
            })
            .ToList();

        if (missing.Count == 0)
            return;

        db.AICategories.AddRange(missing);
        await db.SaveChangesAsync(ct);
    }
}
