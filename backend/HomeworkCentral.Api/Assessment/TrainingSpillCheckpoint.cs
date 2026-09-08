using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Compact weights/bias snapshot written to <c>ChatMonitoringNeuralModelRun.WorkerReplayJson</c>
/// when the training heap is about to fill. Full V2 replay is not written on spill —
/// that serialize path is what OOM'd after ~170 continuous tickets.
/// </summary>
public sealed record TrainingSpillCheckpoint(
    string SchemaVersion,
    Guid SessionId,
    string ChatMonitoringKind,
    int TicketsProcessed,
    DateTimeOffset WrittenAtUtc,
    NeuralNetParameterSnapshot Parameters)
{
    public const string Version = "spill-checkpoint-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(
        Guid sessionId,
        NeuralModelKindChatMonitoring kind,
        int ticketsProcessed,
        NeuralNetParameterSnapshot parameters) =>
        JsonSerializer.Serialize(
            new TrainingSpillCheckpoint(
                Version,
                sessionId,
                kind.ToString(),
                ticketsProcessed,
                DateTimeOffset.UtcNow,
                parameters),
            JsonOptions);

    public static bool TryParse(string? json, out TrainingSpillCheckpoint? checkpoint)
    {
        checkpoint = null;
        if (string.IsNullOrWhiteSpace(json) || !json.Contains(Version, StringComparison.Ordinal))
            return false;

        try
        {
            TrainingSpillCheckpoint? parsed = JsonSerializer.Deserialize<TrainingSpillCheckpoint>(json, JsonOptions);
            if (parsed is null || !string.Equals(parsed.SchemaVersion, Version, StringComparison.Ordinal))
                return false;

            checkpoint = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
