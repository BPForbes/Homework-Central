using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class ChatMonitoringNeuralModelHashedMlpTests
{
    [Fact]
    public void Checkpoint_round_trip_preserves_moderation_prediction()
    {
        using ModerationChatMonitorNeuralNet source = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for cussing.", "Prior conduct was reported.", "That was a rude curse.", 0, .9f, .4f, .5f);
        source.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f, ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "profanity")), 8);
        NeuralNetParameterSnapshot snapshot = source.GetParameterSnapshot(4, 8);
        using ModerationChatMonitorNeuralNet restored = new();
        restored.LoadParameterSnapshot(snapshot);
        ChatMonitoringNeuralModelPrediction expected = source.Predict(input);
        ChatMonitoringNeuralModelPrediction actual = restored.Predict(input);
        Assert.Equal(expected.Evidence, actual.Evidence);
        Assert.Equal(expected.Relevance, actual.Relevance);
        Assert.Equal(expected.Category, actual.Category);
    }

    [Fact]
    public void Mini_batch_average_cost_uses_sample_count_and_moves_predictions()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelTrainingExample[] batch =
        [
            new(new("Monitor for harassment.", "Insults.", "You are worthless.", 0, 1f, .6f, .5f), new(.95f, .9f, 3), "harassment"),
            new(new("Monitor for profanity.", "Cussing.", "That was a damn insult.", 0, 1f, .5f, .5f), new(.9f, .85f, 1), "profanity"),
            new(new("Monitor for spam.", "Flood.", "Buy coins now buy coins now.", 0, .8f, .4f, .5f), new(.8f, .7f, 0), "spam"),
        ];
        TrainingPassTrace trace = model.TrainMiniBatchWithTrace(batch, epochs: 20, detail: NeuralTrainingTraceDetail.Compact);
        Assert.Equal(3, trace.BatchSize);
        Assert.All(trace.Iterations, iteration =>
        {
            Assert.Equal(3, iteration.LossBeforeUpdate.SampleCount);
            Assert.True(iteration.LossBeforeUpdate.TotalLoss > 0f);
            Assert.True(iteration.LossBeforeUpdate.CategoryLoss >= 0f);
        });
        Assert.True(trace.FinalAverageCost > 0f);
        ChatMonitoringNeuralModelPrediction after = model.Predict(batch[0].Input);
        Assert.True(after.Evidence > .5f);
        Assert.False(string.IsNullOrWhiteSpace(after.Category));
        Assert.True(after.CategoryConfidence > 0f);
    }

    [Fact]
    public void Softmax_category_head_is_present_in_topology()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        NeuralNetTopologySnapshot moderationTopology = moderation.GetTopologySnapshot();
        NeuralNetTopologySnapshot tutoringTopology = tutoring.GetTopologySnapshot();
        // 48+20+30+24+18+8 = 148 ; 48+20+32+28+20+6 = 154
        Assert.Equal(148, moderationTopology.Nodes.Count);
        Assert.Equal(154, tutoringTopology.Nodes.Count);
        Assert.Equal("hc-chat-monitoring-moderation-v3", moderationTopology.ModelVersion);
        Assert.Equal("hc-chat-monitoring-tutoring-v3", tutoringTopology.ModelVersion);
        Assert.Contains(moderationTopology.Nodes, node => node.Label == "harassment");
        Assert.Contains(tutoringTopology.Nodes, node => node.Label == "tutoring-math");
    }

    [Fact]
    public void Compact_training_records_loss_without_parameter_deltas()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for harassment.", "Repeated insults.", "You are worthless.", 0, 1f, .6f, .5f);
        TrainingPassTrace trace = model.TrainWithTrace(
            new(input, new ChatMonitoringNeuralModelTargets(.95f, .9f, 3), "harassment"),
            epochs: 40,
            detail: NeuralTrainingTraceDetail.Compact,
            evidenceTolerance: 0.2f,
            relevanceTolerance: 0.2f,
            lossStopThreshold: 1.2f);
        Assert.True(trace.Iterations.Count >= 1);
        Assert.True(trace.Iterations.Count <= 40);
        Assert.All(trace.Iterations, iteration => Assert.Empty(iteration.Update.Parameters));
        Assert.True(trace.Iterations[^1].LossAfterUpdate.TotalLoss > 0f);
    }

    [Fact]
    public void Lineage_vector_keys_are_stable_per_monitor()
    {
        Assert.Equal("chat-monitoring-moderation", ChatMonitoringVectorKeys.LineagePositionId(NeuralModelKindChatMonitoring.Moderation));
        Assert.Equal("chat-monitoring-tutoring", ChatMonitoringVectorKeys.LineagePositionId(NeuralModelKindChatMonitoring.Tutoring));
    }

    [Fact]
    public void Specialized_models_have_separate_fixed_topologies_and_real_updates()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelInput input = new("Monitor math tutoring.", "A student asked for help.", "The quadratic formula is useful here.", .2f, .9f, .5f, .4f);
        TrainingPassTrace trace = tutoring.TrainWithTrace(new(input, new ChatMonitoringNeuralModelTargets(.8f, .9f, 0), "tutoring-math"), 2);
        NeuralNetTopologySnapshot moderationTopology = moderation.GetTopologySnapshot();
        NeuralNetTopologySnapshot tutoringTopology = tutoring.GetTopologySnapshot();
        Assert.Equal(2, trace.Iterations.Count);
        Assert.Equal(154, tutoringTopology.Nodes.Count);
        Assert.Equal(148, moderationTopology.Nodes.Count);
        Assert.Contains(tutoringTopology.Nodes, node => node.LayerId == "learning-thread-history");
        Assert.Contains(moderationTopology.Nodes, node => node.LayerId == "behavior-history");
        Assert.All(trace.Iterations, iteration => Assert.NotEmpty(iteration.Update.Parameters));
    }

    [Fact]
    public void Factory_resolves_independent_models_for_training_modes()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelFactory factory = new(moderation, tutoring);
        IReadOnlyList<IChatMonitoringNeuralModel> both = factory.Resolve(NeuralTrainingMode.Both);
        IReadOnlyList<IChatMonitoringNeuralModel> moderationOnly = factory.Resolve(NeuralTrainingMode.Moderation);
        IReadOnlyList<IChatMonitoringNeuralModel> tutoringOnly = factory.Resolve(NeuralTrainingMode.Tutoring);
        Assert.Equal(2, both.Count);
        Assert.NotSame(both[0], both[1]);
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, both[0].Kind);
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, both[1].Kind);
        Assert.Single(moderationOnly);
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, moderationOnly[0].Kind);
        Assert.Single(tutoringOnly);
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, tutoringOnly[0].Kind);
    }

    [Fact]
    public void Moderation_and_tutoring_networks_do_not_share_weights()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for harassment.", "Repeated insults.", "You are worthless.", 0, 1f, .6f, .5f);
        moderation.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f, 3), 20);
        ChatMonitoringNeuralModelPrediction moderationAfter = moderation.Predict(input);
        ChatMonitoringNeuralModelPrediction tutoringUntrained = tutoring.Predict(input);
        Assert.NotEqual(moderationAfter.Evidence, tutoringUntrained.Evidence);
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, moderationAfter.ChatMonitoringKind);
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, tutoringUntrained.ChatMonitoringKind);
    }

    [Fact]
    public void Training_moves_predictions_toward_targets()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for harassment.", "Repeated insults in chat.", "You are worthless.", 0, 1f, .6f, .5f);
        ChatMonitoringNeuralModelPrediction before = model.Predict(input);
        model.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f, 3), 40);
        ChatMonitoringNeuralModelPrediction after = model.Predict(input);
        Assert.True(after.Evidence > before.Evidence);
        Assert.True(after.Relevance > before.Relevance);
    }

    [Fact]
    public void Support_similarity_raises_confidence_after_training()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for harassment and insults.", "Prior insults.", "You are worthless.", 0, 1f, .6f, .5f);
        ChatMonitoringNeuralModelPrediction before = model.Predict(input);
        model.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f, 3), 30);
        ChatMonitoringNeuralModelPrediction after = model.Predict(input);
        Assert.True(after.Confidence >= before.Confidence);
        Assert.False(string.IsNullOrWhiteSpace(after.Category));
        Assert.False(string.IsNullOrWhiteSpace(after.Reasoning));
    }

    [Fact]
    public void Ticket_context_routes_tutoring_filters_to_tutoring_monitor()
    {
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, ChatMonitoringTicketContext.ResolveKind("Tutor application competency", "learning"));
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, ChatMonitoringTicketContext.ResolveKind("Profanity filter", "moderation"));
    }

    [Fact]
    public void Community_sampling_and_promotion_leasing_are_deterministic()
    {
        SyntheticCommunityIntent intent = new(.9f, 30, .1f, ["helpful"]);
        SyntheticCommunityResolution first = SyntheticCommunitySignalResolver.Resolve(intent, .7f, .8f, .6f, .9f, 123);
        SyntheticCommunityResolution second = SyntheticCommunitySignalResolver.Resolve(intent, .7f, .8f, .6f, .9f, 123);
        DateTime now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(first.Sampling, second.Sampling);
        Assert.True(NeuralNetTrainingPromoter.CanClaim("Pending", null, now));
        Assert.True(NeuralNetTrainingPromoter.CanClaim("Promoting", now.AddSeconds(-1), now));
        Assert.False(NeuralNetTrainingPromoter.CanClaim("Promoted", null, now));
    }
}
