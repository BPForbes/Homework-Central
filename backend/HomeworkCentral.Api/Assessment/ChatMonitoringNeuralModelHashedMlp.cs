using System.Security.Cryptography;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// CPU-only hashed MLP for a preinstalled chat-monitoring model. Features come from
/// <see cref="ChatMonitoringFeatureEncoder"/> (48 dense inputs). Separate Moderation and
/// Tutoring subclasses keep independent weights, topologies, and checkpoint lineages.
/// Hidden layers use leaky ReLU (3Blue1Brown-style cheap nonlinearities with He init);
/// sigmoid outputs pair with binary cross-entropy so the output gradient simplifies to
/// prediction − target.
/// </summary>
public abstract class ChatMonitoringNeuralModelHashedMlp : IChatMonitoringNeuralModelTelemetry
{
    public const string RuntimeKind = "HashedMlpLrelu";
    private const float LearningRate = .035f;
    private const float LeakyReluSlope = .01f;
    private readonly int[] layerWidths;
    private readonly string[] layerLabels;
    private readonly float[][] weights;
    private readonly float[][] biases;
    private readonly NeuralNetTopologySnapshot topology;
    private readonly List<SupportExample> support = [];
    private readonly object gate = new();

    protected ChatMonitoringNeuralModelHashedMlp(
        NeuralModelKindChatMonitoring kind,
        string modelVersion,
        int firstHidden,
        int secondHidden,
        int thirdHidden,
        int fourthHidden,
        IReadOnlyList<string> layerLabels,
        int seed)
    {
        Kind = kind;
        ModelVersion = modelVersion;
        this.layerLabels = layerLabels.ToArray();
        layerWidths = [ChatMonitoringFeatureEncoder.FeatureCount, firstHidden, secondHidden, thirdHidden, fourthHidden, 2];
        weights = new float[layerWidths.Length - 1][];
        biases = new float[layerWidths.Length - 1][];
        Random random = new(seed);
        for (int layer = 0; layer < layerWidths.Length - 1; layer++)
        {
            int sources = layerWidths[layer];
            int targets = layerWidths[layer + 1];
            weights[layer] = new float[targets * sources];
            biases[layer] = new float[targets];
            // He / Kaiming normal-ish init for leaky-ReLU hidden layers; smaller scale for sigmoid output.
            float scale = layer == layerWidths.Length - 2
                ? MathF.Sqrt(1f / sources)
                : MathF.Sqrt(2f / sources);
            for (int i = 0; i < weights[layer].Length; i++)
                weights[layer][i] = (float)((random.NextDouble() * 2d - 1d) * scale);
        }

        topology = BuildTopology(this.layerLabels);
    }

    public NeuralModelKindChatMonitoring Kind { get; }
    public string ModelVersion { get; }
    public IReadOnlyList<string> LayerLabels => layerLabels;

    public ChatMonitoringNeuralModelPrediction Predict(ChatMonitoringNeuralModelInput input)
    {
        lock (gate) return PredictUnlocked(input).Prediction;
    }

    public ChatMonitoringNeuralModelInferenceTrace PredictWithTrace(ChatMonitoringNeuralModelInput input)
    {
        lock (gate) return PredictUnlocked(input);
    }

    public void Train(ChatMonitoringNeuralModelInput input, ChatMonitoringNeuralModelTargets targets, int epochs = 12)
    {
        _ = TrainWithTrace(new ChatMonitoringNeuralModelTrainingExample(input, targets, "general"), epochs);
    }

