using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

public interface IAITrackingService
{
    Task<IReadOnlyList<AIModelLineageDto>> ListLineagesAsync(CancellationToken ct);
    Task<IReadOnlyList<AICategoryDto>> ListCategoriesAsync(string lineageSlug, CancellationToken ct);
    Task<AIModelLineageDto> RegisterCustomLineageAsync(RegisterAIModelLineageRequest request, CancellationToken ct);
    Task<bool> DeleteCustomLineageAsync(string lineageSlug, CancellationToken ct);

    Task<long> RecordCategoryWeightsAsync(
        Guid ticketId,
        int messageIndex,
        NeuralModelKindChatMonitoring modelKind,
        string modelVersion,
        IReadOnlyDictionary<string, double>? categoryWeights,
        CancellationToken ct);

    Task<long> RecordCategoryWeightsAsync(
        string lineageSlug,
        Guid? ticketId,
        int messageIndex,
        string modelVersion,
        IReadOnlyDictionary<string, double>? categoryWeights,
        Guid? createdByUserId,
        CancellationToken ct);

    Task RecordHumanCorrectionAsync(
        long sessionId,
        string categorySlug,
        string correctedCategorySlug,
        Guid? correctedByUserId,
        CancellationToken ct);

    Task RecordPredictionAsync(
        long sessionId,
        string predictedCategorySlug,
        float predictedScore,
        string? actualCategorySlug,
        CancellationToken ct);

    Task<PagedResultDto<AITrackingSessionDto>> QuerySessionsAsync(
        string? lineageSlug,
        Guid? ticketId,
        Guid? createdByUserId,
        DateTime? beforeUtc,
        int limit,
        CancellationToken ct);

    Task<AITrackingSessionDto?> GetSessionAsync(long sessionId, CancellationToken ct);
    Task<bool> DeleteSessionAsync(long sessionId, CancellationToken ct);
    Task<int> DeleteSessionsForTicketAsync(Guid ticketId, CancellationToken ct);
    Task<int> DeleteSessionsForLineageAsync(string lineageSlug, CancellationToken ct);
}

