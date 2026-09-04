using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Single;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Per-layer nonlinearity / head kind for <see cref="NeuralNetwork"/>.</summary>
public enum NeuralLayerActivation
{
    LeakyRelu,
    Tanh,
    /// <summary>Output head: sigmoid evidence, sigmoid relevance, softmax category tail.</summary>
    MixedEvidenceRelevanceSoftmax,
}

/// <summary>
/// Dense layer backed by Math.NET matrices (weights rows = targets, columns = sources).
/// Momentum buffers share the same shapes for heavy-ball SGD.
/// Column-major <see cref="WeightStorage"/> aliases the live matrix so GEMV skips indexer overhead.
/// </summary>
public sealed class DenseLayer
{
    public DenseLayer(int sourceCount, int targetCount, NeuralLayerActivation activation, float initScale, Random random)
    {
        if (sourceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceCount));
        if (targetCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetCount));

        Activation = activation;
        DenseMatrix weights = DenseMatrix.Create(targetCount, sourceCount, (_, _) =>
            (float)((random.NextDouble() * 2d - 1d) * initScale));
        DenseMatrix weightVelocity = DenseMatrix.Create(targetCount, sourceCount, 0f);
        DenseVector biases = DenseVector.Create(targetCount, _ => 0f);
        DenseVector biasVelocity = DenseVector.Create(targetCount, _ => 0f);
        Weights = weights;
        Biases = biases;
        WeightVelocity = weightVelocity;
        BiasVelocity = biasVelocity;
        WeightStorage = weights.AsColumnMajorArray();
        WeightVelocityStorage = weightVelocity.AsColumnMajorArray();
        BiasStorage = biases.AsArray();
        BiasVelocityStorage = biasVelocity.AsArray();
        SourceCount = sourceCount;
        TargetCount = targetCount;
    }

    public NeuralLayerActivation Activation { get; }
    public Matrix<float> Weights { get; }
    public Vector<float> Biases { get; }
    public Matrix<float> WeightVelocity { get; }
    public Vector<float> BiasVelocity { get; }
    public float[] WeightStorage { get; }
    public float[] WeightVelocityStorage { get; }
    public float[] BiasStorage { get; }
    public float[] BiasVelocityStorage { get; }
    public int SourceCount { get; }
    public int TargetCount { get; }
}

/// <summary>Forward activations retained for backprop and optional replay traces.</summary>
public sealed class NeuralNetworkForwardState
{
    public NeuralNetworkForwardState(
        float[][] activations,
        float[][] preActivations,
        ForwardPropagationTrace? trace)
    {
        Activations = activations;
        PreActivations = preActivations;
        Trace = trace;
    }

    public float[][] Activations { get; }
    public float[][] PreActivations { get; }
    public ForwardPropagationTrace? Trace { get; }
    public float[] Output => Activations[^1];
}

/// <summary>Accumulated mini-batch gradients for one <see cref="NeuralNetwork"/>.</summary>
public sealed class NeuralNetworkGradientBuffers
{
    public NeuralNetworkGradientBuffers(IReadOnlyList<DenseLayer> layers)
    {
        // Column-major float buffers match DenseLayer.WeightStorage layout for in-place GEMV/SGD.
        WeightGradientStorage = layers
            .Select(layer => new float[layer.TargetCount * layer.SourceCount])
            .ToArray();
        BiasGradientStorage = layers
            .Select(layer => new float[layer.TargetCount])
            .ToArray();
        TargetCounts = layers.Select(layer => layer.TargetCount).ToArray();
        SourceCounts = layers.Select(layer => layer.SourceCount).ToArray();
    }

    public float[][] WeightGradientStorage { get; }
    public float[][] BiasGradientStorage { get; }
    public int[] TargetCounts { get; }
    public int[] SourceCounts { get; }

    public void Clear()
    {
        for (int layer = 0; layer < WeightGradientStorage.Length; layer++)
        {
            Array.Clear(WeightGradientStorage[layer]);
            Array.Clear(BiasGradientStorage[layer]);
        }
    }
}

/// <summary>
/// Dense feed-forward network with column-major GEMV forward/backprop kernels.
/// Parameter flatten order matches historical HashedMlp checkpoints: per layer, per target,
/// all weights then bias.
/// </summary>
public sealed class NeuralNetwork
{
    public const float DefaultLeakyReluSlope = .01f;
    public const float DefaultMaxAbsLogit = 20f;

