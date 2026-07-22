using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

public readonly record struct TicketReviewerEvaluation(
    double ReviewerScore,
    double ReviewerConfidence,
    double Relevance,
    bool CorrectionNeeded,
    string Explanation,
    string Guidance)
{
    public static bool TryParse(string json, out TicketReviewerEvaluation value)
    {
        value = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!Number(root, "reviewerScore", out double score)
                || !Number(root, "reviewerConfidence", out double confidence)
                || !Number(root, "relevance", out double relevance)) return false;
            value = new(
                Math.Clamp(score, 0, 1), Math.Clamp(confidence, 0, 1), Math.Clamp(relevance, 0, 1),
                root.TryGetProperty("correctionNeeded", out JsonElement correction) && correction.ValueKind == JsonValueKind.True,
                Text(root, "explanation"), Text(root, "guidance"));
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool Number(JsonElement root, string name, out double value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement element) && element.TryGetDouble(out value) && double.IsFinite(value);
    }

    private static string Text(JsonElement root, string name)
    {
        string text = root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim() ?? string.Empty : string.Empty;
        return text.Length <= 500 ? text : text[..500];
    }
}

public static class TicketReviewPolicy
{
    public static bool ShouldReview(double confidence, Guid messageId, double threshold, double auditRate)
    {
        if (!double.IsFinite(confidence) || confidence < Math.Clamp(threshold, 0, 1)) return true;
        uint sample = BitConverter.ToUInt32(messageId.ToByteArray(), 0);
        return sample / (double)uint.MaxValue < Math.Clamp(auditRate, 0, 1);
    }

    public static double Blend(double student, double reviewer, double reviewerWeight)
    {
        double weight = Math.Clamp(reviewerWeight, 0, 1);
        return Math.Clamp(student * (1 - weight) + reviewer * weight, 0, 1);
    }
}
