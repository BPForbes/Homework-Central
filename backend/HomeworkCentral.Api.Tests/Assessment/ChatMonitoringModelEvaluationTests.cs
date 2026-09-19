using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

/// <summary>
/// The promotion gate's arithmetic. These are pure — no database, no Ollama — because the point of
/// the gate is that it is the one thing that must be trustworthy when everything else is uncertain.
/// </summary>
public class ChatMonitoringModelEvaluationTests
{
    [Fact]
    public void Holdout_membership_is_stable_for_the_same_id()
    {
        Guid id = Guid.Parse("6f2c1c2e-6f3d-4a55-9a2b-0f1d2e3c4b5a");

        // The split must survive process restarts and different machines, so it cannot depend on
        // Guid.GetHashCode, which is randomised per process.
        Assert.Equal(ChatMonitoringHoldout.IsHeldOut(id), ChatMonitoringHoldout.IsHeldOut(id));
        Assert.NotEqual(ChatMonitoringHoldout.IsHeldOut(id), ChatMonitoringHoldout.IsTrainable(id));
    }

    [Fact]
    public void Holdout_takes_roughly_the_configured_share()
    {
        List<Guid> ids = [];
        for (int i = 0; i < 4000; i++)
            ids.Add(Guid.NewGuid());

        int heldOut = ids.Count(ChatMonitoringHoldout.IsHeldOut);
        double share = heldOut * 100.0 / ids.Count;

        // Loose bounds: this asserts the hash spreads, not that it is perfectly uniform.
        Assert.InRange(share, ChatMonitoringHoldout.HoldoutPercent - 4, ChatMonitoringHoldout.HoldoutPercent + 4);
    }

    [Fact]
    public void A_perfect_model_scores_one_and_a_worthless_one_scores_lower()
    {
        ChatMonitoringEvaluation perfect = new(100, 0, 0);
        ChatMonitoringEvaluation poor = new(100, 0.5, 0.5);

        Assert.Equal(1.0, perfect.Fitness, precision: 6);
        Assert.Equal(0.5, poor.Fitness, precision: 6);
        Assert.True(perfect.Fitness > poor.Fitness);
    }

    [Fact]
    public void A_materially_worse_candidate_is_a_regression()
    {
        ChatMonitoringEvaluation incumbent = new(100, 0.10, 0.10);
        ChatMonitoringEvaluation candidate = new(100, 0.30, 0.30);

        Assert.True(ChatMonitoringModelEvaluator.IsRegression(candidate, incumbent));
    }

    [Fact]
    public void An_improvement_is_never_a_regression()
    {
        ChatMonitoringEvaluation incumbent = new(100, 0.30, 0.30);
        ChatMonitoringEvaluation candidate = new(100, 0.10, 0.10);

        Assert.False(ChatMonitoringModelEvaluator.IsRegression(candidate, incumbent));
    }

    [Fact]
    public void Jitter_within_tolerance_does_not_block_a_promotion()
    {
        ChatMonitoringEvaluation incumbent = new(100, 0.20, 0.20);
        // Fitness 0.79 against 0.80 — SGD run-to-run noise, not a real regression.
        ChatMonitoringEvaluation candidate = new(100, 0.21, 0.21);

        Assert.False(ChatMonitoringModelEvaluator.IsRegression(candidate, incumbent));
    }

    [Fact]
    public void A_first_model_with_no_incumbent_is_allowed_through()
    {
        ChatMonitoringEvaluation candidate = new(100, 0.9, 0.9);

        Assert.False(ChatMonitoringModelEvaluator.IsRegression(candidate, ChatMonitoringEvaluation.Empty));
    }

    [Fact]
    public void Too_few_held_out_examples_is_inconclusive_rather_than_blocking()
    {
        ChatMonitoringEvaluation incumbent = new(100, 0.10, 0.10);
        ChatMonitoringEvaluation candidate = new(ChatMonitoringHoldout.MinimumForGating - 1, 0.9, 0.9);

        Assert.True(candidate.IsInconclusive);
        // A young dataset must not wedge every promotion shut.
        Assert.False(ChatMonitoringModelEvaluator.IsRegression(candidate, incumbent));
    }

    [Fact]
    public void Evaluating_against_no_examples_yields_the_empty_result()
    {
        ChatMonitoringEvaluation evaluation =
            ChatMonitoringModelEvaluator.Evaluate(new StubModel(0.5f, 0.5f), []);

        Assert.Equal(0, evaluation.ExampleCount);
        Assert.Equal(0.0, evaluation.Fitness);
    }

    [Fact]
    public void Errors_are_averaged_across_the_held_out_examples()
    {
        // Model always answers 0.5; targets are 0.0 and 1.0, so mean absolute error is 0.5 on both
        // heads and fitness lands at 0.5.
        List<ChatMonitoringEvaluationExample> heldOut =
        [
            new(Input("first"), 0f, 0f),
            new(Input("second"), 1f, 1f),
        ];

        ChatMonitoringEvaluation evaluation =
            ChatMonitoringModelEvaluator.Evaluate(new StubModel(0.5f, 0.5f), heldOut);

        Assert.Equal(2, evaluation.ExampleCount);
        Assert.Equal(0.5, evaluation.EvidenceMeanAbsoluteError, precision: 5);
        Assert.Equal(0.5, evaluation.RelevanceMeanAbsoluteError, precision: 5);
        Assert.Equal(0.5, evaluation.Fitness, precision: 5);
    }

    private static ChatMonitoringNeuralModelInput Input(string message) =>
        new("requirement", string.Empty, message, 0, 1, 0, .5f);

    /// <summary>Fixed-output model so the metric arithmetic is asserted, not the network.</summary>
    private sealed class StubModel(float evidence, float relevance) : IChatMonitoringNeuralModel
    {
        public NeuralModelKindChatMonitoring Kind => NeuralModelKindChatMonitoring.Moderation;

        public ChatMonitoringNeuralModelPrediction Predict(ChatMonitoringNeuralModelInput input) =>
            new(evidence, relevance, 1f, Kind, "stub", "general", "stub reasoning");

        public void Train(ChatMonitoringNeuralModelInput input, ChatMonitoringNeuralModelTargets targets, int epochs = 12)
        {
        }
    }
}
