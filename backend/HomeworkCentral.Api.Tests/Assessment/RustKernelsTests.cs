using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class RustKernelsTests
{
    [Fact]
    public void Native_library_is_optional_and_bins_still_match()
    {
        IReadOnlyList<float> values = ChatMonitoringFeatureEncoder.EmbedText("anything");
        Assert.Equal(1f, values[15]);
        Assert.Equal(1d, VectorDocumentStore.Cosine([1f, 0f], [1f, 0f]));
    }

    [Fact]
    public void Loaded_kernels_serve_embed_text_and_store_cosine()
    {
        if (!RustKernels.IsLoaded)
            return;

        float[] bins = new float[ChatMonitoringFeatureEncoder.StructuralFeatureCount];
        Assert.True(RustKernels.TryEmbedText("anything", bins));
        Assert.Equal(1f, bins[15]);

        Assert.True(RustKernels.TryCosine([1f, 0f], [1f, 0f], out double score));
        Assert.Equal(1d, score, 12);
    }
}
