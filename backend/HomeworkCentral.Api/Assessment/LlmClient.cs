using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Assessment;

public class LlmOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen3:0.6b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int TimeoutSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
}

public interface ILlmClient
{
    Task<string?> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken ct = default);
}

public sealed class LlmClient(HttpClient httpClient, IOptions<LlmOptions> options) : ILlmClient
{
    private static readonly TimeSpan OfflineBackoff = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly object availabilityGate = new();
    private DateTime? unavailableUntilUtc;

    public async Task<string?> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        LlmOptions opts = options.Value;
        if (!opts.Enabled || IsTemporarilyUnavailable())
            return null;

        try
        {
            OllamaChatRequest body = new()
            {
                Model = opts.ChatModel,
                Stream = false,
                Think = false,
                Format = "json",
                Options = new OllamaRuntimeOptions(),
                Messages =
                [
                    new OllamaChatMessage { Role = "system", Content = systemPrompt },
                    new OllamaChatMessage { Role = "user", Content = userPrompt },
                ],
            };

            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                $"{opts.BaseUrl.TrimEnd('/')}/api/chat",
                body,
                JsonOptions,
                ct);
            if (!response.IsSuccessStatusCode)
                return null;

            JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            if (payload.TryGetProperty("message", out JsonElement message)
                && message.TryGetProperty("content", out JsonElement content))
            {
                return content.GetString();
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            MarkTemporarilyUnavailable();
            return null;
        }
    }

    public async Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        LlmOptions opts = options.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(text) || IsTemporarilyUnavailable())
            return [];

        try
        {
            OllamaEmbedRequest body = new()
            {
                Model = opts.EmbeddingModel,
                Prompt = text,
            };
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                $"{opts.BaseUrl.TrimEnd('/')}/api/embeddings",
                body,
                JsonOptions,
                ct);
            if (!response.IsSuccessStatusCode)
                return HashEmbed(text);

            JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            if (payload.TryGetProperty("embedding", out JsonElement embedding)
                && embedding.ValueKind == JsonValueKind.Array)
            {
                List<float> values = [];
                foreach (JsonElement el in embedding.EnumerateArray())
                    values.Add(el.GetSingle());
                return values;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            MarkTemporarilyUnavailable();
            // fall through to hash embed
        }

        return HashEmbed(text);
    }

    private bool IsTemporarilyUnavailable()
    {
        lock (availabilityGate)
            return unavailableUntilUtc is DateTime until && until > DateTime.UtcNow;
    }

    private void MarkTemporarilyUnavailable()
    {
        lock (availabilityGate)
            unavailableUntilUtc = DateTime.UtcNow.Add(OfflineBackoff);
    }

    /// <summary>Deterministic fallback embedding when the LLM service is offline.</summary>
    private static IReadOnlyList<float> HashEmbed(string text)
    {
        float[] vector = new float[64];
        foreach (char c in text)
            vector[c % vector.Length] += 1f;
        double norm = Math.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (int i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] / norm);
        }

        return vector;
    }

    private sealed class OllamaChatRequest
    {
        public string Model { get; set; } = string.Empty;
        public bool Stream { get; set; }
        public bool Think { get; set; }
        public string Format { get; set; } = "json";
        public OllamaRuntimeOptions Options { get; set; } = new();
        public List<OllamaChatMessage> Messages { get; set; } = [];
    }

    private sealed class OllamaRuntimeOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0;

        [JsonPropertyName("num_ctx")]
        public int ContextTokens { get; set; } = 2048;

        [JsonPropertyName("num_predict")]
        public int MaxOutputTokens { get; set; } = 256;
    }

    private sealed class OllamaChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    private sealed class OllamaEmbedRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
    }
}
