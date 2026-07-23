using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class NeuralMeshFrameExtractorTests
{
    [Fact]
    public void Extract_SelectsLargestActivationsAndGradients()
    {
        List<SparseValue> activations =
        [
            new(0, 0f),
            new(1, 0.2f),
            new(2, -0.9f),
            new(3, 1e-8f),
        ];
        List<SparseValue> edges =
        [
            new(10, 0.1f),
            new(11, -2f),
            new(12, 0f),
        ];
        ForwardPropagationTrace forward = new(
            [],
            [],
            activations,
            edges,
            [],
            0f,
            0f,
            0f,
            0f,
            0f);
        BackpropagationTrace backward = new(
            [],
            [],
            [new SparseValue(20, 3f), new SparseValue(21, 0f), new SparseValue(22, -1.5f)],
            [],
            0f,
            new GradientHealth(false, false, 0f, 0f, 3f, 1.5f));

        (IReadOnlyList<int> nodes, IReadOnlyList<int> edgeIndexes) =
            NeuralMeshFrameExtractor.Extract(forward, backward);

        Assert.Equal([2, 1], nodes);
        Assert.Equal([20, 22], edgeIndexes);
    }

    [Fact]
    public void Extract_FallsBackToForwardEdgesWhenNoBackprop()
    {
        ForwardPropagationTrace forward = new(
            [],
            [],
            [new SparseValue(4, 1f)],
            [new SparseValue(7, 0.5f), new SparseValue(8, 2f)],
            [],
            0f,
            0f,
            0f,
            0f,
            0f);

        (IReadOnlyList<int> nodes, IReadOnlyList<int> edgeIndexes) =
            NeuralMeshFrameExtractor.Extract(forward, backward: null);

        Assert.Equal([4], nodes);
        Assert.Equal([8, 7], edgeIndexes);
    }
}
