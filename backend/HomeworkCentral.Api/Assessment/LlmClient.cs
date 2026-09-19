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
    /// <summary>Caps concurrent Ollama chat/embed calls to reduce contention and tail latency.</summary>
    public int MaxConcurrentRequests { get; set; } = 2;

    /// <summary>Ollama <c>num_ctx</c> for chat completions.</summary>
    public int ContextTokens { get; set; } = 4096;

    /// <summary>
    /// Ollama <c>num_predict</c> for chat completions. Scenario JSON needs well above 256
    /// tokens; a low cap truncates output and causes parse failures / extra retries.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 1536;
}

/// <summary>
/// Optional local LLM boundary (Ollama). Returns null / empty when disabled, offline, or timed out
/// so assessment can fall back to deterministic scoring without failing the request pipeline.
/// </summary>
public interface ILlmClient
{
    /// <summary>Requests JSON chat completion; null means unavailable or non-JSON response.</summary>
    Task<string?> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>Embeds text for vector evidence; empty when unavailable.</summary>
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
    /// <summary>
    /// Once this client sees 404 for <c>/api/embed</c>, later embeds in the same scope skip that
    /// probe and go straight to legacy <c>/api/embeddings</c> (avoids a failed round-trip per call).
    /// </summary>
    private bool skipModernEmbedEndpoint;
    private readonly SemaphoreSlim concurrency = new(Math.Clamp(options.Value.MaxConcurrentRequests, 1, 16));
    private readonly object availabilityGate = new();
    private DateTime? unavailableUntilUtc;

    public async Task<string?> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        LlmOptions opts = options.Value;
        if (!opts.Enabled || IsTemporarilyUnavailable())
            return null;

        await concurrency.WaitAsync(ct);
        try
        {
            OllamaChatRequest body = new()
            {
                Model = opts.ChatModel,
                Stream = false,
                Think = false,
                Format = "json",
                Options = new OllamaRuntimeOptions
                {
                    ContextTokens = Math.Clamp(opts.ContextTokens, 512, 32768),
                    MaxOutputTokens = Math.Clamp(opts.MaxOutputTokens, 64, 8192),
                },
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
        finally { concurrency.Release(); }
    }

    public async Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        LlmOptions opts = options.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(text) || IsTemporarilyUnavailable())
            return [];

        await concurrency.WaitAsync(ct);
        try
        {
            if (!skipModernEmbedEndpoint)
            {
                IReadOnlyList<float>? modern = await TryModernEmbedAsync(opts, text, ct);
                if (modern is not null)
                    return modern;
            }

            IReadOnlyList<float>? legacy = await TryLegacyEmbedAsync(opts, text, ct);
            if (legacy is not null)
                return legacy;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            MarkTemporarilyUnavailable();
            // fall through to hash embed
        }
        finally { concurrency.Release(); }

        return HashEmbed(text);
    }

    /// <summary>
    /// Current Ollama embed API (<c>POST /api/embed</c> with <c>input</c> → <c>embeddings[]</c>).
    /// Returns null when the endpoint is missing or the payload cannot be parsed.
    /// </summary>
    private async Task<IReadOnlyList<float>?> TryModernEmbedAsync(LlmOptions opts, string text, CancellationToken ct)
    {
        OllamaEmbedRequest body = new()
        {
            Model = opts.EmbeddingModel,
            Input = text,
            // Official default is true; keep explicit so long ticket text truncates instead of 400s.
            Truncate = true,
        };
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"{opts.BaseUrl.TrimEnd('/')}/api/embed",
            body,
            JsonOptions,
            ct);
        // Null means "not usable here" so the caller can try legacy / HashEmbed.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            skipModernEmbedEndpoint = true;
            return null;
        }

        if (!response.IsSuccessStatusCode)
            return null;

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        if (payload.TryGetProperty("embeddings", out JsonElement embeddings)
            && embeddings.ValueKind == JsonValueKind.Array
            && embeddings.GetArrayLength() > 0
            && embeddings[0].ValueKind == JsonValueKind.Array)
        {
            return ReadFloatVector(embeddings[0]);
        }

        return null;
    }

    /// <summary>
    /// Legacy <c>POST /api/embeddings</c> (<c>prompt</c> → <c>embedding</c>) for older Ollama builds.
    /// </summary>
    private async Task<IReadOnlyList<float>?> TryLegacyEmbedAsync(LlmOptions opts, string text, CancellationToken ct)
    {
        OllamaLegacyEmbedRequest body = new()
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
            return null;

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        if (payload.TryGetProperty("embedding", out JsonElement embedding)
            && embedding.ValueKind == JsonValueKind.Array)
        {
            return ReadFloatVector(embedding);
        }

        return null;
    }

    private static IReadOnlyList<float> ReadFloatVector(JsonElement array)
    {
        List<float> values = [];
        foreach (JsonElement el in array.EnumerateArray())
            values.Add(el.GetSingle());
        return values;
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

    internal const int HashEmbedCacheCapacity = 256;
    private static readonly HostLru HashEmbedCache = new(HashEmbedCacheCapacity);

    /// <summary>Deterministic fallback embedding when the LLM service is offline.</summary>
    internal static IReadOnlyList<float> HashEmbed(string text)
    {
        if (HashEmbedCache.TryGetFloats(text, out float[] cached))
            return cached;

        IReadOnlyList<float> computed = RustKernels.TryHashEmbed(text, out float[] rustVector)
            ? rustVector
            : HashEmbedManaged(text);
        float[] stored = computed as float[] ?? computed.ToArray();
        HashEmbedCache.PutFloats(text, stored);
        return stored;
    }

    internal static void ResetHashEmbedCache() => HashEmbedCache.Clear();

    /// <summary>Managed 64-bin histogram used when <c>libhc_kernels</c> is absent.</summary>
    internal static IReadOnlyList<float> HashEmbedManaged(string text)
    {
        float[] vector = new float[RustKernels.HashEmbedBinCount];
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
        public string Input { get; set; } = string.Empty;
        public bool Truncate { get; set; } = true;
    }

    private sealed class OllamaLegacyEmbedRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
    }
}