/// <summary>
/// Persists teacher labels, human corrections, and predictions against lineage/category
/// lookups so moderation, tutoring, and later custom-ticket ANIs share one store.
/// </summary>
public sealed class AITrackingService(AppDbContext db) : IAITrackingService
{
    public async Task<IReadOnlyList<AIModelLineageDto>> ListLineagesAsync(CancellationToken ct)
    {
        await AITrackingCatalogSeedData.SeedAsync(db, ct);
        return await db.AIModelLineages
            .AsNoTracking()
            .OrderBy(lineage => lineage.IsBuiltIn ? 0 : 1)
            .ThenBy(lineage => lineage.Slug)
            .Select(lineage => new AIModelLineageDto
            {
                LineageId = lineage.LineageId,
                Slug = lineage.Slug,
                DisplayName = lineage.DisplayName,
                IsBuiltIn = lineage.IsBuiltIn,
                PortalChannelId = lineage.PortalChannelId,
                CategoryCount = lineage.Categories.Count,
                SessionCount = lineage.Sessions.Count,
                CreatedAtUtc = lineage.CreatedAtUtc,
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AICategoryDto>> ListCategoriesAsync(string lineageSlug, CancellationToken ct)
    {
        await AITrackingCatalogSeedData.SeedAsync(db, ct);
        AIModelLineage? lineage = await RequireLineageAsync(lineageSlug, ct);
        if (lineage is null)
            return [];

        List<AICategory> rows = await db.AICategories
            .AsNoTracking()
            .Where(category => category.LineageId == lineage.LineageId)
            .OrderBy(category => category.SortOrder)
            .ToListAsync(ct);
        return rows.Select(MapCategory).ToList();
    }

    public async Task<AIModelLineageDto> RegisterCustomLineageAsync(
        RegisterAIModelLineageRequest request,
        CancellationToken ct)
    {
        string slug = NormalizeRequiredSlug(request.Slug, nameof(request.Slug));
        if (AITrackingCatalog.TryParseBuiltInKind(slug, out _))
            throw new InvalidOperationException("Built-in moderation and tutoring slugs cannot be registered as custom lineages.");

        if (await db.AIModelLineages.AnyAsync(row => row.Slug == slug, ct))
            throw new InvalidOperationException($"Lineage '{slug}' already exists.");

        if (request.PortalChannelId is Guid portalChannelId)
        {
            bool portalExists = await db.TicketPortalConfigs.AnyAsync(
                portal => portal.ChannelId == portalChannelId, ct);
            if (!portalExists)
                throw new InvalidOperationException("The ticket portal for this lineage does not exist.");
        }

        List<RegisterAICategoryRequest> categories = request.Categories
            .Where(category => !string.IsNullOrWhiteSpace(category.Slug))
            .ToList();
        if (categories.Count == 0)
            throw new InvalidOperationException("A custom lineage needs at least one category.");

        AIModelLineage lineage = new()
        {
            Slug = slug,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? slug : request.DisplayName.Trim(),
            IsBuiltIn = false,
            PortalChannelId = request.PortalChannelId,
            CreatedAtUtc = DateTime.UtcNow,
            Categories = categories
                .Select((category, index) => new AICategory
                {
                    Slug = NormalizeRequiredSlug(category.Slug, nameof(category.Slug)),
                    DisplayName = string.IsNullOrWhiteSpace(category.DisplayName)
                        ? category.Slug.Trim()
                        : category.DisplayName.Trim(),
                    SortOrder = index,
                    IsCatchAll = category.IsCatchAll,
                })
                .ToList(),
        };

        db.AIModelLineages.Add(lineage);
        await db.SaveChangesAsync(ct);
        return new AIModelLineageDto
        {
            LineageId = lineage.LineageId,
            Slug = lineage.Slug,
            DisplayName = lineage.DisplayName,
            IsBuiltIn = false,
            PortalChannelId = lineage.PortalChannelId,
            CategoryCount = lineage.Categories.Count,
            SessionCount = 0,
            CreatedAtUtc = lineage.CreatedAtUtc,
        };
    }

    public async Task<bool> DeleteCustomLineageAsync(string lineageSlug, CancellationToken ct)
    {
        AIModelLineage? lineage = await RequireLineageAsync(lineageSlug, ct);
        if (lineage is null || lineage.IsBuiltIn)
            return false;

        await DeleteSessionsForLineageIdAsync(lineage.LineageId, ct);
        db.AICategories.RemoveRange(
            await db.AICategories.Where(category => category.LineageId == lineage.LineageId).ToListAsync(ct));
        db.AIModelLineages.Remove(lineage);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<long> RecordCategoryWeightsAsync(
        Guid ticketId,
        int messageIndex,
        NeuralModelKindChatMonitoring modelKind,
        string modelVersion,
        IReadOnlyDictionary<string, double>? categoryWeights,
        CancellationToken ct) =>
        RecordCategoryWeightsAsync(
            AITrackingCatalog.SlugFor(modelKind),
            ticketId,
            messageIndex,
            modelVersion,
            categoryWeights,
            createdByUserId: null,
            ct);

    public async Task<long> RecordCategoryWeightsAsync(
        string lineageSlug,
        Guid? ticketId,
        int messageIndex,
        string modelVersion,
        IReadOnlyDictionary<string, double>? categoryWeights,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        await AITrackingCatalogSeedData.SeedAsync(db, ct);
        AIModelLineage lineage = await RequireLineageAsync(lineageSlug, ct)
            ?? throw new InvalidOperationException($"Unknown AI lineage '{lineageSlug}'.");

        AITrackingSession session = new()
        {
            LineageId = lineage.LineageId,
            TicketId = ticketId,
            SourceKind = AITrackingSourceKinds.Ticket,
            MessageIndex = messageIndex,
            ModelVersion = modelVersion,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.AITrackingSessions.Add(session);
        await db.SaveChangesAsync(ct);

        IEnumerable<KeyValuePair<string, double>> usableWeights =
            (categoryWeights ?? new Dictionary<string, double>())
            .Where(weight => weight.Value > 0);

        Dictionary<string, int> categoryIds = await LoadCategoryIdsAsync(lineage.LineageId, ct);
        List<AITrackingCategoryWeight> rows = usableWeights
            .Select(weight => (
                Found: categoryIds.TryGetValue(weight.Key, out int categoryId),
                CategoryId: categoryId,
                Weight: weight.Value))
            .Where(candidate => candidate.Found)
            .Select(candidate => new AITrackingCategoryWeight
            {
                SessionId = session.SessionId,
                CategoryId = candidate.CategoryId,
                Weight = candidate.Weight,
                IsHumanCorrected = false,
            })
            .ToList();

        if (rows.Count > 0)
        {
            db.AITrackingCategoryWeights.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        return session.SessionId;
    }

    public async Task RecordHumanCorrectionAsync(
        long sessionId,
        string categorySlug,
        string correctedCategorySlug,
        Guid? correctedByUserId,
        CancellationToken ct)
    {
        AITrackingCategoryWeight? tracked = await db.AITrackingCategoryWeights
            .Include(row => row.TrackingSession)
            .Include(row => row.Category)
            .FirstOrDefaultAsync(
                row => row.SessionId == sessionId && row.Category.Slug == categorySlug,
                ct);
        if (tracked is null)
            return;

        Dictionary<string, int> categoryIds = await LoadCategoryIdsAsync(tracked.TrackingSession.LineageId, ct);
        if (!categoryIds.TryGetValue(correctedCategorySlug, out int overrideCategoryId))
            return;

        tracked.IsHumanCorrected = true;
        tracked.HumanOverrideCategoryId = overrideCategoryId;
        tracked.HumanCorrectedByUserId = correctedByUserId;
        tracked.HumanCorrectionAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordPredictionAsync(
        long sessionId,
        string predictedCategorySlug,
        float predictedScore,
        string? actualCategorySlug,
        CancellationToken ct)
    {
        AITrackingSession? session = await db.AITrackingSessions
            .FirstOrDefaultAsync(row => row.SessionId == sessionId, ct);
        if (session is null)
            return;

        Dictionary<string, int> categoryIds = await LoadCategoryIdsAsync(session.LineageId, ct);
        if (!categoryIds.TryGetValue(predictedCategorySlug, out int predictedCategoryId))
            return;

        int? actualCategoryId = actualCategorySlug is null
            ? null
            : categoryIds.TryGetValue(actualCategorySlug, out int resolvedActual)
                ? resolvedActual
                : null;

        db.AITrackingPredictions.Add(new AITrackingPrediction
        {
            SessionId = sessionId,
            PredictedCategoryId = predictedCategoryId,
            PredictedScore = predictedScore,
            ActualCategoryId = actualCategoryId,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResultDto<AITrackingSessionDto>> QuerySessionsAsync(
        string? lineageSlug,
        Guid? ticketId,
        Guid? createdByUserId,
        DateTime? beforeUtc,
        int limit,
        CancellationToken ct)
    {
        await AITrackingCatalogSeedData.SeedAsync(db, ct);
        int pageSize = limit is > 0 and <= 100 ? limit : 50;

        IQueryable<AITrackingSession> query = db.AITrackingSessions
            .AsNoTracking()
            .Include(session => session.Lineage)
            .Include(session => session.CategoryWeights)
                .ThenInclude(weight => weight.Category)
            .Include(session => session.CategoryWeights)
                .ThenInclude(weight => weight.HumanOverrideCategory)
            .Include(session => session.Predictions)
                .ThenInclude(prediction => prediction.PredictedCategory)
            .Include(session => session.Predictions)
                .ThenInclude(prediction => prediction.ActualCategory);

        if (!string.IsNullOrWhiteSpace(lineageSlug))
            query = query.Where(session => session.Lineage.Slug == lineageSlug.Trim());
        if (ticketId is Guid ticket)
            query = query.Where(session => session.TicketId == ticket);
        if (createdByUserId is Guid userId)
            query = query.Where(session => session.CreatedByUserId == userId);
        if (beforeUtc is DateTime cursor)
            query = query.Where(session => session.CreatedAtUtc < cursor);

        List<AITrackingSession> rows = await query
            .OrderByDescending(session => session.CreatedAtUtc)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        bool hasMore = rows.Count > pageSize;
        if (hasMore)
            rows = rows.Take(pageSize).ToList();

        List<AITrackingSessionDto> items = rows.Select(MapSession).ToList();
        DateTime? nextBeforeUtc = items.Count == 0 ? null : rows[^1].CreatedAtUtc;
        return new PagedResultDto<AITrackingSessionDto>(items, hasMore, nextBeforeUtc, pageSize);
    }

    public async Task<AITrackingSessionDto?> GetSessionAsync(long sessionId, CancellationToken ct)
    {
        List<AITrackingSession> rows = await db.AITrackingSessions
            .AsNoTracking()
            .Include(session => session.Lineage)
            .Include(session => session.CategoryWeights)
                .ThenInclude(weight => weight.Category)
            .Include(session => session.CategoryWeights)
                .ThenInclude(weight => weight.HumanOverrideCategory)
            .Include(session => session.Predictions)
                .ThenInclude(prediction => prediction.PredictedCategory)
            .Include(session => session.Predictions)
                .ThenInclude(prediction => prediction.ActualCategory)
            .Where(session => session.SessionId == sessionId)
            .ToListAsync(ct);
        return rows.Select(MapSession).FirstOrDefault();
    }

    public async Task<bool> DeleteSessionAsync(long sessionId, CancellationToken ct)
    {
        AITrackingSession? session = await db.AITrackingSessions
            .FirstOrDefaultAsync(row => row.SessionId == sessionId, ct);
        if (session is null)
            return false;

        db.AITrackingSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> DeleteSessionsForTicketAsync(Guid ticketId, CancellationToken ct)
    {
        List<AITrackingSession> sessions = await db.AITrackingSessions
            .Where(session => session.TicketId == ticketId)
            .ToListAsync(ct);
        db.AITrackingSessions.RemoveRange(sessions);
        await db.SaveChangesAsync(ct);
        return sessions.Count;
    }

    public async Task<int> DeleteSessionsForLineageAsync(string lineageSlug, CancellationToken ct)
    {
        AIModelLineage? lineage = await RequireLineageAsync(lineageSlug, ct);
        if (lineage is null)
            return 0;

        return await DeleteSessionsForLineageIdAsync(lineage.LineageId, ct);
    }

    private async Task<int> DeleteSessionsForLineageIdAsync(int lineageId, CancellationToken ct)
    {
        List<AITrackingSession> sessions = await db.AITrackingSessions
            .Where(session => session.LineageId == lineageId)
            .ToListAsync(ct);
        db.AITrackingSessions.RemoveRange(sessions);
        await db.SaveChangesAsync(ct);
        return sessions.Count;
    }

    private async Task<AIModelLineage?> RequireLineageAsync(string lineageSlug, CancellationToken ct)
    {
        string slug = lineageSlug.Trim();
        if (slug.Length == 0)
            return null;

        return await db.AIModelLineages.FirstOrDefaultAsync(row => row.Slug == slug, ct);
    }

    private async Task<Dictionary<string, int>> LoadCategoryIdsAsync(int lineageId, CancellationToken ct) =>
        await db.AICategories
            .AsNoTracking()
            .Where(category => category.LineageId == lineageId)
            .ToDictionaryAsync(category => category.Slug, category => category.CategoryId, StringComparer.OrdinalIgnoreCase, ct);

    private static AICategoryDto MapCategory(AICategory category) => new()
    {
        CategoryId = category.CategoryId,
        Slug = category.Slug,
        DisplayName = category.DisplayName,
        SortOrder = category.SortOrder,
        IsCatchAll = category.IsCatchAll,
    };

    private static AITrackingSessionDto MapSession(AITrackingSession session) => new()
    {
        SessionId = session.SessionId,
        LineageSlug = session.Lineage.Slug,
        TicketId = session.TicketId,
        SourceKind = session.SourceKind,
        MessageIndex = session.MessageIndex,
        ModelVersion = session.ModelVersion,
        CreatedByUserId = session.CreatedByUserId,
        CreatedAtUtc = session.CreatedAtUtc,
        CategoryWeights = session.CategoryWeights
            .OrderBy(weight => weight.Category.SortOrder)
            .Select(weight => new AITrackingCategoryWeightDto
            {
                CategoryWeightId = weight.CategoryWeightId,
                CategorySlug = weight.Category.Slug,
                Weight = weight.Weight,
                IsHumanCorrected = weight.IsHumanCorrected,
                HumanOverrideCategorySlug = weight.HumanOverrideCategory?.Slug,
                HumanCorrectionAtUtc = weight.HumanCorrectionAtUtc,
            })
            .ToList(),
        Predictions = session.Predictions
            .OrderBy(prediction => prediction.CreatedAtUtc)
            .Select(prediction => new AITrackingPredictionDto
            {
                PredictionId = prediction.PredictionId,
                PredictedCategorySlug = prediction.PredictedCategory.Slug,
                PredictedScore = prediction.PredictedScore,
                ActualCategorySlug = prediction.ActualCategory?.Slug,
                CreatedAtUtc = prediction.CreatedAtUtc,
            })
            .ToList(),
    };

    private static string NormalizeRequiredSlug(string value, string argumentName)
    {
        string slug = value.Trim().ToLowerInvariant();
        if (slug.Length == 0)
            throw new ArgumentException("A slug is required.", argumentName);
        return slug;
    }
}
