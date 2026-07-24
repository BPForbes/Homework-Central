using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class NeuralMeshFrameExtractorTests
{
    /// <summary>Widths 2 → 3 → 2: layer 1 owns nodes 0-4 / params 0-8, layer 2 nodes 2-6 / params 9-16.</summary>
    private static readonly int[] LayerWidths = [2, 3, 2];

    [Theory]
    [InlineData(1, 0, 4, 0, 8)]
    [InlineData(2, 2, 6, 9, 16)]
    public void ExtractLayer_KeepsOnlyOneTransition(
        int layerIndex,
        int firstNode,
        int lastNode,
        int firstParameter,
        int lastParameter)
    {
        List<SparseValue> activations = Enumerable.Range(0, 7)
            .Select(index => new SparseValue(index, 1f))
            .ToList();
        List<SparseValue> edges = Enumerable.Range(0, 17)
            .Select(index => new SparseValue(index, 1f))
            .ToList();
        ForwardPropagationTrace forward = new([], [], activations, edges, [], 0f, 0f, 0f, 0f, 0f);

        (IReadOnlyList<int> nodes, IReadOnlyList<int> parameters) =
            NeuralMeshFrameExtractor.ExtractLayer(forward, backward: null, LayerWidths, layerIndex);

        Assert.Equal(Enumerable.Range(firstNode, lastNode - firstNode + 1), nodes.Order());
        Assert.Equal(Enumerable.Range(firstParameter, lastParameter - firstParameter + 1), parameters.Order());
    }

    [Fact]
    public void ExtractLayer_ClampsOutOfRangeLayerToLastTransition()
    {
        ForwardPropagationTrace forward = new(
            [],
            [],
            [new SparseValue(6, 1f)],
            [new SparseValue(16, 1f)],
            [],
            0f,
            0f,
            0f,
            0f,
            0f);

        (IReadOnlyList<int> nodes, IReadOnlyList<int> parameters) =
            NeuralMeshFrameExtractor.ExtractLayer(forward, backward: null, LayerWidths, layerIndex: 99);

        Assert.Equal([6], nodes);
        Assert.Equal([16], parameters);
    }

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
