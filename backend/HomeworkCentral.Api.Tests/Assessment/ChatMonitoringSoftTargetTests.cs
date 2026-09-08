using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

/// <summary>
/// Soft category targets. The load-bearing property is equivalence: a one-hot distribution must
/// train identically to the hard index it replaces, because that is what makes the change safe to
/// adopt gradually — existing hard-labelled examples keep behaving exactly as before while soft
/// ones start carrying more signal.
/// </summary>
public class ChatMonitoringSoftTargetTests
{
    [Fact]
    public void A_one_hot_distribution_trains_identically_to_the_hard_index()
    {
        int category = ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment");

        float[] oneHot = new float[ChatMonitoringCategoryTaxonomy.Moderation.Length];
        oneHot[category] = 1f;

        NeuralNetParameterSnapshot hardTrained = TrainOnce(new(.9f, .8f, category));
        NeuralNetParameterSnapshot softTrained = TrainOnce(new(.9f, .8f, category, oneHot));

        // Same seed, same input, same arithmetic — the packed parameters must match bit for bit.
        Assert.Equal(hardTrained.Checksum, softTrained.Checksum);
    }

    [Fact]
    public void An_unnormalised_one_hot_is_rescaled_rather_than_scaling_the_gradient()
    {
        int category = ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment");

        float[] inflated = new float[ChatMonitoringCategoryTaxonomy.Moderation.Length];
        inflated[category] = 7.5f;

        NeuralNetParameterSnapshot hardTrained = TrainOnce(new(.9f, .8f, category));
        NeuralNetParameterSnapshot inflatedTrained = TrainOnce(new(.9f, .8f, category, inflated));

        // Without normalisation a 7.5 target would inflate the category head's effective learning
        // rate; normalised, it is the same one-hot.
        Assert.Equal(hardTrained.Checksum, inflatedTrained.Checksum);
    }

    [Fact]
    public void A_genuinely_soft_target_trains_differently_from_its_argmax()
    {
        int harassment = ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment");
        int general = ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "moderation-general");

        float[] soft = new float[ChatMonitoringCategoryTaxonomy.Moderation.Length];
        soft[harassment] = .7f;
        soft[general] = .3f;

        NeuralNetParameterSnapshot hardTrained = TrainOnce(new(.9f, .8f, harassment));
        NeuralNetParameterSnapshot softTrained = TrainOnce(new(.9f, .8f, harassment, soft));

        // If these matched, the distribution would be being discarded somewhere.
        Assert.NotEqual(hardTrained.Checksum, softTrained.Checksum);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void An_unusable_distribution_falls_back_to_the_hard_index(float weight)
    {
        int category = ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment");

        float[] unusable = new float[ChatMonitoringCategoryTaxonomy.Moderation.Length];
        Array.Fill(unusable, weight);

        NeuralNetParameterSnapshot hardTrained = TrainOnce(new(.9f, .8f, category));
        NeuralNetParameterSnapshot degenerateTrained = TrainOnce(new(.9f, .8f, category, unusable));

        // A teacher that says nothing must not be trained on; the hard label still applies.
        Assert.Equal(hardTrained.Checksum, degenerateTrained.Checksum);
    }

    [Fact]
    public void A_shorter_distribution_than_the_taxonomy_is_still_usable()
    {
        int category = ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment");

        // A teacher that only names the first few categories, as a sparse emission would.
        float[] truncated = new float[category + 1];
        truncated[category] = 1f;

        NeuralNetParameterSnapshot hardTrained = TrainOnce(new(.9f, .8f, category));
        NeuralNetParameterSnapshot truncatedTrained = TrainOnce(new(.9f, .8f, category, truncated));

        Assert.Equal(hardTrained.Checksum, truncatedTrained.Checksum);
    }

    /// <summary>
    /// Trains a fresh, identically-seeded model on one example and returns its weights.
    ///
    /// Full trace detail is not incidental: it pins training to the in-process path. Soft targets
    /// always take that path (TorchSharp's cross_entropy here is called with hard class indices),
    /// so comparing a soft run against a Torch-accelerated hard run would be comparing backends,
    /// not targets — the two agree mathematically but not bit for bit.
    /// </summary>
    private static NeuralNetParameterSnapshot TrainOnce(ChatMonitoringNeuralModelTargets targets)
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new(
            "Monitor for harassment.",
            "Repeated insults in thread.",
            "You are worthless.",
            0,
            1f,
            .6f,
            .5f);

        _ = model.TrainWithTrace(
            new ChatMonitoringNeuralModelTrainingExample(input, targets, "general"),
            epochs: 4,
            detail: NeuralTrainingTraceDetail.Full);

        return model.GetParameterSnapshot(null, 1);
    }
}