    public TrainingPassTrace TrainWithTrace(
        ChatMonitoringNeuralModelTrainingExample example,
        int epochs = 12,
        NeuralTrainingTraceDetail detail = NeuralTrainingTraceDetail.Full,
        float evidenceTolerance = 0f,
        float relevanceTolerance = 0f,
        float lossStopThreshold = 0f)
    {
        lock (gate)
        {
            float[] features = ChatMonitoringFeatureEncoder.Encode(example.Input);
            List<TrainingIterationReplay> iterations = [];
            int boundedEpochs = Math.Clamp(epochs, 1, 100);
            ChatMonitoringNeuralModelInferenceTrace before = PredictUnlocked(example.Input, features);
            bool earlyStopEnabled = evidenceTolerance > 0f || relevanceTolerance > 0f || lossStopThreshold > 0f;
            for (int epoch = 0; epoch < boundedEpochs; epoch++)
            {
                LossTrace lossBefore = CreateLoss(before.Forward, example.Targets);
                bool captureFull = detail == NeuralTrainingTraceDetail.Full;
                float[]? parameterBefore = captureFull ? ReadParameters() : null;
                float gradientL2 = TrainOneEpochUnlocked(features, example.Targets, out float maxAbsGradient, out float minNonZeroAbsGradient);
                float[]? parameterAfter = captureFull ? ReadParameters() : null;
                IReadOnlyList<ParameterDelta> deltas = captureFull
                    ? BuildParameterDeltas(parameterBefore!, parameterAfter!)
                    : [];
                IReadOnlyList<SparseValue> gradients = captureFull
                    ? deltas.Select(delta => new SparseValue(delta.ParameterIndex, delta.Gradient)).ToList()
                    : [];
                GradientHealth health = new(
                    gradientL2 < 0.000001f,
                    gradientL2 > 1000f,
                    0.000001f,
                    1000f,
                    captureFull
                        ? (gradients.Count == 0 ? 0 : gradients.Max(gradient => MathF.Abs(gradient.Value)))
                        : maxAbsGradient,
                    captureFull
                        ? gradients.Where(gradient => gradient.Value != 0).Select(gradient => MathF.Abs(gradient.Value)).DefaultIfEmpty(0).Min()
                        : minNonZeroAbsGradient);
                BackpropagationTrace backward = new([], [],
                    gradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Weight).ToList(),
                    gradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Bias).ToList(),
                    gradientL2, health);
                // Compact mode still needs post-update probabilities for loss/early-stop without full node traces.
                ChatMonitoringNeuralModelInferenceTrace after = captureFull || epoch == boundedEpochs - 1 || earlyStopEnabled
                    ? PredictUnlocked(example.Input, features)
                    : PredictUnlockedLight(example.Input, features);
                LossTrace lossAfter = CreateLoss(after.Forward, example.Targets);
                iterations.Add(new TrainingIterationReplay(epoch, before.Forward, lossBefore, backward,
                    new ParameterUpdateTrace(LearningRate, "SGD", deltas), after.Forward, lossAfter));
                before = after;

                if (earlyStopEnabled
                    && MathF.Abs(after.Prediction.Evidence - example.Targets.Evidence) <= evidenceTolerance
                    && MathF.Abs(after.Prediction.Relevance - example.Targets.Relevance) <= relevanceTolerance
                    && (lossStopThreshold <= 0f || lossAfter.TotalLoss <= lossStopThreshold))
                {
                    break;
                }
            }

            string category = string.IsNullOrWhiteSpace(example.Category) || example.Category == "general"
                ? ChatMonitoringTicketContext.DetectCategory(example.Input.Requirement, Kind)
                : example.Category;
            support.Add(new SupportExample(features, category));
            if (support.Count > 512)
                support.RemoveAt(0);

