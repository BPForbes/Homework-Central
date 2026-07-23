using System.Security.Cryptography;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// CPU hashed MLP for chat monitoring, shaped for an eventual LLM-free stack.
/// Linear algebra (matrix-vector products, outer products, transpose-multiply)
/// runs through <see cref="NeuralNetwork"/> / Math.NET Numerics.
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
    private const float MaxAbsGradient = 5f;
    private const float MaxAbsWeight = 25f;
    private const float MaxAbsLogit = NeuralNetwork.DefaultMaxAbsLogit;
    private readonly NeuralNetwork network;
    private readonly NeuralNetTopologySnapshot topology;
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
        int outputCount = 2 + categoryLabels.Count;
        int[] widths =
        [
            ChatMonitoringFeatureEncoder.FeatureCount,
            firstHidden,
            secondHidden,
            thirdHidden,
            fourthHidden,
            outputCount,
        ];
        NeuralLayerActivation[] activations =
        [
            NeuralLayerActivation.LeakyRelu,
            NeuralLayerActivation.LeakyRelu,
            NeuralLayerActivation.LeakyRelu,
            NeuralLayerActivation.LeakyRelu,
            NeuralLayerActivation.MixedEvidenceRelevanceSoftmax,
        ];
        network = new NeuralNetwork(
            widths,
            layerLabels,
            activations,
            categoryLabels,
            seed,
            InputFeatureLabel);
        topology = network.BuildTopologySnapshot(ModelVersion);
    }

    public NeuralModelKindChatMonitoring Kind { get; }
    public string ModelVersion { get; }
    public IReadOnlyList<string> LayerLabels => network.LayerLabels;
    public IReadOnlyList<string> CategoryLabels => network.CategoryLabels;
    public IReadOnlyList<Node> Nodes => network.Nodes;

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
            NeuralNetworkGradientBuffers gradients = network.CreateGradientBuffers();
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

                gradients.Clear();
                float evidenceLossSum = 0, relevanceLossSum = 0, categoryLossSum = 0;
                float evidenceProbSum = 0, relevanceProbSum = 0, evidenceLogitSum = 0, relevanceLogitSum = 0;
                GradientMagnitudeTracker gradientMagnitudes = new();
                float[][] inputGradients = new float[n][];

                for (int i = 0; i < n; i++)
                {
                    NeuralNetworkForwardState cache = network.Forward(encoded[i], captureTrace: false);
                    float evidence = cache.Activations[^1][0];
                    float relevance = cache.Activations[^1][1];
                    evidenceProbSum += evidence;
                    relevanceProbSum += relevance;
                    evidenceLogitSum += cache.PreActivations[^1][0];
                    relevanceLogitSum += cache.PreActivations[^1][1];
                    evidenceLossSum += NeuralNetwork.BinaryCrossEntropy(evidence, batch[i].Targets.Evidence);
                    relevanceLossSum += NeuralNetwork.BinaryCrossEntropy(relevance, batch[i].Targets.Relevance);
                    categoryLossSum += network.CategoricalCrossEntropy(cache.Activations[^1], categoryIndices[i]);
                    ChatMonitoringNeuralModelTargets targets = batch[i].Targets with { CategoryIndex = categoryIndices[i] };
                    inputGradients[i] = network.AccumulateMixedHeadGradients(
                        cache,
                        targets.Evidence,
                        targets.Relevance,
                        targets.CategoryIndex,
                        gradients,
                        gradientMagnitudes.Track);
                }

                onSampleInputGradients?.Invoke(inputGradients);

                LossTrace lossBefore = new(
                    "bce+categorical-cross-entropy-avg-v1",
                    NeuralNetFinite.OrZero(evidenceLossSum / n),
                    NeuralNetFinite.OrZero(relevanceLossSum / n),
                    0,
                    NeuralNetFinite.OrZero((evidenceLossSum + relevanceLossSum + categoryLossSum) / n),
                    n,
                    NeuralNetFinite.OrZero(categoryLossSum / n));
                // Compact epochs reuse batch averages; full traces keep a traced forward for replay fidelity.
                ForwardPropagationTrace beforeForward = captureFull
                    ? network.Forward(encoded[0], captureTrace: true).Trace
                        ?? AverageOutputForward(
                            evidenceLogitSum / n, relevanceLogitSum / n, evidenceProbSum / n, relevanceProbSum / n)
                    : AverageOutputForward(
                        evidenceLogitSum / n, relevanceLogitSum / n, evidenceProbSum / n, relevanceProbSum / n);

                float[]? parameterBefore = captureFull ? network.FlattenParameters() : null;
                network.ApplyMomentumUpdate(
                    gradients, n, LearningRate, MomentumCoefficient, MaxAbsGradient, MaxAbsWeight);
                float[]? parameterAfter = captureFull ? network.FlattenParameters() : null;
                float avgGradScale = 1f / n;
                float gradientL2 = NeuralNetFinite.OrZero(
                    MathF.Sqrt((float)(gradientMagnitudes.GradSqSum * avgGradScale * avgGradScale)));
                float maxAbsGradient = NeuralNetFinite.OrZero(gradientMagnitudes.MaxAbs * avgGradScale);
                float minNonZeroAbsGradient = NeuralNetFinite.OrZero(
                    gradientMagnitudes.ResolveMinNonZero() * avgGradScale);

                IReadOnlyList<ParameterDelta> deltas = captureFull
                    ? BuildParameterDeltas(parameterBefore!, parameterAfter!)
                    : [];
                IReadOnlyList<SparseValue> sparseGradients = captureFull
                    ? deltas.Select(delta => new SparseValue(delta.ParameterIndex, NeuralNetFinite.OrZero(delta.Gradient))).ToList()
                    : [];
                GradientHealth health = new(
                    gradientL2 < 0.000001f,
                    gradientL2 > 1000f,
                    0.000001f,
                    1000f,
                    captureFull
                        ? (sparseGradients.Count == 0 ? 0 : sparseGradients.Max(gradient => MathF.Abs(gradient.Value)))
                        : maxAbsGradient,
                    captureFull
                        ? sparseGradients
                            .Where(gradient => MathF.Abs(gradient.Value) > 0f)
                            .Select(gradient => MathF.Abs(gradient.Value))
                            .DefaultIfEmpty(0)
                            .Min()
                        : minNonZeroAbsGradient);
                BackpropagationTrace backward = new([], [],
                    sparseGradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Weight).ToList(),
                    sparseGradients.Where(gradient => topology.Parameters[gradient.Index].Kind == ReplayParameterKind.Bias).ToList(),
                    gradientL2, health);

                float afterEvidenceLoss = 0, afterRelevanceLoss = 0, afterCategoryLoss = 0;
                float afterEvidenceProb = 0, afterRelevanceProb = 0, afterEvidenceLogit = 0, afterRelevanceLogit = 0;
                float meanAbsEvidenceError = 0, meanAbsRelevanceError = 0;
                for (int i = 0; i < n; i++)
                {
                    NeuralNetworkForwardState afterCache = network.Forward(encoded[i], captureTrace: false);
                    float evidence = afterCache.Activations[^1][0];
                    float relevance = afterCache.Activations[^1][1];
                    afterEvidenceProb += evidence;
                    afterRelevanceProb += relevance;
                    afterEvidenceLogit += afterCache.PreActivations[^1][0];
                    afterRelevanceLogit += afterCache.PreActivations[^1][1];
                    afterEvidenceLoss += NeuralNetwork.BinaryCrossEntropy(evidence, batch[i].Targets.Evidence);
                    afterRelevanceLoss += NeuralNetwork.BinaryCrossEntropy(relevance, batch[i].Targets.Relevance);
                    afterCategoryLoss += network.CategoricalCrossEntropy(afterCache.Activations[^1], categoryIndices[i]);
                    meanAbsEvidenceError += MathF.Abs(evidence - batch[i].Targets.Evidence);
                    meanAbsRelevanceError += MathF.Abs(relevance - batch[i].Targets.Relevance);
                }

                LossTrace lossAfter = new(
                    "bce+categorical-cross-entropy-avg-v1",
                    NeuralNetFinite.OrZero(afterEvidenceLoss / n),
                    NeuralNetFinite.OrZero(afterRelevanceLoss / n),
                    0,
                    NeuralNetFinite.OrZero((afterEvidenceLoss + afterRelevanceLoss + afterCategoryLoss) / n),
                    n,
                    NeuralNetFinite.OrZero(afterCategoryLoss / n));
                ForwardPropagationTrace afterForward = captureFull
                    ? network.Forward(encoded[0], captureTrace: true).Trace
                        ?? AverageOutputForward(
                            afterEvidenceLogit / n, afterRelevanceLogit / n, afterEvidenceProb / n, afterRelevanceProb / n)
                    : AverageOutputForward(
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
                    string category = CategoryLabels[lastCategoryIndices[i]];
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
            float[] parameters = network.FlattenParameters();
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
            float[] parameters = network.FlattenParameters();
            return new ChatMonitoringNeuralModelStateSnapshot(
                Kind, ModelVersion, network.LayerWidths.ToArray(), network.LayerLabels.ToArray(), parameters.Length,
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
            network.LoadParameters(values);
        }
    }

    public void Dispose() { }

    private int ResolveCategoryIndex(ChatMonitoringNeuralModelTrainingExample example)
    {
        if (example.Targets.CategoryIndex >= 0 && example.Targets.CategoryIndex < CategoryLabels.Count)
            return example.Targets.CategoryIndex;
        string category = string.IsNullOrWhiteSpace(example.Category) || example.Category == "general"
            ? ChatMonitoringTicketContext.DetectCategory(example.Input.Requirement, Kind)
            : example.Category;
        return ChatMonitoringCategoryTaxonomy.IndexOf(Kind, category);
    }

    private ChatMonitoringNeuralModelInferenceTrace PredictUnlocked(ChatMonitoringNeuralModelInput input, float[]? encoded = null)
    {
        float[] features = encoded ?? ChatMonitoringFeatureEncoder.Encode(input);
        NeuralNetworkForwardState cache = network.Forward(features, captureTrace: true);
        return BuildInference(input, features, cache);
    }

    private ChatMonitoringNeuralModelInferenceTrace BuildInference(
        ChatMonitoringNeuralModelInput input,
        float[] features,
        NeuralNetworkForwardState cache)
    {
        float evidence = cache.Activations[^1][0];
        float relevance = cache.Activations[^1][1];
        int categoryIndex = network.ArgMaxCategory(cache.Activations[^1]);
        float categoryConfidence = cache.Activations[^1][2 + categoryIndex];
        string category = CategoryLabels[categoryIndex];
        double supportSimilarity = support.Count == 0 ? 0 : support.Max(item => Cosine(features, item.Features));
        double separation = Math.Abs(evidence - .5f) * 2;
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

    private sealed class GradientMagnitudeTracker
    {
        public float MaxAbs { get; private set; }
        public double GradSqSum { get; private set; }
        private float _minNonZero = float.MaxValue;

        public void Track(float gradient)
        {
            if (!float.IsFinite(gradient))
                return;

            float abs = MathF.Abs(gradient);
            if (abs > MaxAbs)
                MaxAbs = abs;
            if (abs > 0f && abs < _minNonZero)
                _minNonZero = abs;
            GradSqSum += gradient * gradient;
        }

        public float ResolveMinNonZero() => _minNonZero >= float.MaxValue ? 0f : _minNonZero;
    }

    private static ForwardPropagationTrace AverageOutputForward(
        float evidenceLogit,
        float relevanceLogit,
        float evidenceProbability,
        float relevanceProbability)
    {
        float boundedEvidenceLogit = NeuralNetFinite.ClampFinite(evidenceLogit, -MaxAbsLogit, MaxAbsLogit);
        float boundedRelevanceLogit = NeuralNetFinite.ClampFinite(relevanceLogit, -MaxAbsLogit, MaxAbsLogit);
        float evidence = NeuralNetFinite.ClampFinite(evidenceProbability, 0f, 1f);
        float relevance = NeuralNetFinite.ClampFinite(relevanceProbability, 0f, 1f);
        float confidence = Math.Clamp(MathF.Abs(evidence - .5f) * 2f, .05f, .99f);
        return new([], [], [], [], [], boundedEvidenceLogit, boundedRelevanceLogit, evidence, relevance, confidence);
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

    private static string InputFeatureLabel(int input) =>
        input switch
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
            NeuralNetworkGradientBuffers routerGradients = router.CreateGradientBuffers();

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

                    routerGradients.Clear();
                    for (int i = 0; i < inputGradients.Count; i++)
                    {
                        ReadOnlySpan<float> dCdF = inputGradients[i].AsSpan(
                            ModerationConceptContextRouter.CascadeFeatureStart,
                            ModerationConceptContextRouter.OutputSize);
                        router.AccumulateFromOutputGradient(forwardStates[i], dCdF, routerGradients);
                    }

                    router.ApplyMomentumUpdate(routerGradients, examples.Count);
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
            NeuralNetworkGradientBuffers routerGradients = router.CreateGradientBuffers();

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

                    routerGradients.Clear();
                    for (int i = 0; i < inputGradients.Count; i++)
                    {
                        // ∂C/∂f = ∂C/∂x[78:86] — chain rule through g(f(x)).
                        ReadOnlySpan<float> dCdF = inputGradients[i].AsSpan(
                            TutoringSubjectContextRouter.CascadeFeatureStart,
                            TutoringSubjectContextRouter.OutputSize);
                        router.AccumulateFromOutputGradient(forwardStates[i], dCdF, routerGradients);
                    }

                    router.ApplyMomentumUpdate(routerGradients, examples.Count);
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
