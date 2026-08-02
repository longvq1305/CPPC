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

    [Fact]
    public async Task WriteMethods_UseCurrentOfficialMethodNamesAndCpp17Parameters()
    {
        var requests = new List<(string Method, Dictionary<string, string> Form)>();
        var handler = new DelegateHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            requests.Add((request.RequestUri!.Segments.Last(), ParseForm(body)));
            return request.RequestUri.Segments.Last() == "problem.create"
                ? JsonResponse("""{"status":"OK","result":{"id":77,"name":"new-problem","owner":"owner","deleted":false}}""")
                : JsonResponse("""{"status":"OK","result":{}}""");
        });
        var client = CreateClient(handler);

        var problem = await client.CreateProblemAsync("new-problem");
        await client.UpdateInfoAsync(problem.Id, "stdin", "stdout", 1000, 256);
        await client.SaveStatementAsync(problem.Id, new("english", "Title", "Legend", "Input", "Output", "Note"));
        await client.SaveSolutionAsync(problem.Id, "int main(){}\n");
        await client.SaveSourceFileAsync(problem.Id, "gen.cpp", "int main(){}\n", "cpp.g++17");
        await client.SetCheckerAsync(problem.Id, "ncmp.cpp");
        await client.SaveScriptAsync(problem.Id, "tests", "gen 1 > $");
        await client.EnablePointsAsync(problem.Id, true);

        Assert.Equal(77, problem.Id);
        Assert.Equal([
            "problem.create", "problem.updateInfo", "problem.saveStatement", "problem.saveSolution",
            "problem.saveFile", "problem.setChecker", "problem.saveScript", "problem.enablePoints"
        ], requests.Select(item => item.Method));
        Assert.Equal("cpp.g++17", requests.Single(item => item.Method == "problem.saveSolution").Form["sourceType"]);
        Assert.Equal("MA", requests.Single(item => item.Method == "problem.saveSolution").Form["tag"]);
        Assert.Equal("gen.cpp", requests.Single(item => item.Method == "problem.saveFile").Form["name"]);
        Assert.Equal("false", requests.Single(item => item.Method == "problem.updateInfo").Form["interactive"]);
        Assert.DoesNotContain(requests, item => item.Method.Contains("validator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveTestMetadata_SetsEveryPointAndOnlyTestOneStatementSample()
    {
        var forms = new List<Dictionary<string, string>>();
        var handler = new DelegateHandler(async request =>
        {
            forms.Add(ParseForm(await request.Content!.ReadAsStringAsync()));
            return JsonResponse("""{"status":"OK","result":{}}""");
        });
        var client = CreateClient(handler);

        await client.SaveTestMetadataAsync(77, new("tests", 3, 1.5m, 1, true, "1 2\n", "3\n"));

        Assert.Equal(3, forms.Count);
        Assert.Equal(["1", "2", "3"], forms.Select(item => item["testIndex"]));
        Assert.All(forms, form => Assert.Equal("1.5", form["testPoints"]));
        Assert.Equal("1 2\n", forms[0]["testInputForStatements"]);
        Assert.Equal("true", forms[0]["verifyInputOutputForStatements"]);
        Assert.DoesNotContain("testInputForStatements", forms[1].Keys);
    }

    [Fact]
    public async Task RenderPackagesAndCautions_ParseStructuredResults()
    {
        var handler = new DelegateHandler(request => Task.FromResult(request.RequestUri!.Segments.Last() switch
        {
            "problem.renderStatements" => JsonResponse("""{"status":"OK","result":{"revision":4,"renderingTimeSeconds":12,"statements":[{"language":"english","html":{"status":"OK","sha256":"a","sizeBytes":10},"pdf":{"status":"OK","sha256":"b","sizeBytes":20}}],"tutorials":[]}}"""),
            "problem.packages" => JsonResponse("""{"status":"OK","result":[{"id":9,"revision":4,"creationTimeSeconds":12,"state":"READY","comment":"ok","type":"standard"}]}"""),
            "problem.cautions" => JsonResponse("""{"status":"OK","result":{"common":[{"type":"NO_TAGS","severity":"SOFT","category":"COMMON","message":"No tags","parameters":[]}],"statement":[],"structure":[],"issues":[],"packageReadinessIssues":[],"latestPackageWarnings":[],"ai":{"disabled":true,"statements":[]}}}"""),
            _ => throw new InvalidOperationException(),
        }));
        var client = CreateClient(handler);

        var render = await client.RenderStatementsAsync(77, true);
        var packages = await client.ListPackagesAsync(77);
        var cautions = await client.GetCautionsAsync(77);

        Assert.True(render.Succeeded);
        Assert.Equal(4, render.Revision);
        Assert.Equal("READY", Assert.Single(packages).State);
        Assert.False(cautions.HasBlockingIssues);
        Assert.Equal("NO_TAGS", Assert.Single(cautions.Cautions).Type);
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
