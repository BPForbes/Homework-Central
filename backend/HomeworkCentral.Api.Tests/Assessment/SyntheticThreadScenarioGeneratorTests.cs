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
    public void BuildUserPrompt_FoldsEvaluatorObjectionIntoRevision()
    {
        string prompt = SyntheticThreadScenarioGenerator.BuildUserPrompt(
            NeuralTrainingMode.Moderation,
            hints: null,
            targetCategory: "doxxing",
            revisionNotes: "The thread never shows identifying information being published.");

        Assert.Contains("previous attempt was rejected by your own selfCritique", prompt);
        Assert.Contains("never shows identifying information", prompt);
        Assert.Contains("MUST set \"category\" exactly to \"doxxing\"", prompt);
    }

    [Fact]
    public void BuildUserPrompt_MentionsPriorSelfCritiqueNotes()
    {
        string prompt = SyntheticThreadScenarioGenerator.BuildUserPrompt(
            NeuralTrainingMode.Moderation,
            hints: ["REVISE on doxxing: add a concrete identifying leak"],
            targetCategory: "doxxing");

        Assert.Contains("Prior self-critique notes", prompt);
        Assert.Contains("add a concrete identifying leak", prompt);
    }

    [Fact]
    public void ParseScenario_ReadsEmbeddedSelfCritique()
    {
        const string json = """
            {
              "category": "doxxing",
              "requirement": "Monitor reportedConcept=doxxing",
              "initialContext": "A report about a shared address",
              "messages": [
                {
                  "authorId": "u1",
                  "authorRole": "student",
                  "channel": "lounge",
                  "content": "Here is their home address: 12 Oak St",
                  "isDistractor": false,
                  "channelRelevance": 1,
                  "expectedScore": 0.95,
                  "expectedRelevance": 1,
                  "proposedApproval": 0.1,
                  "proposedVoterCount": 12,
                  "controversy": 0.2,
                  "reasons": ["address shared"]
                }
              ],
              "selfCritique": {
                "verdict": "REVISE",
                "feedback": "Need a clearer non-distractor peer reaction."
              }
            }
            """;

        SyntheticThreadScenario? scenario = SyntheticThreadScenarioGenerator.ParseScenario(json);

        Assert.NotNull(scenario);
        Assert.Equal("REVISE", scenario!.SelfCritiqueVerdict);
        Assert.Contains("non-distractor peer reaction", scenario.SelfCritiqueFeedback);
    }

    [Fact]
    public void ParseScenario_ReadsSnakeCaseSelfCritique_Aliases()
    {
        const string json = """
            {
              "category": "tutoring-mathematics",
              "requirement": "Tutor math applicants",
              "initialContext": "Algebra help",
              "messages": [
                {
                  "authorId": "u1",
                  "authorRole": "student",
                  "channel": "math-help",
                  "content": "How do I solve 2x+3=11?",
                  "isDistractor": false,
                  "channelRelevance": 1,
                  "expectedScore": 0.9,
                  "expectedRelevance": 1,
                  "proposedApproval": 0.8,
                  "proposedVoterCount": 4,
                  "controversy": 0.1,
                  "reasons": ["on topic"]
                }
              ],
              "self_critique": {
                "verdict": "ok",
                "notes": "Clear non-distractor math ask."
              }
            }
            """;

        SyntheticThreadScenario? scenario = SyntheticThreadScenarioGenerator.ParseScenario(json);

        Assert.NotNull(scenario);
        Assert.Equal("LGTM", scenario!.SelfCritiqueVerdict);
        Assert.Contains("Clear non-distractor math ask", scenario.SelfCritiqueFeedback);
    }

    [Fact]
    public void CritiqueTicket_UsesGenerateEvaluateCopy_NotSecondModel()
    {
        SyntheticTicket ticket = new(
            "tutoring-science",
            "Tutor science",
            "Explain photosynthesis",
            "bio thread",
            0.8,
            0.9,
            [
                new SyntheticThreadMessage(
                    0, "u1", "student", "science-help", "Explain photosynthesis",
                    false, 1f, new(0.8f, 3, 0.2f, []), 0.8f, 0.9f, 0.8f, 0.7f),
            ]);
        NeuralNetTrainingLlmModule module = new(new SyntheticThreadScenarioGenerator(new StubLlm()));
        SyntheticEvaluatorResult result = module.CritiqueTicket(ticket);
        Assert.Equal("LGTM", result.Verdict);
        Assert.Contains("generate+evaluate", result.Feedback);
        Assert.DoesNotContain("second model", result.Feedback, StringComparison.OrdinalIgnoreCase);
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

    private sealed class StubLlm : ILlmClient
    {
        public Task<string?> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float>>([]);
    }
}
