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
        "date",
        "multiSelect",
        "dropdown",
    };

    public static void ValidateSchema(IReadOnlyList<TicketIntakeQuestionDto> questions)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
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
        }
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

    private static void ValidateAnswerValue(TicketIntakeQuestionDto question, JsonElement value)
    {
        switch (question.Type)
        {
            case "shortText":
            case "longText":
            case "date":
            case "multipleChoice":
            case "dropdown":
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");
                if (question.Options is { Count: > 0 }
                    && question.Type is "multipleChoice" or "dropdown"
                    && !question.Options.Contains(value.GetString()!, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Answer for '{question.Prompt}' is not an allowed option.");
                }

                break;

            case "trueFalse":
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");
                break;

            case "multiSelect":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                    throw new InvalidOperationException($"Invalid answer for '{question.Prompt}'.");

                if (question.Options is { Count: > 0 })
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
        }

        if (question.TracksUser && !TryParseUserId(value, out _))
        {
            throw new InvalidOperationException(
                $"Answer for '{question.Prompt}' must be a valid user id.");
        }
    }

    public static bool TryParseUserId(JsonElement value, out Guid userId)
    {
        userId = Guid.Empty;
        if (value.ValueKind == JsonValueKind.String)
        {
            return Guid.TryParse(value.GetString(), out userId);
        }

        return false;
    }

    private static bool IsEmptyValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false,
        };
}
