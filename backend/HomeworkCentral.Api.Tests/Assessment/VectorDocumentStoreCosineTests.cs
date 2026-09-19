using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class VectorDocumentStoreCosineTests
{
    [Fact]
    public void Empty_vectors_score_zero()
    {
        Assert.Equal(0d, VectorDocumentStore.Cosine([], [1f]));
        Assert.Equal(0d, VectorDocumentStore.Cosine([1f], []));
    }

    [Fact]
    public void Identical_unit_vectors_score_one()
    {
        Assert.Equal(1d, VectorDocumentStore.Cosine([1f, 0f], [1f, 0f]));
    }

    [Fact]
    public void Orthogonal_vectors_score_zero()
    {
        Assert.Equal(0d, VectorDocumentStore.Cosine([1f, 0f], [0f, 1f]));
    }

    [Fact]
    public void Zero_norm_scores_zero()
    {
        Assert.Equal(0d, VectorDocumentStore.Cosine([0f, 0f], [1f, 2f]));
    }

    [Fact]
    public void Overlapping_prefix_ignores_tail()
    {
        Assert.Equal(1d, VectorDocumentStore.Cosine([1f], [1f, 99f]));
    }

    [Fact]
    public void Float_product_then_widen_matches_rust()
    {
        float[] left = [MathF.PI, MathF.E, 0.1f];
        float[] right = [MathF.E, MathF.PI, 0.2f];
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        double widenedDot = 0;
        for (int index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
            widenedDot += (double)left[index] * right[index];
        }

        Assert.NotEqual(dot, widenedDot);
        double expected = dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        Assert.Equal(expected, VectorDocumentStore.Cosine(left, right));
    }
}
