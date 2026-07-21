using System.Security.Cryptography;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// TorchSharp implementation for a preinstalled chat-monitoring model.  All values
/// exposed through replay telemetry are read from the tensors that produced the prediction.
/// </summary>
public abstract class ChatMonitoringNeuralModelTorchSharp : IChatMonitoringNeuralModelTelemetry, IDisposable
{
    private const float LearningRate = .035f;
    private readonly Linear first;
    private readonly Linear second;
    private readonly Linear third;
    private readonly Linear fourth;
    private readonly Linear output;
    private readonly optim.Optimizer optimizer;
    private readonly Device device;
    private readonly object gate = new();
    private readonly int[] layerWidths;
    private readonly NeuralNetTopologySnapshot topology;

    protected ChatMonitoringNeuralModelTorchSharp(
        NeuralModelKindChatMonitoring kind,
        string modelVersion,
        int firstHidden,
        int secondHidden,
        int thirdHidden,
        int fourthHidden,
        IReadOnlyList<string> layerLabels)
    {
        Kind = kind;
        ModelVersion = modelVersion;
        layerWidths = [48, firstHidden, secondHidden, thirdHidden, fourthHidden, 2];
        device = cuda.is_available() && Environment.GetEnvironmentVariable("HC_TORCH_CUDA") == "1" ? CUDA : CPU;
        first = nn.Linear(48, firstHidden, device: device);
        second = nn.Linear(firstHidden, secondHidden, device: device);
        third = nn.Linear(secondHidden, thirdHidden, device: device);
        fourth = nn.Linear(thirdHidden, fourthHidden, device: device);
        output = nn.Linear(fourthHidden, 2, device: device);
        optimizer = optim.SGD(Parameters(), LearningRate);
        topology = BuildTopology(layerLabels);
    }

    public NeuralModelKindChatMonitoring Kind { get; }
    public string ModelVersion { get; }

    public ChatMonitoringNeuralModelPrediction Predict(ChatMonitoringNeuralModelInput input)
    {
        lock (gate)
        {
            return PredictUnlocked(input).Prediction;
        }
    }

    public ChatMonitoringNeuralModelInferenceTrace PredictWithTrace(ChatMonitoringNeuralModelInput input)
    {
        lock (gate)
        {
            return PredictUnlocked(input);
        }
    }

    public void Train(ChatMonitoringNeuralModelInput input, ChatMonitoringNeuralModelTargets targets, int epochs = 12)
    {
        ChatMonitoringNeuralModelTrainingExample example = new(input, targets, "general");
        _ = TrainWithTrace(example, epochs);
    }

