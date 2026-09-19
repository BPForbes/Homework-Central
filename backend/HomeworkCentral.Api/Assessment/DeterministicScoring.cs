namespace HomeworkCentral.Api.Assessment;

public sealed record RubricWeights(
    double Correctness = 0.35,
    double Reasoning = 0.20,
    double Pedagogy = 0.25,
    double Relevance = 0.10,
    double Professionalism = 0.10);

public sealed record RubricEvaluation(
    double Correctness,
    double Reasoning,
    double Pedagogy,
    double Relevance,
    double Communication,
    double Professionalism,
    double EvaluatorConfidence,
    IReadOnlyList<string> CriticalErrors);

public static class DeterministicScoring
{
    public static double ResponseQuality(RubricEvaluation e, RubricWeights? weights = null)
    {
        RubricWeights w = weights ?? new RubricWeights();
        double professionalismAndComm = (e.Communication + e.Professionalism) / 2.0;
        return Clamp01(
            w.Correctness * e.Correctness
            + w.Reasoning * e.Reasoning
            + w.Pedagogy * e.Pedagogy
            + w.Relevance * e.Relevance
            + w.Professionalism * professionalismAndComm);
    }

    public static double EvidenceWeight(double difficulty, double relevance, double confidence, double authenticity) =>
        Clamp01(difficulty) * Clamp01(relevance) * Clamp01(confidence) * Clamp01(authenticity);

    public static double CommunityLambda(double reliableReviewerWeight, double lambdaMax = 0.55, double k = 8.0) =>
        lambdaMax * (1.0 - Math.Exp(-Math.Max(0, reliableReviewerWeight) / k));

    public static double CombineScores(double llmScore, double? communityScore, double lambda)
    {
        if (communityScore is null)
            return Clamp01(llmScore);
        return Clamp01((1 - lambda) * llmScore + lambda * communityScore.Value);
    }

    public static void UpdateBeta(ref double alpha, ref double beta, double membershipWeight, double combinedScore)
    {
        double w = Math.Max(0, membershipWeight);
        double s = Clamp01(combinedScore);
        alpha += w * s;
        beta += w * (1 - s);
    }

    public static double Mean(double alpha, double beta) =>
        alpha + beta <= 0 ? 0.5 : alpha / (alpha + beta);

    public static double EvidenceVolume(double alpha, double beta, double alpha0 = 1, double beta0 = 1) =>
        Math.Max(0, alpha + beta - alpha0 - beta0);

    public static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}
