using System.Security.Cryptography;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// CPU hashed MLP for chat monitoring, shaped for an eventual LLM-free stack:
/// <list type="bullet">
/// <item>Hidden layers: leaky ReLU + He init</item>
/// <item>Evidence/relevance: sigmoid + BCE (independent probabilities)</item>
/// <item>Category: softmax + categorical CE (3Blue1Brown multi-class)</item>
/// <item>Learning: mini-batch average cost C=(1/n)ΣC_x with momentum SGD on −∇C</item>
/// </list>
/// Softmax is not applied to evidence/relevance — those are not mutually exclusive classes.
/// </summary>
public abstract class ChatMonitoringNeuralModelHashedMlp : IChatMonitoringNeuralModelTelemetry
{
    public const string RuntimeKind = "HashedMlpV3";
    private const float LearningRate = .035f;
    private const float MomentumCoefficient = .9f;
    private const float LeakyReluSlope = .01f;
    private readonly int[] layerWidths;
    private readonly string[] layerLabels;
    private readonly string[] categoryLabels;
    private readonly float[][] weights;
    private readonly float[][] biases;
    private readonly float[][] weightVelocity;
    private readonly float[][] biasVelocity;
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
        IReadOnlyList<string> categoryLabels,
        int seed)
    {
        Kind = kind;
        ModelVersion = modelVersion;
        this.layerLabels = layerLabels.ToArray();
        this.categoryLabels = categoryLabels.ToArray();
        int outputCount = 2 + this.categoryLabels.Length;
        layerWidths = [ChatMonitoringFeatureEncoder.FeatureCount, firstHidden, secondHidden, thirdHidden, fourthHidden, outputCount];
        weights = new float[layerWidths.Length - 1][];
        biases = new float[layerWidths.Length - 1][];
        weightVelocity = new float[layerWidths.Length - 1][];
        biasVelocity = new float[layerWidths.Length - 1][];
        Random random = new(seed);
        for (int layer = 0; layer < layerWidths.Length - 1; layer++)
        {
            int sources = layerWidths[layer];
            int targets = layerWidths[layer + 1];
            weights[layer] = new float[targets * sources];
            biases[layer] = new float[targets];
            weightVelocity[layer] = new float[targets * sources];
            biasVelocity[layer] = new float[targets];
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
    public IReadOnlyList<string> CategoryLabels => categoryLabels;

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
        float lossStopThreshold = 0f) =>
        TrainMiniBatchWithTrace([example], epochs, detail, evidenceTolerance, relevanceTolerance, lossStopThreshold);

    public TrainingPassTrace TrainMiniBatchWithTrace(
        IReadOnlyList<ChatMonitoringNeuralModelTrainingExample> examples,
        int epochs = 12,
        NeuralTrainingTraceDetail detail = NeuralTrainingTraceDetail.Full,
        float evidenceTolerance = 0f,
        float relevanceTolerance = 0f,
        float lossStopThreshold = 0f)
    {
        if (examples is null || examples.Count == 0)
            throw new ArgumentException("Mini-batch training requires at least one example.", nameof(examples));

        lock (gate)
        {
            int n = examples.Count;
            float[][] encoded = examples.Select(example => ChatMonitoringFeatureEncoder.Encode(example.Input)).ToArray();
            int[] categoryIndices = examples.Select(ResolveCategoryIndex).ToArray();
            float[][] weightGrads = weights.Select(layer => new float[layer.Length]).ToArray();
            float[][] biasGrads = biases.Select(layer => new float[layer.Length]).ToArray();
            List<TrainingIterationReplay> iterations = [];
            int boundedEpochs = Math.Clamp(epochs, 1, 100);
            bool earlyStopEnabled = evidenceTolerance > 0f || relevanceTolerance > 0f || lossStopThreshold > 0f;
            bool captureFull = detail == NeuralTrainingTraceDetail.Full;
            string optimizer = n == 1 ? "momentum-SGD" : "momentum-mini-batch-SGD";

            for (int epoch = 0; epoch < boundedEpochs; epoch++)
            {
                ClearGradients(weightGrads, biasGrads);
                float evidenceLossSum = 0, relevanceLossSum = 0, categoryLossSum = 0;
                float evidenceProbSum = 0, relevanceProbSum = 0, evidenceLogitSum = 0, relevanceLogitSum = 0;
                float maxAbsGradient = 0;
                float minNonZeroAbsGradient = float.MaxValue;
                double gradSqSum = 0;

                for (int i = 0; i < n; i++)
                {
                    ForwardCache cache = Forward(encoded[i], captureTrace: false);
                    float evidence = cache.Activations[^1][0];
                    float relevance = cache.Activations[^1][1];
                    evidenceProbSum += evidence;
                    relevanceProbSum += relevance;
                    evidenceLogitSum += cache.PreActivations[^1][0];
                    relevanceLogitSum += cache.PreActivations[^1][1];
                    evidenceLossSum += BinaryCrossEntropy(evidence, examples[i].Targets.Evidence);
                    relevanceLossSum += BinaryCrossEntropy(relevance, examples[i].Targets.Relevance);
                    categoryLossSum += CategoricalCrossEntropy(cache.Activations[^1], categoryIndices[i]);
                    ChatMonitoringNeuralModelTargets targets = examples[i].Targets with { CategoryIndex = categoryIndices[i] };
                    AccumulateGradientsUnlocked(cache, targets, weightGrads, biasGrads,
                        ref maxAbsGradient, ref minNonZeroAbsGradient, ref gradSqSum);
                }

                LossTrace lossBefore = new(
                    "bce+softmax-ce-avg-v1",
                    evidenceLossSum / n,
                    relevanceLossSum / n,
                    0,
                    (evidenceLossSum + relevanceLossSum + categoryLossSum) / n,
                    n,
                    categoryLossSum / n);
                ForwardPropagationTrace beforeForward = AverageOutputForward(
                    evidenceLogitSum / n, relevanceLogitSum / n, evidenceProbSum / n, relevanceProbSum / n);

                float[]? parameterBefore = captureFull ? ReadParameters() : null;
                ApplyMomentumUpdateUnlocked(weightGrads, biasGrads, n);
                float[]? parameterAfter = captureFull ? ReadParameters() : null;
                float avgGradScale = 1f / n;
                float gradientL2 = MathF.Sqrt((float)(gradSqSum * avgGradScale * avgGradScale));
                if (minNonZeroAbsGradient == float.MaxValue) minNonZeroAbsGradient = 0;
                maxAbsGradient *= avgGradScale;
                minNonZeroAbsGradient *= avgGradScale;

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

                float afterEvidenceLoss = 0, afterRelevanceLoss = 0, afterCategoryLoss = 0;
                float afterEvidenceProb = 0, afterRelevanceProb = 0, afterEvidenceLogit = 0, afterRelevanceLogit = 0;
                float meanAbsEvidenceError = 0, meanAbsRelevanceError = 0;
                for (int i = 0; i < n; i++)
                {
                    ForwardCache afterCache = Forward(encoded[i], captureTrace: false);
                    float evidence = afterCache.Activations[^1][0];
                    float relevance = afterCache.Activations[^1][1];
                    afterEvidenceProb += evidence;
                    afterRelevanceProb += relevance;
                    afterEvidenceLogit += afterCache.PreActivations[^1][0];
                    afterRelevanceLogit += afterCache.PreActivations[^1][1];
                    afterEvidenceLoss += BinaryCrossEntropy(evidence, examples[i].Targets.Evidence);
                    afterRelevanceLoss += BinaryCrossEntropy(relevance, examples[i].Targets.Relevance);
                    afterCategoryLoss += CategoricalCrossEntropy(afterCache.Activations[^1], categoryIndices[i]);
                    meanAbsEvidenceError += MathF.Abs(evidence - examples[i].Targets.Evidence);
                    meanAbsRelevanceError += MathF.Abs(relevance - examples[i].Targets.Relevance);
                }

                LossTrace lossAfter = new(
                    "bce+softmax-ce-avg-v1",
                    afterEvidenceLoss / n,
                    afterRelevanceLoss / n,
                    0,
                    (afterEvidenceLoss + afterRelevanceLoss + afterCategoryLoss) / n,
                    n,
                    afterCategoryLoss / n);
                ForwardPropagationTrace afterForward = AverageOutputForward(
                    afterEvidenceLogit / n, afterRelevanceLogit / n, afterEvidenceProb / n, afterRelevanceProb / n);

                iterations.Add(new TrainingIterationReplay(epoch, beforeForward, lossBefore, backward,
                    new ParameterUpdateTrace(LearningRate, optimizer, deltas), afterForward, lossAfter));

                if (earlyStopEnabled
                    && meanAbsEvidenceError / n <= evidenceTolerance
                    && meanAbsRelevanceError / n <= relevanceTolerance
                    && (lossStopThreshold <= 0f || lossAfter.TotalLoss <= lossStopThreshold))
                {
                    break;
                }
            }

            for (int i = 0; i < n; i++)
            {
                string category = categoryLabels[categoryIndices[i]];
                support.Add(new SupportExample(encoded[i], category));
                if (support.Count > 512)
                    support.RemoveAt(0);
            }

            float finalAverageCost = iterations.Count == 0 ? 0f : iterations[^1].LossAfterUpdate.TotalLoss;
            return new TrainingPassTrace(iterations, n, finalAverageCost);
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
            ClearGradients(weightVelocity, biasVelocity);
        }
    }

    public void Dispose() { }

    private int ResolveCategoryIndex(ChatMonitoringNeuralModelTrainingExample example)
    {
        if (example.Targets.CategoryIndex >= 0 && example.Targets.CategoryIndex < categoryLabels.Length)
            return example.Targets.CategoryIndex;
        string category = string.IsNullOrWhiteSpace(example.Category) || example.Category == "general"
            ? ChatMonitoringTicketContext.DetectCategory(example.Input.Requirement, Kind)
            : example.Category;
        return ChatMonitoringCategoryTaxonomy.IndexOf(Kind, category);
    }

    private ChatMonitoringNeuralModelInferenceTrace PredictUnlocked(ChatMonitoringNeuralModelInput input, float[]? encoded = null)
    {
        float[] features = encoded ?? ChatMonitoringFeatureEncoder.Encode(input);
        ForwardCache cache = Forward(features, captureTrace: true);
        return BuildInference(input, features, cache);
    }

    private ChatMonitoringNeuralModelInferenceTrace BuildInference(ChatMonitoringNeuralModelInput input, float[] features, ForwardCache cache)
    {
        float evidence = cache.Activations[^1][0];
        float relevance = cache.Activations[^1][1];
        int categoryIndex = ArgMaxCategory(cache.Activations[^1]);
        float categoryConfidence = cache.Activations[^1][2 + categoryIndex];
        string category = categoryLabels[categoryIndex];
        double supportSimilarity = support.Count == 0 ? 0 : support.Max(item => Cosine(features, item.Features));
        double separation = Math.Abs(evidence - .5f) * 2;
        // NN-first confidence: separation × support × softmax peak — no LLM required.
        float confidence = (float)Math.Clamp(
            separation * (.25 + .45 * supportSimilarity + .30 * categoryConfidence),
            .05, .99);
        string reasoning = supportSimilarity >= .55
            ? $"Chat-monitor pattern match for {category} (softmax {categoryConfidence:F2}); reviewer optional when confidence is high."
            : $"Limited training support for {category}; neural score stands alone when LLM review is disabled.";
        return new ChatMonitoringNeuralModelInferenceTrace(
            new ChatMonitoringNeuralModelPrediction(evidence, relevance, confidence, Kind, ModelVersion, category, reasoning, categoryConfidence),
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

    private static void ClearGradients(float[][] weightGrads, float[][] biasGrads)
    {
        for (int layer = 0; layer < weightGrads.Length; layer++)
        {
            Array.Clear(weightGrads[layer]);
            Array.Clear(biasGrads[layer]);
        }
    }

    private void AccumulateGradientsUnlocked(
        ForwardCache cache,
        ChatMonitoringNeuralModelTargets targets,
        float[][] weightGrads,
        float[][] biasGrads,
        ref float maxAbsGradient,
        ref float minNonZeroAbsGradient,
        ref double gradSqSum)
    {
        float[] output = cache.Activations[^1];
        float[][] activationGradients = new float[cache.Activations.Length][];
        float[] outputGrad = new float[output.Length];
        // Sigmoid + BCE: ∂C/∂z = σ(z) − y
        outputGrad[0] = output[0] - Math.Clamp(targets.Evidence, 0, 1);
        outputGrad[1] = output[1] - Math.Clamp(targets.Relevance, 0, 1);
        // Softmax + CE: ∂C/∂z_i = p_i − y_i (3Blue1Brown)
        int categoryIndex = Math.Clamp(targets.CategoryIndex, 0, categoryLabels.Length - 1);
        for (int c = 0; c < categoryLabels.Length; c++)
            outputGrad[2 + c] = output[2 + c] - (c == categoryIndex ? 1f : 0f);
        activationGradients[^1] = outputGrad;

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

                TrackGradientMagnitude(gradient, ref maxAbsGradient, ref minNonZeroAbsGradient, ref gradSqSum);
                biasGrads[layer][target] += gradient;
                for (int sourceIndex = 0; sourceIndex < sources; sourceIndex++)
                {
                    int weightIndex = target * sources + sourceIndex;
                    float weightGradient = gradient * source[sourceIndex];
                    TrackGradientMagnitude(weightGradient, ref maxAbsGradient, ref minNonZeroAbsGradient, ref gradSqSum);
                    weightGrads[layer][weightIndex] += weightGradient;
                    nextGradient[sourceIndex] += gradient * weights[layer][weightIndex];
                }
            }

            activationGradients[layer] = nextGradient;
        }
    }

    /// <summary>Heavy-ball momentum on averaged mini-batch gradients: v ← μv + ∇C, θ ← θ − ηv.</summary>
    private void ApplyMomentumUpdateUnlocked(float[][] weightGrads, float[][] biasGrads, int batchSize)
    {
        float invN = 1f / Math.Max(1, batchSize);
        for (int layer = 0; layer < weights.Length; layer++)
        {
            for (int i = 0; i < weights[layer].Length; i++)
            {
                float avgGrad = weightGrads[layer][i] * invN;
                weightVelocity[layer][i] = MomentumCoefficient * weightVelocity[layer][i] + avgGrad;
                weights[layer][i] -= LearningRate * weightVelocity[layer][i];
            }

            for (int i = 0; i < biases[layer].Length; i++)
            {
                float avgGrad = biasGrads[layer][i] * invN;
                biasVelocity[layer][i] = MomentumCoefficient * biasVelocity[layer][i] + avgGrad;
                biases[layer][i] -= LearningRate * biasVelocity[layer][i];
            }
        }
    }

    private static void TrackGradientMagnitude(float gradient, ref float maxAbs, ref float minNonZero, ref double gradSqSum)
    {
        float abs = MathF.Abs(gradient);
        if (abs > maxAbs) maxAbs = abs;
        if (abs > 0f && abs < minNonZero) minNonZero = abs;
        gradSqSum += gradient * gradient;
    }

    private static ForwardPropagationTrace AverageOutputForward(float evidenceLogit, float relevanceLogit, float evidenceProbability, float relevanceProbability)
    {
        float confidence = Math.Clamp(MathF.Abs(evidenceProbability - .5f) * 2f, .05f, .99f);
        return new([], [], [], [], [], evidenceLogit, relevanceLogit, evidenceProbability, relevanceProbability, confidence);
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
                if (!outputLayer)
                    layerAct[target] = LeakyRelu(sum);
            }

            if (outputLayer)
            {
                layerAct[0] = Sigmoid(layerPre[0]);
                layerAct[1] = Sigmoid(layerPre[1]);
                Softmax(layerPre.AsSpan(2), layerAct.AsSpan(2));
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
    private static float Sigmoid(float sum) => 1f / (1f + MathF.Exp(-Math.Clamp(sum, -20f, 20f)));

    private static void Softmax(ReadOnlySpan<float> logits, Span<float> destination)
    {
        float max = float.NegativeInfinity;
        for (int i = 0; i < logits.Length; i++)
            if (logits[i] > max) max = logits[i];
        float sum = 0;
        for (int i = 0; i < logits.Length; i++)
        {
            destination[i] = MathF.Exp(Math.Clamp(logits[i] - max, -20f, 20f));
            sum += destination[i];
        }

        if (sum <= 0f) sum = 1f;
        for (int i = 0; i < destination.Length; i++)
            destination[i] /= sum;
    }

    private int ArgMaxCategory(float[] activations)
    {
        int best = 0;
        float bestValue = activations[2];
        for (int c = 1; c < categoryLabels.Length; c++)
        {
            if (activations[2 + c] > bestValue)
            {
                bestValue = activations[2 + c];
                best = c;
            }
        }

        return best;
    }

    private float CategoricalCrossEntropy(float[] activations, int categoryIndex)
    {
        int index = Math.Clamp(categoryIndex, 0, categoryLabels.Length - 1);
        float p = Math.Clamp(activations[2 + index], .000001f, .999999f);
        return -MathF.Log(p);
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
                string label;
                if (layer == layerWidths.Length - 1)
                {
                    label = node switch
                    {
                        0 => "Evidence",
                        1 => "Relevance",
                        _ => categoryLabels[node - 2],
                    };
                }
                else
                {
                    label = $"{layerLabels[layer]}-{node + 1}";
                }

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

public sealed class ModerationChatMonitorNeuralNet : ChatMonitoringNeuralModelHashedMlp
{
    public ModerationChatMonitorNeuralNet()
        : base(
            NeuralModelKindChatMonitoring.Moderation,
            "hc-chat-monitoring-moderation-v3",
            20, 30, 24, 18,
            ["input", "current-conduct", "behavior-history", "report-correlation", "moderation-decision", "output"],
            ChatMonitoringCategoryTaxonomy.Moderation,
            seed: 0x4D4F4433)
    {
    }
}

public sealed class TutoringChatMonitorNeuralNet : ChatMonitoringNeuralModelHashedMlp
{
    public TutoringChatMonitorNeuralNet()
        : base(
            NeuralModelKindChatMonitoring.Tutoring,
            "hc-chat-monitoring-tutoring-v3",
            20, 32, 28, 20,
            ["input", "current-subject-response", "learning-thread-history", "application-correlation", "tutoring-decision", "output"],
            ChatMonitoringCategoryTaxonomy.Tutoring,
            seed: 0x54555433)
    {
    }
}
