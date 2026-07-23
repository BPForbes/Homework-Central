using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Builds live mesh highlight indexes from forward/backprop traces.
/// Uses parallel partitions so large activation/gradient bags do not stall the training loop
/// on the visualization publish path.
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

    private static List<int> ExtractActiveNodes(ForwardPropagationTrace? forward)
    {
        if (forward is null || forward.NodeActivations.Count == 0)
            return [];

        ConcurrentBag<(int Index, float AbsValue)> candidates = new();
        Parallel.ForEach(
            forward.NodeActivations,
            activation =>
            {
                float absolute = MathF.Abs(activation.Value);
                if (absolute <= ActivationEpsilon)
                    return;
                candidates.Add((activation.Index, absolute));
            });

        return candidates
            .OrderByDescending(item => item.AbsValue)
            .Select(item => item.Index)
            .Distinct()
            .Take(MaxActiveNodes)
            .ToList();
    }

    private static List<int> ExtractActiveEdges(ForwardPropagationTrace? forward, BackpropagationTrace? backward)
    {
        if (backward is not null && backward.WeightGradients.Count > 0)
        {
            return TopParameterIndexes(
                backward.WeightGradients,
                value => MathF.Abs(value) > ActivationEpsilon,
                MaxActiveEdges);
        }

        if (forward is null || forward.EdgeContributions.Count == 0)
            return [];

        return TopParameterIndexes(
            forward.EdgeContributions,
            value => MathF.Abs(value) > ActivationEpsilon,
            MaxActiveEdges);
    }

    private static List<int> TopParameterIndexes(
        IReadOnlyList<SparseValue> values,
        Func<float, bool> include,
        int take)
    {
        ConcurrentBag<(int Index, float AbsValue)> candidates = new();
        Parallel.ForEach(
            values,
            item =>
            {
                if (!include(item.Value))
                    return;
                candidates.Add((item.Index, MathF.Abs(item.Value)));
            });

        return candidates
            .OrderByDescending(item => item.AbsValue)
            .Select(item => item.Index)
            .Take(take)
            .ToList();
    }
}
