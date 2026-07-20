using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class TicketStudentReplayTests
{
    [Fact]
    public void Snapshot_round_trip_preserves_prediction()
    {
        TicketStudentModel source = new();
        StudentTrainingExample example = new("Monitor math tutoring.", "The quadratic formula is useful here.", .8, .9, "tutoring");
        source.Train(example, 8);
        NeuralNetParameterSnapshot snapshot = source.GetParameterSnapshot(4, 8);
        TicketStudentModel restored = new();
        restored.LoadParameterSnapshot(snapshot);
        TicketStudentPrediction expected = source.Predict(example.Requirement, example.Message);
        TicketStudentPrediction actual = restored.Predict(example.Requirement, example.Message);
        Assert.Equal(expected.EvidenceScore, actual.EvidenceScore);
        Assert.Equal(expected.Relevance, actual.Relevance);
    }

    [Fact]
    public void Trace_contains_real_epoch_updates_and_fixed_topology()
    {
        TicketStudentModel model = new();
        StudentTrainingExample example = new("Watch for cussing.", "That was a rude curse.", .95, .9, "moderation");
        TrainingPassTrace trace = model.TrainWithTrace(example, 2);
        NeuralNetTopologySnapshot topology = model.GetTopologySnapshot();
        Assert.Equal(2, trace.Iterations.Count);
        Assert.Equal(266, topology.Nodes.Count);
        Assert.Equal(2064, topology.Edges.Count);
        Assert.All(trace.Iterations, iteration => Assert.NotEmpty(iteration.Update.Parameters));
    }

    [Fact]
    public void Community_sampling_is_deterministic_and_bounded()
    {
        SyntheticCommunityIntent intent = new(.9f, 30, .1f, ["helpful"]);
        SyntheticCommunityResolution first = SyntheticCommunitySignalResolver.Resolve(intent, .7f, .8f, .6f, .9f, 123);
        SyntheticCommunityResolution second = SyntheticCommunitySignalResolver.Resolve(intent, .7f, .8f, .6f, .9f, 123);
        Assert.Equal(first.Sampling, second.Sampling);
        Assert.InRange(first.ResolvedEvidence, .5f, .7f);
    }

    [Fact]
    public void Promotion_claim_reclaims_only_expired_or_unleased_work()
    {
        DateTime now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(NeuralNetTrainingPromoter.CanClaim("Pending", null, now));
        Assert.True(NeuralNetTrainingPromoter.CanClaim("RetryPending", null, now));
        Assert.True(NeuralNetTrainingPromoter.CanClaim("Promoting", now.AddSeconds(-1), now));
        Assert.False(NeuralNetTrainingPromoter.CanClaim("Promoting", now.AddMinutes(1), now));
        Assert.False(NeuralNetTrainingPromoter.CanClaim("Promoted", null, now));
    }
}
