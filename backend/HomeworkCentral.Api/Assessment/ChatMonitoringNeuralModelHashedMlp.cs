using System.Security.Cryptography;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// CPU-only hashed MLP for a preinstalled chat-monitoring model. Features come from
/// <see cref="ChatMonitoringFeatureEncoder"/> (48 dense inputs). Separate Moderation and
/// Tutoring subclasses keep independent weights, topologies, and checkpoint lineages.
/// </summary>
public abstract class ChatMonitoringNeuralModelHashedMlp : IChatMonitoringNeuralModelTelemetry
{
    public const string RuntimeKind = "HashedMlp";
    private const float LearningRate = .035f;
    private readonly int[] layerWidths;
    private readonly float[][] weights;
    private readonly float[][] biases;
    private readonly NeuralNetTopologySnapshot topology;
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
            for (int i = 0; i < weights[layer].Length; i++)
                weights[layer][i] = (float)((random.NextDouble() - .5) * .08);
        }

        topology = BuildTopology(layerLabels);
    }

    public NeuralModelKindChatMonitoring Kind { get; }
    public string ModelVersion { get; }

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

    public TrainingPassTrace TrainWithTrace(ChatMonitoringNeuralModelTrainingExample example, int epochs = 12)
    {
        lock (gate)
        {
            List<TrainingIterationReplay> iterations = [];
            int boundedEpochs = Math.Clamp(epochs, 1, 100);
            ChatMonitoringNeuralModelInferenceTrace before = PredictUnlocked(example.Input);
            for (int epoch = 0; epoch < boundedEpochs; epoch++)
            {
                LossTrace lossBefore = CreateLoss(before.Forward, example.Targets);
                float[] parameterBefore = ReadParameters();
                TrainOneEpochUnlocked(ChatMonitoringFeatureEncoder.Encode(example.Input), example.Targets);
                float[] parameterAfter = ReadParameters();
                IReadOnlyList<ParameterDelta> deltas = BuildParameterDeltas(parameterBefore, parameterAfter);
                IReadOnlyList<SparseValue> gradients = deltas.Select(delta => new SparseValue(delta.ParameterIndex, delta.Gradient)).ToList();
                float gradientL2 = MathF.Sqrt(gradients.Sum(gradient => gradient.Value * gradient.Value));
                GradientHealth health = new(gradientL2 < 0.000001f, gradientL2 > 1000f, 0.000001f, 1000f,
                    gradients.Count == 0 ? 0 : gradients.Max(gradient => MathF.Abs(gradient.Value)),
                    gradients.Where(gradient => gradient.Value != 0).Select(gradient => MathF.Abs(gradient.Value)).DefaultIfEmpty(0).Min());
                BackpropagationTrace backward = new([], [],
                    gradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Weight).ToList(),
                    gradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Bias).ToList(),
                    gradientL2, health);
                ChatMonitoringNeuralModelInferenceTrace after = PredictUnlocked(example.Input);
                iterations.Add(new TrainingIterationReplay(epoch, before.Forward, lossBefore, backward,
                    new ParameterUpdateTrace(LearningRate, "SGD", deltas), after.Forward, CreateLoss(after.Forward, example.Targets)));
                before = after;
            }

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
            return new ChatMonitoringNeuralModelStateSnapshot(Kind, ModelVersion, layerWidths, parameters.Length,
                MathF.Sqrt(parameters.Sum(value => value * value)));
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

    private ChatMonitoringNeuralModelInferenceTrace PredictUnlocked(ChatMonitoringNeuralModelInput input)
    {
        ForwardCache cache = Forward(ChatMonitoringFeatureEncoder.Encode(input), captureTrace: true);
        float evidence = cache.Activations[^1][0];
        float relevance = cache.Activations[^1][1];
        float confidence = Math.Clamp(MathF.Abs(evidence - .5f) * 2f, .05f, .99f);
        return new ChatMonitoringNeuralModelInferenceTrace(
            new ChatMonitoringNeuralModelPrediction(evidence, relevance, confidence, Kind, ModelVersion), cache.Trace!);
    }

    private void TrainOneEpochUnlocked(float[] features, ChatMonitoringNeuralModelTargets targets)
    {
        ForwardCache cache = Forward(features, captureTrace: false);
        float[] output = cache.Activations[^1];
        float[][] activationGradients = new float[cache.Activations.Length][];
        activationGradients[^1] = [output[0] - Math.Clamp(targets.Evidence, 0, 1), output[1] - Math.Clamp(targets.Relevance, 0, 1)];
        for (int layer = weights.Length - 1; layer >= 0; layer--)
        {
            float[] upstream = activationGradients[layer + 1];
            float[] source = cache.Activations[layer];
            float[] nextGradient = new float[source.Length];
            int sources = layerWidths[layer];
            int targetsCount = layerWidths[layer + 1];
            for (int target = 0; target < targetsCount; target++)
            {
                float gradient = upstream[target];
                if (layer < weights.Length - 1)
                {
                    float activation = cache.Activations[layer + 1][target];
                    gradient *= 1f - activation * activation;
                }

                biases[layer][target] -= LearningRate * gradient;
                for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                {
                    int weightIndex = target * sources + sourceIndex;
                    nextGradient[sourceIndex] += gradient * weights[layer][weightIndex];
                    weights[layer][weightIndex] -= LearningRate * gradient * source[sourceIndex];
                }
            }

            activationGradients[layer] = nextGradient;
        }
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
                    : MathF.Tanh(sum);
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
            "hc-chat-monitoring-moderation-v1",
            20, 30, 24, 18,
            ["input", "current-conduct", "behavior-history", "report-correlation", "moderation-decision", "output"],
            seed: 0x4D4F4431)
    {
    }
}

/// <summary>Isolated tutoring chat-monitor network (separate weights and checkpoint lineage).</summary>
public sealed class TutoringChatMonitorNeuralNet : ChatMonitoringNeuralModelHashedMlp
{
    public TutoringChatMonitorNeuralNet()
        : base(
            NeuralModelKindChatMonitoring.Tutoring,
            "hc-chat-monitoring-tutoring-v1",
            20, 32, 28, 20,
            ["input", "current-subject-response", "learning-thread-history", "application-correlation", "tutoring-decision", "output"],
            seed: 0x54555431)
    {
    }
}
