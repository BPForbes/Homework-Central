using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class ChatMonitoringNeuralModelTorchSharpTests
{
    [Fact]
    public void Checkpoint_round_trip_preserves_moderation_prediction()
    {
        using ModerationChatMonitorNeuralNet source = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for cussing.", "Prior conduct was reported.", "That was a rude curse.", 0, .9f, .4f, .5f);
        source.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f), 8);
        NeuralNetParameterSnapshot snapshot = source.GetParameterSnapshot(4, 8);
        using ModerationChatMonitorNeuralNet restored = new();
        restored.LoadParameterSnapshot(snapshot);
        ChatMonitoringNeuralModelPrediction expected = source.Predict(input);
        ChatMonitoringNeuralModelPrediction actual = restored.Predict(input);
        Assert.Equal(expected.Evidence, actual.Evidence);
        Assert.Equal(expected.Relevance, actual.Relevance);
    }

    [Fact]
    public void Specialized_models_have_separate_fixed_topologies_and_real_updates()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelInput input = new("Monitor math tutoring.", "A student asked for help.", "The quadratic formula is useful here.", .2f, .9f, .5f, .4f);
        ChatMonitoringNeuralModelTrainingExample example = new(input, new ChatMonitoringNeuralModelTargets(.8f, .9f), "tutoring");
        TrainingPassTrace trace = tutoring.TrainWithTrace(example, 2);
        NeuralNetTopologySnapshot moderationTopology = moderation.GetTopologySnapshot();
        NeuralNetTopologySnapshot tutoringTopology = tutoring.GetTopologySnapshot();
        Assert.Equal(2, trace.Iterations.Count);
        Assert.Equal(150, tutoringTopology.Nodes.Count);
        Assert.Equal(142, moderationTopology.Nodes.Count);
        Assert.NotEqual(moderationTopology.ModelVersion, tutoringTopology.ModelVersion);
        Assert.Contains(tutoringTopology.Nodes, node => node.LayerId == "learning-thread-history");
        Assert.Contains(moderationTopology.Nodes, node => node.LayerId == "behavior-history");
        Assert.All(trace.Iterations, iteration => Assert.NotEmpty(iteration.Update.Parameters));
    }

    [Fact]
    public void Factory_resolves_independent_models_for_both_mode()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelFactory factory = new(moderation, tutoring);
        IReadOnlyList<IChatMonitoringNeuralModel> models = factory.Resolve(NeuralTrainingMode.Both);
        Assert.Equal(2, models.Count);
        Assert.NotSame(models[0], models[1]);
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, models[0].Kind);
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, models[1].Kind);
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
