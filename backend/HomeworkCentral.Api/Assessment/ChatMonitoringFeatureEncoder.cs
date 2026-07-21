namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Shared compact feature encoder used by the TorchSharp monitors and vector retrieval.
/// It intentionally exposes 48 values, not the old 256-input handwritten model surface.
/// </summary>
public static class ChatMonitoringFeatureEncoder
{
    public const int FeatureCount = 48;

    public static IReadOnlyList<float> EmbedText(string text)
    {
        float[] values = new float[FeatureCount];
        AddTokens(values, text, 1f);
        return values;
    }

    public static float[] Encode(ChatMonitoringNeuralModelInput input)
    {
        float[] values = new float[FeatureCount];
        AddTokens(values, input.Requirement, .65f);
        AddTokens(values, input.ThreadContext, .5f);
        AddTokens(values, input.Message, 1f);
        values[44] = input.CommunityVote;
        values[45] = input.ChannelRelevance;
        values[46] = input.ThreadContinuity;
        values[47] = input.PriorScore;
        return values;
    }

    private static void AddTokens(float[] values, string text, float weight)
    {
        string[] tokens = text.ToLowerInvariant().Split([' ', '\r', '\n', '\t', '.', ',', '!', '?', ':', ';', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in tokens.Take(400))
        {
            uint hash = 2166136261;
            foreach (char character in token) hash = (hash ^ character) * 16777619;
            int index = (int)(hash % 44);
            values[index] = Math.Clamp(values[index] + weight, -4, 4);
        }
    }
}
