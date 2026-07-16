using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Tickets;

public sealed class OllamaTicketTrackingAnalyzer(
    HttpClient httpClient,
    IOptions<TicketOptions> options,
    ILogger<OllamaTicketTrackingAnalyzer> logger) : ITicketTrackingAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<TicketAnalysisResult> AnalyzeAsync(
        TicketPortalConfig portal,
        Ticket ticket,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        TicketOptions ticketOptions = options.Value;
        if (!ticketOptions.OllamaEnabled)
            return new TicketAnalysisResult(false, null, null, null);

        if (string.Equals(portal.TrackingMode, TicketTrackingModes.None, StringComparison.Ordinal))
            return new TicketAnalysisResult(false, null, null, null);

        try
        {
            string prompt = BuildPrompt(portal, ticket, messages);
            OllamaChatRequest request = new()
            {
                Model = ticketOptions.ModelName,
                Stream = false,
                Format = "json",
                Messages =
                [
                    new OllamaChatMessage
                    {
                        Role = "system",
                        Content =
                            "You analyze school ticket chat transcripts. Respond with JSON only: "
                            + "{\"decision\":string|null,\"summary\":string,\"trackedUserId\":string|null}. "
                            + "Pick decision from the allowed labels when possible; otherwise null. "
                            + "trackedUserId is a user UUID only when clearly identified.",
                    },
                    new OllamaChatMessage { Role = "user", Content = prompt },
                ],
            };

            string baseUrl = ticketOptions.OllamaBaseUrl.TrimEnd('/');
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                $"{baseUrl}/api/chat",
                request,
                JsonOptions,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Ollama returned {StatusCode} for ticket {TicketId}.",
                    (int)response.StatusCode,
                    ticket.TicketId);
                return new TicketAnalysisResult(false, null, null, null);
            }

            OllamaChatResponse? chatResponse =
                await response.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, ct);
            if (chatResponse?.Message?.Content is not string content || string.IsNullOrWhiteSpace(content))
                return new TicketAnalysisResult(false, null, null, null);

            OllamaDecisionPayload? payload =
                JsonSerializer.Deserialize<OllamaDecisionPayload>(content, JsonOptions);
            if (payload is null)
                return new TicketAnalysisResult(false, null, null, null);

            string? decision = string.IsNullOrWhiteSpace(payload.Decision) ? null : payload.Decision.Trim();
            List<string> allowedLabels = TicketJson.DeserializeStringList(portal.DecisionLabelsJson);
            if (decision is not null
                && allowedLabels.Count > 0
                && !allowedLabels.Contains(decision, StringComparer.Ordinal))
            {
                decision = null;
            }

            Guid? trackedUserId = null;
            if (!string.IsNullOrWhiteSpace(payload.TrackedUserId)
                && Guid.TryParse(payload.TrackedUserId, out Guid parsedUserId))
            {
                trackedUserId = parsedUserId;
            }

            string? summary = string.IsNullOrWhiteSpace(payload.Summary) ? null : payload.Summary.Trim();
            if (decision is null && summary is null && trackedUserId is null)
                return new TicketAnalysisResult(true, null, null, null);

            return new TicketAnalysisResult(true, decision, summary, trackedUserId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Ollama analysis failed for ticket {TicketId}.", ticket.TicketId);
            return new TicketAnalysisResult(false, null, null, null);
        }
    }

    private static string BuildPrompt(
        TicketPortalConfig portal,
        Ticket ticket,
        IReadOnlyList<ChatMessage> messages)
    {
        List<string> labels = TicketJson.DeserializeStringList(portal.DecisionLabelsJson);
        List<TicketIntakeQuestionDto> schema = TicketJson.DeserializeIntakeSchema(portal.IntakeSchemaJson);
        Dictionary<string, JsonElement> answers = TicketJson.DeserializeStoredAnswers(ticket.IntakeAnswersJson);
        List<TicketIntakeAnswerDto> intakeAnswers = TicketJson.BuildIntakeAnswerDtos(schema, answers);

        System.Text.StringBuilder builder = new();
        builder.AppendLine($"Ticket: {ticket.Purpose} #{ticket.DisplayNumber:D4}");
        builder.AppendLine($"Tracking mode: {portal.TrackingMode}");
        if (!string.IsNullOrWhiteSpace(portal.TrackingInstructions))
            builder.AppendLine($"Instructions: {portal.TrackingInstructions}");
        if (labels.Count > 0)
            builder.AppendLine($"Allowed decisions: {string.Join(", ", labels)}");

        if (intakeAnswers.Count > 0)
        {
            builder.AppendLine("Intake answers:");
            foreach (TicketIntakeAnswerDto answer in intakeAnswers)
                builder.AppendLine($"- {answer.Prompt}: {answer.ValueDisplay}");
        }

        if (messages.Count > 0)
        {
            builder.AppendLine("Chat transcript:");
            foreach (ChatMessage message in messages.OrderBy(m => m.CreatedAtUtc))
                builder.AppendLine($"{message.SenderUsername}: {message.RawContent}");
        }
        else
        {
            builder.AppendLine("Chat transcript: (no messages yet)");
        }

        return builder.ToString();
    }

    private sealed class OllamaChatRequest
    {
        public string Model { get; set; } = null!;
        public bool Stream { get; set; }
        public string? Format { get; set; }
        public List<OllamaChatMessage> Messages { get; set; } = [];
    }

    private sealed class OllamaChatMessage
    {
        public string Role { get; set; } = null!;
        public string Content { get; set; } = null!;
    }

    private sealed class OllamaChatResponse
    {
        public OllamaChatMessage? Message { get; set; }
    }

    private sealed class OllamaDecisionPayload
    {
        public string? Decision { get; set; }
        public string? Summary { get; set; }
        public string? TrackedUserId { get; set; }
    }
}
