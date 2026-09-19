namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Selects the held-out slice of approved training examples.
///
/// Membership is derived from the example's own id rather than stored on the row, so the split is
/// stable across processes, machines and restarts without a schema change, and an example can
/// never drift between train and holdout as rows are added. That stability is the whole point: a
/// holdout that reshuffles is not a measurement.
///
/// <see cref="System.Guid.GetHashCode"/> is deliberately not used — it is randomised per process,
/// so it would place the same example in different buckets on different runs.
/// </summary>
public static class ChatMonitoringHoldout
{
    /// <summary>Share of approved examples reserved for evaluation and never trained on.</summary>
    public const int HoldoutPercent = 15;

    /// <summary>
    /// Below this many held-out examples the measured difference between two models is noise, so
    /// <see cref="ChatMonitoringModelEvaluator"/> reports the comparison as inconclusive and the
    /// promoter lets the promotion through rather than blocking every publish on a young dataset.
    /// </summary>
    public const int MinimumForGating = 20;

    public static bool IsHeldOut(Guid trainingExampleId) => Bucket(trainingExampleId) < HoldoutPercent;

    public static bool IsTrainable(Guid trainingExampleId) => !IsHeldOut(trainingExampleId);

    /// <summary>FNV-1a over the id's bytes: cheap, and identical on every runtime and machine.</summary>
    private static int Bucket(Guid trainingExampleId)
    {
        Span<byte> bytes = stackalloc byte[16];
        trainingExampleId.TryWriteBytes(bytes);

        uint hash = 2166136261;
        foreach (byte value in bytes)
            hash = (hash ^ value) * 16777619;

        return (int)(hash % 100);
    }
}

/// <summary>
/// Held-out scores for one model. <see cref="Fitness"/> is the single number the promotion gate
/// compares; it is in 0..1 and higher is better, so a regression is simply a lower value.
/// </summary>
public sealed record ChatMonitoringEvaluation(
    int ExampleCount,
    double EvidenceMeanAbsoluteError,
    double RelevanceMeanAbsoluteError)
{
    public static readonly ChatMonitoringEvaluation Empty = new(0, 0, 0);

    public double Fitness => ExampleCount == 0
        ? 0
        : 1 - ((EvidenceMeanAbsoluteError + RelevanceMeanAbsoluteError) / 2);

    /// <summary>True when there are too few held-out examples for a comparison to mean anything.</summary>
    public bool IsInconclusive => ExampleCount < ChatMonitoringHoldout.MinimumForGating;
}

/// <summary>
/// Scores a model against held-out examples it was never trained on.
///
/// Only evidence and relevance are measured. Both are supervised on every example and are exactly
/// the values the ticket pipeline blends into a confidence score, so a regression here is a
/// regression a user would feel. Category accuracy is deliberately excluded for now: category is
/// about to move from a hard index to a soft distribution, and a metric that changes meaning
/// mid-migration cannot be compared across generations.
/// </summary>
public static class ChatMonitoringModelEvaluator
{
    public static ChatMonitoringEvaluation Evaluate(
        IChatMonitoringNeuralModel model,
        IReadOnlyList<ChatMonitoringEvaluationExample> heldOut)
    {
        if (heldOut.Count == 0)
            return ChatMonitoringEvaluation.Empty;

        double evidenceError = 0;
        double relevanceError = 0;
        foreach (ChatMonitoringEvaluationExample example in heldOut)
        {
            ChatMonitoringNeuralModelPrediction prediction = model.Predict(example.Input);
            evidenceError += Math.Abs(prediction.Evidence - example.ExpectedEvidence);
            relevanceError += Math.Abs(prediction.Relevance - example.ExpectedRelevance);
        }

        return new ChatMonitoringEvaluation(
            heldOut.Count,
            evidenceError / heldOut.Count,
            relevanceError / heldOut.Count);
    }

    /// <summary>
    /// Whether a candidate may replace the incumbent. A candidate must not be materially worse;
    /// equal or better passes, and so does a first model with no incumbent to compare against.
    /// <paramref name="tolerance"/> absorbs the run-to-run jitter of SGD so an unchanged model is
    /// not rejected for noise.
    /// </summary>
    public static bool IsRegression(
        ChatMonitoringEvaluation candidate,
        ChatMonitoringEvaluation incumbent,
        double tolerance = 0.02)
    {
        if (candidate.IsInconclusive || incumbent.ExampleCount == 0)
            return false;

        return candidate.Fitness < incumbent.Fitness - tolerance;
    }
}

/// <summary>One held-out example: the model input and the approved targets it should reproduce.</summary>
public sealed record ChatMonitoringEvaluationExample(
    ChatMonitoringNeuralModelInput Input,
    float ExpectedEvidence,
    float ExpectedRelevance);