    private readonly DenseLayer[] _layers;
    private readonly Node[] _nodes;
    private readonly int[] _layerWidths;
    private readonly string[] _layerLabels;
    private readonly string[] _categoryLabels;
    private readonly float _leakyReluSlope;

    public NeuralNetwork(
        IReadOnlyList<int> layerWidths,
        IReadOnlyList<string> layerLabels,
        IReadOnlyList<NeuralLayerActivation> layerActivations,
        IReadOnlyList<string>? categoryLabels,
        int seed,
        Func<int, string>? inputLabelFactory = null,
        float leakyReluSlope = DefaultLeakyReluSlope)
    {
        if (layerWidths is null || layerWidths.Count < 2)
            throw new ArgumentException("At least one dense layer (two widths) is required.", nameof(layerWidths));
        if (layerLabels is null || layerLabels.Count != layerWidths.Count)
            throw new ArgumentException("Layer labels must match width count.", nameof(layerLabels));
        if (layerActivations is null || layerActivations.Count != layerWidths.Count - 1)
            throw new ArgumentException("One activation per dense layer is required.", nameof(layerActivations));

        _layerWidths = layerWidths.ToArray();
        _layerLabels = layerLabels.ToArray();
        _categoryLabels = categoryLabels?.ToArray() ?? [];
        _leakyReluSlope = leakyReluSlope;
        Random random = new(seed);
        _layers = new DenseLayer[_layerWidths.Length - 1];
        for (int layer = 0; layer < _layers.Length; layer++)
        {
            int sources = _layerWidths[layer];
            int targets = _layerWidths[layer + 1];
            bool outputLayer = layer == _layers.Length - 1;
            float scale = outputLayer && layerActivations[layer] == NeuralLayerActivation.MixedEvidenceRelevanceSoftmax
                ? MathF.Sqrt(1f / sources)
                : MathF.Sqrt(2f / sources);
            _layers[layer] = new DenseLayer(sources, targets, layerActivations[layer], scale, random);
        }

        _nodes = BuildNodes(inputLabelFactory);
        ParameterCount = CountParameters();
    }

    public IReadOnlyList<int> LayerWidths => _layerWidths;
    public IReadOnlyList<string> LayerLabels => _layerLabels;
    public IReadOnlyList<string> CategoryLabels => _categoryLabels;
    public IReadOnlyList<DenseLayer> Layers => _layers;
    public IReadOnlyList<Node> Nodes => _nodes;
    public int ParameterCount { get; }
    public int InputSize => _layerWidths[0];
    public int OutputSize => _layerWidths[^1];

    public NeuralNetworkGradientBuffers CreateGradientBuffers() => new(_layers);

    public NeuralNetworkForwardState Forward(ReadOnlySpan<float> features, bool captureTrace = false)
    {
        if (features.Length != InputSize)
            throw new ArgumentException($"Expected {InputSize} input features.", nameof(features));

        float[][] activations = new float[_layers.Length + 1][];
        float[][] preActivations = new float[_layers.Length][];
        activations[0] = features.ToArray();

        for (int layer = 0; layer < _layers.Length; layer++)
        {
            DenseLayer dense = _layers[layer];
            float[] layerPre = new float[dense.TargetCount];
            // z = W x + b via column-major GEMV (no temporary Math.NET vectors).
            MultiplyBias(dense.WeightStorage, dense.TargetCount, dense.SourceCount, activations[layer], dense.BiasStorage, layerPre);
            float[] layerAct = ApplyActivation(dense.Activation, layerPre);
            preActivations[layer] = layerPre;
            activations[layer + 1] = layerAct;
        }

        // Node mesh state is only needed for Full replay / visualizer traces.
        if (captureTrace)
            UpdateNodeState(activations, preActivations);

        ForwardPropagationTrace? trace = captureTrace
            ? BuildForwardTrace(activations[0], activations, preActivations)
            : null;
        return new NeuralNetworkForwardState(activations, preActivations, trace);
    }