            return new TrainingPassTrace(iterations);
        }
    }

    public NeuralNetTopologySnapshot GetTopologySnapshot() => topology;

    public NeuralNetParameterSnapshot GetParameterSnapshot(long? canonicalGeneration, int localRevision)
    {
        lock (gate)
        {
            float[] parameters = ReadParameters();
            byte[] bytes = new byte[parameters.Length * sizeof(float)];
            Buffer.BlockCopy(parameters, 0, bytes, 0, bytes.Length);
            return new NeuralNetParameterSnapshot(canonicalGeneration, localRevision, "ieee754-float32-le", "dense-base64",
                parameters.Length, Convert.ToBase64String(bytes), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    public ChatMonitoringNeuralModelStateSnapshot GetStateSnapshot()
    {
        lock (gate)
        {
            float[] parameters = ReadParameters();
            return new ChatMonitoringNeuralModelStateSnapshot(
                Kind, ModelVersion, layerWidths, layerLabels, parameters.Length,
                MathF.Sqrt(parameters.Sum(value => value * value)), support.Count);
        }
    }

    public void LoadParameterSnapshot(NeuralNetParameterSnapshot snapshot)
    {
        lock (gate)
        {
            if (snapshot.NumericFormat != "ieee754-float32-le" || snapshot.Encoding != "dense-base64")
                throw new InvalidOperationException("Only dense IEEE-754 float32 hashed-MLP checkpoints are supported.");
            byte[] bytes = Convert.FromBase64String(snapshot.PackedValues);
            if (bytes.Length != snapshot.ParameterCount * sizeof(float) || snapshot.ParameterCount != topology.Parameters.Count)
                throw new InvalidOperationException("The checkpoint parameter count does not match this chat-monitoring architecture.");
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), snapshot.Checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The checkpoint checksum is invalid.");
            float[] values = new float[snapshot.ParameterCount];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            WriteParameters(values);
        }
    }

    public void Dispose() { }

    private ChatMonitoringNeuralModelInferenceTrace PredictUnlocked(ChatMonitoringNeuralModelInput input, float[]? encoded = null)
    {
        float[] features = encoded ?? ChatMonitoringFeatureEncoder.Encode(input);
        ForwardCache cache = Forward(features, captureTrace: true);
        return BuildInference(input, features, cache);
    }

    /// <summary>Forward pass with a minimal output-only trace for compact epoch bookkeeping.</summary>
    private ChatMonitoringNeuralModelInferenceTrace PredictUnlockedLight(ChatMonitoringNeuralModelInput input, float[] encoded)
    {
        ForwardCache cache = Forward(encoded, captureTrace: false);
        float evidence = cache.Activations[^1][0];
        float relevance = cache.Activations[^1][1];
        float confidence = Math.Clamp(MathF.Abs(evidence - .5f) * 2f, .05f, .99f);
        ForwardPropagationTrace forward = new([], [], [], [], [],
            cache.PreActivations[^1][0], cache.PreActivations[^1][1], evidence, relevance, confidence);
        string category = ChatMonitoringTicketContext.DetectCategory(input.Requirement, Kind);
        return new ChatMonitoringNeuralModelInferenceTrace(
            new ChatMonitoringNeuralModelPrediction(evidence, relevance, confidence, Kind, ModelVersion, category, "compact"),
            forward);
    }

    private ChatMonitoringNeuralModelInferenceTrace BuildInference(ChatMonitoringNeuralModelInput input, float[] features, ForwardCache cache)
    {
        float evidence = cache.Activations[^1][0];
        float relevance = cache.Activations[^1][1];
        double supportSimilarity = support.Count == 0 ? 0 : support.Max(item => Cosine(features, item.Features));
        double separation = Math.Abs(evidence - .5f) * 2;
        float confidence = (float)Math.Clamp(separation * (.35 + .65 * supportSimilarity), .05, .99);
        string category = ChatMonitoringTicketContext.DetectCategory(input.Requirement, Kind);
        string reasoning = supportSimilarity >= .55
            ? $"Chat-monitor pattern match for {category}; reviewer recommended when confidence is below threshold."
            : $"Limited training support for {category}; reviewer recommended.";
        return new ChatMonitoringNeuralModelInferenceTrace(
            new ChatMonitoringNeuralModelPrediction(evidence, relevance, confidence, Kind, ModelVersion, category, reasoning),
            cache.Trace!);
    }

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        int length = Math.Min(left.Length, right.Length);
        for (int index = 0; index < length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        return leftNorm <= 0 || rightNorm <= 0 ? 0 : Math.Clamp(dot / Math.Sqrt(leftNorm * rightNorm), 0, 1);
    }

    private sealed record SupportExample(float[] Features, string Category);

    private float TrainOneEpochUnlocked(
        float[] features,
        ChatMonitoringNeuralModelTargets targets,
        out float maxAbsGradient,
        out float minNonZeroAbsGradient)
    {
        ForwardCache cache = Forward(features, captureTrace: false);
        float[] output = cache.Activations[^1];
        // Sigmoid + BCE: ∂L/∂z = σ(z) − y (3Blue1Brown / standard ML identity).
        float[][] activationGradients = new float[cache.Activations.Length][];
        activationGradients[^1] = [output[0] - Math.Clamp(targets.Evidence, 0, 1), output[1] - Math.Clamp(targets.Relevance, 0, 1)];
        double gradSq = 0;
        maxAbsGradient = 0;
        minNonZeroAbsGradient = float.MaxValue;
        for (int layer = weights.Length - 1; layer >= 0; layer--)
        {
            float[] upstream = activationGradients[layer + 1];
            float[] source = cache.Activations[layer];
            float[] nextGradient = new float[source.Length];
            int sources = layerWidths[layer];
            int targetsCount = layerWidths[layer + 1];
            bool hiddenLayer = layer < weights.Length - 1;
            for (int target = 0; target < targetsCount; target++)
            {
                float gradient = upstream[target];
                if (hiddenLayer)
                    gradient *= LeakyReluDerivative(cache.PreActivations[layer][target]);

                float abs = MathF.Abs(gradient);
                if (abs > maxAbsGradient) maxAbsGradient = abs;
                if (abs > 0f && abs < minNonZeroAbsGradient) minNonZeroAbsGradient = abs;
                gradSq += gradient * gradient;

                biases[layer][target] -= LearningRate * gradient;
                for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                {
                    int weightIndex = target * sources + sourceIndex;
                    float weightGradient = gradient * source[sourceIndex];
                    float weightAbs = MathF.Abs(weightGradient);
                    if (weightAbs > maxAbsGradient) maxAbsGradient = weightAbs;
                    if (weightAbs > 0f && weightAbs < minNonZeroAbsGradient) minNonZeroAbsGradient = weightAbs;
                    gradSq += weightGradient * weightGradient;
                    nextGradient[sourceIndex] += gradient * weights[layer][weightIndex];
                    weights[layer][weightIndex] -= LearningRate * weightGradient;
                }
            }

            activationGradients[layer] = nextGradient;
        }

        if (minNonZeroAbsGradient == float.MaxValue) minNonZeroAbsGradient = 0;
        return MathF.Sqrt((float)gradSq);
    }

    private ForwardCache Forward(float[] features, bool captureTrace)
    {
        float[][] preActivations = new float[weights.Length][];
        float[][] activations = new float[weights.Length + 1][];
        activations[0] = features;
        for (int layer = 0; layer < weights.Length; layer++)
        {
            int sources = layerWidths[layer];
            int targets = layerWidths[layer + 1];
            float[] layerPre = new float[targets];
            float[] layerAct = new float[targets];
            float[] source = activations[layer];
            bool outputLayer = layer == weights.Length - 1;
            for (int target = 0; target < targets; target++)
            {
                float sum = biases[layer][target];
                for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                    sum += weights[layer][target * sources + sourceIndex] * source[sourceIndex];
                layerPre[target] = sum;
                layerAct[target] = outputLayer
                    ? 1f / (1f + MathF.Exp(-Math.Clamp(sum, -20f, 20f)))
                    : LeakyRelu(sum);
            }

            preActivations[layer] = layerPre;
            activations[layer + 1] = layerAct;
        }

        ForwardPropagationTrace? trace = null;
        if (captureTrace)
        {
            List<FeatureActivation> featureActivations = features.Select((value, index) => new FeatureActivation(index, value, [])).ToList();
            List<SparseValue> nodePreActivations = [];
            List<SparseValue> nodeActivations = [];
            List<SparseValue> edgeContributions = [];
            List<SparseValue> biasContributions = [];
            int nodeOffset = layerWidths[0];
            for (int layer = 0; layer < preActivations.Length; layer++)
            {
                for (int node = 0; node < preActivations[layer].Length; node++)
                {
                    nodePreActivations.Add(new SparseValue(nodeOffset + node, preActivations[layer][node]));
                    nodeActivations.Add(new SparseValue(nodeOffset + node, activations[layer + 1][node]));
                }

                nodeOffset += preActivations[layer].Length;
            }

            int edgeOffset = 0;
            int biasOffset = 0;
            for (int layer = 0; layer < weights.Length; layer++)
            {
                int sources = layerWidths[layer];
                int targets = layerWidths[layer + 1];
                float[] source = activations[layer];
                for (int target = 0; target < targets; target++)
                {
                    for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                        edgeContributions.Add(new SparseValue(edgeOffset + target * sources + sourceIndex,
                            source[sourceIndex] * weights[layer][target * sources + sourceIndex]));
                    biasContributions.Add(new SparseValue(biasOffset + target, biases[layer][target]));
                }

                edgeOffset += targets * sources;
                biasOffset += targets;
            }

            float confidence = Math.Clamp(MathF.Abs(activations[^1][0] - .5f) * 2f, .05f, .99f);
            trace = new ForwardPropagationTrace(featureActivations, nodePreActivations, nodeActivations, edgeContributions, biasContributions,
                preActivations[^1][0], preActivations[^1][1], activations[^1][0], activations[^1][1], confidence);
        }

        return new ForwardCache(activations, preActivations, trace);
    }

    private static float LeakyRelu(float value) => value >= 0f ? value : LeakyReluSlope * value;
    private static float LeakyReluDerivative(float preActivation) => preActivation >= 0f ? 1f : LeakyReluSlope;

    private IReadOnlyList<ParameterDelta> BuildParameterDeltas(float[] before, float[] after)
    {
        List<ParameterDelta> deltas = [];
        for (int index = 0; index < before.Length; index++)
        {
            float delta = after[index] - before[index];
            deltas.Add(new ParameterDelta(index, before[index], -delta / LearningRate, delta, after[index]));
        }

        return deltas;
    }

    private static LossTrace CreateLoss(ForwardPropagationTrace forward, ChatMonitoringNeuralModelTargets targets)
    {
        float evidenceLoss = BinaryCrossEntropy(forward.EvidenceProbability, targets.Evidence);
        float relevanceLoss = BinaryCrossEntropy(forward.RelevanceProbability, targets.Relevance);
        return new LossTrace("binary-cross-entropy-v1", evidenceLoss, relevanceLoss, 0, evidenceLoss + relevanceLoss);
    }

    private static float BinaryCrossEntropy(float probability, float target)
    {
        float bounded = Math.Clamp(probability, .000001f, .999999f);
        return -(target * MathF.Log(bounded) + (1 - target) * MathF.Log(1 - bounded));
    }

    private NeuralNetTopologySnapshot BuildTopology(IReadOnlyList<string> layerLabels)
    {
        List<ReplayNode> nodes = [];
        for (int input = 0; input < layerWidths[0]; input++)
        {
            string label = input < 44 ? $"feature-{input}" : input switch
            {
                44 => "community-vote",
                45 => "channel-relevance",
                46 => "thread-continuity",
                _ => "prior-score",
            };
            nodes.Add(new ReplayNode(nodes.Count, $"input-{input}", layerLabels[0], label, input, false));
        }

        for (int layer = 1; layer < layerWidths.Length; layer++)
        {
            for (int node = 0; node < layerWidths[layer]; node++)
            {
                string label = layer == layerWidths.Length - 1
                    ? (node == 0 ? "Evidence" : "Relevance")
                    : $"{layerLabels[layer]}-{node + 1}";
                nodes.Add(new ReplayNode(nodes.Count, $"{layerLabels[layer]}-{node}", layerLabels[layer], label, null, true));
            }
        }

        List<ReplayEdge> edges = [];
        List<ReplayParameter> parameters = [];
        int sourceOffset = 0;
        int targetOffset = layerWidths[0];
        for (int layer = 0; layer < layerWidths.Length - 1; layer++)
        {
            for (int target = 0; target < layerWidths[layer + 1]; target++)
            {
                for (int source = 0; source < layerWidths[layer]; source++)
                {
                    int parameterIndex = parameters.Count;
                    parameters.Add(new ReplayParameter(parameterIndex, $"weight-{layer}-{target}-{source}", ReplayParameterKind.Weight, sourceOffset + source, targetOffset + target, true));
                    edges.Add(new ReplayEdge(edges.Count, $"edge-{layer}-{source}-{target}", sourceOffset + source, targetOffset + target, parameterIndex));
                }

                parameters.Add(new ReplayParameter(parameters.Count, $"bias-{layer}-{target}", ReplayParameterKind.Bias, null, targetOffset + target, true));
            }

            sourceOffset = targetOffset;
            targetOffset += layerWidths[layer + 1];
        }

        return new NeuralNetTopologySnapshot(ModelVersion, nodes, edges, parameters);
    }

    private float[] ReadParameters()
    {
        float[] values = new float[topology.Parameters.Count];
        int offset = 0;
        for (int layer = 0; layer < weights.Length; layer++)
        {
            int sources = layerWidths[layer];
            int targets = layerWidths[layer + 1];
            for (int target = 0; target < targets; target++)
            {
                for (int source = 0; source < sources; source++)
                    values[offset++] = weights[layer][target * sources + source];
                values[offset++] = biases[layer][target];
            }
        }

        return values;
    }

    private void WriteParameters(float[] values)
    {
        int offset = 0;
        for (int layer = 0; layer < weights.Length; layer++)
        {
            int sources = layerWidths[layer];
            int targets = layerWidths[layer + 1];
            for (int target = 0; target < targets; target++)
            {
                for (int source = 0; source < sources; source++)
                    weights[layer][target * sources + source] = values[offset++];
                biases[layer][target] = values[offset++];
            }
        }
    }

    private sealed record ForwardCache(float[][] Activations, float[][] PreActivations, ForwardPropagationTrace? Trace);
}

/// <summary>Isolated moderation chat-monitor network (separate weights and checkpoint lineage).</summary>
public sealed class ModerationChatMonitorNeuralNet : ChatMonitoringNeuralModelHashedMlp
{
    public ModerationChatMonitorNeuralNet()
        : base(
            NeuralModelKindChatMonitoring.Moderation,
            "hc-chat-monitoring-moderation-v2",
            20, 30, 24, 18,
            ["input", "current-conduct", "behavior-history", "report-correlation", "moderation-decision", "output"],
            seed: 0x4D4F4432)
    {
    }
}

/// <summary>Isolated tutoring chat-monitor network (separate weights and checkpoint lineage).</summary>
public sealed class TutoringChatMonitorNeuralNet : ChatMonitoringNeuralModelHashedMlp
{
    public TutoringChatMonitorNeuralNet()
        : base(
            NeuralModelKindChatMonitoring.Tutoring,
            "hc-chat-monitoring-tutoring-v2",
            20, 32, 28, 20,
            ["input", "current-subject-response", "learning-thread-history", "application-correlation", "tutoring-decision", "output"],
            seed: 0x54555432)
    {
    }
}
