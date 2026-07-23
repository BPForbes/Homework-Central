namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Stage-1 of the moderation cascade: f(x). Maps reported-concept hypothesis + family /
/// related-concept signals into an 8-d embedding for stage-2 g. Topology: 30 → 24 → 8 (tanh).
/// Linear algebra uses <see cref="NeuralNetwork"/> / Math.NET Numerics.
/// Trained end-to-end via the chain rule: ∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f) where ∂C/∂f comes
/// from backprop through g's cascade input slots (78–85).
/// </summary>
public sealed class ModerationConceptContextRouter : IDisposable
{
    public const int InputSize = 30;
    public const int OutputSize = 8;
    public const int CascadeFeatureStart = 78;

    private const float LearningRate = .04f;
    private const float Momentum = .9f;
    private readonly NeuralNetwork network;
    private readonly object gate = new();

    public ModerationConceptContextRouter(int seed = 0x4D4F4452)
    {
        network = new NeuralNetwork(
            [InputSize, 24, OutputSize],
            ["input", "hidden", "output"],
            [NeuralLayerActivation.Tanh, NeuralLayerActivation.Tanh],
            categoryLabels: null,
            seed);
    }

    public IReadOnlyList<Node> Nodes => network.Nodes;

    public static float[] BuildRouterInput(ModerationConceptSnapshot snapshot)
    {
        float[] values = new float[InputSize];
        values[0] = snapshot.HasHypothesis;
        values[1] = snapshot.RelatedCountNorm;
        values[2] = snapshot.ExactFamilyMatch;
        values[3] = snapshot.RelatedOverlap;
        for (int i = 0; i < ChatMonitoringModerationConceptSignals.FamilyCount; i++)
        {
            if (i < snapshot.FamilyMultiHot.Count)
                values[4 + i] = snapshot.FamilyMultiHot[i];
            if (i < snapshot.TextFamilyScores.Count)
                values[14 + i] = snapshot.TextFamilyScores[i];
        }

        if (snapshot.ReportedFamily is not null)
        {
            int familyIndex = ChatMonitoringModerationConceptSignals.FamilyIndex(snapshot.ReportedFamily);
            if (familyIndex >= 0)
                values[24] = (familyIndex + 1f) / 11f;
        }

        if (snapshot.ReportedConcept is not null
            && ChatMonitoringModerationConcepts.TryGet(snapshot.ReportedConcept, out _))
        {
            int conceptIndex = ChatMonitoringCategoryTaxonomy.IndexOf(
                NeuralModelKindChatMonitoring.Moderation, snapshot.ReportedConcept);
            values[25] = (conceptIndex + 1f) / Math.Max(1, ChatMonitoringCategoryTaxonomy.Moderation.Length);
        }

        values[26] = snapshot.ReportedConcept is null ? 0f : 1f;
        values[27] = Math.Clamp(snapshot.RelatedConcepts.Count / 10f, 0f, 1f);
        return values;
    }

    public ForwardCache ForwardCacheFor(ModerationConceptSnapshot snapshot)
    {
        lock (gate) return new ForwardCache(network.Forward(BuildRouterInput(snapshot)));
    }

    public float[] Forward(ModerationConceptSnapshot snapshot) => ForwardCacheFor(snapshot).Output.ToArray();

    /// <summary>
    /// Accumulate ∇θ_f from upstream ∂C/∂f (chain rule through g(f(x))).
    /// Must use the same <see cref="ForwardCache"/> that produced f for g's forward pass.
    /// </summary>
    public void AccumulateFromOutputGradient(
        ForwardCache state,
        ReadOnlySpan<float> dLossDf,
        NeuralNetworkGradientBuffers gradients)
    {
        if (dLossDf.Length < OutputSize)
            throw new ArgumentException($"Expected at least {OutputSize} upstream gradients.", nameof(dLossDf));

        lock (gate)
            network.AccumulateFromOutputGradient(state.State, dLossDf, gradients);
    }

    public NeuralNetworkGradientBuffers CreateGradientBuffers() => network.CreateGradientBuffers();

    public void ApplyMomentumUpdate(NeuralNetworkGradientBuffers gradients, int batchSize)
    {
        lock (gate)
        {
            // Routers historically did not hard-clamp weights; keep that behavior.
            network.ApplyMomentumUpdate(
                gradients,
                batchSize,
                LearningRate,
                Momentum,
                maxAbsGradient: float.MaxValue,
                maxAbsWeight: null);
        }
    }

    public float ParameterL2Norm()
    {
        lock (gate) return network.ParameterL2Norm();
    }

    public void Dispose() { }

    public sealed class ForwardCache
    {
        internal ForwardCache(NeuralNetworkForwardState state) => State = state;
        internal NeuralNetworkForwardState State { get; }
        public ReadOnlySpan<float> Output => State.Output;
    }
}
