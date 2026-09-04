using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

/// <summary>
/// The feature layout. Index stability matters as much as the new semantic region: the cascade
/// routers address index 78 directly, so the structural block has to stay exactly where it was.
/// </summary>
public class ChatMonitoringFeatureEncoderTests
{
    [Fact]
    public void The_semantic_region_is_appended_after_the_structural_block()
    {
        Assert.Equal(86, ChatMonitoringFeatureEncoder.StructuralFeatureCount);
        Assert.Equal(86, ChatMonitoringFeatureEncoder.TextVectorStart);
        Assert.Equal(
            ChatMonitoringFeatureEncoder.StructuralFeatureCount + ChatMonitoringFeatureEncoder.TextVectorSize,
            ChatMonitoringFeatureEncoder.FeatureCount);

        // The routers slice at 78, so it must remain inside the preserved block.
        Assert.True(ModerationConceptContextRouter.CascadeFeatureStart < ChatMonitoringFeatureEncoder.TextVectorStart);
        Assert.True(TutoringSubjectContextRouter.CascadeFeatureStart < ChatMonitoringFeatureEncoder.TextVectorStart);
    }

    [Fact]
    public void An_embedding_lands_in_the_semantic_region_and_leaves_the_structural_block_alone()
    {
        float[] embedding = new float[ChatMonitoringFeatureEncoder.TextVectorSize];
        for (int i = 0; i < embedding.Length; i++)
            embedding[i] = 0.25f;

        float[] withText = ChatMonitoringFeatureEncoder.Encode(Input("payment please", embedding));
        float[] withoutText = ChatMonitoringFeatureEncoder.Encode(Input("payment please", null));

        // Same text and structure in, so indices 0..85 must be byte-for-byte identical.
        for (int i = 0; i < ChatMonitoringFeatureEncoder.StructuralFeatureCount; i++)
            Assert.Equal(withoutText[i], withText[i]);

        Assert.Equal(0.25f, withText[ChatMonitoringFeatureEncoder.TextVectorStart]);
        Assert.Equal(0.25f, withText[ChatMonitoringFeatureEncoder.FeatureCount - 1]);
    }

    [Fact]
    public void A_missing_embedding_zeroes_the_semantic_region_rather_than_throwing()
    {
        float[] encoded = ChatMonitoringFeatureEncoder.Encode(Input("no embedder available", null));

        Assert.Equal(ChatMonitoringFeatureEncoder.FeatureCount, encoded.Length);
        for (int i = ChatMonitoringFeatureEncoder.TextVectorStart; i < encoded.Length; i++)
            Assert.Equal(0f, encoded[i]);
    }

    [Fact]
    public void A_narrow_embedding_is_zero_padded()
    {
        // LlmClient's offline fallback is 64 wide, far narrower than nomic-embed-text.
        float[] narrow = [1f, 2f, 3f];

        float[] encoded = ChatMonitoringFeatureEncoder.Encode(Input("offline", narrow));

        Assert.Equal(1f, encoded[ChatMonitoringFeatureEncoder.TextVectorStart]);
        Assert.Equal(3f, encoded[ChatMonitoringFeatureEncoder.TextVectorStart + 2]);
        Assert.Equal(0f, encoded[ChatMonitoringFeatureEncoder.TextVectorStart + 3]);
    }

    [Fact]
    public void A_wide_embedding_is_truncated_rather_than_overrunning_the_vector()
    {
        float[] wide = new float[ChatMonitoringFeatureEncoder.TextVectorSize + 64];
        Array.Fill(wide, 0.5f);

        float[] encoded = ChatMonitoringFeatureEncoder.Encode(Input("wide model", wide));

        Assert.Equal(ChatMonitoringFeatureEncoder.FeatureCount, encoded.Length);
        Assert.Equal(0.5f, encoded[ChatMonitoringFeatureEncoder.FeatureCount - 1]);
    }

    [Fact]
    public void Different_embeddings_separate_messages_the_hash_bins_cannot()
    {
        // The point of the change: identical structure and near-identical text, distinguished
        // only by what the embedder understood.
        float[] first = new float[ChatMonitoringFeatureEncoder.TextVectorSize];
        float[] second = new float[ChatMonitoringFeatureEncoder.TextVectorSize];
        Array.Fill(first, 1f);
        Array.Fill(second, -1f);

        float[] a = ChatMonitoringFeatureEncoder.Encode(Input("same words", first));
        float[] b = ChatMonitoringFeatureEncoder.Encode(Input("same words", second));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Embed_text_anything_matches_rust_golden_bins()
    {
        IReadOnlyList<float> values = ChatMonitoringFeatureEncoder.EmbedText("anything");
        Assert.Equal(1f, values[15]);
        Assert.Equal(1, CountNonzero(values));
    }

    [Fact]
    public void Embed_text_payment_please_matches_rust_golden_bins()
    {
        IReadOnlyList<float> values = ChatMonitoringFeatureEncoder.EmbedText("payment please");
        Assert.Equal(1f, values[13]);
        Assert.Equal(1f, values[19]);
        Assert.Equal(0.7f, values[28]);
        Assert.Equal(3, CountNonzero(values));
    }

    [Fact]
    public void Embed_text_punctuation_matches_rust_golden_bins()
    {
        IReadOnlyList<float> values = ChatMonitoringFeatureEncoder.EmbedText("Hello, WORLD!! payment-please");
        Assert.Equal(0.7f, values[2]);
        Assert.Equal(1f, values[11]);
        Assert.Equal(1f, values[13]);
        Assert.Equal(0.7f, values[16]);
        Assert.Equal(1f, values[19]);
        Assert.Equal(0.7f, values[28]);
        Assert.Equal(1f, values[39]);
        Assert.Equal(7, CountNonzero(values));
    }

    [Fact]
    public void Embed_text_repeated_tokens_clamp_to_four()
    {
        IReadOnlyList<float> values = ChatMonitoringFeatureEncoder.EmbedText(string.Concat(Enumerable.Repeat("a ", 500)));
        Assert.Equal(4f, values[20]);
        Assert.Equal(4f, values[40]);
        Assert.All(values, slot => Assert.InRange(slot, -4f, 4f));
    }

    [Fact]
    public void Retrieval_vectors_keep_the_persisted_structural_width()
    {
        // VectorDocumentStore rows are stored JSON float arrays compared by cosine; widening this
        // would mismatch every document already written.
        Assert.Equal(
            ChatMonitoringFeatureEncoder.StructuralFeatureCount,
            ChatMonitoringFeatureEncoder.EmbedText("anything").Count);
    }

    private static int CountNonzero(IReadOnlyList<float> values) =>
        values.Count(slot => BitConverter.SingleToInt32Bits(slot) != 0);

    private static ChatMonitoringNeuralModelInput Input(string message, IReadOnlyList<float>? embedding) =>
        new("requirement", "context", message, 0, 1, 0, .5f, TextEmbedding: embedding);
}