    /// <summary>
    /// Mixed-head backprop for evidence/relevance BCE + categorical CE.
    /// Accumulates into <paramref name="gradients"/> and returns ∂C/∂x.
    /// </summary>
    public float[] AccumulateMixedHeadGradients(
        NeuralNetworkForwardState state,
        float evidenceTarget,
        float relevanceTarget,
        int categoryIndex,
        NeuralNetworkGradientBuffers gradients,
        Action<float>? trackGradient = null)
    {
        if (_layers[^1].Activation != NeuralLayerActivation.MixedEvidenceRelevanceSoftmax)
            throw new InvalidOperationException("Mixed-head backprop requires a MixedEvidenceRelevanceSoftmax output layer.");

        float[] output = state.Activations[^1];
        float[][] activationGradients = new float[state.Activations.Length][];
        float[] outputGrad = new float[output.Length];
        outputGrad[0] = output[0] - Math.Clamp(evidenceTarget, 0f, 1f);
        outputGrad[1] = output[1] - Math.Clamp(relevanceTarget, 0f, 1f);
        int clampedCategory = Math.Clamp(categoryIndex, 0, Math.Max(0, _categoryLabels.Length - 1));
        for (int category = 0; category < _categoryLabels.Length; category++)
            outputGrad[2 + category] = output[2 + category] - (category == clampedCategory ? 1f : 0f);
        activationGradients[^1] = outputGrad;

        return Backpropagate(state, activationGradients, gradients, trackGradient);
    }

    /// <summary>
    /// Mixed-head backprop against a soft category distribution. Softmax cross-entropy's output
    /// gradient is (prediction - target) whatever the target is, so this is the same arithmetic as
    /// the index overload with the one-hot replaced by the caller's distribution.
    ///
    /// The distribution is used as given: callers normalise. An unnormalised target still produces
    /// a finite gradient, but one whose magnitude no longer corresponds to a probability, which
    /// would quietly scale the category head's learning rate.
    /// </summary>
    public float[] AccumulateMixedHeadGradients(
        NeuralNetworkForwardState state,
        float evidenceTarget,
        float relevanceTarget,
        ReadOnlySpan<float> categoryDistribution,
        NeuralNetworkGradientBuffers gradients,
        Action<float>? trackGradient = null)
    {
        if (_layers[^1].Activation != NeuralLayerActivation.MixedEvidenceRelevanceSoftmax)
            throw new InvalidOperationException("Mixed-head backprop requires a MixedEvidenceRelevanceSoftmax output layer.");

        float[] output = state.Activations[^1];
        float[][] activationGradients = new float[state.Activations.Length][];
        float[] outputGrad = new float[output.Length];
        outputGrad[0] = output[0] - Math.Clamp(evidenceTarget, 0f, 1f);
        outputGrad[1] = output[1] - Math.Clamp(relevanceTarget, 0f, 1f);
        for (int category = 0; category < _categoryLabels.Length; category++)
        {
            float target = category < categoryDistribution.Length ? categoryDistribution[category] : 0f;
            outputGrad[2 + category] = output[2 + category] - target;
        }

        activationGradients[^1] = outputGrad;
        return Backpropagate(state, activationGradients, gradients, trackGradient);
    }

    /// <summary>
    /// Tanh-network backprop from an upstream output gradient (cascade chain rule into f).
    /// </summary>
    public float[] AccumulateFromOutputGradient(
        NeuralNetworkForwardState state,
        ReadOnlySpan<float> outputGradient,
        NeuralNetworkGradientBuffers gradients,
        Action<float>? trackGradient = null)
    {
        if (outputGradient.Length < OutputSize)
            throw new ArgumentException($"Expected at least {OutputSize} upstream gradients.", nameof(outputGradient));

        float[][] activationGradients = new float[state.Activations.Length][];
        float[] outputGrad = new float[OutputSize];
        outputGradient[..OutputSize].CopyTo(outputGrad);
        activationGradients[^1] = outputGrad;
        return Backpropagate(state, activationGradients, gradients, trackGradient);
    }

