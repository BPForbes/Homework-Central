using System.Text.Json;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tickets.Preface;

namespace HomeworkCentral.Api.Tickets;

public static class TicketIntakeValidator
{
    private static readonly HashSet<string> AllowedQuestionTypes = new(StringComparer.Ordinal)
    {
        "shortText",
        "longText",
        "multipleChoice",
        "trueFalse",
        "checkbox",
        "date",
        "multiSelect",
        "dropdown",
        "fileUpload",
        "link",
        "messageForward",
        "mixed",
    };

    private static readonly HashSet<string> AllowedResponseKinds = new(StringComparer.Ordinal)
    {
        "text",
        "file",
        "link",
        "forward",
    };

    public static void ValidateSchema(IReadOnlyList<TicketIntakeQuestionDto> questions)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        int aiOptOutCount = 0;
        foreach (TicketIntakeQuestionDto question in questions)
        {
            ValidateQuestionIdentity(question, ids);
            ValidateQuestionTypeAndOptions(question);
            ValidateMixedQuestion(question);
            if (ValidateAiOptOutQuestion(question))
                aiOptOutCount++;
        }

        if (aiOptOutCount > 1)
            throw new InvalidOperationException("At most one AI opt-out question is allowed per portal.");
    }

    private static void ValidateQuestionIdentity(TicketIntakeQuestionDto question, HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(question.Id))
            throw new InvalidOperationException("Each intake question must have an id.");

        if (!ids.Add(question.Id.Trim()))
            throw new InvalidOperationException("Intake question ids must be unique.");

        if (string.IsNullOrWhiteSpace(question.Prompt))
            throw new InvalidOperationException("Each intake question must have a prompt.");
    }

    private static void ValidateQuestionTypeAndOptions(TicketIntakeQuestionDto question)
    {
        if (!AllowedQuestionTypes.Contains(question.Type))
            throw new InvalidOperationException($"Unsupported intake question type '{question.Type}'.");

        bool needsOptions = question.Type is "multipleChoice" or "multiSelect" or "dropdown";
        if (needsOptions && (question.Options is null || question.Options.Count == 0))
        {
            throw new InvalidOperationException(
                $"Question '{question.Id}' requires at least one option.");
        }
    }

    private static bool ValidateAiOptOutQuestion(TicketIntakeQuestionDto question)
    {
        if (!question.AiOptOut)
            return false;

        if (question.Type is not ("checkbox" or "trueFalse"))
            throw new InvalidOperationException("AI opt-out questions must be checkbox or true/false.");

        return true;
    }

    private static void ValidateMixedQuestion(TicketIntakeQuestionDto question)
    {
        if (question.Type != "mixed")
            return;

        if (question.AllowedResponseKinds is null || question.AllowedResponseKinds.Count == 0)
            throw new InvalidOperationException($"Mixed question '{question.Id}' needs allowedResponseKinds.");

        foreach (string kind in question.AllowedResponseKinds)
        {
            if (!AllowedResponseKinds.Contains(kind))
                throw new InvalidOperationException($"Unsupported response kind '{kind}'.");
        }
    }

    /// <summary>
    /// Validates answers and runs any bound <see cref="ITicketPrefaceCheck"/> (tutor subjects,
    /// mod concepts, or future custom-portal checks registered via DI).
    /// </summary>
    public static Dictionary<string, TicketPrefaceResult> ValidateAnswers(
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        Dictionary<string, JsonElement> answers,
        ITicketPrefaceCheckResolver? prefaceChecks = null,
        string? filterName = null)
    {
        ValidateSchema(schema);
        Dictionary<string, TicketPrefaceResult> prefaceByQuestion = new(StringComparer.OrdinalIgnoreCase);

        foreach (TicketIntakeQuestionDto question in schema)
        {
            if (!TryGetSubmittedAnswer(question, answers, out JsonElement value))
                continue;

            ValidateAnswerValue(question, value);
            ProcessPrefaceCheck(question, value, answers, prefaceByQuestion, prefaceChecks, filterName);
        }

        return prefaceByQuestion;
    }

    private static bool TryGetSubmittedAnswer(
        TicketIntakeQuestionDto question,
        IReadOnlyDictionary<string, JsonElement> answers,
        out JsonElement value)
    {
        bool hasAnswer = answers.TryGetValue(question.Id, out value);
        if (question.Required && (!hasAnswer || IsEmptyValue(value)))
            throw new InvalidOperationException($"Answer required for '{question.Prompt}'.");

        return hasAnswer && !IsEmptyValue(value);
    }

    private static void ProcessPrefaceCheck(
        TicketIntakeQuestionDto question,
        JsonElement value,
        Dictionary<string, JsonElement> answers,
        Dictionary<string, TicketPrefaceResult> prefaceByQuestion,
        ITicketPrefaceCheckResolver? prefaceChecks,
        string? filterName)
    {
        ITicketPrefaceCheck? check = prefaceChecks?.Resolve(question.Id, filterName);
        if (check is null || value.ValueKind != JsonValueKind.String)
            return;

        TicketPrefaceResult processed = check.Process(value.GetString());
        prefaceByQuestion[question.Id] = processed;
        if (!processed.Ok)
        {
            throw new InvalidOperationException(
                processed.ErrorMessage
                ?? $"Could not verify the answer for '{question.Prompt}'. Please re-enter it.");
        }

        if (check.RewriteAnswerOnSuccess && !string.IsNullOrWhiteSpace(processed.CanonicalDisplay))
            answers[question.Id] = JsonSerializer.SerializeToElement(processed.CanonicalDisplay);
    }

    public static bool IsAiOptOut(
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        TicketIntakeQuestionDto? optOut = schema.FirstOrDefault(q => q.AiOptOut);
        if (optOut is null || !answers.TryGetValue(optOut.Id, out JsonElement value))
            return false;

        return value.ValueKind == JsonValueKind.True;
    }

    private static void ValidateAnswerValue(TicketIntakeQuestionDto question, JsonElement value)
    {
        switch (question.Type)
        {
            case "shortText" or "longText" or "date":
                ValidateRequiredStringAnswer(question.Prompt, value);
                break;

            case "multipleChoice" or "dropdown":
                ValidateSingleOptionAnswer(question, value);
                break;

            case "link":
                ValidateLinkAnswer(question.Prompt, value);
                break;

            case "trueFalse" or "checkbox":
                ValidateBooleanAnswer(question.Prompt, value);
                break;

            case "fileUpload":
                ValidateRequiredArrayAnswer(question.Prompt, value);
                break;

            case "multiSelect":
                ValidateMultiSelectAnswer(question, value);
                break;

            case "messageForward":
                ValidateForwardSnapshot(question.Prompt, value);
                break;

            case "mixed":
                ValidateMixedAnswer(question, value);
                break;
        }

        if (question.TracksUser && !TryParseUserId(value, out _))
        {
            throw new InvalidOperationException(
                $"Answer for '{question.Prompt}' must be a valid user id.");
        }
    }

    private static string ValidateRequiredStringAnswer(string prompt, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Invalid answer for '{prompt}'.");

        string? answer = value.GetString();
        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException($"Invalid answer for '{prompt}'.");

        return answer;
    }

    private static void ValidateSingleOptionAnswer(TicketIntakeQuestionDto question, JsonElement value)
    {
        string answer = ValidateRequiredStringAnswer(question.Prompt, value);
        if (question.Options is { Count: > 0 }
            && !question.Options.Contains(answer, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Answer for '{question.Prompt}' is not an allowed option.");
        }
    }

    private static void ValidateLinkAnswer(string prompt, JsonElement value)
    {
        string answer = ValidateRequiredStringAnswer(prompt, value);
        // Ticket intake links are browser-opened evidence, so custom URL schemes are rejected.
        if (!Uri.TryCreate(answer, UriKind.Absolute, out Uri? linkUri)
            || linkUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"Answer for '{prompt}' must be an http(s) URL.");
        }
    }

    private static void ValidateBooleanAnswer(string prompt, JsonElement value)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"Invalid answer for '{prompt}'.");
    }

    private static void ValidateRequiredArrayAnswer(string prompt, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            throw new InvalidOperationException($"Invalid answer for '{prompt}'.");
    }

    private static void ValidateMultiSelectAnswer(TicketIntakeQuestionDto question, JsonElement value)
    {
        ValidateRequiredArrayAnswer(question.Prompt, value);
        if (question.Options is not { Count: > 0 })
            return;

        foreach (JsonElement element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String
                || !question.Options.Contains(element.GetString()!, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Answer for '{question.Prompt}' contains an invalid option.");
            }
        }
    }

    private static void ValidateMixedAnswer(TicketIntakeQuestionDto question, JsonElement value)
    {
        ValidateRequiredArrayAnswer(question.Prompt, value);
        HashSet<string> allowedResponseKinds = new(
            question.AllowedResponseKinds ?? [],
            StringComparer.Ordinal);

        // Mixed answers preserve each part kind for ticket AI evidence and moderator review.
        foreach (JsonElement part in value.EnumerateArray())
        {
            if (IsAllowedMixedPart(part, allowedResponseKinds))
                continue;

            throw new InvalidOperationException(
                $"Answer for '{question.Prompt}' contains an invalid part.");
        }
    }

    private static bool IsAllowedMixedPart(JsonElement part, IReadOnlySet<string> allowedResponseKinds) =>
        part is { ValueKind: JsonValueKind.Object }
        && part.TryGetProperty("kind", out JsonElement kindElement)
        && kindElement is { ValueKind: JsonValueKind.String }
        && allowedResponseKinds.Contains(kindElement.GetString()!);

    private static void ValidateForwardSnapshot(string prompt, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Invalid answer for '{prompt}'.");
        if (!value.TryGetProperty("messageId", out _) || !value.TryGetProperty("roomId", out _))
            throw new InvalidOperationException($"Forwarded message for '{prompt}' is incomplete.");
    }

    public static bool TryParseUserId(JsonElement value, out Guid userId)
    {
        userId = Guid.Empty;
        if (value.ValueKind == JsonValueKind.String)
            return Guid.TryParse(value.GetString(), out userId);

        return false;
    }

    private static bool IsEmptyValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            JsonValueKind.False => false,
            JsonValueKind.True => false,
            _ => false,
        };
}
