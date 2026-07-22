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
    public const string RuntimeKind = "HashedMlpV8";
    private const float LearningRate = .035f;
    private const float MomentumCoefficient = .9f;
    /// <summary>Per-parameter gradient clip before momentum update (prevents ±Infinity weights).</summary>
    private const float MaxAbsGradient = 5f;
    /// <summary>Hard bound on weights/biases after each update.</summary>
    private const float MaxAbsWeight = 25f;
    /// <summary>Same bound used by <see cref="Sigmoid"/> so compact traces stay JSON-safe.</summary>
    private const float MaxAbsLogit = 20f;
    private const float LeakyReluSlope = .01f;
    private readonly int[] layerWidths;
    private readonly string[] layerLabels;
    private readonly string[] categoryLabels;
    private readonly float[][] weights;
    private readonly float[][] biases;
    private readonly float[][] weightVelocity;
    private readonly float[][] biasVelocity;
    private readonly NeuralNetTopologySnapshot topology;
    // Bounded FIFO of recent supervised examples (cap 512) for support-set training.
    private readonly Queue<SupportExample> support = new();
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
        float lossStopThreshold = 0f) =>
        TrainMiniBatchWithTrace(
            examples,
            epochs,
            detail,
            evidenceTolerance,
            relevanceTolerance,
            lossStopThreshold,
            resolveBatch: null,
            onSampleInputGradients: null);

    /// <summary>
    /// Mini-batch SGD with optional cascade hooks. When <paramref name="resolveBatch"/> is set,
    /// it is invoked at the start of each epoch (so f(x) can be refreshed). When
    /// <paramref name="onSampleInputGradients"/> is set, it receives ∂C_x/∂x per sample after
    /// backprop and before the stage-2 weight update — used to chain-rule into f.
    /// Readability exception: physical length and nesting remain above normal
    /// limits because separating the forward pass, ∂C/∂x cascade hook, and
    /// momentum update would obscure the chain-rule training path. Replay
    /// validation and hashed-MLP tests are the guardrails; see docs/tickets.md.
    /// </summary>
    internal TrainingPassTrace TrainMiniBatchWithTrace(
        IReadOnlyList<ChatMonitoringNeuralModelTrainingExample> examples,
        int epochs,
        NeuralTrainingTraceDetail detail,
        float evidenceTolerance,
        float relevanceTolerance,
        float lossStopThreshold,
        Func<IReadOnlyList<ChatMonitoringNeuralModelTrainingExample>>? resolveBatch,
        Action<IReadOnlyList<float[]>>? onSampleInputGradients)
    {
        if (examples is null || examples.Count == 0)
            throw new ArgumentException("Mini-batch training requires at least one example.", nameof(examples));

        lock (gate)
        {
            int n = examples.Count;
            float[][] weightGrads = weights.Select(layer => new float[layer.Length]).ToArray();
            float[][] biasGrads = biases.Select(layer => new float[layer.Length]).ToArray();
            List<TrainingIterationReplay> iterations = [];
            int boundedEpochs = Math.Clamp(epochs, 1, 100);
            bool earlyStopEnabled = evidenceTolerance > 0f || relevanceTolerance > 0f || lossStopThreshold > 0f;
            bool captureFull = detail == NeuralTrainingTraceDetail.Full;
            string optimizer = onSampleInputGradients is null
                ? (n == 1 ? "momentum-SGD" : "momentum-mini-batch-SGD")
                : (n == 1 ? "momentum-cascade-chain-rule-SGD" : "momentum-cascade-chain-rule-mini-batch-SGD");

            float[][]? lastEncoded = null;
            int[]? lastCategoryIndices = null;

            for (int epoch = 0; epoch < boundedEpochs; epoch++)
            {
                IReadOnlyList<ChatMonitoringNeuralModelTrainingExample> batch = resolveBatch?.Invoke() ?? examples;
                if (batch.Count != n)
                    throw new InvalidOperationException("Cascade resolveBatch must preserve mini-batch size.");

                float[][] encoded = batch.Select(example => ChatMonitoringFeatureEncoder.Encode(example.Input)).ToArray();
                int[] categoryIndices = batch.Select(ResolveCategoryIndex).ToArray();
                lastEncoded = encoded;
                lastCategoryIndices = categoryIndices;

                ClearGradients(weightGrads, biasGrads);
                float evidenceLossSum = 0, relevanceLossSum = 0, categoryLossSum = 0;
                float evidenceProbSum = 0, relevanceProbSum = 0, evidenceLogitSum = 0, relevanceLogitSum = 0;
                float maxAbsGradient = 0;
                float minNonZeroAbsGradient = float.MaxValue;
                double gradSqSum = 0;
                float[][] inputGradients = new float[n][];

                for (int i = 0; i < n; i++)
                {
                    ForwardCache cache = Forward(encoded[i], captureTrace: false);
                    float evidence = cache.Activations[^1][0];
                    float relevance = cache.Activations[^1][1];
                    evidenceProbSum += evidence;
                    relevanceProbSum += relevance;
                    evidenceLogitSum += cache.PreActivations[^1][0];
                    relevanceLogitSum += cache.PreActivations[^1][1];
                    evidenceLossSum += BinaryCrossEntropy(evidence, batch[i].Targets.Evidence);
                    relevanceLossSum += BinaryCrossEntropy(relevance, batch[i].Targets.Relevance);
                    categoryLossSum += CategoricalCrossEntropy(cache.Activations[^1], categoryIndices[i]);
                    ChatMonitoringNeuralModelTargets targets = batch[i].Targets with { CategoryIndex = categoryIndices[i] };
                    inputGradients[i] = AccumulateGradientsUnlocked(cache, targets, weightGrads, biasGrads,
                        ref maxAbsGradient, ref minNonZeroAbsGradient, ref gradSqSum);
                }

                // Chain rule into stage-1 before stage-2 θ update (same forward activations).
                onSampleInputGradients?.Invoke(inputGradients);

                LossTrace lossBefore = new(
                    "bce+softmax-ce-avg-v1",
                    NeuralNetFinite.OrZero(evidenceLossSum / n),
                    NeuralNetFinite.OrZero(relevanceLossSum / n),
                    0,
                    NeuralNetFinite.OrZero((evidenceLossSum + relevanceLossSum + categoryLossSum) / n),
                    n,
                    NeuralNetFinite.OrZero(categoryLossSum / n));
                ForwardPropagationTrace beforeForward = AverageOutputForward(
                    evidenceLogitSum / n, relevanceLogitSum / n, evidenceProbSum / n, relevanceProbSum / n);

                float[]? parameterBefore = captureFull ? ReadParameters() : null;
                ApplyMomentumUpdateUnlocked(weightGrads, biasGrads, n);
                float[]? parameterAfter = captureFull ? ReadParameters() : null;
                float avgGradScale = 1f / n;
                float gradientL2 = NeuralNetFinite.OrZero(MathF.Sqrt((float)(gradSqSum * avgGradScale * avgGradScale)));
                // Sentinel was float.MaxValue when no non-zero gradient was tracked.
                if (!(minNonZeroAbsGradient < float.MaxValue))
                    minNonZeroAbsGradient = 0;
                maxAbsGradient = NeuralNetFinite.OrZero(maxAbsGradient * avgGradScale);
                minNonZeroAbsGradient = NeuralNetFinite.OrZero(minNonZeroAbsGradient * avgGradScale);

                IReadOnlyList<ParameterDelta> deltas = captureFull
                    ? BuildParameterDeltas(parameterBefore!, parameterAfter!)
                    : [];
                IReadOnlyList<SparseValue> gradients = captureFull
                    ? deltas.Select(delta => new SparseValue(delta.ParameterIndex, NeuralNetFinite.OrZero(delta.Gradient))).ToList()
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
                        ? gradients
                            .Where(gradient => MathF.Abs(gradient.Value) > 0f)
                            .Select(gradient => MathF.Abs(gradient.Value))
                            .DefaultIfEmpty(0)
                            .Min()
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
                    afterEvidenceLoss += BinaryCrossEntropy(evidence, batch[i].Targets.Evidence);
                    afterRelevanceLoss += BinaryCrossEntropy(relevance, batch[i].Targets.Relevance);
                    afterCategoryLoss += CategoricalCrossEntropy(afterCache.Activations[^1], categoryIndices[i]);
                    meanAbsEvidenceError += MathF.Abs(evidence - batch[i].Targets.Evidence);
                    meanAbsRelevanceError += MathF.Abs(relevance - batch[i].Targets.Relevance);
                }

                LossTrace lossAfter = new(
                    "bce+softmax-ce-avg-v1",
                    NeuralNetFinite.OrZero(afterEvidenceLoss / n),
                    NeuralNetFinite.OrZero(afterRelevanceLoss / n),
                    0,
                    NeuralNetFinite.OrZero((afterEvidenceLoss + afterRelevanceLoss + afterCategoryLoss) / n),
                    n,
                    NeuralNetFinite.OrZero(afterCategoryLoss / n));
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

            if (lastEncoded is not null && lastCategoryIndices is not null)
            {
                for (int i = 0; i < n; i++)
                {
                    string category = categoryLabels[lastCategoryIndices[i]];
                    // Cap at 512 by dequeuing the oldest example after each enqueue.
                    support.Enqueue(new SupportExample(lastEncoded[i], category));
                    if (support.Count > 512)
                        support.Dequeue();
                }
            }

            float finalAverageCost = iterations.Count == 0
                ? 0f
                : NeuralNetFinite.OrZero(iterations[^1].LossAfterUpdate.TotalLoss);
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

    /// <summary>Backprop through g; returns ∂C/∂x (input-feature gradients) for chain rule into f.</summary>
    private float[] AccumulateGradientsUnlocked(
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

        return activationGradients[0];
    }

    /// <summary>Heavy-ball momentum on averaged mini-batch gradients: v ← μv + ∇C, θ ← θ − ηv.</summary>
    private void ApplyMomentumUpdateUnlocked(float[][] weightGrads, float[][] biasGrads, int batchSize)
    {
        float invN = 1f / Math.Max(1, batchSize);
        for (int layer = 0; layer < weights.Length; layer++)
        {
            for (int i = 0; i < weights[layer].Length; i++)
            {
                float avgGrad = NeuralNetFinite.ClampFinite(weightGrads[layer][i] * invN, -MaxAbsGradient, MaxAbsGradient);
                weightVelocity[layer][i] = MomentumCoefficient * NeuralNetFinite.OrZero(weightVelocity[layer][i]) + avgGrad;
                weights[layer][i] = NeuralNetFinite.ClampFinite(
                    weights[layer][i] - LearningRate * weightVelocity[layer][i], -MaxAbsWeight, MaxAbsWeight);
            }

            for (int i = 0; i < biases[layer].Length; i++)
            {
                float avgGrad = NeuralNetFinite.ClampFinite(biasGrads[layer][i] * invN, -MaxAbsGradient, MaxAbsGradient);
                biasVelocity[layer][i] = MomentumCoefficient * NeuralNetFinite.OrZero(biasVelocity[layer][i]) + avgGrad;
                biases[layer][i] = NeuralNetFinite.ClampFinite(
                    biases[layer][i] - LearningRate * biasVelocity[layer][i], -MaxAbsWeight, MaxAbsWeight);
            }
        }
    }

    private static void TrackGradientMagnitude(float gradient, ref float maxAbs, ref float minNonZero, ref double gradSqSum)
    {
        if (!float.IsFinite(gradient))
            return;
        float abs = MathF.Abs(gradient);
        if (abs > maxAbs) maxAbs = abs;
        if (abs > 0f && abs < minNonZero) minNonZero = abs;
        gradSqSum += gradient * gradient;
    }

    private static ForwardPropagationTrace AverageOutputForward(float evidenceLogit, float relevanceLogit, float evidenceProbability, float relevanceProbability)
    {
        float boundedEvidenceLogit = NeuralNetFinite.ClampFinite(evidenceLogit, -MaxAbsLogit, MaxAbsLogit);
        float boundedRelevanceLogit = NeuralNetFinite.ClampFinite(relevanceLogit, -MaxAbsLogit, MaxAbsLogit);
        float evidence = NeuralNetFinite.ClampFinite(evidenceProbability, 0f, 1f);
        float relevance = NeuralNetFinite.ClampFinite(relevanceProbability, 0f, 1f);
        float confidence = Math.Clamp(MathF.Abs(evidence - .5f) * 2f, .05f, .99f);
        return new([], [], [], [], [], boundedEvidenceLogit, boundedRelevanceLogit, evidence, relevance, confidence);
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
                    nodePreActivations.Add(new SparseValue(nodeOffset + node, NeuralNetFinite.ClampFinite(preActivations[layer][node], -MaxAbsLogit * 4f, MaxAbsLogit * 4f)));
                    nodeActivations.Add(new SparseValue(nodeOffset + node, NeuralNetFinite.OrZero(activations[layer + 1][node])));
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
                            NeuralNetFinite.OrZero(source[sourceIndex] * weights[layer][target * sources + sourceIndex])));
                    biasContributions.Add(new SparseValue(biasOffset + target, NeuralNetFinite.OrZero(biases[layer][target])));
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
            deltas.Add(new ParameterDelta(
                index,
                NeuralNetFinite.OrZero(before[index]),
                NeuralNetFinite.OrZero(-delta / LearningRate),
                NeuralNetFinite.OrZero(delta),
                NeuralNetFinite.OrZero(after[index])));
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
            string label = input switch
            {
                < 44 => $"feature-{input}",
                44 => "community-vote",
                45 => "channel-relevance",
                46 => "thread-continuity",
                47 => "prior-score",
                48 => "applied-subject-count",
                49 => "exact-subject-match",
                50 => "related-subject-match",
                51 => "cross-subject-support",
                >= 52 and < 65 => $"applied-{ChatMonitoringSubjectSignals.GeneralSubjectsInOrder[input - 52]}",
                >= 65 and < 78 => $"channel-{ChatMonitoringSubjectSignals.GeneralSubjectsInOrder[input - 65]}",
                >= 78 and < 86 => $"cascade-context-{input - 78}",
                _ => $"feature-{input}",
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

/// <summary>Stage-2 moderation evidence scorer (text + cascade concept-context embedding).</summary>
public sealed class ModerationEvidenceScorerNeuralNet : ChatMonitoringNeuralModelHashedMlp
{
    public ModerationEvidenceScorerNeuralNet()
        : base(
            NeuralModelKindChatMonitoring.Moderation,
            "hc-chat-monitoring-moderation-evidence-v8",
            48, 72, 64, 56,
            ["input", "current-conduct", "behavior-history", "report-correlation", "moderation-decision", "output"],
            ChatMonitoringCategoryTaxonomy.Moderation,
            seed: 0x4D4F4438)
    {
    }
}

/// <summary>
/// Moderation cascade g(f(x)): stage-1 <see cref="ModerationConceptContextRouter"/> is f
/// (reported concept + family/relatedness); stage-2 <see cref="ModerationEvidenceScorerNeuralNet"/>
/// is g over 100 fine concepts + catch-all. Training uses the chain rule
/// ∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f) with ∂C/∂f from cascade slots 78–85.
/// </summary>
public sealed class ModerationChatMonitorNeuralNet : IChatMonitoringNeuralModelTelemetry
{
    private readonly ModerationConceptContextRouter router = new();
    private readonly ModerationEvidenceScorerNeuralNet scorer = new();
    private readonly object gate = new();

    public NeuralModelKindChatMonitoring Kind => NeuralModelKindChatMonitoring.Moderation;

    /// <summary>Stage-1 parameter L2 (diagnostics / cascade chain-rule tests).</summary>
    public float RouterParameterL2Norm => router.ParameterL2Norm();

    public ChatMonitoringNeuralModelPrediction Predict(ChatMonitoringNeuralModelInput input)
    {
        lock (gate) return scorer.Predict(Enrich(input));
    }

    public ChatMonitoringNeuralModelInferenceTrace PredictWithTrace(ChatMonitoringNeuralModelInput input)
    {
        lock (gate) return scorer.PredictWithTrace(Enrich(input));
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
            List<ModerationConceptSnapshot> snapshots = examples
                .Select(example => SnapshotFrom(example.Input, example.Category))
                .ToList();
            List<ModerationConceptContextRouter.ForwardCache>? forwardStates = null;
            float[][] routerWeightGrads = router.CreateWeightGradientBuffers();
            float[][] routerBiasGrads = router.CreateBiasGradientBuffers();

            return scorer.TrainMiniBatchWithTrace(
                examples,
                epochs,
                detail,
                evidenceTolerance,
                relevanceTolerance,
                lossStopThreshold,
                resolveBatch: () =>
                {
                    forwardStates = new List<ModerationConceptContextRouter.ForwardCache>(examples.Count);
                    List<ChatMonitoringNeuralModelTrainingExample> enriched = new(examples.Count);
                    for (int i = 0; i < examples.Count; i++)
                    {
                        ModerationConceptContextRouter.ForwardCache state = router.ForwardCacheFor(snapshots[i]);
                        forwardStates.Add(state);
                        enriched.Add(examples[i] with
                        {
                            Input = EnrichWithContext(examples[i].Input, state.Output),
                        });
                    }

                    return enriched;
                },
                onSampleInputGradients: inputGradients =>
                {
                    if (forwardStates is null || forwardStates.Count != inputGradients.Count)
                        throw new InvalidOperationException("Cascade forward states were not captured for this epoch.");

                    ModerationConceptContextRouter.ClearGradientBuffers(routerWeightGrads, routerBiasGrads);
                    for (int i = 0; i < inputGradients.Count; i++)
                    {
                        ReadOnlySpan<float> dCdF = inputGradients[i].AsSpan(
                            ModerationConceptContextRouter.CascadeFeatureStart,
                            ModerationConceptContextRouter.OutputSize);
                        router.AccumulateFromOutputGradient(
                            forwardStates[i], dCdF, routerWeightGrads, routerBiasGrads);
                    }

                    router.ApplyMomentumUpdate(routerWeightGrads, routerBiasGrads, examples.Count);
                });
        }
    }

    public NeuralNetTopologySnapshot GetTopologySnapshot() => scorer.GetTopologySnapshot();
    public NeuralNetParameterSnapshot GetParameterSnapshot(long? canonicalGeneration, int localRevision) =>
        scorer.GetParameterSnapshot(canonicalGeneration, localRevision);
    public ChatMonitoringNeuralModelStateSnapshot GetStateSnapshot() => scorer.GetStateSnapshot();
    public void LoadParameterSnapshot(NeuralNetParameterSnapshot snapshot) => scorer.LoadParameterSnapshot(snapshot);
    public void Dispose()
    {
        router.Dispose();
        scorer.Dispose();
    }

    private ChatMonitoringNeuralModelInput Enrich(ChatMonitoringNeuralModelInput input, string? categoryHint = null)
    {
        ModerationConceptSnapshot snap = SnapshotFrom(input, categoryHint);
        float[] context = router.Forward(snap);
        return EnrichWithContext(input, context);
    }

    private static ChatMonitoringNeuralModelInput EnrichWithContext(
        ChatMonitoringNeuralModelInput input,
        ReadOnlySpan<float> cascadeContext) =>
        input with { CascadeContext = cascadeContext.ToArray() };

    private static ModerationConceptSnapshot SnapshotFrom(ChatMonitoringNeuralModelInput input, string? categoryHint = null) =>
        ChatMonitoringModerationConceptSignals.Resolve(
            categoryHint,
            input.Requirement,
            input.ThreadContext,
            input.Message);
}

/// <summary>Stage-2 tutoring evidence scorer (text + subject features + cascade context).</summary>
public sealed class TutoringEvidenceScorerNeuralNet : ChatMonitoringNeuralModelHashedMlp
{
    public TutoringEvidenceScorerNeuralNet()
        : base(
            NeuralModelKindChatMonitoring.Tutoring,
            "hc-chat-monitoring-tutoring-evidence-v8",
            40, 56, 48, 40,
            ["input", "current-subject-response", "learning-thread-history", "application-correlation", "tutoring-decision", "output"],
            ChatMonitoringCategoryTaxonomy.Tutoring,
            seed: 0x54555438)
    {
    }
}

/// <summary>
/// Tutoring cascade g(f(x)): stage-1 <see cref="TutoringSubjectContextRouter"/> is f;
/// stage-2 <see cref="TutoringEvidenceScorerNeuralNet"/> is g. Training uses the chain rule
/// ∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f) where ∂C/∂f is backprop through g's cascade input slots (78–85).
/// Example: applied Math+Science, Physics channel → f raises cross-subject context; g can
/// reward a strong physics answer more.
/// </summary>
public sealed class TutoringChatMonitorNeuralNet : IChatMonitoringNeuralModelTelemetry
{
    private readonly TutoringSubjectContextRouter router = new();
    private readonly TutoringEvidenceScorerNeuralNet scorer = new();
    private readonly object gate = new();

    public NeuralModelKindChatMonitoring Kind => NeuralModelKindChatMonitoring.Tutoring;

    /// <summary>Stage-1 parameter L2 (diagnostics / cascade chain-rule tests).</summary>
    public float RouterParameterL2Norm => router.ParameterL2Norm();

    public ChatMonitoringNeuralModelPrediction Predict(ChatMonitoringNeuralModelInput input)
    {
        lock (gate) return scorer.Predict(Enrich(input));
    }

    public ChatMonitoringNeuralModelInferenceTrace PredictWithTrace(ChatMonitoringNeuralModelInput input)
    {
        lock (gate) return scorer.PredictWithTrace(Enrich(input));
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
            List<SubjectSignalSnapshot> snapshots = examples.Select(example => SnapshotFrom(example.Input)).ToList();
            List<TutoringSubjectContextRouter.ForwardCache>? forwardStates = null;
            float[][] routerWeightGrads = router.CreateWeightGradientBuffers();
            float[][] routerBiasGrads = router.CreateBiasGradientBuffers();

            return scorer.TrainMiniBatchWithTrace(
                examples,
                epochs,
                detail,
                evidenceTolerance,
                relevanceTolerance,
                lossStopThreshold,
                resolveBatch: () =>
                {
                    forwardStates = new List<TutoringSubjectContextRouter.ForwardCache>(examples.Count);
                    List<ChatMonitoringNeuralModelTrainingExample> enriched = new(examples.Count);
                    for (int i = 0; i < examples.Count; i++)
                    {
                        TutoringSubjectContextRouter.ForwardCache state = router.ForwardCacheFor(snapshots[i]);
                        forwardStates.Add(state);
                        enriched.Add(examples[i] with
                        {
                            Input = EnrichWithContext(examples[i].Input, snapshots[i], state.Output),
                        });
                    }

                    return enriched;
                },
                onSampleInputGradients: inputGradients =>
                {
                    if (forwardStates is null || forwardStates.Count != inputGradients.Count)
                        throw new InvalidOperationException("Cascade forward states were not captured for this epoch.");

                    TutoringSubjectContextRouter.ClearGradientBuffers(routerWeightGrads, routerBiasGrads);
                    for (int i = 0; i < inputGradients.Count; i++)
                    {
                        // ∂C/∂f = ∂C/∂x[78:86] — chain rule through g(f(x)).
                        ReadOnlySpan<float> dCdF = inputGradients[i].AsSpan(
                            TutoringSubjectContextRouter.CascadeFeatureStart,
                            TutoringSubjectContextRouter.OutputSize);
                        router.AccumulateFromOutputGradient(
                            forwardStates[i], dCdF, routerWeightGrads, routerBiasGrads);
                    }

                    router.ApplyMomentumUpdate(routerWeightGrads, routerBiasGrads, examples.Count);
                });
        }
    }

    public NeuralNetTopologySnapshot GetTopologySnapshot() => scorer.GetTopologySnapshot();
    public NeuralNetParameterSnapshot GetParameterSnapshot(long? canonicalGeneration, int localRevision) =>
        scorer.GetParameterSnapshot(canonicalGeneration, localRevision);
    public ChatMonitoringNeuralModelStateSnapshot GetStateSnapshot() => scorer.GetStateSnapshot();
    public void LoadParameterSnapshot(NeuralNetParameterSnapshot snapshot) => scorer.LoadParameterSnapshot(snapshot);
    public void Dispose()
    {
        router.Dispose();
        scorer.Dispose();
    }

    private ChatMonitoringNeuralModelInput Enrich(ChatMonitoringNeuralModelInput input, SubjectSignalSnapshot? snapshot = null)
    {
        SubjectSignalSnapshot snap = snapshot ?? SnapshotFrom(input);
        float[] context = router.Forward(snap);
        return EnrichWithContext(input, snap, context);
    }

    private static ChatMonitoringNeuralModelInput EnrichWithContext(
        ChatMonitoringNeuralModelInput input,
        SubjectSignalSnapshot snap,
        ReadOnlySpan<float> cascadeContext)
    {
        float[] context = cascadeContext.ToArray();
        ChatMonitoringNeuralModelInput baseInput = ChatMonitoringNeuralModelInput.Create(
            input.Requirement,
            input.ThreadContext,
            input.Message,
            input.CommunityVote,
            input.ThreadContinuity,
            input.PriorScore,
            snap,
            context);
        return baseInput with
        {
            CommunityVote = input.CommunityVote,
            PriorScore = input.PriorScore,
        };
    }

    private static SubjectSignalSnapshot SnapshotFrom(ChatMonitoringNeuralModelInput input)
    {
        TutorSubjectTextProcessor.SubjectExtraction extraction =
            ChatMonitoringSubjectSignals.ParseAppliedExtraction(input.Requirement, input.ThreadContext);
        List<string> applied = [];
        if (input.AppliedSubjectMultiHot is not null)
        {
            for (int i = 0; i < Math.Min(input.AppliedSubjectMultiHot.Count, ChatMonitoringSubjectSignals.GeneralSubjectCount); i++)
            {
                if (input.AppliedSubjectMultiHot[i] >= .5f)
                    applied.Add(ChatMonitoringSubjectSignals.GeneralSubjectsInOrder[i]);
            }
        }

        if (applied.Count == 0)
            applied.AddRange(extraction.GeneralMasks);

        string? channel = null;
        if (input.ChannelSubjectMultiHot is not null)
        {
            float best = 0;
            for (int i = 0; i < Math.Min(input.ChannelSubjectMultiHot.Count, ChatMonitoringSubjectSignals.GeneralSubjectCount); i++)
            {
                if (input.ChannelSubjectMultiHot[i] > best)
                {
                    best = input.ChannelSubjectMultiHot[i];
                    channel = ChatMonitoringSubjectSignals.GeneralSubjectsInOrder[i];
                }
            }
        }

        channel ??= ChatMonitoringSubjectSignals.ResolveChannelSubject(input.Requirement);
        return ChatMonitoringSubjectSignals.Resolve(
            applied,
            channel,
            input.ChannelRelevance,
            extraction.ExpertiseLabels);
    }
}
