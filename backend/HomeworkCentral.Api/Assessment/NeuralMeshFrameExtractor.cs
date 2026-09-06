namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Builds live mesh highlight indexes from forward/backprop traces.
/// Streams <see cref="SparseValue"/>s into a bounded min-heap so a dense
/// parameter bag never materializes <c>float[n]</c> + <c>int[n]</c>.
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

        return SelectTopKFromSparse(
            forward.NodeActivations,
            static value => MathF.Abs(value) > ActivationEpsilon,
            MaxActiveNodes,
            distinct: true);
    }

    private static List<int> ExtractActiveEdges(ForwardPropagationTrace? forward, BackpropagationTrace? backward)
    {
        if (backward is not null && backward.WeightGradients.Count > 0)
        {
            return SelectTopKFromSparse(
                backward.WeightGradients,
                static value => MathF.Abs(value) > ActivationEpsilon,
                MaxActiveEdges,
                distinct: false);
        }

        if (forward is null || forward.EdgeContributions.Count == 0)
            return [];

        return SelectTopKFromSparse(
            forward.EdgeContributions,
            static value => MathF.Abs(value) > ActivationEpsilon,
            MaxActiveEdges,
            distinct: false);
    }

    /// <summary>
    /// Streams sparse values into an O(k) min-heap. When <paramref name="distinct"/> is true,
    /// unique indexes are ranked by max |value| and then capped (old sort → distinct → take).
    /// </summary>
    internal static List<int> SelectTopKFromSparse(
        IReadOnlyList<SparseValue> values,
        Func<float, bool> include,
        int take,
        bool distinct)
    {
        if (take <= 0 || values.Count == 0)
            return [];

        if (distinct)
            return SelectUniqueThenCap(values, include, take);

        return SelectTopKStreaming(values, include, take);
    }

    private static List<int> SelectUniqueThenCap(
        IReadOnlyList<SparseValue> values,
        Func<float, bool> include,
        int take)
    {
        Dictionary<int, float> bestAbs = [];
        for (int index = 0; index < values.Count; index++)
        {
            SparseValue item = values[index];
            if (!include(item.Value))
                continue;

            float absolute = MathF.Abs(item.Value);
            if (!float.IsFinite(absolute) || absolute <= ActivationEpsilon)
                continue;

            if (!bestAbs.TryGetValue(item.Index, out float existing) || absolute > existing)
                bestAbs[item.Index] = absolute;
        }

        if (bestAbs.Count == 0)
            return [];

        PriorityQueue<int, float> worstKept = new();
        foreach ((int parameterIndex, float absolute) in bestAbs)
        {
            if (worstKept.Count < take)
            {
                worstKept.Enqueue(parameterIndex, absolute);
            }
            else if (worstKept.TryPeek(out _, out float worst) && absolute > worst)
            {
                worstKept.Dequeue();
                worstKept.Enqueue(parameterIndex, absolute);
            }
        }

        return RankDescending(worstKept);
    }

    private static List<int> SelectTopKStreaming(
        IReadOnlyList<SparseValue> values,
        Func<float, bool> include,
        int take)
    {
        PriorityQueue<int, float> worstKept = new();
        for (int index = 0; index < values.Count; index++)
        {
            SparseValue item = values[index];
            if (!include(item.Value))
                continue;

            float absolute = MathF.Abs(item.Value);
            if (!float.IsFinite(absolute) || absolute <= ActivationEpsilon)
                continue;

            if (worstKept.Count < take)
            {
                worstKept.Enqueue(item.Index, absolute);
            }
            else if (worstKept.TryPeek(out _, out float worst) && absolute > worst)
            {
                worstKept.Dequeue();
                worstKept.Enqueue(item.Index, absolute);
            }
        }

        return RankDescending(worstKept);
    }

    /// <summary>Managed min-heap of size <paramref name="take"/>; same skip rule as <c>hc_heap_top_k_abs</c>.</summary>
    internal static List<int> SelectTopKManaged(
        float[] values,
        int[] indexes,
        int take,
        bool distinct)
    {
        List<SparseValue> packed = new(values.Length);
        for (int index = 0; index < values.Length; index++)
            packed.Add(new SparseValue(indexes[index], values[index]));

        return SelectTopKFromSparse(packed, static _ => true, take, distinct);
    }

    private static List<int> RankDescending(PriorityQueue<int, float> worstKept)
    {
        List<(int Index, float Abs)> ranked = [];
        while (worstKept.TryDequeue(out int selected, out float absolute))
            ranked.Add((selected, absolute));

        ranked.Sort(static (left, right) => right.Abs.CompareTo(left.Abs));
        return ranked.Select(static item => item.Index).ToList();
    }
}
