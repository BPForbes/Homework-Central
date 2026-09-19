using System.Text.Json;
using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tickets;
using HomeworkCentral.Api.Tickets.Preface;

namespace HomeworkCentral.Api.Tests.Assessment;

public class TicketPrefaceCheckTests
{
    private static readonly ITicketPrefaceCheckResolver Resolver = new TicketPrefaceCheckResolver(
    [
        TutorSubjectPrefaceCheck.Instance,
        ModerationConceptPrefaceCheck.Instance,
    ]);

    [Theory]
    [InlineData("Rust")]
    [InlineData("RUst")]
    [InlineData("RUST")]
    public void Tutor_case_folding_maps_rust(string input)
    {
        TicketPrefaceResult result = TutorSubjectPrefaceCheck.Instance.ProcessStrict(input);
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal([SubjectMaskNames.ComputerScience], result.Categories);
        Assert.Equal(["Rust"], result.SpecificLabels);
    }

    [Fact]
    public void Tutor_and_mod_share_resolver_by_question_id()
    {
        Assert.Same(TutorSubjectPrefaceCheck.Instance, Resolver.Resolve("tutor-subjects", "Tutor"));
        Assert.Same(ModerationConceptPrefaceCheck.Instance, Resolver.Resolve("report-reason", "Mod-Mail"));
        Assert.Null(Resolver.Resolve("why-consider", "Tutor"));
    }

    [Fact]
    public void Intake_validator_uses_shared_preface_checks()
    {
        List<TicketIntakeQuestionDto> tutorSchema = DefaultTicketPortalPresets.TutorIntakeQuestions();
        Dictionary<string, JsonElement> bad = new()
        {
            ["tutor-subjects"] = JsonSerializer.SerializeToElement("NotARealSubject"),
            ["unpaid-ack"] = JsonSerializer.SerializeToElement("student1"),
        };
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TicketIntakeValidator.ValidateAnswers(tutorSchema, bad, Resolver, "Tutor"));
        Assert.Contains("re-enter", ex.Message, StringComparison.OrdinalIgnoreCase);

        Dictionary<string, JsonElement> good = new()
        {
            ["tutor-subjects"] = JsonSerializer.SerializeToElement("RUst, biology"),
            ["unpaid-ack"] = JsonSerializer.SerializeToElement("student1"),
        };
        Dictionary<string, TicketPrefaceResult> tutorPreface =
            TicketIntakeValidator.ValidateAnswers(tutorSchema, good, Resolver, "Tutor");
        Assert.Equal("Rust, Biology", good["tutor-subjects"].GetString());
        Assert.True(tutorPreface["tutor-subjects"].Ok);

        List<TicketIntakeQuestionDto> modSchema = DefaultTicketPortalPresets.ModIntakeQuestions();
        Dictionary<string, JsonElement> modAnswers = new()
        {
            ["report-reason"] = JsonSerializer.SerializeToElement("They asked me for Cash App payment for homework help."),
            ["reported-user"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
            ["proof"] = JsonSerializer.SerializeToElement(new[] { new { kind = "link", url = "https://example.com" } }),
        };
        Dictionary<string, TicketPrefaceResult> modPreface =
            TicketIntakeValidator.ValidateAnswers(modSchema, modAnswers, Resolver, "Mod-Mail");
        Assert.True(modPreface["report-reason"].Ok);
        // Lenient: original narrative preserved.
        Assert.Contains("Cash App", modAnswers["report-reason"].GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("off-platform-payment", modPreface["report-reason"].PrimaryCategory);
    }

    [Fact]
    public void Mod_extractor_maps_payment_phrases_for_cascade()
    {
        TicketPrefaceExtraction extraction =
            ModerationConceptPrefaceCheck.Instance.Extract("User keeps tip begging after every answer.");
        Assert.Equal("tip-solicitation", extraction.PrimaryCategory);
        ModerationConceptSnapshot snapshot =
            ChatMonitoringModerationConceptSignals.Resolve(null, "User keeps tip begging after every answer.");
        Assert.Equal("tip-solicitation", snapshot.ReportedConcept);
    }

    [Fact]
    public void Facade_still_exposes_tutor_subject_api()
    {
        TutorSubjectTextProcessor.ProcessResult result =
            TutorSubjectTextProcessor.ProcessStrict("biology, rust");
        Assert.True(result.Ok);
        Assert.Equal(["Biology", "Rust"], result.ExpertiseLabels);
    }

    [Fact]
    public void Tutor_expertise_hashes_feed_widened_router()
    {
        SubjectSignalSnapshot snapshot = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.Science, SubjectMaskNames.ComputerScience],
            SubjectMaskNames.Science,
            appliedExpertise: ["Biology", "Rust"]);
        float[] routerInput = TutoringSubjectContextRouter.BuildRouterInput(snapshot);
        Assert.Equal(TutoringSubjectContextRouter.InputSize, routerInput.Length);
        Assert.Equal(62, routerInput.Length);
        Assert.True(routerInput.Skip(TutoringSubjectContextRouter.BaseInputSize).Sum() >= 1f);
    }
}
