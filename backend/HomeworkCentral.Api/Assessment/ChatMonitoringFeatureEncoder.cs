namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Shared feature encoder for chat monitors and vector retrieval.
///
/// Structural region (86 floats), unchanged and index-stable — the cascade routers address
/// index 78 directly:
/// 0–43 hashed text bins; 44 community vote; 45 effective channel relevance;
/// 46 thread continuity; 47 prior score; 48 applied-subject count (norm);
/// 49 exact channel match; 50 related match; 51 cross-subject support;
/// 52–64 applied-subject multi-hot (13 Mask-C); 65–77 channel-subject multi-hot;
/// 78–85 cascade stage-1 embedding (concept-context for moderation; subject-context for tutoring).
///
/// Semantic region (<see cref="TextVectorSize"/> floats from <see cref="TextVectorStart"/>): the
/// sentence embedding of the message, supplied by the caller and produced by the same embedder in
/// training and inference. This is the model's real text channel. The 44 hashed bins above cannot
/// be one: every unigram and bigram of the message, thread context and requirement collapses into
/// 44 buckets, so distinct concepts routinely share a bucket, and longer inputs saturate the ±4
/// clamp until the vector is nearly constant. The bins are kept because they cost nothing and
/// still carry weak lexical signal when the embedder is unavailable.
/// </summary>
public static class ChatMonitoringFeatureEncoder
{
    /// <summary>Width of the structural region. Every index below is relative to zero and fixed.</summary>
    public const int StructuralFeatureCount = 86;

    public const int TextVectorStart = StructuralFeatureCount;

    /// <summary>
    /// Native width of nomic-embed-text. Tunable, but note the cost: the first hidden layer is 48
    /// wide, so the input weight count scales directly with this and a small training set can
    /// overfit a wide input. The promotion gate is what should decide the value empirically.
    /// </summary>
    public const int TextVectorSize = 768;

    public const int FeatureCount = StructuralFeatureCount + TextVectorSize;

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
    internal const int TextMemoCapacity = 256;
    private static readonly HostLru TextEmbeddings = new(TextMemoCapacity);
    private static readonly HostLru EncodedInputs = new(TextMemoCapacity);

    /// <summary>
    /// Lexical-only vector for <c>VectorDocumentStore</c> retrieval. Deliberately still the
    /// structural width: stored rows are persisted JSON float arrays compared by cosine, so
    /// widening this would silently mismatch every document already in the table.
    /// Runtime prefers <c>libhc_kernels</c> (`hc_embed_text`). Managed bins stay
    /// as the fallback when the native library is absent.
    /// </summary>
    public static IReadOnlyList<float> EmbedText(string text)
    {
        if (TextEmbeddings.TryGetFloats(text, out float[] cached))
            return cached;

        float[] values = new float[StructuralFeatureCount];
        if (!RustKernels.TryEmbedText(text, values))
            AddTokensManaged(values, text, 1f);

        TextEmbeddings.PutFloats(text, values);
        return values;
    }

    internal static void ResetTextMemos()
    {
        TextEmbeddings.Clear();
        EncodedInputs.Clear();
    }

    public static float[] Encode(ChatMonitoringNeuralModelInput input)
    {
        string fingerprint = FingerprintInput(input);
        if (EncodedInputs.TryGetFloats(fingerprint, out float[] cachedEncode))
            return cachedEncode;

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

        WriteTextVector(values, input.TextEmbedding);
        EncodedInputs.PutFloats(fingerprint, values);
        return values;
    }

    internal static string FingerprintInput(ChatMonitoringNeuralModelInput input)
    {
        System.Text.StringBuilder builder = new(input.Requirement.Length + input.ThreadContext.Length + input.Message.Length + 64);
        builder.Append(input.Requirement).Append('\u001f');
        builder.Append(input.ThreadContext).Append('\u001f');
        builder.Append(input.Message).Append('\u001f');
        builder.Append(input.CommunityVote).Append('\u001f');
        builder.Append(input.ChannelRelevance).Append('\u001f');
        builder.Append(input.ThreadContinuity).Append('\u001f');
        builder.Append(input.PriorScore).Append('\u001f');
        builder.Append(input.AppliedSubjectCountNorm).Append('\u001f');
        builder.Append(input.ExactSubjectMatch).Append('\u001f');
        builder.Append(input.RelatedSubjectMatch).Append('\u001f');
        builder.Append(input.CrossSubjectSupport).Append('\u001f');
        AppendFloats(builder, input.AppliedSubjectMultiHot);
        builder.Append('\u001f');
        AppendFloats(builder, input.ChannelSubjectMultiHot);
        builder.Append('\u001f');
        AppendFloats(builder, input.CascadeContext);
        builder.Append('\u001f');
        AppendFloats(builder, input.TextEmbedding);
        return builder.ToString();
    }

    private static void AppendFloats(System.Text.StringBuilder builder, IReadOnlyList<float>? values)
    {
        if (values is null)
        {
            builder.Append('-');
            return;
        }

        for (int index = 0; index < values.Count; index++)
        {
            if (index > 0)
                builder.Append(',');
            builder.Append(BitConverter.SingleToInt32Bits(values[index]));
        }
    }

    /// <summary>
    /// Copies the supplied sentence embedding into the semantic region, truncating or zero-padding
    /// to <see cref="TextVectorSize"/> so a different embedding model — or the client's own hashed
    /// fallback when the service is offline — still produces a fixed-width input.
    ///
    /// A null embedding leaves the region zeroed, which means "no semantic signal for this
    /// example". That is survivable rather than silent: a path that forgets to supply one trains
    /// and scores on a degraded input, and the held-out promotion gate is what catches the
    /// resulting regression.
    /// </summary>
    private static void WriteTextVector(float[] values, IReadOnlyList<float>? embedding)
    {
        if (embedding is null)
            return;

        int count = Math.Min(TextVectorSize, embedding.Count);
        for (int i = 0; i < count; i++)
            values[TextVectorStart + i] = embedding[i];
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
        if (RustKernels.TryAddWeightedTokens(values, text, weight))
            return;

        AddTokensManaged(values, text, weight);
    }

    private static void AddTokensManaged(float[] values, string text, float weight)
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
