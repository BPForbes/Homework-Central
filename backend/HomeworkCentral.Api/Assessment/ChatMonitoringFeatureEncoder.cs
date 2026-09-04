namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Shared feature encoder for chat monitors and vector retrieval.
/// Layout (86 floats):
/// 0–43 hashed text bins; 44 community vote; 45 effective channel relevance;
/// 46 thread continuity; 47 prior score; 48 applied-subject count (norm);
/// 49 exact channel match; 50 related match; 51 cross-subject support;
/// 52–64 applied-subject multi-hot (13 Mask-C); 65–77 channel-subject multi-hot;
/// 78–85 cascade stage-1 embedding (concept-context for moderation; subject-context for tutoring).
/// </summary>
public static class ChatMonitoringFeatureEncoder
{
    public const int FeatureCount = 86;
    private const int HashBinCount = 44;
    private const int MetaCommunityVote = 44;
    private const int MetaChannelRelevance = 45;
    private const int MetaThreadContinuity = 46;
    private const int MetaPriorScore = 47;
    private const int MetaAppliedCount = 48;
    private const int MetaExactMatch = 49;
    private const int MetaRelatedMatch = 50;
    private const int MetaCrossSupport = 51;
    private const int AppliedHotStart = 52;
    private const int ChannelHotStart = 65;
    private const int CascadeContextStart = 78;

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
        values[MetaCommunityVote] = input.CommunityVote;
        values[MetaChannelRelevance] = input.ChannelRelevance;
        values[MetaThreadContinuity] = input.ThreadContinuity;
        values[MetaPriorScore] = input.PriorScore;
        values[MetaAppliedCount] = input.AppliedSubjectCountNorm;
        values[MetaExactMatch] = input.ExactSubjectMatch;
        values[MetaRelatedMatch] = input.RelatedSubjectMatch;
        values[MetaCrossSupport] = input.CrossSubjectSupport;

        WriteMultiHot(values, AppliedHotStart, input.AppliedSubjectMultiHot);
        WriteMultiHot(values, ChannelHotStart, input.ChannelSubjectMultiHot);
        if (input.CascadeContext is not null)
        {
            int count = Math.Min(TutoringSubjectContextRouter.OutputSize, input.CascadeContext.Count);
            for (int i = 0; i < count; i++)
                values[CascadeContextStart + i] = Math.Clamp(input.CascadeContext[i], -1f, 1f);
        }

        return values;
    }

    private static void WriteMultiHot(float[] values, int start, IReadOnlyList<float>? hot)
    {
        if (hot is null) return;
        int count = Math.Min(ChatMonitoringSubjectSignals.GeneralSubjectCount, hot.Count);
        for (int i = 0; i < count; i++)
            values[start + i] = Math.Clamp(hot[i], 0f, 1f);
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
