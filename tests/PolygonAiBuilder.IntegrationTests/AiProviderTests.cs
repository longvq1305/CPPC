using System.Net;
using System.Text;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;
using PolygonAiBuilder.Integrations;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class AiProviderTests
{
    [Fact]
    public async Task OpenAi_ListsModelsAndParsesResponseSse()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("Bearer test-openai", request.Headers.Authorization?.ToString());
            if (request.Method == HttpMethod.Get)
            {
                return Json("""{"data":[{"id":"gpt-5.6"},{"id":"text-embedding-3-small"}]}""");
            }

            Assert.Equal("/v1/responses", request.RequestUri?.AbsolutePath);
            return Sse("""
                data: {"type":"response.created","response":{"id":"resp_123"}}

                data: {"type":"response.output_text.delta","delta":"Xin "}

                data: {"type":"response.output_text.delta","delta":"chào"}

                data: {"type":"response.completed","response":{"id":"resp_123"}}

                data: [DONE]

                """);
        });
        var provider = new OpenAiProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") },
            new MemorySecretStore(new("test-openai", "", "", "")),
            TimeProvider.System);

        var models = await provider.ListModelsAsync();
        Assert.Single(models);
        Assert.Equal("gpt-5.6", models[0].Id);

        var events = new List<AiStreamEvent>();
        await foreach (var item in provider.StreamChatAsync(new(
                           "gpt-5.6",
                           "system",
                           [new(MessageRole.User, "hello", [])])))
        {
            events.Add(item);
        }

        Assert.Equal("Xin chào", string.Concat(events.Where(item => item.Kind == AiStreamEventKind.TextDelta).Select(item => item.Text)));
        Assert.Contains(events, item => item.Kind == AiStreamEventKind.Completed && item.ProviderResponseId == "resp_123");
        Assert.Contains("\"store\":false", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gemini_ListsModelsAndParsesInteractionSse()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.True(request.Headers.TryGetValues("x-goog-api-key", out var values));
            Assert.Equal("test-gemini", values.Single());
            if (request.Method == HttpMethod.Get)
            {
                return Json("""{"models":[{"name":"models/gemini-3.6-flash","displayName":"Gemini Flash"},{"name":"models/text-embedding-004"}]}""");
            }

            Assert.Equal("/v1/interactions", request.RequestUri?.AbsolutePath);
            return Sse("""
                data: {"event_type":"interaction.created","interaction":{"id":"int_123"}}

                data: {"event_type":"step.delta","index":0,"delta":{"type":"text","text":"Xin "}}

                data: {"event_type":"step.delta","index":0,"delta":{"type":"text","text":"chào"}}

                data: {"event_type":"interaction.completed","interaction":{"id":"int_123"}}

                """);
        });
        var provider = new GeminiProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") },
            new MemorySecretStore(new("", "test-gemini", "", "")),
            TimeProvider.System);

        var models = await provider.ListModelsAsync();
        Assert.Single(models);
        Assert.Equal("gemini-3.6-flash", models[0].Id);

        var events = new List<AiStreamEvent>();
        await foreach (var item in provider.StreamChatAsync(new(
                           "gemini-3.6-flash",
                           "system",
                           [new(MessageRole.User, "hello", [])])))
        {
            events.Add(item);
        }

        Assert.Equal("Xin chào", string.Concat(events.Where(item => item.Kind == AiStreamEventKind.TextDelta).Select(item => item.Text)));
        Assert.Contains(events, item => item.Kind == AiStreamEventKind.Completed && item.ProviderResponseId == "int_123");
        Assert.Contains("\"type\":\"user_input\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"store\":false", handler.LastRequestBody, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Sse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responder(request);
        }
    }

    private sealed class MemorySecretStore(SecretBundle secrets) : ISecretStore
    {
        public string FilePath => string.Empty;
        public Task<SecretBundle> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(secrets);
        public Task SaveAsync(SecretBundle value, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
