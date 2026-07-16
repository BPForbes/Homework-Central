using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Assessment;

public class LlmOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen3:1.7b";
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<string?> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        LlmOptions opts = options.Value;
        if (!opts.Enabled)
            return null;

        try
        {
            var body = new
            {
                model = opts.ChatModel,
                stream = false,
                format = "json",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
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
            return null;
        }
    }

    public async Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        LlmOptions opts = options.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(text))
            return [];

        try
        {
            var body = new { model = opts.EmbeddingModel, prompt = text };
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
            // fall through to hash embed
        }

        return HashEmbed(text);
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
}
