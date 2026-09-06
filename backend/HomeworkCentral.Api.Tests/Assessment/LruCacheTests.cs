using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class LruCacheTests
{
    [Fact]
    public void Client_d_shift_on_a_b_c_is_d_a_c()
    {
        LruCache<char, int> cache = new(3);
        cache.Put('A', 1);
        cache.Put('B', 2);
        cache.Put('C', 3);
        Assert.Equal(1, cache.TryGet('A', out int reused) ? reused : 0);
        cache.Put('D', 4);
        Assert.Equal(['D', 'A', 'C'], cache.KeysMruToLru());
        Assert.False(cache.TryGet('B', out _));
        Assert.NotEqual(new[] { 'D', 'B', 'B' }, cache.KeysMruToLru());
        Assert.NotEqual(new[] { 'D', 'C', 'B' }, cache.KeysMruToLru());
    }

    [Fact]
    public void Embed_text_second_call_is_a_memo_hit()
    {
        ChatMonitoringFeatureEncoder.ResetTextMemos();
        IReadOnlyList<float> first = ChatMonitoringFeatureEncoder.EmbedText("lru-memo-probe");
        IReadOnlyList<float> second = ChatMonitoringFeatureEncoder.EmbedText("lru-memo-probe");
        Assert.Equal(first, second);
        ChatMonitoringFeatureEncoder.ResetTextMemos();
    }

    [Fact]
    public void Hash_embed_second_call_is_a_memo_hit()
    {
        LlmClient.ResetHashEmbedCache();
        IReadOnlyList<float> first = LlmClient.HashEmbed("offline text prediction");
        IReadOnlyList<float> second = LlmClient.HashEmbed("offline text prediction");
        Assert.Equal(first, second);
        LlmClient.ResetHashEmbedCache();
    }

    [Fact]
    public void Parsed_embedding_json_is_memoized()
    {
        VectorRetrievalMemo.Reset();
        string json = "[1,0,0]";
        float[] first = VectorDocumentStore.ParseEmbeddingJson(json);
        float[] second = VectorDocumentStore.ParseEmbeddingJson(json);
        Assert.Equal(first, second);
        VectorRetrievalMemo.Reset();
    }

    [Fact]
    public void Encode_second_call_is_a_memo_hit()
    {
        ChatMonitoringFeatureEncoder.ResetTextMemos();
        ChatMonitoringNeuralModelInput input = new(
            "Monitor for harassment.",
            "Repeated insults.",
            "You are worthless.",
            0, 1f, .6f, .5f);
        float[] first = ChatMonitoringFeatureEncoder.Encode(input);
        float[] second = ChatMonitoringFeatureEncoder.Encode(input);
        Assert.Equal(first, second);
        Assert.Equal(
            ChatMonitoringFeatureEncoder.FingerprintInput(input),
            ChatMonitoringFeatureEncoder.FingerprintInput(input));
        ChatMonitoringFeatureEncoder.ResetTextMemos();
    }

    [Fact]
    public void Host_lru_dispose_is_idempotent_and_stops_native_gets()
    {
        using HostLru cache = new(2);
        cache.PutBytes("k", [7]);
        Assert.True(cache.TryGetBytes("k", out byte[] before));
        Assert.Equal(new byte[] { 7 }, before);
        bool wasNative = cache.IsNative;
        cache.Dispose();
        cache.Dispose();
        Assert.False(cache.IsNative);
        if (wasNative)
            Assert.False(cache.TryGetBytes("k", out _));
    }

    [Fact]
    public void Host_lru_uses_rust_when_kernels_export_lru()
    {
        if (!RustKernels.HasLru)
            return;

        using HostLru cache = new(2);
        Assert.True(cache.IsNative);
    }

    [Fact]
    public void Host_lru_managed_fallback_follows_client_walk()
    {
        using HostLru cache = HostLru.CreateManaged(3);
        Assert.False(cache.IsNative);
        cache.PutBytes("A", [1]);
        cache.PutBytes("B", [2]);
        cache.PutBytes("C", [3]);
        Assert.True(cache.TryGetBytes("A", out byte[] reused));
        Assert.Equal(new byte[] { 1 }, reused);
        cache.PutBytes("D", [4]);
        Assert.False(cache.TryGetBytes("B", out _));
        Assert.True(cache.TryGetBytes("A", out byte[] a));
        Assert.True(cache.TryGetBytes("C", out byte[] c));
        Assert.True(cache.TryGetBytes("D", out byte[] d));
        Assert.Equal(new byte[] { 1 }, a);
        Assert.Equal(new byte[] { 3 }, c);
        Assert.Equal(new byte[] { 4 }, d);
    }
}
