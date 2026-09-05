namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Builds live mesh highlight indexes from forward/backprop traces.
/// Uses a bounded top-K (Rust min-heap when <c>libhc_kernels</c> is loaded)
/// so a dense parameter bag never materializes a full sort.
/// </summary>
public static class NeuralMeshFrameExtractor
{
    private const int MaxActiveNodes = 480;
    private const int MaxActiveEdges = 1200;
    private const float ActivationEpsilon = 1e-6f;

    public static (IReadOnlyList<int> ActiveNodeIndexes, IReadOnlyList<int> ActiveEdgeParameterIndexes) Extract(
        ForwardPropagationTrace? forward,
        BackpropagationTrace? backward)
    {
        List<int> activeNodes = ExtractActiveNodes(forward);
        List<int> activeEdges = ExtractActiveEdges(forward, backward);
        return (activeNodes, activeEdges);
    }

    /// <summary>
    /// Restricts a frame to a single layer transition so replay advances one layer at a time
    /// instead of lighting the whole input-to-output path at once. <paramref name="layerIndex"/>
    /// is the destination layer: nodes come from layers <c>layerIndex - 1</c> and
    /// <c>layerIndex</c>, edges from the dense block that feeds <paramref name="layerIndex"/>.
    /// </summary>
    public static (IReadOnlyList<int> ActiveNodeIndexes, IReadOnlyList<int> ActiveEdgeParameterIndexes) ExtractLayer(
        ForwardPropagationTrace? forward,
        BackpropagationTrace? backward,
        IReadOnlyList<int> layerWidths,
        int layerIndex)
    {
        (IReadOnlyList<int> allNodes, IReadOnlyList<int> allEdges) = Extract(forward, backward);
        if (layerWidths.Count < 2)
            return (allNodes, allEdges);

        int destination = Math.Clamp(layerIndex, 1, layerWidths.Count - 1);
        (int nodeStart, int nodeEnd) = NodeWindow(layerWidths, destination);
        (int parameterStart, int parameterEnd) = ParameterWindow(layerWidths, destination);

        List<int> nodes = allNodes.Where(index => index >= nodeStart && index < nodeEnd).ToList();
        List<int> edges = allEdges.Where(index => index >= parameterStart && index < parameterEnd).ToList();
        return (nodes, edges);
    }

    /// <summary>Global node index range covering the source and destination layers of a transition.</summary>
    private static (int Start, int End) NodeWindow(IReadOnlyList<int> layerWidths, int destination)
    {
        int start = 0;
        for (int layer = 0; layer < destination - 1; layer++)
            start += layerWidths[layer];

        int end = start + layerWidths[destination - 1] + layerWidths[destination];
        return (start, end);
    }

    /// <summary>
    /// Flattened parameter range for the dense block feeding <paramref name="destination"/>.
    /// Each block stores, per target node, one weight per source followed by that node's bias.
    /// </summary>
    private static (int Start, int End) ParameterWindow(IReadOnlyList<int> layerWidths, int destination)
    {
        int start = 0;
        for (int layer = 1; layer < destination; layer++)
            start += layerWidths[layer] * (layerWidths[layer - 1] + 1);

        int end = start + layerWidths[destination] * (layerWidths[destination - 1] + 1);
        return (start, end);
    }

    private static List<int> ExtractActiveNodes(ForwardPropagationTrace? forward)
    {
        if (forward is null || forward.NodeActivations.Count == 0)
            return [];

        return TopParameterIndexes(
            forward.NodeActivations,
            static value => MathF.Abs(value) > ActivationEpsilon,
            MaxActiveNodes,
            distinct: true);
    }

    private static List<int> ExtractActiveEdges(ForwardPropagationTrace? forward, BackpropagationTrace? backward)
    {
        if (backward is not null && backward.WeightGradients.Count > 0)
        {
            return TopParameterIndexes(
                backward.WeightGradients,
                static value => MathF.Abs(value) > ActivationEpsilon,
                MaxActiveEdges,
                distinct: false);
        }

        if (forward is null || forward.EdgeContributions.Count == 0)
            return [];

        return TopParameterIndexes(
            forward.EdgeContributions,
            static value => MathF.Abs(value) > ActivationEpsilon,
            MaxActiveEdges,
            distinct: false);
    }

    private static List<int> TopParameterIndexes(
        IReadOnlyList<SparseValue> values,
        Func<float, bool> include,
        int take,
        bool distinct)
    {
        if (take <= 0 || values.Count == 0)
            return [];

        int count = values.Count;
        float[] packedValues = new float[count];
        int[] packedIndexes = new int[count];
        for (int index = 0; index < count; index++)
        {
            SparseValue item = values[index];
            packedValues[index] = include(item.Value) ? item.Value : 0f;
            packedIndexes[index] = item.Index;
        }

        if (RustKernels.TryTopKAbs(packedValues, packedIndexes, take, out int[] rustIndexes))
        {
            return distinct ? rustIndexes.Distinct().ToList() : [.. rustIndexes];
        }

        return SelectTopKManaged(packedValues, packedIndexes, take, distinct);
    }

    /// <summary>Managed min-heap of size <paramref name="take"/>; same skip rule as <c>hc_heap_top_k_abs</c>.</summary>
    internal static List<int> SelectTopKManaged(
        float[] values,
        int[] indexes,
        int take,
        bool distinct)
    {
        PriorityQueue<int, float> worstKept = new();
        for (int index = 0; index < values.Length; index++)
        {
            float absolute = MathF.Abs(values[index]);
            if (!float.IsFinite(absolute) || absolute <= ActivationEpsilon)
                continue;

            if (worstKept.Count < take)
            {
                worstKept.Enqueue(indexes[index], absolute);
            }
            else if (worstKept.TryPeek(out _, out float worst) && absolute > worst)
            {
                worstKept.Dequeue();
                worstKept.Enqueue(indexes[index], absolute);
            }
        }

        List<(int Index, float Abs)> ranked = [];
        while (worstKept.TryDequeue(out int selected, out float absolute))
            ranked.Add((selected, absolute));

        ranked.Sort(static (left, right) => right.Abs.CompareTo(left.Abs));
        IEnumerable<int> ordered = ranked.Select(static item => item.Index);
        return distinct ? ordered.Distinct().ToList() : ordered.ToList();
    }
}
