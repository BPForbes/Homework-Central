using System.Text.Json;
using HomeworkCentral.Api.DTOs;

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
            if (string.IsNullOrWhiteSpace(question.Id))
                throw new InvalidOperationException("Each intake question must have an id.");

            if (!ids.Add(question.Id.Trim()))
                throw new InvalidOperationException("Intake question ids must be unique.");

            if (string.IsNullOrWhiteSpace(question.Prompt))
                throw new InvalidOperationException("Each intake question must have a prompt.");

            if (!AllowedQuestionTypes.Contains(question.Type))
                throw new InvalidOperationException($"Unsupported intake question type '{question.Type}'.");

            bool needsOptions = question.Type is "multipleChoice" or "multiSelect" or "dropdown";
            if (needsOptions && (question.Options is null || question.Options.Count == 0))
            {
                throw new InvalidOperationException(
                    $"Question '{question.Id}' requires at least one option.");
            }

            if (question.AiOptOut)
            {
                if (question.Type is not ("checkbox" or "trueFalse"))
                    throw new InvalidOperationException("AI opt-out questions must be checkbox or true/false.");
                aiOptOutCount++;
            }

            if (question.Type == "mixed")
            {
                if (question.AllowedResponseKinds is null || question.AllowedResponseKinds.Count == 0)
                    throw new InvalidOperationException($"Mixed question '{question.Id}' needs allowedResponseKinds.");
                foreach (string kind in question.AllowedResponseKinds)
                {
                    if (!AllowedResponseKinds.Contains(kind))
                        throw new InvalidOperationException($"Unsupported response kind '{kind}'.");
                }
            }
        }

        if (aiOptOutCount > 1)
            throw new InvalidOperationException("At most one AI opt-out question is allowed per portal.");
    }

    public static void ValidateAnswers(
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        ValidateSchema(schema);

        foreach (TicketIntakeQuestionDto question in schema)
        {
            bool hasAnswer = answers.TryGetValue(question.Id, out JsonElement value);
            if (question.Required && (!hasAnswer || IsEmptyValue(value)))
            {
                throw new InvalidOperationException($"Answer required for '{question.Prompt}'.");
            }

            if (!hasAnswer || IsEmptyValue(value))
                continue;

            ValidateAnswerValue(question, value);
        }
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
            case "shortText":
            case "longText":
            case "date":
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");
                break;

            case "multipleChoice":
            case "dropdown":
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");
                if (question.Options is { Count: > 0 }
                    && !question.Options.Contains(value.GetString()!, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Answer for '{question.Prompt}' is not an allowed option.");
                }

                break;

            case "link":
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");
                if (!Uri.TryCreate(value.GetString(), UriKind.Absolute, out Uri? linkUri)
                    || linkUri.Scheme is not ("http" or "https"))
                {
                    throw new InvalidOperationException($"Answer for '{question.Prompt}' must be an http(s) URL.");
                }

                break;

            case "trueFalse":
            case "checkbox":
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");
                break;

            case "multiSelect":
            case "fileUpload":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");

                if (question.Type == "multiSelect" && question.Options is { Count: > 0 })
                {
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

                break;

            case "messageForward":
                ValidateForwardSnapshot(question.Prompt, value);
                break;

            case "mixed":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");
                HashSet<string> allowed = new(
                    question.AllowedResponseKinds ?? [],
                    StringComparer.Ordinal);
                foreach (JsonElement part in value.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object
                        || !part.TryGetProperty("kind", out JsonElement kindEl)
                        || kindEl.ValueKind != JsonValueKind.String
                        || !allowed.Contains(kindEl.GetString()!))
                    {
                        throw new InvalidOperationException(
                            $"Answer for '{question.Prompt}' contains an invalid part.");
                    }
                }

                break;
        }

        if (question.TracksUser && !TryParseUserId(value, out _))
        {
            throw new InvalidOperationException(
                $"Answer for '{question.Prompt}' must be a valid user id.");
        }
    }

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
