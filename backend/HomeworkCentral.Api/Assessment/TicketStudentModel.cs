using HomeworkCentral.Api.Models;
using System.Security.Cryptography;

namespace HomeworkCentral.Api.Assessment;

public sealed record StudentTrainingExample(
    string Requirement,
    string Message,
    double EvidenceScore,
    double Relevance,
    string Category);

public sealed record TicketStudentPrediction(
    double EvidenceScore,
    double Relevance,
    double Confidence,
    string Category,
    string Reasoning,
    string ModelVersion);

public sealed record NeuralNetStateSnapshot(
    int InputNodes,
    int HiddenNodes,
    int OutputNodes,
    int SupportExamples,
    double InputWeightL2,
    double OutputWeightL2,
    double HiddenBiasL2,
    double OutputBiasL2,
    IReadOnlyList<float> HiddenBias,
    IReadOnlyList<float> OutputBias);

public interface ITicketStudentModel
{
    TicketStudentPrediction Predict(string requirement, string message);
    IReadOnlyList<float> Embed(string text);
    void Train(StudentTrainingExample example, int epochs = 12);
    NeuralNetStateSnapshot GetStateSnapshot();
}

public interface ITicketStudentModelTelemetry
{
    TicketStudentInferenceTrace PredictWithTrace(string requirement, string message);
    TrainingPassTrace TrainWithTrace(StudentTrainingExample example, int epochs = 12);
    NeuralNetTopologySnapshot GetTopologySnapshot();
    NeuralNetParameterSnapshot GetParameterSnapshot(long? canonicalGeneration, int localRevision);
    void LoadParameterSnapshot(NeuralNetParameterSnapshot snapshot);
}
/// <summary>
/// A deliberately tiny, CPU-only neural student: 256 hashed text inputs, one
/// 8-neuron hidden layer, and evidence/relevance outputs. Its weights occupy
/// roughly 17 KB and inference does not allocate a dense feature vector.
/// </summary>
public sealed class TicketStudentModel : ITicketStudentModel, ITicketStudentModelTelemetry
{
    private const int InputSize = 256;
    private const int HiddenSize = 8;
    private const int EmbeddingSize = 64;
    private const double LearningRate = 0.035;
    private readonly float[] inputWeights = new float[InputSize * HiddenSize];
    private readonly float[] hiddenBias = new float[HiddenSize];
    private readonly float[] outputWeights = new float[HiddenSize * 2];
    private readonly float[] outputBias = new float[2];
    private readonly List<SupportExample> support = [];
    private readonly ReaderWriterLockSlim gate = new();

    public TicketStudentModel()
    {
        Random random = new(0x48434D4C);
        for (int i = 0; i < inputWeights.Length; i++)
            inputWeights[i] = (float)((random.NextDouble() - 0.5) * 0.08);
        for (int i = 0; i < outputWeights.Length; i++)
            outputWeights[i] = (float)((random.NextDouble() - 0.5) * 0.08);

    }

