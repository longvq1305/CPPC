using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Globalization;
using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Integrations;

public sealed class PolygonClient(
    HttpClient httpClient,
    ISecretStore secretStore,
    TimeProvider timeProvider) : IPolygonClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<PolygonProblem>> ListProblemsAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        var parameters = string.IsNullOrWhiteSpace(name)
            ? Array.Empty<KeyValuePair<string, string>>()
            : [new KeyValuePair<string, string>("name", name.Trim())];
        using var document = await SendAsync("problems.list", parameters, cancellationToken);
        var problems = document.RootElement.GetProperty("result")
            .Deserialize<PolygonProblemWire[]>(SerializerOptions) ?? [];
        return problems
            .Select(problem => new PolygonProblem(problem.Id, problem.Name, problem.Owner, problem.Deleted))
            .ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var probeName = $"__polygon_ai_builder_probe_{Guid.NewGuid():N}";
        await ListProblemsAsync(probeName, cancellationToken);
        stopwatch.Stop();
        return new(true, "Kết nối Polygon thành công.", stopwatch.Elapsed);
    }

    public async Task<PolygonProblem> CreateProblemAsync(string name, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync("problem.create", [Pair("name", name.Trim())], cancellationToken);
        var problem = Result<PolygonProblemWire>(document);
        return new(problem.Id, problem.Name, problem.Owner, problem.Deleted);
    }

    public async Task UpdateInfoAsync(
        long problemId,
        string inputFile,
        string outputFile,
        int timeLimitMs,
        int memoryLimitMb,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.updateInfo", problemId,
        [
            Pair("inputFile", inputFile), Pair("outputFile", outputFile),
            Pair("timeLimit", Invariant(timeLimitMs)), Pair("memoryLimit", Invariant(memoryLimitMb)),
            Pair("interactive", "false"), Pair("wellFormed", "true"),
        ], cancellationToken);
    }

    public async Task SaveStatementAsync(
        long problemId,
        PolygonStatementPayload statement,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.saveStatement", problemId,
        [
            Pair("lang", statement.Language), Pair("encoding", "UTF-8"), Pair("name", statement.Title),
            Pair("legend", statement.Legend), Pair("input", statement.Input), Pair("output", statement.Output),
            Pair("notes", statement.Note),
        ], cancellationToken);
    }

    public async Task SaveSolutionAsync(long problemId, string source, CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.saveSolution", problemId,
        [
            Pair("name", "solution.cpp"), Pair("file", source), Pair("sourceType", "cpp.g++17"), Pair("tag", "MA"),
        ], cancellationToken);
    }

    public async Task SaveSourceFileAsync(
        long problemId,
        string name,
        string source,
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.saveFile", problemId,
        [
            Pair("type", "source"), Pair("name", name), Pair("file", source), Pair("sourceType", sourceType),
        ], cancellationToken);
    }

    public async Task SetCheckerAsync(long problemId, string checkerName, CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.setChecker", problemId,
            [Pair("checker", checkerName)], cancellationToken);
    }

    public async Task SaveScriptAsync(
        long problemId,
        string testset,
        string source,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.saveScript", problemId,
            [Pair("testset", testset), Pair("source", source)], cancellationToken);
    }

    public async Task EnablePointsAsync(long problemId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.enablePoints", problemId,
            [Pair("enable", Boolean(enabled))], cancellationToken);
    }

    public async Task SaveTestMetadataAsync(
        long problemId,
        PolygonTestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        for (var index = 1; index <= metadata.TestCount; index++)
        {
            var parameters = new List<KeyValuePair<string, string>>
            {
                Pair("testset", metadata.Testset), Pair("testIndex", Invariant(index)),
                Pair("testPoints", metadata.PointsPerTest.ToString("0.##", CultureInfo.InvariantCulture)),
            };
            if (index == metadata.SampleTestIndex)
            {
                parameters.Add(Pair("testUseInStatements", Boolean(metadata.UseSampleInStatements)));
                parameters.Add(Pair("testInputForStatements", metadata.SampleInput));
                parameters.Add(Pair("testOutputForStatements", metadata.SampleOutput));
                parameters.Add(Pair("verifyInputOutputForStatements", "true"));
            }
            using var document = await SendProblemAsync("problem.saveTest", problemId, parameters, cancellationToken);
        }
    }

    public async Task<RenderStatementsResult> RenderStatementsAsync(
        long problemId,
        bool includeContent,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.renderStatements", problemId,
            [Pair("includeContent", Boolean(includeContent))], cancellationToken);
        var wire = Result<RenderStatementsWire>(document);
        return new(wire.Revision, wire.RenderingTimeSeconds,
            (wire.Statements ?? []).Select(item => new PolygonRenderedStatement(
                item.Language, Map(item.Html), Map(item.Pdf))).ToArray());
    }

    public async Task<PolygonCommitResult> CommitAsync(
        long problemId,
        string? message,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<KeyValuePair<string, string>> { Pair("minorChanges", "false") };
        if (!string.IsNullOrWhiteSpace(message)) parameters.Add(Pair("message", message.Trim()));
        using var document = await SendProblemAsync("problem.commitChanges", problemId, parameters, cancellationToken);
        var result = Result<CommitResultWire>(document);
        return new(result.Committed, result.ConflictOccurred, result.Message);
    }

    public async Task BuildStandardPackageAsync(
        long problemId,
        bool verify,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.buildPackage", problemId,
            [Pair("full", "false"), Pair("verify", Boolean(verify))], cancellationToken);
    }

    public async Task<IReadOnlyList<PolygonPackage>> ListPackagesAsync(
        long problemId,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.packages", problemId, [], cancellationToken);
        return Result<PackageWire[]>(document).Select(item => new PolygonPackage(
            item.Id, item.Revision, item.CreationTimeSeconds, item.State, item.Comment, item.Type)).ToArray();
    }

    public async Task<PolygonCautions> GetCautionsAsync(
        long problemId,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendProblemAsync("problem.cautions", problemId, [], cancellationToken);
        var result = Result<CautionsWire>(document);
        var cautions = (result.Common ?? []).Concat(result.Statement ?? []).Concat(result.Structure ?? [])
            .Concat(result.Issues ?? []).Select(item => new PolygonCaution(
                item.Type, item.Severity, item.Category, item.Message)).ToArray();
        return new(cautions,
            (result.PackageReadinessIssues ?? []).Select(item => new PolygonPackageReadinessIssue(
                item.Type, item.Reason, item.Message)).ToArray(),
            result.LatestPackageWarnings ?? []);
    }

    private Task<JsonDocument> SendProblemAsync(
        string methodName,
        long problemId,
        IEnumerable<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken) =>
        SendAsync(methodName, parameters.Prepend(Pair("problemId", Invariant(problemId))), cancellationToken);

    private async Task<JsonDocument> SendAsync(
        string methodName,
        IEnumerable<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        var secrets = await secretStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(secrets.PolygonApiKey)
            || string.IsNullOrWhiteSpace(secrets.PolygonApiSecret))
        {
            throw new IntegrationConfigurationException(
                "Polygon API key và API secret chưa được cấu hình trong Settings.");
        }

        var signed = PolygonSignature.Create(
            methodName,
            parameters,
            secrets.PolygonApiKey,
            secrets.PolygonApiSecret,
            timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            PolygonSignature.CreateRandomPrefix());
        var requestBody = $"{signed.CanonicalQuery}&apiSig={PolygonSignature.Encode(signed.ApiSignature)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, methodName)
        {
            Content = new StringContent(requestBody, Encoding.UTF8),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ExternalServiceException(
                "Polygon",
                "network_error",
                "Không thể kết nối tới Polygon. Hãy kiểm tra mạng và thử lại.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ExternalServiceException(
                    "Polygon",
                    "authentication_failed",
                    "Polygon từ chối credential. Hãy kiểm tra API key và API secret.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalServiceException(
                    "Polygon",
                    $"http_{(int)response.StatusCode}",
                    $"Polygon trả về HTTP {(int)response.StatusCode}. Hãy thử lại sau.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("status", out var status)
                    || !string.Equals(status.GetString(), "OK", StringComparison.Ordinal))
                {
                    var safeComment = document.RootElement.TryGetProperty("comment", out var comment)
                        ? Redact(comment.GetString(), secrets)
                        : "Polygon trả về phản hồi không thành công.";
                    document.Dispose();
                    throw new ExternalServiceException(
                        "Polygon",
                        "api_failed",
                        string.IsNullOrWhiteSpace(safeComment)
                            ? "Polygon trả về phản hồi không thành công."
                            : safeComment);
                }

                if (!document.RootElement.TryGetProperty("result", out _))
                {
                    document.Dispose();
                    throw new ExternalServiceException(
                        "Polygon",
                        "invalid_response",
                        "Polygon trả về phản hồi thiếu trường result.");
                }

                return document;
            }
            catch (JsonException exception)
            {
                throw new ExternalServiceException(
                    "Polygon",
                    "invalid_json",
                    "Không thể đọc phản hồi JSON từ Polygon.",
                    exception);
            }
        }
    }

    private static string? Redact(string? value, SecretBundle secrets)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value
            .Replace(secrets.PolygonApiKey, "[REDACTED]", StringComparison.Ordinal)
            .Replace(secrets.PolygonApiSecret, "[REDACTED]", StringComparison.Ordinal);
    }

    private static T Result<T>(JsonDocument document) =>
        document.RootElement.GetProperty("result").Deserialize<T>(SerializerOptions)
        ?? throw new ExternalServiceException("Polygon", "invalid_response", "Polygon trả về result không đúng cấu trúc.");

    private static PolygonRenderResult Map(RenderResultWire? value) => value is null
        ? new("FAILED", "Polygon không trả render result.", null, null)
        : new(value.Status, value.Message, value.Sha256, value.SizeBytes);

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);
    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed class PolygonProblemWire
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public bool Deleted { get; set; }
    }

    private sealed class RenderStatementsWire
    {
        public int Revision { get; set; }
        public long RenderingTimeSeconds { get; set; }
        public RenderedStatementWire[]? Statements { get; set; }
    }
    private sealed class RenderedStatementWire
    {
        public string Language { get; set; } = string.Empty;
        public RenderResultWire? Html { get; set; }
        public RenderResultWire? Pdf { get; set; }
    }
    private sealed class RenderResultWire
    {
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? Sha256 { get; set; }
        public long? SizeBytes { get; set; }
    }
    private sealed class CommitResultWire
    {
        public bool Committed { get; set; }
        public bool ConflictOccurred { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    private sealed class PackageWire
    {
        public long Id { get; set; }
        public int Revision { get; set; }
        public long CreationTimeSeconds { get; set; }
        public string State { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
    private sealed class CautionWire
    {
        public string Type { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
    private sealed class ReadinessIssueWire
    {
        public string Type { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    private sealed class CautionsWire
    {
        public CautionWire[]? Common { get; set; }
        public CautionWire[]? Statement { get; set; }
        public CautionWire[]? Structure { get; set; }
        public CautionWire[]? Issues { get; set; }
        public ReadinessIssueWire[]? PackageReadinessIssues { get; set; }
        public string[]? LatestPackageWarnings { get; set; }
    }
}
