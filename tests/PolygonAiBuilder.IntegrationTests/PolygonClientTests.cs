using System.Net;
using System.Security.Cryptography;
using System.Text;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Integrations;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class PolygonClientTests
{
    [Fact]
    public async Task ListProblems_SendsVerifiableSignatureAndParsesResponse()
    {
        string? capturedBody = null;
        var handler = new DelegateHandler(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync(CancellationToken.None);
            return JsonResponse("""
                {"status":"OK","result":[{"id":123,"name":"sample name","owner":"owner","deleted":false}]}
                """);
        });
        var client = CreateClient(handler);

        var problems = await client.ListProblemsAsync("sample name");

        var values = ParseForm(capturedBody!);
        Assert.Equal("sample name", values["name"]);
        Assert.Equal("test-api-key", values["apiKey"]);
        Assert.Equal("1722600000", values["time"]);
        AssertSignature(values, "problems.list", "test-api-secret");
        var problem = Assert.Single(problems);
        Assert.Equal(123, problem.Id);
        Assert.Equal("sample name", problem.Name);
    }

    [Fact]
    public async Task ListProblems_RedactsCredentialFromPolygonFailure()
    {
        var handler = new DelegateHandler(_ => Task.FromResult(JsonResponse(
            "{\"status\":\"FAILED\",\"comment\":\"bad test-api-key / test-api-secret\"}")));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ExternalServiceException>(
            () => client.ListProblemsAsync("sample"));

        Assert.DoesNotContain("test-api-key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-api-secret", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", exception.Message, StringComparison.Ordinal);
    }

    private static PolygonClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://polygon.example/api/"),
        };
        return new(
            httpClient,
            new MemorySecretStore(new("", "", "test-api-key", "test-api-secret")),
            new FixedTimeProvider(new DateTimeOffset(2024, 8, 2, 12, 0, 0, TimeSpan.Zero)));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static Dictionary<string, string> ParseForm(string body) => body
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(
            pair => WebUtility.UrlDecode(pair[0]),
            pair => WebUtility.UrlDecode(pair[1]),
            StringComparer.Ordinal);

    private static void AssertSignature(
        IReadOnlyDictionary<string, string> values,
        string methodName,
        string secret)
    {
        var apiSignature = values["apiSig"];
        var prefix = apiSignature[..6];
        var canonical = string.Join(
            "&",
            values
                .Where(pair => pair.Key != "apiSig")
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ThenBy(pair => pair.Value, StringComparer.Ordinal)
                .Select(pair => $"{PolygonSignature.Encode(pair.Key)}={PolygonSignature.Encode(pair.Value)}"));
        var source = $"{prefix}/{methodName}?{canonical}#{secret}";
        var expected = prefix + Convert.ToHexStringLower(
            SHA512.HashData(Encoding.UTF8.GetBytes(source)));
        Assert.Equal(expected, apiSignature);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }

    private sealed class MemorySecretStore(SecretBundle secrets) : ISecretStore
    {
        public string FilePath => "memory";
        public Task<SecretBundle> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(secrets);
        public Task SaveAsync(SecretBundle value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