    public TicketStudentPrediction Predict(string requirement, string message)
    {
        Dictionary<int, float> features = Featurize(requirement, message, InputSize);
        gate.EnterReadLock();
        try
        {
            (double evidence, double relevance) = Forward(features);
            double supportSimilarity = support.Count == 0
                ? 0
                : support.Max(item => Cosine(features, item.Features));
            double separation = Math.Abs(evidence - 0.5) * 2;
            double confidence = Math.Clamp(separation * (0.35 + 0.65 * supportSimilarity), 0.05, 0.99);
            string category = DetectCategory(requirement);
            string reasoning = supportSimilarity >= 0.55
                ? $"Student pattern match for {category}; reviewer recommended when confidence is below threshold."
                : $"Limited training support for {category}; reviewer recommended."
                ;
            return new TicketStudentPrediction(evidence, relevance, confidence, category, reasoning, "hc-student-mlp-v1");
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public IReadOnlyList<float> Embed(string text)
    {
        Dictionary<int, float> sparse = Featurize(string.Empty, text, EmbeddingSize);
        float[] embedding = new float[EmbeddingSize];
        double norm = Math.Sqrt(sparse.Values.Sum(value => value * value));
        if (norm <= 0)
            return embedding;
        foreach ((int index, float value) in sparse)
            embedding[index] = (float)(value / norm);
        return embedding;
    }

    public void Train(StudentTrainingExample example, int epochs = 12)
    {
        gate.EnterWriteLock();
        try
        {
            TrainCore(example, Math.Clamp(epochs, 1, 100), remember: true);
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    public NeuralNetStateSnapshot GetStateSnapshot()
    {
        gate.EnterReadLock();
        try
        {
            return new NeuralNetStateSnapshot(
                InputSize, HiddenSize, 2, support.Count,
                L2(inputWeights), L2(outputWeights), L2(hiddenBias), L2(outputBias),
                hiddenBias.ToArray(), outputBias.ToArray());
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public TicketStudentInferenceTrace PredictWithTrace(string requirement, string message)
    {
        Dictionary<int, float> features = Featurize(requirement, message, InputSize);
        gate.EnterReadLock();
        try
        {
            ForwardPropagationTrace forward = CaptureForward(features);
            TicketStudentPrediction prediction = BuildPrediction(requirement, features, forward.EvidenceProbability, forward.RelevanceProbability);
            return new TicketStudentInferenceTrace(prediction, forward);
        }
        finally { gate.ExitReadLock(); }
    }

    public TrainingPassTrace TrainWithTrace(StudentTrainingExample example, int epochs = 12)
    {
        gate.EnterWriteLock();
        try
        {
            Dictionary<int, float> features = Featurize(example.Requirement, example.Message, InputSize);
            List<TrainingIterationReplay> iterations = [];
            for (int epoch = 0; epoch < Math.Clamp(epochs, 1, 100); epoch++)
            {
                ForwardPropagationTrace before = CaptureForward(features);
                LossTrace lossBefore = Loss(before, example);
                float[] hidden = before.NodeActivations.Where(x => x.Index is >= 256 and < 264).OrderBy(x => x.Index).Select(x => x.Value).ToArray();
                float[] outputGradient = [(float)(before.EvidenceProbability - example.EvidenceScore), (float)(before.RelevanceProbability - example.Relevance)];
                float[] hiddenGradient = new float[HiddenSize];
                for (int h = 0; h < HiddenSize; h++)
                {
                    float downstream = outputGradient[0] * outputWeights[h] + outputGradient[1] * outputWeights[HiddenSize + h];
                    hiddenGradient[h] = downstream * (1 - hidden[h] * hidden[h]);
                }
                List<SparseValue> activationGradients = []; List<SparseValue> preActivationGradients = []; List<SparseValue> weightGradients = []; List<SparseValue> biasGradients = []; List<ParameterDelta> deltas = [];
                for (int output = 0; output < 2; output++)
                {
                    float probability = output == 0 ? before.EvidenceProbability : before.RelevanceProbability;
                    float target = output == 0 ? (float)example.EvidenceScore : (float)example.Relevance;
                    float activationGradient = (probability - target) / Math.Max(probability * (1 - probability), 1e-6f);
                    activationGradients.Add(new SparseValue(InputSize + HiddenSize + output, activationGradient));
                    preActivationGradients.Add(new SparseValue(InputSize + HiddenSize + output, outputGradient[output]));
                }
                for (int h = 0; h < HiddenSize; h++)
                {
                    float derivative = 1 - hidden[h] * hidden[h];
                    activationGradients.Add(new SparseValue(InputSize + h, derivative == 0 ? 0 : hiddenGradient[h] / derivative));
                    preActivationGradients.Add(new SparseValue(InputSize + h, hiddenGradient[h]));
                }
                for (int output = 0; output < 2; output++)
                    for (int h = 0; h < HiddenSize; h++)
                    {
                        int parameter = InputSize * HiddenSize + HiddenSize + output * HiddenSize + h;
                        float gradient = outputGradient[output] * hidden[h]; float beforeValue = outputWeights[output * HiddenSize + h]; float delta = -((float)LearningRate * gradient);
                        outputWeights[output * HiddenSize + h] = beforeValue + delta; weightGradients.Add(new SparseValue(parameter, gradient)); deltas.Add(new ParameterDelta(parameter, beforeValue, gradient, delta, outputWeights[output * HiddenSize + h]));
                    }
                for (int output = 0; output < 2; output++)
                {
                    int parameter = InputSize * HiddenSize + HiddenSize + HiddenSize * 2 + output; float gradient = outputGradient[output]; float beforeValue = outputBias[output]; float delta = -((float)LearningRate * gradient);
                    outputBias[output] = beforeValue + delta; biasGradients.Add(new SparseValue(parameter, gradient)); deltas.Add(new ParameterDelta(parameter, beforeValue, gradient, delta, outputBias[output]));
                }
                for (int h = 0; h < HiddenSize; h++)
                {
                    foreach ((int index, float value) in features)
                    {
                        int parameter = h * InputSize + index; float gradient = hiddenGradient[h] * value; float beforeValue = inputWeights[parameter]; float delta = -((float)LearningRate * gradient);
                        inputWeights[parameter] = beforeValue + delta; weightGradients.Add(new SparseValue(parameter, gradient)); deltas.Add(new ParameterDelta(parameter, beforeValue, gradient, delta, inputWeights[parameter]));
                    }
                    int biasParameter = InputSize * HiddenSize + h; float biasBefore = hiddenBias[h]; float biasDelta = -((float)LearningRate * hiddenGradient[h]); hiddenBias[h] = biasBefore + biasDelta; biasGradients.Add(new SparseValue(biasParameter, hiddenGradient[h])); deltas.Add(new ParameterDelta(biasParameter, biasBefore, hiddenGradient[h], biasDelta, hiddenBias[h]));
                }
                float[] allGradients = weightGradients.Concat(biasGradients).Select(x => x.Value).ToArray(); float max = allGradients.Length == 0 ? 0 : allGradients.Max(x => MathF.Abs(x)); float min = allGradients.Where(x => x != 0).DefaultIfEmpty(0).Min(x => MathF.Abs(x)); float l2 = MathF.Sqrt(allGradients.Sum(x => x * x));
                BackpropagationTrace backward = new(activationGradients, preActivationGradients, weightGradients, biasGradients, l2, new GradientHealth(l2 > 0 && l2 < 1e-6f, l2 > 100f, 1e-6f, 100f, max, min));
                ForwardPropagationTrace after = CaptureForward(features); LossTrace lossAfter = Loss(after, example);
                iterations.Add(new TrainingIterationReplay(epoch, before, lossBefore, backward, new ParameterUpdateTrace((float)LearningRate, "SGD", deltas), after, lossAfter));
            }
            support.Add(new SupportExample(features, example.Category)); if (support.Count > 512) support.RemoveAt(0);
            return new TrainingPassTrace(iterations);
        }
        finally { gate.ExitWriteLock(); }
    }

    public NeuralNetTopologySnapshot GetTopologySnapshot()
    {
        List<ReplayNode> nodes = []; List<ReplayEdge> edges = []; List<ReplayParameter> parameters = [];
        for (int input = 0; input < InputSize; input++) nodes.Add(new ReplayNode(input, $"input-{input}", "input", $"Feature {input}", input, false));
        for (int hidden = 0; hidden < HiddenSize; hidden++) nodes.Add(new ReplayNode(InputSize + hidden, $"hidden-{hidden}", "hidden", $"Hidden {hidden + 1}", null, true));
        nodes.Add(new ReplayNode(InputSize + HiddenSize, "output-evidence", "output", "Evidence", null, true)); nodes.Add(new ReplayNode(InputSize + HiddenSize + 1, "output-relevance", "output", "Relevance", null, true));
        int edge = 0;
        for (int hidden = 0; hidden < HiddenSize; hidden++) for (int input = 0; input < InputSize; input++) { int p = hidden * InputSize + input; parameters.Add(new ReplayParameter(p, $"w-input-{input}-hidden-{hidden}", ReplayParameterKind.Weight, input, InputSize + hidden, true)); edges.Add(new ReplayEdge(edge++, $"edge-{input}-{hidden}", input, InputSize + hidden, p)); }
        for (int hidden = 0; hidden < HiddenSize; hidden++) parameters.Add(new ReplayParameter(InputSize * HiddenSize + hidden, $"b-hidden-{hidden}", ReplayParameterKind.Bias, null, InputSize + hidden, true));
        for (int output = 0; output < 2; output++) for (int hidden = 0; hidden < HiddenSize; hidden++) { int p = InputSize * HiddenSize + HiddenSize + output * HiddenSize + hidden; parameters.Add(new ReplayParameter(p, $"w-hidden-{hidden}-output-{output}", ReplayParameterKind.Weight, InputSize + hidden, InputSize + HiddenSize + output, true)); edges.Add(new ReplayEdge(edge++, $"edge-hidden-{hidden}-output-{output}", InputSize + hidden, InputSize + HiddenSize + output, p)); }
        for (int output = 0; output < 2; output++) parameters.Add(new ReplayParameter(InputSize * HiddenSize + HiddenSize + HiddenSize * 2 + output, $"b-output-{output}", ReplayParameterKind.Bias, null, InputSize + HiddenSize + output, true));
        return new NeuralNetTopologySnapshot("hc-student-mlp-v2", nodes, edges, parameters);
    }

    public NeuralNetParameterSnapshot GetParameterSnapshot(long? canonicalGeneration, int localRevision)
    {
        gate.EnterReadLock();
        try
        {
            float[] values = new float[inputWeights.Length + hiddenBias.Length + outputWeights.Length + outputBias.Length];
            int offset = 0;
            Array.Copy(inputWeights, 0, values, offset, inputWeights.Length); offset += inputWeights.Length;
            Array.Copy(hiddenBias, 0, values, offset, hiddenBias.Length); offset += hiddenBias.Length;
            Array.Copy(outputWeights, 0, values, offset, outputWeights.Length); offset += outputWeights.Length;
            Array.Copy(outputBias, 0, values, offset, outputBias.Length);
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return new(canonicalGeneration, localRevision, "ieee754-float32-le", "dense-base64", values.Length,
                Convert.ToBase64String(bytes), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        finally { gate.ExitReadLock(); }
    }

    public void LoadParameterSnapshot(NeuralNetParameterSnapshot snapshot)
    {
        if (snapshot.NumericFormat != "ieee754-float32-le" || snapshot.ParameterCount != inputWeights.Length + hiddenBias.Length + outputWeights.Length + outputBias.Length)
            throw new InvalidDataException("Unsupported student-model checkpoint.");
        byte[] bytes = Convert.FromBase64String(snapshot.PackedValues);
        if (bytes.Length != snapshot.ParameterCount * sizeof(float) || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), snapshot.Checksum, StringComparison.Ordinal))
            throw new InvalidDataException("Checkpoint integrity validation failed.");
        float[] values = new float[snapshot.ParameterCount]; Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        gate.EnterWriteLock();
        try { int offset = 0; Array.Copy(values, offset, inputWeights, 0, inputWeights.Length); offset += inputWeights.Length; Array.Copy(values, offset, hiddenBias, 0, hiddenBias.Length); offset += hiddenBias.Length; Array.Copy(values, offset, outputWeights, 0, outputWeights.Length); offset += outputWeights.Length; Array.Copy(values, offset, outputBias, 0, outputBias.Length); }
        finally { gate.ExitWriteLock(); }
    }

    private TicketStudentPrediction BuildPrediction(string requirement, Dictionary<int, float> features, float evidence, float relevance)
    {
        double similarity = support.Count == 0 ? 0 : support.Max(item => Cosine(features, item.Features)); double separation = Math.Abs(evidence - .5) * 2; double confidence = Math.Clamp(separation * (.35 + .65 * similarity), .05, .99); string category = DetectCategory(requirement); string reasoning = similarity >= .55 ? $"Student pattern match for {category}; reviewer recommended when confidence is below threshold." : $"Limited training support for {category}; reviewer recommended.";
        return new TicketStudentPrediction(evidence, relevance, confidence, category, reasoning, "hc-student-mlp-v2");
    }

    private ForwardPropagationTrace CaptureForward(Dictionary<int, float> features)
    {
        float[] pre = new float[HiddenSize]; float[] hidden = new float[HiddenSize]; List<SparseValue> nodePre = []; List<SparseValue> nodeAct = []; List<SparseValue> contributions = []; List<SparseValue> biases = [];
        foreach ((int index, float value) in features) contributions.Add(new SparseValue(index, value));
        for (int h = 0; h < HiddenSize; h++) { float sum = hiddenBias[h]; biases.Add(new SparseValue(InputSize * HiddenSize + h, hiddenBias[h])); foreach ((int index, float value) in features) { float c = inputWeights[h * InputSize + index] * value; if (c != 0) contributions.Add(new SparseValue(h * InputSize + index, c)); sum += c; } pre[h] = sum; hidden[h] = MathF.Tanh(sum); nodePre.Add(new SparseValue(InputSize + h, sum)); nodeAct.Add(new SparseValue(InputSize + h, hidden[h])); }
        float[] logits = new float[2]; float[] probabilities = new float[2];
        for (int output = 0; output < 2; output++) { float sum = outputBias[output]; biases.Add(new SparseValue(InputSize * HiddenSize + HiddenSize + HiddenSize * 2 + output, outputBias[output])); for (int h = 0; h < HiddenSize; h++) { float c = outputWeights[output * HiddenSize + h] * hidden[h]; if (c != 0) contributions.Add(new SparseValue(InputSize * HiddenSize + HiddenSize + output * HiddenSize + h, c)); sum += c; } logits[output] = sum; probabilities[output] = 1f / (1f + MathF.Exp(-Math.Clamp(sum, -20, 20))); nodePre.Add(new SparseValue(InputSize + HiddenSize + output, sum)); nodeAct.Add(new SparseValue(InputSize + HiddenSize + output, probabilities[output])); }
        List<FeatureActivation> featureActivations = features.OrderBy(x => x.Key).Select(x => new FeatureActivation(x.Key, x.Value, [])).ToList(); float confidence = Math.Clamp(Math.Abs(probabilities[0] - .5f) * 2f, .05f, .99f);
        return new ForwardPropagationTrace(featureActivations, nodePre, nodeAct, contributions, biases, logits[0], logits[1], probabilities[0], probabilities[1], confidence);
    }

    private static LossTrace Loss(ForwardPropagationTrace forward, StudentTrainingExample example)
    {
        static float Bce(float target, float prediction) { prediction = Math.Clamp(prediction, 1e-6f, 1 - 1e-6f); return -(target * MathF.Log(prediction) + (1 - target) * MathF.Log(1 - prediction)); }
        float evidence = Bce((float)example.EvidenceScore, forward.EvidenceProbability); float relevance = Bce((float)example.Relevance, forward.RelevanceProbability); return new LossTrace("binary-cross-entropy-v1", evidence, relevance, 0, evidence + relevance);
    }
    private void TrainCore(StudentTrainingExample example, int epochs, bool remember)
    {
        Dictionary<int, float> features = Featurize(example.Requirement, example.Message, InputSize);
        double evidenceTarget = Math.Clamp(example.EvidenceScore, 0, 1);
        double relevanceTarget = Math.Clamp(example.Relevance, 0, 1);

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            float[] hidden = Hidden(features);
            double evidence = Output(hidden, 0);
            double relevance = Output(hidden, 1);
            double[] outputGradient = [evidence - evidenceTarget, relevance - relevanceTarget];
            float[] hiddenGradient = new float[HiddenSize];

            for (int h = 0; h < HiddenSize; h++)
            {
                double downstream = outputGradient[0] * outputWeights[h]
                                    + outputGradient[1] * outputWeights[HiddenSize + h];
                hiddenGradient[h] = (float)(downstream * (1 - hidden[h] * hidden[h]));
            }

            for (int output = 0; output < 2; output++)
            {
                for (int h = 0; h < HiddenSize; h++)
                    outputWeights[output * HiddenSize + h] -= (float)(LearningRate * outputGradient[output] * hidden[h]);
                outputBias[output] -= (float)(LearningRate * outputGradient[output]);
            }

            for (int h = 0; h < HiddenSize; h++)
            {
                foreach ((int index, float value) in features)
                    inputWeights[h * InputSize + index] -= (float)(LearningRate * hiddenGradient[h] * value);
                hiddenBias[h] -= (float)(LearningRate * hiddenGradient[h]);
            }
        }

        if (remember)
        {
            support.Add(new SupportExample(features, example.Category));
            if (support.Count > 512)
                support.RemoveAt(0);
        }
    }

    private (double Evidence, double Relevance) Forward(Dictionary<int, float> features)
    {
        float[] hidden = Hidden(features);
        return (Output(hidden, 0), Output(hidden, 1));
    }

    private float[] Hidden(Dictionary<int, float> features)
    {
        float[] hidden = new float[HiddenSize];
        for (int h = 0; h < HiddenSize; h++)
        {
            double sum = hiddenBias[h];
            foreach ((int index, float value) in features)
                sum += inputWeights[h * InputSize + index] * value;
            hidden[h] = (float)Math.Tanh(sum);
        }
        return hidden;
    }

    private double Output(float[] hidden, int output)
    {
        double sum = outputBias[output];
        for (int h = 0; h < HiddenSize; h++)
            sum += outputWeights[output * HiddenSize + h] * hidden[h];
        return 1 / (1 + Math.Exp(-Math.Clamp(sum, -20, 20)));
    }

    private static Dictionary<int, float> Featurize(string requirement, string message, int size)
    {
        Dictionary<int, float> result = [];
        int textSize = size == InputSize ? 252 : size;
        AddTokens(result, "requirement " + requirement, textSize, 0.65f);
        AddTokens(result, "message " + message, textSize, 1f);
        if (size == InputSize)
        {
            result[252] = MetadataValue(message, "community_vote");
            result[253] = MetadataValue(message, "channel_relevance");
            result[254] = MetadataValue(message, "thread_position");
            result[255] = MetadataValue(message, "prior_score");
        }
        return result;
    }

    private static float MetadataValue(string text, string name)
    {
        string marker = name + "=";
        int start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return 0;
        start += marker.Length;
        int end = text.IndexOfAny([' ', '\r', '\n', '>'], start);
        string raw = end < 0 ? text[start..] : text[start..end];
        return float.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out float value) ? Math.Clamp(value, -1, 1) : 0;
    }

    private static void AddTokens(Dictionary<int, float> target, string text, int size, float weight)
    {
        string[] tokens = text.ToLowerInvariant().Split(
            [' ', '\r', '\n', '\t', '.', ',', '!', '?', ':', ';', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string previous = string.Empty;
        foreach (string token in tokens.Take(400))
        {
            AddFeature(target, token, size, weight);
            if (previous.Length > 0)
                AddFeature(target, previous + "_" + token, size, weight * 0.7f);
            previous = token;
        }
    }

    private static void AddFeature(Dictionary<int, float> target, string value, int size, float weight)
    {
        uint hash = 2166136261;
        foreach (char character in value)
            hash = (hash ^ character) * 16777619;
        int index = (int)(hash % (uint)size);
        target[index] = Math.Clamp(target.GetValueOrDefault(index) + weight, -4, 4);
    }

    private static double Cosine(Dictionary<int, float> left, Dictionary<int, float> right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        foreach ((int index, float value) in left)
        {
            leftNorm += value * value;
            if (right.TryGetValue(index, out float other))
                dot += value * other;
        }
        foreach (float value in right.Values)
            rightNorm += value * value;
        return leftNorm <= 0 || rightNorm <= 0 ? 0 : Math.Clamp(dot / Math.Sqrt(leftNorm * rightNorm), 0, 1);
    }

    private static double L2(IEnumerable<float> values) => Math.Sqrt(values.Sum(value => value * value));

    private static string DetectCategory(string requirement)
    {
        string value = requirement.ToLowerInvariant();
        if (value.Contains("spam") || value.Contains("flood")) return "spam";
        if (value.Contains("profan") || value.Contains("cuss")) return "profanity";
        if (value.Contains("threat") || value.Contains("harm")) return "threat";
        if (value.Contains("harass") || value.Contains("insult") || value.Contains("abuse")) return "harassment";
        if (value.Contains("evad") || value.Contains("filter")) return "evasion";
        return "general";
    }

    private sealed record SupportExample(Dictionary<int, float> Features, string Category);
}

public static class TicketStudentContext
{
    public static string BuildRequirement(TicketUserWatch watch, int maxCharacters)
    {
        string value = $"Filter: {watch.Ticket.FilterName}. Watch context: {watch.ContextLabel}. "
                       + $"Instructions: {watch.Ticket.Portal.TrackingInstructions ?? "none"}. "
                       + $"Frozen template: {watch.Ticket.TrackingTemplateJson ?? "none"}.";
        return value.Length <= maxCharacters ? value : value[..maxCharacters];
    }
}
