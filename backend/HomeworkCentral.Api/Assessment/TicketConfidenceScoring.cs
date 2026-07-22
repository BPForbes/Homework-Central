using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

public readonly record struct TicketConfidenceUpdate(
    double PreviousScore,
    double ScoreDelta,
    double CurrentScore);

public readonly record struct TicketEvidenceEvaluation(
    double EvidenceConfidence,
    double Relevance,
    string Reason)
{
    public static bool TryParse(string json, out TicketEvidenceEvaluation evaluation)
    {
        evaluation = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!TryReadFiniteNumber(root, "evidenceConfidence", out double confidence)
                || !TryReadFiniteNumber(root, "relevance", out double relevance))
            {
                return false;
            }

            string reason = root.TryGetProperty("reason", out JsonElement reasonElement)
                            && reasonElement.ValueKind == JsonValueKind.String
                ? reasonElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (reason.Length > 500)
                reason = reason[..500];

            evaluation = new TicketEvidenceEvaluation(
                Math.Clamp(confidence, 0, 1),
                Math.Clamp(relevance, 0, 1),
                reason);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadFiniteNumber(JsonElement root, string name, out double value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetDouble(out value)
               && double.IsFinite(value);
    }
}

public static class TicketConfidenceScoring
{
    /// <summary>
    /// Converts a per-message evidence probability into a bounded change to the
    /// ticket's running confidence. A value of 0.5 is neutral; relevance scales
    /// the update; the application, rather than the model, enforces the maximum.
    /// </summary>
    public static TicketConfidenceUpdate Update(
        double previousScore,
        double evidenceConfidence,
        double relevance,
        double maxAbsoluteDelta)
    {
        double previous = ClampFinite01(previousScore, 0.5);
        double evidence = ClampFinite01(evidenceConfidence, 0.5);
        double relevanceWeight = ClampFinite01(relevance, 0);
        double deltaLimit = double.IsFinite(maxAbsoluteDelta)
            ? Math.Clamp(maxAbsoluteDelta, 0, 1)
            : 0;

        double requestedDelta = (evidence - 0.5) * 2 * relevanceWeight * deltaLimit;
        double current = Math.Clamp(previous + requestedDelta, 0, 1);
        return new TicketConfidenceUpdate(previous, current - previous, current);
    }

    private static double ClampFinite01(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}
