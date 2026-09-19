using System.Text.Json;
using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tickets;
using HomeworkCentral.Api.Tickets.Preface;

namespace HomeworkCentral.Api.Tests.Assessment;

public class TutorSubjectTextProcessorTests
{
    private static readonly ITicketPrefaceCheckResolver Resolver = new TicketPrefaceCheckResolver(
    [
        TutorSubjectPrefaceCheck.Instance,
        ModerationConceptPrefaceCheck.Instance,
    ]);

    [Theory]
    [InlineData("Rust")]
    [InlineData("rust")]
    [InlineData("RUst")]
    [InlineData("rUsT")]
    [InlineData("RUST")]
    public void Case_folding_maps_rust_to_computer_science(string input)
    {
        TutorSubjectTextProcessor.ProcessResult result = TutorSubjectTextProcessor.ProcessStrict(input);
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal([SubjectMaskNames.ComputerScience], result.GeneralMasks);
        Assert.Equal(["Rust"], result.ExpertiseLabels);
        Assert.Equal("Rust", result.CanonicalDisplay);
    }

    [Fact]
    public void Alias_biology_maps_to_science_and_keeps_expertise()
    {
        TutorSubjectTextProcessor.ProcessResult result = TutorSubjectTextProcessor.ProcessStrict("Biology");
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal([SubjectMaskNames.Science], result.GeneralMasks);
        Assert.Equal(["Biology"], result.ExpertiseLabels);
        Assert.Equal("Biology", result.CanonicalDisplay);
    }

    [Fact]
    public void Multi_subject_list_keeps_specifics_and_maps_generals()
    {
        TutorSubjectTextProcessor.ProcessResult result =
            TutorSubjectTextProcessor.ProcessStrict("biology, rust, Mathematics");
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(
            [SubjectMaskNames.Science, SubjectMaskNames.ComputerScience, SubjectMaskNames.Mathematics],
            result.GeneralMasks);
        Assert.Equal(["Biology", "Rust"], result.ExpertiseLabels);
        Assert.Equal("Biology, Rust, Mathematics", result.CanonicalDisplay);
    }

    [Fact]
    public void Spell_check_corrects_near_miss_typos()
    {
        TutorSubjectTextProcessor.ProcessResult result = TutorSubjectTextProcessor.ProcessStrict("biologoy");
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal([SubjectMaskNames.Science], result.GeneralMasks);
        Assert.Contains("Biology", result.ExpertiseLabels);
    }

    [Fact]
    public void Unverified_subject_asks_user_to_reenter()
    {
        TutorSubjectTextProcessor.ProcessResult result = TutorSubjectTextProcessor.ProcessStrict("UnderwaterBasketWeaving");
        Assert.False(result.Ok);
        Assert.Contains("re-enter", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Intake_validator_rejects_unknown_and_canonicalizes_known()
    {
        List<TicketIntakeQuestionDto> schema = DefaultTicketPortalPresets.TutorIntakeQuestions();
        Dictionary<string, JsonElement> bad = new()
        {
            ["tutor-subjects"] = JsonSerializer.SerializeToElement("NotARealSubject"),
            ["unpaid-ack"] = JsonSerializer.SerializeToElement("student1"),
        };
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TicketIntakeValidator.ValidateAnswers(schema, bad, Resolver, "Tutor"));
        Assert.Contains("re-enter", ex.Message, StringComparison.OrdinalIgnoreCase);

        Dictionary<string, JsonElement> good = new()
        {
            ["tutor-subjects"] = JsonSerializer.SerializeToElement("RUst, biology"),
            ["unpaid-ack"] = JsonSerializer.SerializeToElement("student1"),
        };
        TicketIntakeValidator.ValidateAnswers(schema, good, Resolver, "Tutor");
        Assert.Equal("Rust, Biology", good["tutor-subjects"].GetString());
    }

    [Fact]
    public void Extractor_returns_generals_and_expertise_from_prose()
    {
        TutorSubjectTextProcessor.SubjectExtraction extraction =
            TutorSubjectTextProcessor.ExtractSubjects("Applicant wants to tutor Rust and Biology.");
        Assert.Contains(SubjectMaskNames.ComputerScience, extraction.GeneralMasks);
        Assert.Contains(SubjectMaskNames.Science, extraction.GeneralMasks);
        Assert.Contains("Rust", extraction.ExpertiseLabels);
        Assert.Contains("Biology", extraction.ExpertiseLabels);
    }

    [Fact]
    public void Subject_signals_carry_expertise_into_router_input()
    {
        SubjectSignalSnapshot snapshot = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.Science, SubjectMaskNames.ComputerScience],
            SubjectMaskNames.Science,
            appliedExpertise: ["Biology", "Rust"]);
        Assert.Equal(["Biology", "Rust"], snapshot.AppliedExpertise);

        float[] routerInput = TutoringSubjectContextRouter.BuildRouterInput(snapshot);
        Assert.Equal(TutoringSubjectContextRouter.InputSize, routerInput.Length);
        Assert.Equal(TutoringSubjectContextRouter.InputSize, routerInput.Length);
        Assert.Equal(62, routerInput.Length);
        Assert.True(routerInput.Skip(TutoringSubjectContextRouter.BaseInputSize).Sum() >= 1f);
        Assert.Equal(1f, routerInput[5]); // Science applied hot (index 1 in generals → slot 4+1)
        Assert.Equal(1f, routerInput[6]); // ComputerScience applied hot
    }

    [Fact]
    public void Tutoring_router_topology_includes_expertise_hash_bins()
    {
        Assert.Equal(62, TutoringSubjectContextRouter.InputSize);
        Assert.Equal(32, TutoringSubjectContextRouter.HiddenSize);
        Assert.Equal(8, TutoringSubjectContextRouter.OutputSize);
        using TutoringSubjectContextRouter router = new();
        SubjectSignalSnapshot snapshot = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.ComputerScience],
            SubjectMaskNames.ComputerScience,
            appliedExpertise: ["Rust"]);
        float[] embedding = router.Forward(snapshot);
        Assert.Equal(8, embedding.Length);
    }
}
