using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class TicketConfidenceScoringTests
{
    [Fact]
    public void Strong_relevant_evidence_increases_by_configured_limit()
    {
        TicketConfidenceUpdate update = TicketConfidenceScoring.Update(0.5, 1, 1, 0.15);

        Assert.Equal(0.5, update.PreviousScore, precision: 10);
        Assert.Equal(0.15, update.ScoreDelta, precision: 10);
        Assert.Equal(0.65, update.CurrentScore, precision: 10);
    }

    [Fact]
    public void Strong_relevant_counterevidence_decreases_by_configured_limit()
    {
        TicketConfidenceUpdate update = TicketConfidenceScoring.Update(0.5, 0, 1, 0.15);

        Assert.Equal(-0.15, update.ScoreDelta, precision: 10);
        Assert.Equal(0.35, update.CurrentScore, precision: 10);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void Neutral_or_irrelevant_evidence_does_not_move_score(double evidence, double relevance)
    {
        TicketConfidenceUpdate update = TicketConfidenceScoring.Update(0.72, evidence, relevance, 0.15);

        Assert.Equal(0, update.ScoreDelta, precision: 10);
        Assert.Equal(0.72, update.CurrentScore, precision: 10);
    }

    [Fact]
    public void Score_and_actual_delta_are_clamped_at_boundary()
    {
        TicketConfidenceUpdate update = TicketConfidenceScoring.Update(0.96, 1, 1, 0.15);

        Assert.Equal(0.04, update.ScoreDelta, precision: 10);
        Assert.Equal(1, update.CurrentScore, precision: 10);
    }

    [Fact]
    public void Evaluation_parser_clamps_values_and_truncates_reason()
    {
        string json = $$"""{"evidenceConfidence":1.4,"relevance":-0.2,"reason":"{{new string('x', 550)}}"}""";

        bool parsed = TicketEvidenceEvaluation.TryParse(json, out TicketEvidenceEvaluation evaluation);

        Assert.True(parsed);
        Assert.Equal(1, evaluation.EvidenceConfidence);
        Assert.Equal(0, evaluation.Relevance);
        Assert.Equal(500, evaluation.Reason.Length);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"evidenceConfidence\":0.5}")]
    [InlineData("{not-json}")]
    public void Evaluation_parser_rejects_missing_or_invalid_numbers(string json)
    {
        Assert.False(TicketEvidenceEvaluation.TryParse(json, out _));
    }
}