    public TrainingPassTrace TrainWithTrace(ChatMonitoringNeuralModelTrainingExample example, int epochs = 12)
    {
        lock (gate)
        {
            List<TrainingIterationReplay> iterations = [];
            int boundedEpochs = Math.Clamp(epochs, 1, 100);
            // An epoch's post-update forward pass and the next epoch's pre-update forward pass are the
            // same weights/input, so the trace is carried forward instead of being recomputed from scratch.
            // That halves the forward-pass (and per-parameter trace allocation) cost of a training pass.
            ChatMonitoringNeuralModelInferenceTrace before = PredictUnlocked(example.Input);
            for (int epoch = 0; epoch < boundedEpochs; epoch++)
            {
                LossTrace lossBefore = CreateLoss(before.Forward, example.Targets);
                float[] parameterBefore = ReadParameters();
                TrainOneEpochUnlocked(example.Input, example.Targets);
                float[] parameterAfter = ReadParameters();
                IReadOnlyList<ParameterDelta> deltas = BuildParameterDeltas(parameterBefore, parameterAfter);
                IReadOnlyList<SparseValue> gradients = deltas.Select(delta => new SparseValue(delta.ParameterIndex, delta.Gradient)).ToList();
                float gradientL2 = MathF.Sqrt(gradients.Sum(gradient => gradient.Value * gradient.Value));
                GradientHealth health = new(gradientL2 < 0.000001f, gradientL2 > 1000f, 0.000001f, 1000f,
                    gradients.Count == 0 ? 0 : gradients.Max(gradient => MathF.Abs(gradient.Value)),
                    gradients.Where(gradient => gradient.Value != 0).Select(gradient => MathF.Abs(gradient.Value)).DefaultIfEmpty(0).Min());
                BackpropagationTrace backward = new([], [], gradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Weight).ToList(), gradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Bias).ToList(), gradientL2, health);
                ParameterUpdateTrace update = new(LearningRate, "SGD", deltas);
                ChatMonitoringNeuralModelInferenceTrace after = PredictUnlocked(example.Input);
                LossTrace lossAfter = CreateLoss(after.Forward, example.Targets);
                iterations.Add(new TrainingIterationReplay(epoch, before.Forward, lossBefore, backward, update, after.Forward, lossAfter));
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
            string packedValues = Convert.ToBase64String(bytes);
            string checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new NeuralNetParameterSnapshot(canonicalGeneration, localRevision, "ieee754-float32-le", "dense-base64", parameters.Length, packedValues, checksum);
        }
    }

    public ChatMonitoringNeuralModelStateSnapshot GetStateSnapshot()
    {
        lock (gate)
        {
            float[] parameters = ReadParameters();
            float norm = MathF.Sqrt(parameters.Sum(value => value * value));
            return new ChatMonitoringNeuralModelStateSnapshot(Kind, ModelVersion, layerWidths, parameters.Length, norm);
        }
    }

    public void LoadParameterSnapshot(NeuralNetParameterSnapshot snapshot)
    {
        lock (gate)
        {
            if (snapshot.NumericFormat != "ieee754-float32-le" || snapshot.Encoding != "dense-base64")
                throw new InvalidOperationException("Only dense IEEE-754 float32 TorchSharp checkpoints are supported.");
            byte[] bytes = Convert.FromBase64String(snapshot.PackedValues);
            if (bytes.Length != snapshot.ParameterCount * sizeof(float) || snapshot.ParameterCount != topology.Parameters.Count)
                throw new InvalidOperationException("The checkpoint parameter count does not match this chat-monitoring architecture.");
            string checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(checksum, snapshot.Checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The checkpoint checksum is invalid.");
            float[] values = new float[snapshot.ParameterCount];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            int offset = 0;
            using IDisposable noGrad = no_grad();
            foreach (Tensor parameter in ParameterTensors())
            {
                int count = checked((int)parameter.NumberOfElements);
                using Tensor replacement = tensor(values.Skip(offset).Take(count).ToArray(), dtype: ScalarType.Float32, device: device).reshape(parameter.shape);
                parameter.copy_(replacement, false);
                offset += count;
            }
        }
    }

    public void Dispose()
    {
        optimizer.Dispose();
        first.Dispose();
        second.Dispose();
        third.Dispose();
        fourth.Dispose();
        output.Dispose();
    }

    private ChatMonitoringNeuralModelInferenceTrace PredictUnlocked(ChatMonitoringNeuralModelInput input)
    {
        using Tensor features = Encode(input);
        using Tensor firstPre = first.forward(features);
        using Tensor firstActivation = tanh(firstPre);
        using Tensor secondPre = second.forward(firstActivation);
        using Tensor secondActivation = tanh(secondPre);
        using Tensor thirdPre = third.forward(secondActivation);
        using Tensor thirdActivation = tanh(thirdPre);
        using Tensor fourthPre = fourth.forward(thirdActivation);
        using Tensor fourthActivation = tanh(fourthPre);
        using Tensor logits = output.forward(fourthActivation);
        using Tensor probabilities = logits.sigmoid();
        float evidence = probabilities[0, 0].ToSingle();
        float relevance = probabilities[0, 1].ToSingle();
        float confidence = Math.Clamp(MathF.Abs(evidence - .5f) * 2f, .05f, .99f);
        ForwardPropagationTrace forward = BuildForwardTrace(input, features, [firstPre, secondPre, thirdPre, fourthPre, logits], [firstActivation, secondActivation, thirdActivation, fourthActivation, probabilities], confidence);
        return new ChatMonitoringNeuralModelInferenceTrace(new ChatMonitoringNeuralModelPrediction(evidence, relevance, confidence, Kind, ModelVersion), forward);
    }

    private void TrainOneEpochUnlocked(ChatMonitoringNeuralModelInput input, ChatMonitoringNeuralModelTargets targets)
    {
        using Tensor features = Encode(input);
        using Tensor target = tensor(new[] { targets.Evidence, targets.Relevance }, dtype: ScalarType.Float32, device: device).reshape(1, 2);
        using Tensor firstPre = first.forward(features);
        using Tensor firstActivation = tanh(firstPre);
        using Tensor secondPre = second.forward(firstActivation);
        using Tensor secondActivation = tanh(secondPre);
        using Tensor thirdPre = third.forward(secondActivation);
        using Tensor thirdActivation = tanh(thirdPre);
        using Tensor fourthPre = fourth.forward(thirdActivation);
        using Tensor fourthActivation = tanh(fourthPre);
        using Tensor logits = output.forward(fourthActivation);
        using Tensor loss = nn.functional.binary_cross_entropy_with_logits(logits, target);
        optimizer.zero_grad();
        loss.backward();
        optimizer.step();
    }

    private ForwardPropagationTrace BuildForwardTrace(ChatMonitoringNeuralModelInput input, Tensor features, IReadOnlyList<Tensor> preActivations, IReadOnlyList<Tensor> activations, float confidence)
    {
        float[] inputValues = ReadTensor(features);
        List<FeatureActivation> featureActivations = inputValues.Select((value, index) => new FeatureActivation(index, value, [])).ToList();
        List<SparseValue> nodePreActivations = [];
        List<SparseValue> nodeActivations = [];
        int nodeOffset = 48;
        for (int layer = 0; layer < preActivations.Count; layer++)
        {
            float[] layerPreActivations = ReadTensor(preActivations[layer]);
            float[] layerActivations = ReadTensor(activations[layer]);
            for (int node = 0; node < layerPreActivations.Length; node++)
            {
                nodePreActivations.Add(new SparseValue(nodeOffset + node, layerPreActivations[node]));
                nodeActivations.Add(new SparseValue(nodeOffset + node, layerActivations[node]));
            }
            nodeOffset += layerPreActivations.Length;
        }

        List<SparseValue> edgeContributions = [];
        List<SparseValue> biasContributions = [];
        float[] source = inputValues;
        Tensor[] weights = [first.weight, second.weight, third.weight, fourth.weight, output.weight];
        Tensor[] biases = [first.bias!, second.bias!, third.bias!, fourth.bias!, output.bias!];
        int edgeOffset = 0;
        int biasOffset = 0;
        for (int layer = 0; layer < weights.Length; layer++)
        {
            float[] weightValues = ReadTensor(weights[layer]);
            float[] biasValues = ReadTensor(biases[layer]);
            int targets = layerWidths[layer + 1];
            int sources = layerWidths[layer];
            for (int target = 0; target < targets; target++)
            {
                for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                    edgeContributions.Add(new SparseValue(edgeOffset + target * sources + sourceIndex, source[sourceIndex] * weightValues[target * sources + sourceIndex]));
                biasContributions.Add(new SparseValue(biasOffset + target, biasValues[target]));
            }
            edgeOffset += targets * sources;
            biasOffset += targets;
            source = ReadTensor(activations[layer]);
        }
        float[] logits = ReadTensor(preActivations[^1]);
        float[] probabilities = ReadTensor(activations[^1]);
        return new ForwardPropagationTrace(featureActivations, nodePreActivations, nodeActivations, edgeContributions, biasContributions, logits[0], logits[1], probabilities[0], probabilities[1], confidence);
    }

    private IReadOnlyList<ParameterDelta> BuildParameterDeltas(float[] before, float[] after)
    {
        List<ParameterDelta> deltas = [];
        for (int index = 0; index < before.Length; index++)
        {
            float delta = after[index] - before[index];
            float gradient = -delta / LearningRate;
            deltas.Add(new ParameterDelta(index, before[index], gradient, delta, after[index]));
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
        float boundedProbability = Math.Clamp(probability, .000001f, .999999f);
        return -(target * MathF.Log(boundedProbability) + (1 - target) * MathF.Log(1 - boundedProbability));
    }

    private NeuralNetTopologySnapshot BuildTopology(IReadOnlyList<string> layerLabels)
    {
        List<ReplayNode> nodes = [];
        for (int input = 0; input < 48; input++) nodes.Add(new ReplayNode(nodes.Count, $"input-{input}", layerLabels[0], input < 44 ? $"feature-{input}" : input switch { 44 => "community-vote", 45 => "channel-relevance", 46 => "thread-continuity", _ => "prior-score" }, input, false));
        for (int layer = 1; layer < layerWidths.Length; layer++)
            for (int node = 0; node < layerWidths[layer]; node++) nodes.Add(new ReplayNode(nodes.Count, $"{layerLabels[layer]}-{node}", layerLabels[layer], layer == layerWidths.Length - 1 ? (node == 0 ? "Evidence" : "Relevance") : $"{layerLabels[layer]}-{node + 1}", null, true));

        List<ReplayEdge> edges = [];
        List<ReplayParameter> parameters = [];
        int sourceOffset = 0;
        int targetOffset = 48;
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

    private float[] ReadParameters() => ParameterTensors().SelectMany(ReadTensor).ToArray();

    private IEnumerable<Tensor> ParameterTensors()
    {
        yield return first.weight; yield return first.bias!;
        yield return second.weight; yield return second.bias!;
        yield return third.weight; yield return third.bias!;
        yield return fourth.weight; yield return fourth.bias!;
        yield return output.weight; yield return output.bias!;
    }

    private IEnumerable<Parameter> Parameters() => first.parameters().Concat(second.parameters()).Concat(third.parameters()).Concat(fourth.parameters()).Concat(output.parameters());

    private static float[] ReadTensor(Tensor tensor)
    {
        using Tensor cpuTensor = tensor.cpu();
        int count = checked((int)cpuTensor.NumberOfElements);
        float[] values = new float[count];
        for (int index = 0; index < count; index++) values[index] = cpuTensor.ReadCpuSingle(index);
        return values;
    }

    private Tensor Encode(ChatMonitoringNeuralModelInput input)
    {
        float[] values = ChatMonitoringFeatureEncoder.Encode(input);
        return tensor(values, dtype: ScalarType.Float32, device: device).reshape(1, 48);
    }
}

public sealed class ModerationChatMonitorNeuralNet : ChatMonitoringNeuralModelTorchSharp
{
    public ModerationChatMonitorNeuralNet()
        : base(NeuralModelKindChatMonitoring.Moderation, "hc-chat-monitoring-moderation-v1", 20, 30, 24, 18, ["input", "current-conduct", "behavior-history", "report-correlation", "moderation-decision", "output"])
    {
    }
}

public sealed class TutoringChatMonitorNeuralNet : ChatMonitoringNeuralModelTorchSharp
{
    public TutoringChatMonitorNeuralNet()
        : base(NeuralModelKindChatMonitoring.Tutoring, "hc-chat-monitoring-tutoring-v1", 20, 32, 28, 20, ["input", "current-subject-response", "learning-thread-history", "application-correlation", "tutoring-decision", "output"])
    {
    }
}