    public void ApplyMomentumUpdate(
        NeuralNetworkGradientBuffers gradients,
        int batchSize,
        float learningRate,
        float momentumCoefficient,
        float maxAbsGradient,
        float? maxAbsWeight)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        float invN = 1f / batchSize;
        for (int layer = 0; layer < _layers.Length; layer++)
        {
            DenseLayer dense = _layers[layer];
            float[] weightGrad = gradients.WeightGradientStorage[layer];
            float[] biasGrad = gradients.BiasGradientStorage[layer];
            float[] weights = dense.WeightStorage;
            float[] weightVelocity = dense.WeightVelocityStorage;
            float[] biases = dense.BiasStorage;
            float[] biasVelocity = dense.BiasVelocityStorage;
            int rows = dense.TargetCount;

            for (int index = 0; index < weightGrad.Length; index++)
            {
                float avgGrad = NeuralNetFinite.ClampFinite(
                    weightGrad[index] * invN, -maxAbsGradient, maxAbsGradient);
                float velocity = momentumCoefficient * NeuralNetFinite.OrZero(weightVelocity[index]) + avgGrad;
                weightVelocity[index] = velocity;
                float updated = NeuralNetFinite.OrZero(weights[index] - learningRate * velocity);
                weights[index] = maxAbsWeight is float bound
                    ? NeuralNetFinite.ClampFinite(updated, -bound, bound)
                    : updated;
            }

            for (int row = 0; row < rows; row++)
            {
                float avgBiasGrad = NeuralNetFinite.ClampFinite(
                    biasGrad[row] * invN, -maxAbsGradient, maxAbsGradient);
                float velocity = momentumCoefficient * NeuralNetFinite.OrZero(biasVelocity[row]) + avgBiasGrad;
                biasVelocity[row] = velocity;
                float updatedBias = NeuralNetFinite.OrZero(biases[row] - learningRate * velocity);
                biases[row] = maxAbsWeight is float biasBound
                    ? NeuralNetFinite.ClampFinite(updatedBias, -biasBound, biasBound)
                    : updatedBias;
            }
        }
    }

    public float[] FlattenParameters()
    {
        float[] values = new float[ParameterCount];
        int offset = 0;
        for (int layer = 0; layer < _layers.Length; layer++)
        {
            DenseLayer dense = _layers[layer];
            float[] weights = dense.WeightStorage;
            float[] biases = dense.BiasStorage;
            int rows = dense.TargetCount;
            for (int target = 0; target < rows; target++)
            {
                for (int source = 0; source < dense.SourceCount; source++)
                    values[offset++] = weights[source * rows + target];
                values[offset++] = biases[target];
            }
        }

        return values;
    }

    public void LoadParameters(ReadOnlySpan<float> values)
    {
        if (values.Length != ParameterCount)
            throw new ArgumentException($"Expected {ParameterCount} parameters.", nameof(values));

        int offset = 0;
        for (int layer = 0; layer < _layers.Length; layer++)
        {
            DenseLayer dense = _layers[layer];
            float[] weights = dense.WeightStorage;
            float[] biases = dense.BiasStorage;
            int rows = dense.TargetCount;
            for (int target = 0; target < rows; target++)
            {
                for (int source = 0; source < dense.SourceCount; source++)
                    weights[source * rows + target] = values[offset++];
                biases[target] = values[offset++];
            }

            Array.Clear(dense.WeightVelocityStorage);
            Array.Clear(dense.BiasVelocityStorage);
        }
    }

    public float ParameterL2Norm()
    {
        double sumSquares = 0;
        for (int layer = 0; layer < _layers.Length; layer++)
        {
            DenseLayer dense = _layers[layer];
            sumSquares += dense.Weights.FrobeniusNorm() * dense.Weights.FrobeniusNorm();
            sumSquares += dense.Biases.L2Norm() * dense.Biases.L2Norm();
        }

        return (float)Math.Sqrt(sumSquares);
    }

    public NeuralNetTopologySnapshot BuildTopologySnapshot(string modelVersion)
    {
        List<ReplayNode> replayNodes = _nodes.Select(node => node.ToReplayNode()).ToList();
        List<ReplayEdge> edges = [];
        List<ReplayParameter> parameters = [];
        int sourceOffset = 0;
        int targetOffset = _layerWidths[0];
        for (int layer = 0; layer < _layerWidths.Length - 1; layer++)
        {
            for (int target = 0; target < _layerWidths[layer + 1]; target++)
            {
                for (int source = 0; source < _layerWidths[layer]; source++)
                {
                    int parameterIndex = parameters.Count;
                    parameters.Add(new ReplayParameter(
                        parameterIndex,
                        $"weight-{layer}-{target}-{source}",
                        ReplayParameterKind.Weight,
                        sourceOffset + source,
                        targetOffset + target,
                        true));
                    edges.Add(new ReplayEdge(
                        edges.Count,
                        $"edge-{layer}-{source}-{target}",
                        sourceOffset + source,
                        targetOffset + target,
                        parameterIndex));
                }

                parameters.Add(new ReplayParameter(
                    parameters.Count,
                    $"bias-{layer}-{target}",
                    ReplayParameterKind.Bias,
                    null,
                    targetOffset + target,
                    true));
            }

            sourceOffset = targetOffset;
            targetOffset += _layerWidths[layer + 1];
        }

        return new NeuralNetTopologySnapshot(modelVersion, replayNodes, edges, parameters);
    }

    public static float BinaryCrossEntropy(float probability, float target)
    {
        float bounded = Math.Clamp(probability, .000001f, .999999f);
        return -(target * MathF.Log(bounded) + (1 - target) * MathF.Log(1 - bounded));
    }

    public float CategoricalCrossEntropy(ReadOnlySpan<float> activations, int categoryIndex)
    {
        if (_categoryLabels.Length == 0)
            return 0f;
        int index = Math.Clamp(categoryIndex, 0, _categoryLabels.Length - 1);
        float probability = Math.Clamp(activations[2 + index], .000001f, .999999f);
        return -MathF.Log(probability);
    }

    /// <summary>
    /// Cross-entropy against a full target distribution: -sum(q_i * log p_i). The single-index
    /// overload above is the q = one-hot case of this, and both agree to floating-point error.
    /// Zero-probability categories contribute nothing, so a sparse teacher distribution costs
    /// only the categories it actually names.
    /// </summary>
    public float CategoricalCrossEntropy(ReadOnlySpan<float> activations, ReadOnlySpan<float> targetDistribution)
    {
        if (_categoryLabels.Length == 0)
            return 0f;

        float loss = 0f;
        int count = Math.Min(_categoryLabels.Length, targetDistribution.Length);
        for (int category = 0; category < count; category++)
        {
            float target = targetDistribution[category];
            if (target <= 0f)
                continue;

            float probability = Math.Clamp(activations[2 + category], .000001f, .999999f);
            loss -= target * MathF.Log(probability);
        }

        return loss;
    }

    public int ArgMaxCategory(ReadOnlySpan<float> activations)
    {
        if (_categoryLabels.Length == 0)
            return 0;
        int best = 0;
        float bestValue = activations[2];
        for (int category = 1; category < _categoryLabels.Length; category++)
        {
            if (activations[2 + category] <= bestValue)
                continue;
            bestValue = activations[2 + category];
            best = category;
        }

        return best;
    }

    private float[] Backpropagate(
        NeuralNetworkForwardState state,
        float[][] activationGradients,
        NeuralNetworkGradientBuffers gradients,
        Action<float>? trackGradient)
    {
        for (int layer = _layers.Length - 1; layer >= 0; layer--)
        {
            DenseLayer dense = _layers[layer];
            float[] upstream = activationGradients[layer + 1];
            float[] localGrad = new float[dense.TargetCount];
            float[] biasGradient = gradients.BiasGradientStorage[layer];
            float[] weightGradient = gradients.WeightGradientStorage[layer];
            float[] sourceActivations = state.Activations[layer];
            int rows = dense.TargetCount;
            int cols = dense.SourceCount;

            for (int target = 0; target < rows; target++)
            {
                float gradient = upstream[target];
                gradient *= ActivationDerivative(
                    dense.Activation,
                    state.PreActivations[layer][target],
                    state.Activations[layer + 1][target]);
                localGrad[target] = gradient;
                trackGradient?.Invoke(gradient);
                biasGradient[target] += gradient;
            }

            // ∂C/∂W = δ xᵀ into column-major storage (index = column * rows + row).
            for (int column = 0; column < cols; column++)
            {
                float sourceValue = sourceActivations[column];
                if (sourceValue == 0f)
                    continue;
                int offset = column * rows;
                for (int row = 0; row < rows; row++)
                {
                    float contribution = localGrad[row] * sourceValue;
                    weightGradient[offset + row] += contribution;
                    trackGradient?.Invoke(contribution);
                }
            }

            // ∂C/∂x = Wᵀ δ
            float[] inputGrad = new float[cols];
            MultiplyTranspose(dense.WeightStorage, rows, cols, localGrad, inputGrad);
            activationGradients[layer] = inputGrad;
        }

        return activationGradients[0];
    }

    /// <summary>y = W x + b with W stored column-major (rows = targets).</summary>
    private static void MultiplyBias(
        float[] weightsColumnMajor,
        int rows,
        int cols,
        float[] source,
        float[] biases,
        float[] destination)
    {
        Array.Copy(biases, destination, rows);
        for (int column = 0; column < cols; column++)
        {
            float sourceValue = source[column];
            if (sourceValue == 0f)
                continue;
            int offset = column * rows;
            for (int row = 0; row < rows; row++)
                destination[row] += weightsColumnMajor[offset + row] * sourceValue;
        }
    }

    /// <summary>destination = Wᵀ delta with W stored column-major.</summary>
    private static void MultiplyTranspose(
        float[] weightsColumnMajor,
        int rows,
        int cols,
        float[] delta,
        float[] destination)
    {
        for (int column = 0; column < cols; column++)
        {
            float sum = 0f;
            int offset = column * rows;
            for (int row = 0; row < rows; row++)
                sum += weightsColumnMajor[offset + row] * delta[row];
            destination[column] = sum;
        }
    }

    private float[] ApplyActivation(NeuralLayerActivation activation, float[] pre)
    {
        float[] act = new float[pre.Length];
        switch (activation)
        {
            case NeuralLayerActivation.LeakyRelu:
                for (int i = 0; i < pre.Length; i++)
                    act[i] = LeakyRelu(pre[i]);
                return act;
            case NeuralLayerActivation.Tanh:
                for (int i = 0; i < pre.Length; i++)
                    act[i] = MathF.Tanh(pre[i]);
                return act;
            case NeuralLayerActivation.MixedEvidenceRelevanceSoftmax:
                act[0] = Sigmoid(pre[0]);
                act[1] = Sigmoid(pre[1]);
                Softmax(pre.AsSpan(2), act.AsSpan(2));
                return act;
            default:
                throw new ArgumentOutOfRangeException(nameof(activation));
        }
    }

    private float ActivationDerivative(
        NeuralLayerActivation activation,
        float preActivation,
        float activationValue)
    {
        return activation switch
        {
            NeuralLayerActivation.LeakyRelu => preActivation >= 0f ? 1f : _leakyReluSlope,
            // d(tanh)/dz = 1 − tanh(z)²; use stored activation.
            NeuralLayerActivation.Tanh => 1f - activationValue * activationValue,
            // Mixed head: BCE/CE gradients are already in probability/logit-combined form.
            NeuralLayerActivation.MixedEvidenceRelevanceSoftmax => 1f,
            _ => throw new ArgumentOutOfRangeException(nameof(activation)),
        };
    }

    private float LeakyRelu(float value) => value >= 0f ? value : _leakyReluSlope * value;

    private static float Sigmoid(float sum) =>
        1f / (1f + MathF.Exp(-Math.Clamp(sum, -DefaultMaxAbsLogit, DefaultMaxAbsLogit)));

    private static void Softmax(ReadOnlySpan<float> logits, Span<float> destination)
    {
        if (logits.Length == 0)
            return;

        float max = float.NegativeInfinity;
        for (int i = 0; i < logits.Length; i++)
        {
            if (logits[i] > max)
                max = logits[i];
        }

        float sum = 0f;
        for (int i = 0; i < logits.Length; i++)
        {
            destination[i] = MathF.Exp(Math.Clamp(logits[i] - max, -DefaultMaxAbsLogit, DefaultMaxAbsLogit));
            sum += destination[i];
        }

        if (sum <= 0f)
            sum = 1f;
        for (int i = 0; i < destination.Length; i++)
            destination[i] /= sum;
    }

    private void UpdateNodeState(float[][] activations, float[][] preActivations)
    {
        int cursor = 0;
        for (int feature = 0; feature < activations[0].Length; feature++)
        {
            _nodes[cursor].PreActivation = activations[0][feature];
            _nodes[cursor].Activation = activations[0][feature];
            cursor++;
        }

        for (int layer = 0; layer < preActivations.Length; layer++)
        {
            for (int node = 0; node < preActivations[layer].Length; node++)
            {
                _nodes[cursor].PreActivation = preActivations[layer][node];
                _nodes[cursor].Activation = activations[layer + 1][node];
                cursor++;
            }
        }
    }

    private ForwardPropagationTrace BuildForwardTrace(
        float[] features,
        float[][] activations,
        float[][] preActivations)
    {
        List<FeatureActivation> featureActivations = features
            .Select((value, index) => new FeatureActivation(index, value, []))
            .ToList();
        List<SparseValue> nodePreActivations = [];
        List<SparseValue> nodeActivations = [];
        List<SparseValue> edgeContributions = [];
        List<SparseValue> biasContributions = [];
        int nodeOffset = _layerWidths[0];
        for (int layer = 0; layer < preActivations.Length; layer++)
        {
            for (int node = 0; node < preActivations[layer].Length; node++)
            {
                nodePreActivations.Add(new SparseValue(
                    nodeOffset + node,
                    NeuralNetFinite.ClampFinite(preActivations[layer][node], -DefaultMaxAbsLogit * 4f, DefaultMaxAbsLogit * 4f)));
                nodeActivations.Add(new SparseValue(
                    nodeOffset + node,
                    NeuralNetFinite.OrZero(activations[layer + 1][node])));
            }

            nodeOffset += preActivations[layer].Length;
        }

        int parameterCursor = 0;
        for (int layer = 0; layer < _layers.Length; layer++)
        {
            DenseLayer dense = _layers[layer];
            float[] source = activations[layer];
            float[] weights = dense.WeightStorage;
            float[] biases = dense.BiasStorage;
            int rows = dense.TargetCount;
            for (int target = 0; target < rows; target++)
            {
                for (int sourceIndex = 0; sourceIndex < dense.SourceCount; sourceIndex++)
                {
                    edgeContributions.Add(new SparseValue(
                        parameterCursor++,
                        NeuralNetFinite.OrZero(source[sourceIndex] * weights[sourceIndex * rows + target])));
                }

                biasContributions.Add(new SparseValue(
                    parameterCursor++,
                    NeuralNetFinite.OrZero(biases[target])));
            }
        }

        float confidence = Math.Clamp(MathF.Abs(activations[^1][0] - .5f) * 2f, .05f, .99f);
        return new ForwardPropagationTrace(
            featureActivations,
            nodePreActivations,
            nodeActivations,
            edgeContributions,
            biasContributions,
            preActivations[^1][0],
            preActivations[^1].Length > 1 ? preActivations[^1][1] : 0f,
            activations[^1][0],
            activations[^1].Length > 1 ? activations[^1][1] : 0f,
            confidence);
    }

    private Node[] BuildNodes(Func<int, string>? inputLabelFactory)
    {
        List<Node> nodes = [];
        for (int input = 0; input < _layerWidths[0]; input++)
        {
            string label = inputLabelFactory?.Invoke(input) ?? $"feature-{input}";
            nodes.Add(new Node(
                nodes.Count,
                $"input-{input}",
                _layerLabels[0],
                0,
                label,
                input,
                false));
        }

        for (int layer = 1; layer < _layerWidths.Length; layer++)
        {
            for (int node = 0; node < _layerWidths[layer]; node++)
            {
                bool outputLayer = layer == _layerWidths.Length - 1;
                string label;
                if (!outputLayer)
                {
                    label = $"{_layerLabels[layer]}-{node + 1}";
                }
                else if (_layers[^1].Activation == NeuralLayerActivation.MixedEvidenceRelevanceSoftmax)
                {
                    label = node switch
                    {
                        0 => "Evidence",
                        1 => "Relevance",
                        _ when node - 2 < _categoryLabels.Length => _categoryLabels[node - 2],
                        _ => $"{_layerLabels[layer]}-{node + 1}",
                    };
                }
                else
                {
                    label = $"{_layerLabels[layer]}-{node + 1}";
                }

                nodes.Add(new Node(
                    nodes.Count,
                    $"{_layerLabels[layer]}-{node}",
                    _layerLabels[layer],
                    layer,
                    label,
                    null,
                    true));
            }
        }

        return nodes.ToArray();
    }

    private int CountParameters()
    {
        int count = 0;
        for (int layer = 0; layer < _layers.Length; layer++)
            count += _layers[layer].TargetCount * (_layers[layer].SourceCount + 1);
        return count;
    }
}
