using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Records AI-generated category labels for training data, enabling audit trails,
/// feedback loops, and analysis of model performance vs. training labels.
/// </summary>
public sealed class AITrackingService(AppDbContext db)
{
    /// <summary>
    /// Records the LLM-generated category weights for a message in a ticket.
    /// Sparse dictionary (only non-zero weights) becomes multiple rows, one per category.
    /// </summary>
    public async Task<long> RecordCategoryWeightsAsync(
        Guid ticketId,
        int messageIndex,
        NeuralModelKindChatMonitoring modelKind,
        string modelVersion,
        IReadOnlyDictionary<string, double>? categoryWeights,
        CancellationToken ct)
    {
        if (categoryWeights is null || categoryWeights.Count == 0)
        {
            return await RecordEmptyWeightsAsync(ticketId, messageIndex, modelKind, modelVersion, ct);
        }

        AITrackingSession session = new()
        {
            TicketId = ticketId,
            MessageIndex = messageIndex,
            NeuralModelKind = modelKind.ToString(),
            ModelVersion = modelVersion,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.AITrackingSessions.Add(session);
        await db.SaveChangesAsync(ct);

        foreach (KeyValuePair<string, double> weight in categoryWeights)
        {
            if (weight.Value <= 0)
                continue;

            AITrackingCategoryWeight tracked = new()
            {
                TrackingSessionId = session.Id,
                CategoryName = weight.Key,
                Weight = weight.Value,
                IsHumanCorrected = false,
            };

            db.AITrackingCategoryWeights.Add(tracked);
        }

        await db.SaveChangesAsync(ct);
        return session.Id;
    }

    /// <summary>
    /// Records when a human reviewer corrects an AI-assigned category.
    /// </summary>
    public async Task RecordHumanCorrectionAsync(
        long trackingSessionId,
        string categoryName,
        string correctedCategory,
        CancellationToken ct)
    {
        AITrackingCategoryWeight? tracked = await db.AITrackingCategoryWeights
            .FirstOrDefaultAsync(
                w => w.TrackingSessionId == trackingSessionId && w.CategoryName == categoryName,
                ct);

        if (tracked is null)
        {
            return;
        }

        tracked.IsHumanCorrected = true;
        tracked.HumanCategoryOverride = correctedCategory;
        tracked.HumanCorrectionAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records the model's prediction for a message after training on the soft labels.
    /// Useful for analyzing performance: did the trained model match the training distribution?
    /// </summary>
    public async Task RecordPredictionAsync(
        long trackingSessionId,
        string predictedCategory,
        float predictedScore,
        string? actualOutcome = null,
        CancellationToken ct = default)
    {
        AITrackingPrediction prediction = new()
        {
            TrackingSessionId = trackingSessionId,
            PredictedCategory = predictedCategory,
            PredictedScore = predictedScore,
            ActualOutcome = actualOutcome,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.AITrackingPredictions.Add(prediction);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Query methods for analysis and debugging.
    /// </summary>
    public IQueryable<AITrackingSession> GetTrackingSessionsByTicket(Guid ticketId) =>
        db.AITrackingSessions
            .AsNoTracking()
            .Where(s => s.TicketId == ticketId)
            .Include(s => s.CategoryWeights);

    public IQueryable<AITrackingCategoryWeight> GetCategoryWeightsByModel(
        NeuralModelKindChatMonitoring modelKind,
        string modelVersion) =>
        db.AITrackingCategoryWeights
            .AsNoTracking()
            .Where(w => w.TrackingSession.NeuralModelKind == modelKind.ToString()
                && w.TrackingSession.ModelVersion == modelVersion);

    public IQueryable<AITrackingCategoryWeight> GetHumanCorrectedWeights() =>
        db.AITrackingCategoryWeights
            .AsNoTracking()
            .Where(w => w.IsHumanCorrected)
            .Include(w => w.TrackingSession);

    private async Task<long> RecordEmptyWeightsAsync(
        Guid ticketId,
        int messageIndex,
        NeuralModelKindChatMonitoring modelKind,
        string modelVersion,
        CancellationToken ct)
    {
        AITrackingSession session = new()
        {
            TicketId = ticketId,
            MessageIndex = messageIndex,
            NeuralModelKind = modelKind.ToString(),
            ModelVersion = modelVersion,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.AITrackingSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }
}
