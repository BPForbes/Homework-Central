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
        Weights = DenseMatrix.Create(targetCount, sourceCount, (_, _) =>
            (float)((random.NextDouble() * 2d - 1d) * initScale));
        Biases = DenseVector.Create(targetCount, _ => 0f);
        WeightVelocity = DenseMatrix.Create(targetCount, sourceCount, 0f);
        BiasVelocity = DenseVector.Create(targetCount, _ => 0f);
    }

    public NeuralLayerActivation Activation { get; }
    public Matrix<float> Weights { get; }
    public Vector<float> Biases { get; }
    public Matrix<float> WeightVelocity { get; }
    public Vector<float> BiasVelocity { get; }
    public int SourceCount => Weights.ColumnCount;
    public int TargetCount => Weights.RowCount;
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
        WeightGradients = layers.Select(layer => DenseMatrix.Create(layer.TargetCount, layer.SourceCount, 0f)).ToArray();
        BiasGradients = layers.Select(layer => DenseVector.Create(layer.TargetCount, _ => 0f)).ToArray();
    }

    public Matrix<float>[] WeightGradients { get; }
    public Vector<float>[] BiasGradients { get; }

    public void Clear()
    {
        for (int layer = 0; layer < WeightGradients.Length; layer++)
        {
            WeightGradients[layer].Clear();
            BiasGradients[layer].Clear();
        }
    }
}

/// <summary>
/// Dense feed-forward network using Math.NET Numerics for matrix-vector products,
/// outer products, and transpose-multiply during backprop. Parameter flatten order
/// matches historical HashedMlp checkpoints: per layer, per target, all weights then bias.
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
            Vector<float> source = DenseVector.OfArray(activations[layer]);
            // Math.NET: z = W x + b
            Vector<float> pre = dense.Weights * source + dense.Biases;
            float[] layerPre = pre.ToArray();
            float[] layerAct = ApplyActivation(dense.Activation, layerPre);
            preActivations[layer] = layerPre;
            activations[layer + 1] = layerAct;
        }

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
        Vector<float>[] activationGradients = new Vector<float>[state.Activations.Length];
        float[] outputGrad = new float[output.Length];
        outputGrad[0] = output[0] - Math.Clamp(evidenceTarget, 0f, 1f);
        outputGrad[1] = output[1] - Math.Clamp(relevanceTarget, 0f, 1f);
        int clampedCategory = Math.Clamp(categoryIndex, 0, Math.Max(0, _categoryLabels.Length - 1));
        for (int category = 0; category < _categoryLabels.Length; category++)
            outputGrad[2 + category] = output[2 + category] - (category == clampedCategory ? 1f : 0f);
        activationGradients[^1] = DenseVector.OfArray(outputGrad);

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

        Vector<float>[] activationGradients = new Vector<float>[state.Activations.Length];
        float[] outputGrad = new float[OutputSize];
        outputGradient[..OutputSize].CopyTo(outputGrad);
        activationGradients[^1] = DenseVector.OfArray(outputGrad);
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
            Matrix<float> weightGrad = gradients.WeightGradients[layer];
            Vector<float> biasGrad = gradients.BiasGradients[layer];

            for (int row = 0; row < dense.TargetCount; row++)
            {
                for (int column = 0; column < dense.SourceCount; column++)
                {
                    float avgGrad = NeuralNetFinite.ClampFinite(
                        weightGrad[row, column] * invN, -maxAbsGradient, maxAbsGradient);
                    float velocity = momentumCoefficient * NeuralNetFinite.OrZero(dense.WeightVelocity[row, column]) + avgGrad;
                    dense.WeightVelocity[row, column] = velocity;
                    float updated = NeuralNetFinite.OrZero(dense.Weights[row, column] - learningRate * velocity);
                    dense.Weights[row, column] = maxAbsWeight is float bound
                        ? NeuralNetFinite.ClampFinite(updated, -bound, bound)
                        : updated;
                }

                float avgBiasGrad = NeuralNetFinite.ClampFinite(
                    biasGrad[row] * invN, -maxAbsGradient, maxAbsGradient);
                float biasVelocity = momentumCoefficient * NeuralNetFinite.OrZero(dense.BiasVelocity[row]) + avgBiasGrad;
                dense.BiasVelocity[row] = biasVelocity;
                float updatedBias = NeuralNetFinite.OrZero(dense.Biases[row] - learningRate * biasVelocity);
                dense.Biases[row] = maxAbsWeight is float biasBound
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
            for (int target = 0; target < dense.TargetCount; target++)
            {
                for (int source = 0; source < dense.SourceCount; source++)
                    values[offset++] = dense.Weights[target, source];
                values[offset++] = dense.Biases[target];
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
            for (int target = 0; target < dense.TargetCount; target++)
            {
                for (int source = 0; source < dense.SourceCount; source++)
                    dense.Weights[target, source] = values[offset++];
                dense.Biases[target] = values[offset++];
            }

            dense.WeightVelocity.Clear();
            dense.BiasVelocity.Clear();
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
        Vector<float>[] activationGradients,
        NeuralNetworkGradientBuffers gradients,
        Action<float>? trackGradient)
    {
        for (int layer = _layers.Length - 1; layer >= 0; layer--)
        {
            DenseLayer dense = _layers[layer];
            Vector<float> upstream = activationGradients[layer + 1];
            Vector<float> source = DenseVector.OfArray(state.Activations[layer]);
            float[] localGrad = new float[dense.TargetCount];

            for (int target = 0; target < dense.TargetCount; target++)
            {
                float gradient = upstream[target];
                gradient *= ActivationDerivative(
                    dense.Activation,
                    state.PreActivations[layer][target],
                    state.Activations[layer + 1][target]);
                localGrad[target] = gradient;
                trackGradient?.Invoke(gradient);
                gradients.BiasGradients[layer][target] += gradient;
            }

            Vector<float> delta = DenseVector.OfArray(localGrad);
            // ∂C/∂W = δ xᵀ  (outer product via Math.NET)
            Matrix<float> weightGrad = delta.OuterProduct(source);
            gradients.WeightGradients[layer].Add(weightGrad, gradients.WeightGradients[layer]);
            if (trackGradient is not null)
            {
                for (int row = 0; row < weightGrad.RowCount; row++)
                {
                    for (int column = 0; column < weightGrad.ColumnCount; column++)
                        trackGradient(weightGrad[row, column]);
                }
            }

            // ∂C/∂x = Wᵀ δ
            activationGradients[layer] = dense.Weights.TransposeThisAndMultiply(delta);
        }

        return activationGradients[0].ToArray();
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
            for (int target = 0; target < dense.TargetCount; target++)
            {
                for (int sourceIndex = 0; sourceIndex < dense.SourceCount; sourceIndex++)
                {
                    edgeContributions.Add(new SparseValue(
                        parameterCursor++,
                        NeuralNetFinite.OrZero(source[sourceIndex] * dense.Weights[target, sourceIndex])));
                }

                biasContributions.Add(new SparseValue(
                    parameterCursor++,
                    NeuralNetFinite.OrZero(dense.Biases[target])));
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
