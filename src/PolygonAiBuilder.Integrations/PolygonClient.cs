using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    private sealed class PolygonProblemWire
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public bool Deleted { get; set; }
    }
}
