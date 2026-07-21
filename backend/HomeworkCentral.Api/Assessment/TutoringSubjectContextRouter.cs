namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Stage-1 of the tutoring cascade: f(x). Maps multi-subject application + channel signals
/// into an 8-d embedding consumed by stage-2 g. Topology: 30 → 24 → 8 (tanh).
/// Trained end-to-end via the chain rule: ∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f) where ∂C/∂f comes
/// from backprop through g's cascade input slots.
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

    public ForwardCache ForwardCacheFor(SubjectSignalSnapshot snapshot)
    {
        lock (gate) return ForwardUnlocked(BuildRouterInput(snapshot));
    }

    public float[] Forward(SubjectSignalSnapshot snapshot) => ForwardCacheFor(snapshot).Output.ToArray();

    /// <summary>
    /// Accumulate ∇θ_f from upstream ∂C/∂f (chain rule through g(f(x))).
    /// Must use the same <see cref="ForwardCache"/> that produced f for g's forward pass.
    /// </summary>
    public void AccumulateFromOutputGradient(
        ForwardCache state,
        ReadOnlySpan<float> dLossDf,
        float[][] weightGrads,
        float[][] biasGrads)
    {
        if (dLossDf.Length < OutputSize)
            throw new ArgumentException($"Expected at least {OutputSize} upstream gradients.", nameof(dLossDf));
        if (weightGrads.Length != weights.Length || biasGrads.Length != biases.Length)
            throw new ArgumentException("Gradient buffer rank does not match router layers.");

        lock (gate)
        {
            float[][] activationGradients = new float[state.Activations.Length][];
            activationGradients[^1] = new float[OutputSize];
            for (int i = 0; i < OutputSize; i++)
                activationGradients[^1][i] = dLossDf[i];

            for (int layer = weights.Length - 1; layer >= 0; layer--)
            {
                float[] upstream = activationGradients[layer + 1];
                float[] source = state.Activations[layer];
                float[] next = new float[source.Length];
                int sources = widths[layer];
                int targetsCount = widths[layer + 1];
                for (int target = 0; target < targetsCount; target++)
                {
                    // f = tanh(z); ∂C/∂z = (∂C/∂f) · (1 − f²)
                    float gradient = upstream[target] * TanhDerivativeFromActivation(state.Activations[layer + 1][target]);
                    biasGrads[layer][target] += gradient;
                    for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                    {
                        int weightIndex = target * sources + sourceIndex;
                        weightGrads[layer][weightIndex] += gradient * source[sourceIndex];
                        // Use pre-update weights for ∂C/∂a_source (correct chain rule).
                        next[sourceIndex] += gradient * weights[layer][weightIndex];
                    }
                }

                activationGradients[layer] = next;
            }
        }
    }

    public float[][] CreateWeightGradientBuffers() => weights.Select(layer => new float[layer.Length]).ToArray();
    public float[][] CreateBiasGradientBuffers() => biases.Select(layer => new float[layer.Length]).ToArray();

    public static void ClearGradientBuffers(float[][] weightGrads, float[][] biasGrads)
    {
        for (int layer = 0; layer < weightGrads.Length; layer++)
        {
            Array.Clear(weightGrads[layer]);
            Array.Clear(biasGrads[layer]);
        }
    }

    /// <summary>Mini-batch momentum SGD: v ← μv + (1/n)Σ∇, θ ← θ − ηv.</summary>
    public void ApplyMomentumUpdate(float[][] weightGrads, float[][] biasGrads, int batchSize)
    {
        float invN = 1f / Math.Max(1, batchSize);
        lock (gate)
        {
            for (int layer = 0; layer < weights.Length; layer++)
            {
                for (int i = 0; i < weights[layer].Length; i++)
                {
                    float avgGrad = weightGrads[layer][i] * invN;
                    weightVelocity[layer][i] = Momentum * weightVelocity[layer][i] + avgGrad;
                    weights[layer][i] -= LearningRate * weightVelocity[layer][i];
                }

                for (int i = 0; i < biases[layer].Length; i++)
                {
                    float avgGrad = biasGrads[layer][i] * invN;
                    biasVelocity[layer][i] = Momentum * biasVelocity[layer][i] + avgGrad;
                    biases[layer][i] -= LearningRate * biasVelocity[layer][i];
                }
            }
        }
    }

    public float ParameterL2Norm()
    {
        lock (gate)
        {
            double sum = 0;
            foreach (float[] layer in weights)
            {
                foreach (float value in layer)
                    sum += value * value;
            }

            foreach (float[] layer in biases)
            {
                foreach (float value in layer)
                    sum += value * value;
            }

            return MathF.Sqrt((float)sum);
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

    public sealed class ForwardCache
    {
        internal ForwardCache(float[][] activations) => Activations = activations;
        internal float[][] Activations { get; }
        public ReadOnlySpan<float> Output => Activations[^1];
    }
}
