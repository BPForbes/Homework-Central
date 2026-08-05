using TorchSharp;
using TorchSharp.Utils;
using static TorchSharp.torch;
using F = TorchSharp.torch.nn.functional;
using Reduction = TorchSharp.torch.nn.Reduction;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Batched mixed-head forward/backward on LibTorch. Gradients land in the same
/// column-major <see cref="NeuralNetworkGradientBuffers"/> layout Math.NET uses so
/// <see cref="NeuralNetwork.ApplyMomentumUpdate"/> and IEEE754 checkpoints stay unchanged.
/// </summary>
public static class NeuralTorchMixedHeadBatch
{
    public readonly struct BatchGradientResult
    {
        public BatchGradientResult(
            float[][] inputGradients,
            float evidenceLossSum,
            float relevanceLossSum,
            float categoryLossSum,
            float evidenceProbSum,
            float relevanceProbSum,
            float evidenceLogitSum,
            float relevanceLogitSum,
            float gradSqSum,
            float maxAbsGrad)
        {
            InputGradients = inputGradients;
            EvidenceLossSum = evidenceLossSum;
            RelevanceLossSum = relevanceLossSum;
            CategoryLossSum = categoryLossSum;
            EvidenceProbSum = evidenceProbSum;
            RelevanceProbSum = relevanceProbSum;
            EvidenceLogitSum = evidenceLogitSum;
            RelevanceLogitSum = relevanceLogitSum;
            GradSqSum = gradSqSum;
            MaxAbsGrad = maxAbsGrad;
        }

        public float[][] InputGradients { get; }
        public float EvidenceLossSum { get; }
        public float RelevanceLossSum { get; }
        public float CategoryLossSum { get; }
        public float EvidenceProbSum { get; }
        public float RelevanceProbSum { get; }
        public float EvidenceLogitSum { get; }
        public float RelevanceLogitSum { get; }
        public float GradSqSum { get; }
        public float MaxAbsGrad { get; }
    }

    /// <summary>
    /// Runs one mini-batch mixed-head backward on Torch and accumulates into
    /// <paramref name="gradients"/> (caller must <c>Clear</c> first). Returns false on
    /// unsupported topologies or LibTorch errors so callers fall back to Math.NET.
    /// </summary>
    public static bool TryAccumulate(
        NeuralNetwork network,
        float[][] encodedInputs,
        IReadOnlyList<ChatMonitoringNeuralModelTrainingExample> batch,
        int[] categoryIndices,
        NeuralNetworkGradientBuffers gradients,
        bool computeInputGradients,
        out BatchGradientResult result)
    {
        result = default;
        if (!NeuralTorchRuntime.TryEnsureReady())
            return false;
        if (encodedInputs.Length == 0 || encodedInputs.Length != batch.Count || encodedInputs.Length != categoryIndices.Length)
            return false;
        if (network.Layers.Count == 0)
            return false;
        if (network.Layers[^1].Activation != NeuralLayerActivation.MixedEvidenceRelevanceSoftmax)
            return false;

        for (int layer = 0; layer < network.Layers.Count - 1; layer++)
        {
            NeuralLayerActivation activation = network.Layers[layer].Activation;
            if (activation is not (NeuralLayerActivation.LeakyRelu or NeuralLayerActivation.Tanh))
                return false;
        }

        BatchGradientResult? captured = null;
        bool ok = NeuralTorchAcceleratorGuard.TryRun(
            () =>
            {
                captured = AccumulateCore(
                    network,
                    encodedInputs,
                    batch,
                    categoryIndices,
                    gradients,
                    computeInputGradients);
            },
            onFailure: _ => gradients.Clear());

        if (!ok || captured is null)
        {
            gradients.Clear();
            return false;
        }

        result = captured.Value;
        return true;
    }

