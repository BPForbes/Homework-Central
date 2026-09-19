using System.Net;
using System.Text;
using System.Text.Json;
using HomeworkCentral.Api.Assessment;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class LlmClientTests
{
    [Fact]
    public async Task EmbedAsync_UsesModernEmbedEndpoint()
    {
        using RecordingHandler handler = new(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/embed", request.RequestUri!.AbsolutePath);
            string body = await request.Content!.ReadAsStringAsync(ct);
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.Equal("nomic-embed-text", document.RootElement.GetProperty("model").GetString());
            Assert.Equal("hello world", document.RootElement.GetProperty("input").GetString());
            Assert.True(document.RootElement.GetProperty("truncate").GetBoolean());
            Assert.False(document.RootElement.TryGetProperty("prompt", out _));

            return JsonResponse(HttpStatusCode.OK, """{"embeddings":[[0.25,0.5,0.75]]}""");
        });

        using HttpClient httpClient = CreateHttpClient(handler);
        LlmClient client = CreateClient(httpClient);
        IReadOnlyList<float> vector = await client.EmbedAsync("hello world");

        Assert.Equal([0.25f, 0.5f, 0.75f], vector);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EmbedAsync_FallsBackToLegacyEmbeddingsEndpoint()
    {
        using RecordingHandler handler = new(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/embed")
                return JsonResponse(HttpStatusCode.NotFound, """{"error":"not found"}""");

            Assert.Equal("/api/embeddings", request.RequestUri.AbsolutePath);
            string body = await request.Content!.ReadAsStringAsync(ct);
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.Equal("legacy text", document.RootElement.GetProperty("prompt").GetString());

            return JsonResponse(HttpStatusCode.OK, """{"embedding":[1,0,0]}""");
        });

        using HttpClient httpClient = CreateHttpClient(handler);
        LlmClient client = CreateClient(httpClient);
        IReadOnlyList<float> vector = await client.EmbedAsync("legacy text");

        Assert.Equal([1f, 0f, 0f], vector);
        Assert.Equal(2, handler.RequestCount);

        // After a 404 on /api/embed, later calls should skip the modern probe.
        IReadOnlyList<float> second = await client.EmbedAsync("legacy text");
        Assert.Equal([1f, 0f, 0f], second);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task EmbedAsync_UsesHashEmbedWhenOllamaUnavailable()
    {
        using RecordingHandler handler = new((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.ServiceUnavailable, """{"error":"unavailable"}""")));

        using HttpClient httpClient = CreateHttpClient(handler);
        LlmClient client = CreateClient(httpClient);
        IReadOnlyList<float> vector = await client.EmbedAsync("offline");

        Assert.Equal(64, vector.Count);
        Assert.True(vector.Sum(value => value * value) > 0.99f);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsEmptyWhenDisabled()
    {
        using RecordingHandler handler = new((_, _) =>
            throw new InvalidOperationException("HTTP should not be called when LLM is disabled."));

        using HttpClient httpClient = CreateHttpClient(handler);
        LlmClient client = CreateClient(httpClient, enabled: false);
        IReadOnlyList<float> vector = await client.EmbedAsync("ignored");

        Assert.Empty(vector);
        Assert.Equal(0, handler.RequestCount);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler, disposeHandler: true) { BaseAddress = new Uri("http://llm.test") };

    private static LlmClient CreateClient(HttpClient httpClient, bool enabled = true)
    {
        LlmOptions options = new()
        {
            BaseUrl = "http://llm.test",
            Enabled = enabled,
            EmbeddingModel = "nomic-embed-text",
            MaxConcurrentRequests = 1,
        };
        return new LlmClient(httpClient, Options.Create(options));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        private readonly List<HttpResponseMessage> responses = [];

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            HttpResponseMessage response = await responder(request, cancellationToken);
            lock (responses)
                responses.Add(response);
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (responses)
                {
                    foreach (HttpResponseMessage response in responses)
                        response.Dispose();
                    responses.Clear();
                }
            }

            base.Dispose(disposing);
        }
    }
}
