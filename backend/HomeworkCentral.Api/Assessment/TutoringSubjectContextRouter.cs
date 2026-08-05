namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Stage-1 of the tutoring cascade: f(x). Maps multi-subject application + channel signals
/// plus hashed specific expertise labels (biology, rust, …) into an 8-d embedding for stage-2 g.
/// Topology: 62 → 32 → 8 (tanh). Linear algebra uses <see cref="NeuralNetwork"/> / Math.NET Numerics.
/// Trained end-to-end via the chain rule: ∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f).
/// </summary>
public sealed class TutoringSubjectContextRouter : IDisposable
{
    public const int ExpertiseHashBins = 32;
    public const int BaseInputSize = 30;
    public const int InputSize = BaseInputSize + ExpertiseHashBins;
    public const int HiddenSize = 32;
    public const int OutputSize = 8;
    public const int CascadeFeatureStart = 78;

    private const float LearningRate = .04f;
    private const float Momentum = .9f;
    private readonly NeuralNetwork network;
    private readonly object gate = new();

    public TutoringSubjectContextRouter(int seed = 0x53554231)
    {
        network = new NeuralNetwork(
            [InputSize, HiddenSize, OutputSize],
            ["input", "hidden", "output"],
            [NeuralLayerActivation.Tanh, NeuralLayerActivation.Tanh],
            categoryLabels: null,
            seed);
    }

    public IReadOnlyList<Node> Nodes => network.Nodes;

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

        // Slots 30–61: hashed specific expertise (biology, rust, …) so the cascade sees
        // fine categories without a separate NN stage or custom rooms.
        foreach (string label in snapshot.AppliedExpertise)
            AddExpertiseHash(values, label);

        return values;
    }

    private static void AddExpertiseHash(float[] values, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;
        string key = label.Trim().ToLowerInvariant();
        uint hash = 2166136261;
        foreach (char character in key)
            hash = (hash ^ character) * 16777619;
        int index = BaseInputSize + (int)(hash % ExpertiseHashBins);
        values[index] = Math.Clamp(values[index] + 1f, 0f, 1f);
    }

    public ForwardCache ForwardCacheFor(SubjectSignalSnapshot snapshot)
    {
        lock (gate) return new ForwardCache(network.Forward(BuildRouterInput(snapshot)));
    }

    public float[] Forward(SubjectSignalSnapshot snapshot) => ForwardCacheFor(snapshot).Output.ToArray();

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
