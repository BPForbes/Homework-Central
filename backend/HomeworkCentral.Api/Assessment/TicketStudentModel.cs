using HomeworkCentral.Api.Models;

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

public interface ITicketStudentModel
{
    TicketStudentPrediction Predict(string requirement, string message);
    IReadOnlyList<float> Embed(string text);
    void Train(StudentTrainingExample example, int epochs = 12);
}

/// <summary>
/// A deliberately tiny, CPU-only neural student: 256 hashed text inputs, one
/// 8-neuron hidden layer, and evidence/relevance outputs. Its weights occupy
/// roughly 17 KB and inference does not allocate a dense feature vector.
/// </summary>
public sealed class TicketStudentModel : ITicketStudentModel
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
        AddTokens(result, "requirement " + requirement, size, 0.65f);
        AddTokens(result, "message " + message, size, 1f);
        return result;
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
