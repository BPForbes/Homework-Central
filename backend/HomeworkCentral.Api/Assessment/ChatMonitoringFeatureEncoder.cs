namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Shared compact feature encoder used by the hashed-MLP chat monitors and vector retrieval.
/// Exposes 48 values: 44 hashed token bins (unigrams + bigrams) plus 4 structured metadata slots.
/// </summary>
public static class ChatMonitoringFeatureEncoder
{
    public const int FeatureCount = 48;
    private const int HashBinCount = 44;

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
        string[] tokens = text.ToLowerInvariant().Split(
            [' ', '\r', '\n', '\t', '.', ',', '!', '?', ':', ';', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string previous = string.Empty;
        foreach (string token in tokens.Take(400))
        {
            AddFeature(values, token, weight);
            if (previous.Length > 0)
                AddFeature(values, previous + "_" + token, weight * .7f);
            previous = token;
        }
    }

    private static void AddFeature(float[] values, string value, float weight)
    {
        uint hash = 2166136261;
        foreach (char character in value)
            hash = (hash ^ character) * 16777619;
        int index = (int)(hash % HashBinCount);
        values[index] = Math.Clamp(values[index] + weight, -4, 4);
    }
}
