using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class SyntheticThreadScenarioGeneratorTests
{
    [Fact]
    public void BuildUserPrompt_ForcesExactModerationTarget()
    {
        string prompt = SyntheticThreadScenarioGenerator.BuildUserPrompt(
            NeuralTrainingMode.Moderation,
            hints: null,
            targetCategory: "doxxing");

        Assert.Contains("MUST set \"category\" exactly to \"doxxing\"", prompt);
        Assert.Contains("reportedConcept=doxxing", prompt);
        Assert.DoesNotContain("payment-solicitation", prompt);
    }

    [Fact]
    public void AlignScenarioToTarget_OverwritesDriftedCategory()
    {
        SyntheticThreadScenario drifted = new(
            "payment-solicitation",
            "Some free-form requirement",
            "context",
            [
                new(0, "u", "student", "general", "hello", false, 1f, new(.5f, 10, .5f, []), .9f, 1f, .5f, .7f),
            ]);

        SyntheticThreadScenario aligned = SyntheticThreadScenarioGenerator.AlignScenarioToTarget(
            drifted,
            NeuralTrainingMode.Moderation,
            "credential-theft");

        Assert.Equal("credential-theft", aligned.Category);
        Assert.Contains("reportedConcept=credential-theft", aligned.Requirement);
    }

    [Fact]
    public void CreateFallback_UsesTargetConceptInsteadOfPaymentSolicitation()
    {
        SyntheticThreadScenario scenario = SyntheticThreadScenarioGenerator.CreateFallback(
            NeuralTrainingMode.Moderation,
            "false-reporting");

        Assert.Equal("false-reporting", scenario.Category);
        Assert.Contains("reportedConcept=false-reporting", scenario.Requirement);
        Assert.DoesNotContain("send me $10", scenario.Messages[0].Content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("payment-solicitation")]
    [InlineData("staff-impersonation")]
    [InlineData("medical-misinformation")]
    [InlineData("moderation-general")]
    public void CreateFallback_AcceptsAllSoftmaxLabels(string slug)
    {
        SyntheticThreadScenario scenario = SyntheticThreadScenarioGenerator.CreateFallback(
            NeuralTrainingMode.Moderation,
            slug);

        Assert.Equal(slug, scenario.Category);
        Assert.NotEmpty(scenario.Messages);
    }
}
