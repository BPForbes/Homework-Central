namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Stage-1 tutoring cascade net: maps multi-subject application + channel signals into an
/// 8-d subject-context embedding for the evidence scorer. Learns relatedness structure
/// (e.g. Physics/Science supported by Mathematics) without needing the full text encoder.
/// Topology: 30 → 24 → 8.
/// </summary>
public sealed class TutoringSubjectContextRouter : IDisposable
{
    public const int InputSize = 30;
    public const int OutputSize = 8;
    public const int CascadeFeatureStart = 78;

    private const float LearningRate = .04f;
    private const float Momentum = .9f;
    private readonly float[][] weights;
    private readonly float[][] biases;
    private readonly float[][] weightVelocity;
    private readonly float[][] biasVelocity;
    private readonly int[] widths = [InputSize, 24, OutputSize];
    private readonly object gate = new();

    public TutoringSubjectContextRouter(int seed = 0x53554231)
    {
        weights = new float[widths.Length - 1][];
        biases = new float[widths.Length - 1][];
        weightVelocity = new float[widths.Length - 1][];
        biasVelocity = new float[widths.Length - 1][];
        Random random = new(seed);
        for (int layer = 0; layer < widths.Length - 1; layer++)
        {
            int sources = widths[layer];
            int targets = widths[layer + 1];
            weights[layer] = new float[targets * sources];
            biases[layer] = new float[targets];
            weightVelocity[layer] = new float[targets * sources];
            biasVelocity[layer] = new float[targets];
            float scale = MathF.Sqrt(2f / sources);
            for (int i = 0; i < weights[layer].Length; i++)
                weights[layer][i] = (float)((random.NextDouble() * 2d - 1d) * scale);
        }
    }

    public static float[] BuildRouterInput(SubjectSignalSnapshot snapshot)
    {
        float[] values = new float[InputSize];
        values[0] = snapshot.AppliedCountNorm;
        values[1] = snapshot.ExactMatch;
        values[2] = snapshot.RelatedMatch;
        values[3] = snapshot.CrossSubjectSupport;
        for (int i = 0; i < ChatMonitoringSubjectSignals.GeneralSubjectCount; i++)
        {
            string general = ChatMonitoringSubjectSignals.GeneralSubjectsInOrder[i];
            if (snapshot.AppliedGenerals.Any(s => string.Equals(s, general, StringComparison.OrdinalIgnoreCase)))
                values[4 + i] = 1f;
            if (string.Equals(snapshot.ChannelGeneral, general, StringComparison.OrdinalIgnoreCase))
                values[17 + i] = 1f;
        }

        return values;
    }

    public static float[] BuildRouterTargets(SubjectSignalSnapshot snapshot) =>
    [
        snapshot.ExactMatch,
        snapshot.RelatedMatch,
        snapshot.CrossSubjectSupport,
        snapshot.AppliedCountNorm,
        snapshot.EffectiveChannelRelevance,
        Math.Clamp(snapshot.RewardScale / 1.15f, 0f, 1f),
        snapshot.ChannelGeneral is null ? 0f : (ChatMonitoringSubjectSignals.GeneralIndex(snapshot.ChannelGeneral) + 1f) / 14f,
        Math.Clamp(snapshot.AppliedGenerals.Count / 13f, 0f, 1f),
    ];

    public float[] Forward(SubjectSignalSnapshot snapshot)
    {
        lock (gate) return ForwardUnlocked(BuildRouterInput(snapshot)).Activations[^1].ToArray();
    }

    public void Train(SubjectSignalSnapshot snapshot, int epochs = 4)
    {
        float[] input = BuildRouterInput(snapshot);
        float[] targets = BuildRouterTargets(snapshot);
        lock (gate)
        {
            for (int epoch = 0; epoch < Math.Clamp(epochs, 1, 20); epoch++)
            {
                ForwardCache cache = ForwardUnlocked(input);
                float[] output = cache.Activations[^1];
                float[][] grads = new float[cache.Activations.Length][];
                grads[^1] = new float[OutputSize];
                for (int i = 0; i < OutputSize; i++)
                    grads[^1][i] = output[i] - targets[i];

                for (int layer = weights.Length - 1; layer >= 0; layer--)
                {
                    float[] upstream = grads[layer + 1];
                    float[] source = cache.Activations[layer];
                    float[] next = new float[source.Length];
                    int sources = widths[layer];
                    int targetsCount = widths[layer + 1];
                    for (int target = 0; target < targetsCount; target++)
                    {
                        float gradient = upstream[target] * TanhDerivativeFromActivation(cache.Activations[layer + 1][target]);
                        biasVelocity[layer][target] = Momentum * biasVelocity[layer][target] + gradient;
                        biases[layer][target] -= LearningRate * biasVelocity[layer][target];
                        for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                        {
                            int weightIndex = target * sources + sourceIndex;
                            float weightGrad = gradient * source[sourceIndex];
                            weightVelocity[layer][weightIndex] = Momentum * weightVelocity[layer][weightIndex] + weightGrad;
                            weights[layer][weightIndex] -= LearningRate * weightVelocity[layer][weightIndex];
                            next[sourceIndex] += gradient * weights[layer][weightIndex];
                        }
                    }

                    grads[layer] = next;
                }
            }
        }
    }

    public void Dispose() { }

    private ForwardCache ForwardUnlocked(float[] input)
    {
        float[][] activations = new float[widths.Length][];
        activations[0] = input;
        for (int layer = 0; layer < weights.Length; layer++)
        {
            int sources = widths[layer];
            int targets = widths[layer + 1];
            float[] layerAct = new float[targets];
            float[] source = activations[layer];
            for (int target = 0; target < targets; target++)
            {
                float sum = biases[layer][target];
                for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                    sum += weights[layer][target * sources + sourceIndex] * source[sourceIndex];
                layerAct[target] = MathF.Tanh(sum);
            }

            activations[layer + 1] = layerAct;
        }

        return new ForwardCache(activations);
    }

    private static float TanhDerivativeFromActivation(float activation) => 1f - activation * activation;

    private sealed record ForwardCache(float[][] Activations);
}