    private static BatchGradientResult AccumulateCore(
        NeuralNetwork network,
        float[][] encodedInputs,
        IReadOnlyList<ChatMonitoringNeuralModelTrainingExample> batch,
        int[] categoryIndices,
        NeuralNetworkGradientBuffers gradients,
        bool computeInputGradients)
    {
        Device device = NeuralTorchRuntime.ResolveDevice();
        int batchSize = encodedInputs.Length;
        int inputSize = network.InputSize;
        int categoryCount = network.CategoryLabels.Count;
        float leakySlope = NeuralNetwork.DefaultLeakyReluSlope;

        using DisposeScope scope = NewDisposeScope();

        float[] flatInputs = new float[batchSize * inputSize];
        float[] evidenceTargets = new float[batchSize];
        float[] relevanceTargets = new float[batchSize];
        long[] categoryLong = new long[batchSize];
        for (int sample = 0; sample < batchSize; sample++)
        {
            float[] row = encodedInputs[sample];
            if (row.Length != inputSize)
                throw new InvalidOperationException($"Encoded input width {row.Length} != {inputSize}.");
            Buffer.BlockCopy(row, 0, flatInputs, sample * inputSize * sizeof(float), inputSize * sizeof(float));
            evidenceTargets[sample] = Math.Clamp(batch[sample].Targets.Evidence, 0f, 1f);
            relevanceTargets[sample] = Math.Clamp(batch[sample].Targets.Relevance, 0f, 1f);
            categoryLong[sample] = Math.Clamp(categoryIndices[sample], 0, Math.Max(0, categoryCount - 1));
        }

        Tensor inputLeaf = tensor(flatInputs, new long[] { batchSize, inputSize })
            .to(device)
            .requires_grad_(computeInputGradients);
        Tensor activation = inputLeaf;
        List<Tensor> weightParameters = new(network.Layers.Count);
        List<Tensor> biasParameters = new(network.Layers.Count);
        Tensor? logits = null;

        for (int layer = 0; layer < network.Layers.Count; layer++)
        {
            DenseLayer dense = network.Layers[layer];
            int rows = dense.TargetCount;
            int cols = dense.SourceCount;
            // WeightStorage is column-major W[r,c]; reshape to [cols,rows] then transpose → [out,in].
            Tensor weight = tensor(dense.WeightStorage, new long[] { cols, rows })
                .t()
                .contiguous()
                .to(device)
                .requires_grad_(true);
            Tensor bias = tensor(dense.BiasStorage, new long[] { rows })
                .to(device)
                .requires_grad_(true);
            weightParameters.Add(weight);
            biasParameters.Add(bias);

            Tensor preActivation = F.linear(activation, weight, bias);
            bool outputLayer = layer == network.Layers.Count - 1;
            if (outputLayer)
            {
                logits = preActivation;
                break;
            }

            activation = dense.Activation switch
            {
                NeuralLayerActivation.LeakyRelu => F.leaky_relu(preActivation, negative_slope: leakySlope),
                NeuralLayerActivation.Tanh => tanh(preActivation),
                _ => throw new InvalidOperationException($"Unsupported hidden activation {dense.Activation}."),
            };
        }

        if (logits is null)
            throw new InvalidOperationException("Torch mixed-head forward did not produce logits.");

        Tensor evidenceLogits = logits.select(1, 0);
        Tensor relevanceLogits = logits.select(1, 1);
        Tensor evidenceTargetsTensor = tensor(evidenceTargets).to(device);
        Tensor relevanceTargetsTensor = tensor(relevanceTargets).to(device);

        // Sum reduction matches Math.NET AccumulateMixedHeadGradients (ApplyMomentumUpdate divides by n).
        Tensor evidenceLoss = F.binary_cross_entropy_with_logits(
            evidenceLogits, evidenceTargetsTensor, reduction: Reduction.Sum);
        Tensor relevanceLoss = F.binary_cross_entropy_with_logits(
            relevanceLogits, relevanceTargetsTensor, reduction: Reduction.Sum);
        Tensor totalLoss = evidenceLoss + relevanceLoss;
        Tensor categoryLoss = tensor(0f).to(device);
        if (categoryCount > 0)
        {
            Tensor categoryLogits = logits.narrow(1, 2, categoryCount);
            Tensor categoryTargets = tensor(categoryLong).to(device);
            categoryLoss = F.cross_entropy(categoryLogits, categoryTargets, reduction: Reduction.Sum);
            totalLoss = totalLoss + categoryLoss;
        }
        totalLoss.backward();

        float evidenceLossSum = evidenceLoss.item<float>();
        float relevanceLossSum = relevanceLoss.item<float>();
        float categoryLossSum = categoryCount > 0 ? categoryLoss.item<float>() : 0f;

        Tensor evidenceProbs = sigmoid(evidenceLogits.detach());
        Tensor relevanceProbs = sigmoid(relevanceLogits.detach());
        float evidenceProbSum = evidenceProbs.sum().item<float>();
        float relevanceProbSum = relevanceProbs.sum().item<float>();
        float evidenceLogitSum = evidenceLogits.detach().sum().item<float>();
        float relevanceLogitSum = relevanceLogits.detach().sum().item<float>();

        float gradSqSum = 0f;
        float maxAbsGrad = 0f;
        for (int layer = 0; layer < network.Layers.Count; layer++)
        {
            Tensor weightGrad = weightParameters[layer].grad
                ?? throw new InvalidOperationException($"Missing weight.grad for layer {layer}.");
            Tensor biasGrad = biasParameters[layer].grad
                ?? throw new InvalidOperationException($"Missing bias.grad for layer {layer}.");

            using Tensor weightGradColumnMajor = weightGrad.t().contiguous().cpu();
            using Tensor biasGradCpu = biasGrad.contiguous().cpu();
            CopyAccessor(weightGradColumnMajor.data<float>(), gradients.WeightGradientStorage[layer]);
            CopyAccessor(biasGradCpu.data<float>(), gradients.BiasGradientStorage[layer]);
            AccumulateMagnitude(gradients.WeightGradientStorage[layer], ref gradSqSum, ref maxAbsGrad);
            AccumulateMagnitude(gradients.BiasGradientStorage[layer], ref gradSqSum, ref maxAbsGrad);
        }

        float[][] inputGradients = new float[batchSize][];
        if (computeInputGradients)
        {
            Tensor inputGradTensor = inputLeaf.grad
                ?? throw new InvalidOperationException("Missing input.grad for cascade chain-rule.");
            using Tensor inputGradCpu = inputGradTensor.contiguous().cpu();
            TensorAccessor<float> accessor = inputGradCpu.data<float>();
            for (int sample = 0; sample < batchSize; sample++)
            {
                float[] row = new float[inputSize];
                long offset = (long)sample * inputSize;
                for (int feature = 0; feature < inputSize; feature++)
                    row[feature] = accessor[offset + feature];
                inputGradients[sample] = row;
            }
        }
        else
        {
            for (int sample = 0; sample < batchSize; sample++)
                inputGradients[sample] = [];
        }

        return new BatchGradientResult(
            inputGradients,
            evidenceLossSum,
            relevanceLossSum,
            categoryLossSum,
            evidenceProbSum,
            relevanceProbSum,
            evidenceLogitSum,
            relevanceLogitSum,
            gradSqSum,
            maxAbsGrad);
    }

    private static void CopyAccessor(TensorAccessor<float> source, float[] destination)
    {
        if (source.Count != destination.Length)
            throw new InvalidOperationException($"Gradient length mismatch {source.Count} vs {destination.Length}.");
        for (long index = 0; index < source.Count; index++)
            destination[index] = source[index];
    }

    private static void AccumulateMagnitude(float[] values, ref float gradSqSum, ref float maxAbsGrad)
    {
        for (int index = 0; index < values.Length; index++)
        {
            float value = values[index];
            gradSqSum += value * value;
            float absolute = MathF.Abs(value);
            if (absolute > maxAbsGrad)
                maxAbsGrad = absolute;
        }
    }
}
