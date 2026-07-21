using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tickets;
using System.Text.Json;

namespace HomeworkCentral.Api.Tests.Assessment;

public class TutorSubjectTextProcessorTests
{
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
        Assert.Equal("Computer Science", result.CanonicalDisplay);
    }

    [Fact]
    public void Alias_biology_maps_to_science()
    {
        TutorSubjectTextProcessor.ProcessResult result = TutorSubjectTextProcessor.ProcessStrict("Biology");
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal([SubjectMaskNames.Science], result.GeneralMasks);
    }

    [Fact]
    public void Multi_subject_list_dedupes_to_generals()
    {
        TutorSubjectTextProcessor.ProcessResult result =
            TutorSubjectTextProcessor.ProcessStrict("biology, rust, Mathematics");
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(
            [SubjectMaskNames.Science, SubjectMaskNames.ComputerScience, SubjectMaskNames.Mathematics],
            result.GeneralMasks);
        Assert.Equal("Science, Computer Science, Mathematics", result.CanonicalDisplay);
    }

    [Fact]
    public void Spell_check_corrects_near_miss_typos()
    {
        TutorSubjectTextProcessor.ProcessResult result = TutorSubjectTextProcessor.ProcessStrict("biologoy");
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal([SubjectMaskNames.Science], result.GeneralMasks);
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
            () => TicketIntakeValidator.ValidateAnswers(schema, bad));
        Assert.Contains("re-enter", ex.Message, StringComparison.OrdinalIgnoreCase);

        Dictionary<string, JsonElement> good = new()
        {
            ["tutor-subjects"] = JsonSerializer.SerializeToElement("RUst, biology"),
            ["unpaid-ack"] = JsonSerializer.SerializeToElement("student1"),
        };
        TicketIntakeValidator.ValidateAnswers(schema, good);
        Assert.Equal("Computer Science, Science", good["tutor-subjects"].GetString());
    }

    [Fact]
    public void Subject_signals_parse_expertise_aliases_from_prose()
    {
        IReadOnlyList<string> applied = ChatMonitoringSubjectSignals.ParseAppliedSubjects(
            "Applicant wants to tutor Rust and Biology.");
        Assert.Contains(SubjectMaskNames.ComputerScience, applied);
        Assert.Contains(SubjectMaskNames.Science, applied);
    }
}
