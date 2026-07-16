using System.Text.Json;
using System.Text.Json.Serialization;
using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Tickets;

public static class TicketJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        List<string>? values = JsonSerializer.Deserialize<List<string>>(json, SerializerOptions);
        return values ?? [];
    }

    public static string SerializeStringList(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values, SerializerOptions);

    public static List<CustomChannelAccessRuleInput> DeserializeAccessRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        List<CustomChannelAccessRuleInput>? rules =
            JsonSerializer.Deserialize<List<CustomChannelAccessRuleInput>>(json, SerializerOptions);
        return rules ?? [];
    }

    public static string SerializeAccessRules(IReadOnlyList<CustomChannelAccessRuleInput> rules) =>
        JsonSerializer.Serialize(rules, SerializerOptions);

    public static List<TicketIntakeQuestionDto> DeserializeIntakeSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        List<TicketIntakeQuestionDto>? questions =
            JsonSerializer.Deserialize<List<TicketIntakeQuestionDto>>(json, SerializerOptions);
        return questions ?? [];
    }

    public static string SerializeIntakeSchema(IReadOnlyList<TicketIntakeQuestionDto> questions) =>
        JsonSerializer.Serialize(questions, SerializerOptions);

    public static string SerializeStoredAnswers(IReadOnlyDictionary<string, JsonElement> answers) =>
        JsonSerializer.Serialize(answers, SerializerOptions);

    public static Dictionary<string, JsonElement> DeserializeStoredAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        Dictionary<string, JsonElement>? answers =
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions);
        return answers ?? [];
    }

    public static string SerializeOpenedPayload(TicketOpenedPayloadDto payload) =>
        JsonSerializer.Serialize(payload, SerializerOptions);

    public static string SerializeDecisionPayload(TicketDecisionPayloadDto payload) =>
        JsonSerializer.Serialize(payload, SerializerOptions);

    public static TicketOpenedPayloadDto? TryDeserializeOpenedPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<TicketOpenedPayloadDto>(json, SerializerOptions);
    }

    public static TicketDecisionPayloadDto? TryDeserializeDecisionPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<TicketDecisionPayloadDto>(json, SerializerOptions);
    }

    public static List<TicketIntakeAnswerDto> BuildIntakeAnswerDtos(
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        List<TicketIntakeAnswerDto> result = [];
        foreach (TicketIntakeQuestionDto question in schema)
        {
            if (!answers.TryGetValue(question.Id, out JsonElement value))
                continue;

            result.Add(new TicketIntakeAnswerDto
            {
                QuestionId = question.Id,
                Prompt = question.Prompt,
                Type = question.Type,
                ValueDisplay = FormatValueDisplay(question, value),
            });
        }

        return result;
    }

    public static string FormatValueDisplay(TicketIntakeQuestionDto question, JsonElement value)
    {
        return question.Type switch
        {
            "trueFalse" => value.ValueKind switch
            {
                JsonValueKind.True => "Yes",
                JsonValueKind.False => "No",
                _ => value.ToString(),
            },
            "multiSelect" => value.ValueKind == JsonValueKind.Array
                ? string.Join(", ", value.EnumerateArray().Select(element => element.ToString()))
                : value.ToString(),
            _ => value.ToString(),
        };
    }
}
