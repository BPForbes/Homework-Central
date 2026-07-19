using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class TicketStudentModelTests
{
    [Fact]
    public void Training_moves_matching_example_toward_approved_target()
    {
        TicketStudentModel model = new();
        StudentTrainingExample example = new(
            "Monitor for spam.", "free prize click this link repeatedly", .98, .95, "spam");
        double before = model.Predict(example.Requirement, example.Message).EvidenceScore;

        model.Train(example, 100);
        TicketStudentPrediction after = model.Predict(example.Requirement, example.Message);

        Assert.True(after.EvidenceScore > before);
        Assert.InRange(after.EvidenceScore, 0, 1);
        Assert.InRange(after.Confidence, 0, 1);
        Assert.Equal("spam", after.Category);
    }

    [Fact]
    public void Identically_trained_models_are_deterministic()
    {
        TicketStudentModel first = new();
        TicketStudentModel second = new();
        StudentTrainingExample example = new("Monitor threats.", "I will hurt you.", .99, .99, "threat");
        first.Train(example, 30);
        second.Train(example, 30);

        Assert.Equal(
            first.Predict(example.Requirement, example.Message),
            second.Predict(example.Requirement, example.Message));
        Assert.Equal(first.Embed(example.Message), second.Embed(example.Message));
    }

    [Fact]
    public void Low_confidence_always_uses_reviewer_and_blending_is_bounded()
    {
        Assert.True(TicketReviewPolicy.ShouldReview(.4, Guid.Empty, .75, 0));
        Assert.False(TicketReviewPolicy.ShouldReview(.9, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), .75, 0));
        Assert.Equal(.8, TicketReviewPolicy.Blend(.1, 1, 7d / 9d), precision: 10);
    }

    [Fact]
    public void Reviewer_parser_accepts_structured_correction()
    {
        const string json = """{"reviewerScore":0.9,"reviewerConfidence":0.8,"relevance":0.7,"correctionNeeded":true,"explanation":"clear spam","guidance":"weight repeated links"}""";

        Assert.True(TicketReviewerEvaluation.TryParse(json, out TicketReviewerEvaluation review));
        Assert.True(review.CorrectionNeeded);
        Assert.Equal(.9, review.ReviewerScore);
    }
}
