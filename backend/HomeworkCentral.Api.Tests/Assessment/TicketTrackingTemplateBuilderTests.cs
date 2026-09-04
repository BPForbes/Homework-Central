using System.Text.Json;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tickets;
using HomeworkCentral.Api.Tickets.Preface;

namespace HomeworkCentral.Api.Tests.Assessment;

public class TicketTrackingTemplateBuilderTests
{
    [Fact]
    public void Build_allows_missing_optional_answers_without_throwing()
    {
        // Untouched optional checkboxes are omitted from the POST body. Serializing
        // default(JsonElement) for those slots used to throw the BCL default message
        // "Operation is not valid due to the current state of the object."
        List<TicketIntakeQuestionDto> schema = DefaultTicketPortalPresets.ModIntakeQuestions();
        Dictionary<string, JsonElement> answers = new()
        {
            ["report-reason"] = JsonSerializer.SerializeToElement("They asked for Cash App payment."),
            ["reported-user"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
            ["proof"] = JsonSerializer.SerializeToElement(new[] { new { kind = "link", url = "https://example.com" } }),
        };

        Dictionary<string, TicketPrefaceResult> preface =
            TicketIntakeValidator.ValidateAnswers(schema, answers, new TicketPrefaceCheckResolver(
            [
                TutorSubjectPrefaceCheck.Instance,
                ModerationConceptPrefaceCheck.Instance,
            ]), DefaultTicketPortalPresets.ModFilterName);

        string json = TicketTrackingTemplateBuilder.Build(
            DefaultTicketPortalPresets.ModFilterName,
            schema,
            answers,
            preface);

        Assert.Contains("report-reason", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"answer\":{}", json, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement intake = document.RootElement.GetProperty("intake");
        Assert.Equal(schema.Count, intake.GetArrayLength());
        Assert.Contains(
            intake.EnumerateArray(),
            item => item.GetProperty("id").GetString() == "ai-opt-out"
                && !item.TryGetProperty("answer", out _));
    }

    [Fact]
    public void Build_includes_present_optional_checkbox_answers()
    {
        List<TicketIntakeQuestionDto> schema = DefaultTicketPortalPresets.TutorIntakeQuestions();
        Dictionary<string, JsonElement> answers = new()
        {
            ["tutor-subjects"] = JsonSerializer.SerializeToElement("Rust"),
            ["unpaid-ack"] = JsonSerializer.SerializeToElement("DevAdmin"),
            ["ai-opt-out"] = JsonSerializer.SerializeToElement(false),
        };

        Dictionary<string, TicketPrefaceResult> preface =
            TicketIntakeValidator.ValidateAnswers(schema, answers, new TicketPrefaceCheckResolver(
            [
                TutorSubjectPrefaceCheck.Instance,
                ModerationConceptPrefaceCheck.Instance,
            ]), DefaultTicketPortalPresets.TutorFilterName);

        string json = TicketTrackingTemplateBuilder.Build(
            DefaultTicketPortalPresets.TutorFilterName,
            schema,
            answers,
            preface);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement optOut = document.RootElement.GetProperty("intake")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "ai-opt-out");
        Assert.Equal(JsonValueKind.False, optOut.GetProperty("answer").ValueKind);
    }
}
