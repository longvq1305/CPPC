using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Integrations;

public sealed class OpenAiProvider(
    HttpClient httpClient,
    ISecretStore secretStore,
    TimeProvider timeProvider) : IAiProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    public AiProviderKind Kind => AiProviderKind.OpenAI;

    public async Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "v1/models", null, cancellationToken);
        using var response = await SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        var now = timeProvider.GetUtcNow();
        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id) && IsChatModel(id!))
            .Select(id => new AiModelInfo(
                Kind,
                id!,
                id!,
                SupportsImages(id!),
                true,
                true,
                now))
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async IAsyncEnumerable<AiStreamEvent> StreamChatAsync(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request, stream: true);
        using var httpRequest = await CreateRequestAsync(HttpMethod.Post, "v1/responses", body, cancellationToken);
        using var response = await SendAsync(httpRequest, cancellationToken);
        string? responseId = null;
        await foreach (var document in SseJsonReader.ReadAsync(response.Content, cancellationToken))
        {
            using (document)
            {
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeProperty)
                    ? typeProperty.GetString()
                    : null;
                if (string.Equals(type, "response.created", StringComparison.Ordinal)
                    && root.TryGetProperty("response", out var createdResponse)
                    && createdResponse.TryGetProperty("id", out var createdId))
                {
                    responseId = createdId.GetString();
                    yield return new(AiStreamEventKind.ResponseStarted, ProviderResponseId: responseId);
                }
                else if (string.Equals(type, "response.output_text.delta", StringComparison.Ordinal)
                         && root.TryGetProperty("delta", out var delta)
                         && delta.ValueKind == JsonValueKind.String)
                {
                    yield return new(AiStreamEventKind.TextDelta, delta.GetString() ?? string.Empty, responseId);
                }
                else if (string.Equals(type, "response.completed", StringComparison.Ordinal))
                {
                    if (root.TryGetProperty("response", out var completedResponse)
                        && completedResponse.TryGetProperty("id", out var completedId))
                    {
                        responseId = completedId.GetString() ?? responseId;
                    }

                    yield return new(AiStreamEventKind.Completed, ProviderResponseId: responseId);
                }
                else if (string.Equals(type, "error", StringComparison.Ordinal)
                         || string.Equals(type, "response.failed", StringComparison.Ordinal)
                         || string.Equals(type, "response.incomplete", StringComparison.Ordinal))
                {
                    throw new ExternalServiceException(
                        "OpenAI",
                        type ?? "stream_error",
                        ReadSafeError(root, "OpenAI không thể hoàn tất phản hồi."));
                }
            }
        }
    }

    public async Task<T> GenerateStructuredAsync<T>(
        AiStructuredRequest request,
        CancellationToken cancellationToken = default)
    {
        var schema = JsonNode.Parse(request.JsonSchema)
            ?? throw new ArgumentException("JSON Schema không hợp lệ.", nameof(request));
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["instructions"] = request.SystemInstruction,
            ["input"] = request.Prompt,
            ["store"] = false,
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = request.SchemaName,
                    ["schema"] = schema,
                    ["strict"] = true,
                },
            },
        };
        using var httpRequest = await CreateRequestAsync(HttpMethod.Post, "v1/responses", body, cancellationToken);
        using var response = await SendAsync(httpRequest, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = ReadOpenAiOutputText(document.RootElement)
            ?? throw InvalidResponse();
        try
        {
            return JsonSerializer.Deserialize<T>(text, SerializerOptions)
                ?? throw new JsonException("Structured output was null.");
        }
        catch (JsonException exception)
        {
            throw new ExternalServiceException(
                "OpenAI",
                "invalid_structured_output",
                "OpenAI trả về dữ liệu có cấu trúc không hợp lệ.",
                exception);
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var models = await ListModelsAsync(cancellationToken);
        stopwatch.Stop();
        return new(
            true,
            $"Kết nối OpenAI thành công; tìm thấy {models.Count} model chat phù hợp.",
            stopwatch.Elapsed);
    }

    private static JsonObject BuildRequestBody(AiChatRequest request, bool stream)
    {
        var input = new JsonArray();
        foreach (var turn in request.Turns)
        {
            var role = turn.Role == MessageRole.Assistant ? "assistant" : "user";
            if (turn.Attachments.Count == 0)
            {
                input.Add(new JsonObject { ["role"] = role, ["content"] = turn.Content });
                continue;
            }

            var content = new JsonArray { new JsonObject { ["type"] = "input_text", ["text"] = turn.Content } };
            foreach (var attachment in turn.Attachments)
            {
                if (attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "input_image",
                        ["image_url"] = $"data:{attachment.MimeType};base64,{Convert.ToBase64String(attachment.Data)}",
                    });
                }
                else if (string.Equals(attachment.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "input_file",
                        ["filename"] = attachment.OriginalFileName,
                        ["file_data"] = $"data:{attachment.MimeType};base64,{Convert.ToBase64String(attachment.Data)}",
                    });
                }
                else if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = $"\n<attachment name=\"{attachment.OriginalFileName}\">\n{attachment.ExtractedText}\n</attachment>",
                    });
                }
            }

            input.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }

        return new JsonObject
        {
            ["model"] = request.Model,
            ["instructions"] = request.SystemInstruction,
            ["input"] = input,
            ["stream"] = stream,
            ["store"] = false,
        };
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        var secrets = await secretStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(secrets.OpenAiApiKey))
        {
            throw new IntegrationConfigurationException("OpenAI API key chưa được cấu hình trong Settings.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.OpenAiApiKey);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
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
                "OpenAI",
                "network_error",
                "Không thể kết nối tới OpenAI. Hãy kiểm tra mạng và thử lại.",
                exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "OpenAI từ chối API key. Hãy kiểm tra credential trong Settings."
                : $"OpenAI trả về HTTP {(int)response.StatusCode}.";
            throw new ExternalServiceException("OpenAI", $"http_{(int)response.StatusCode}", message);
        }
    }

    private static bool IsChatModel(string id) =>
        (id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
         || id.StartsWith("o", StringComparison.OrdinalIgnoreCase))
        && !id.Contains("audio", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("realtime", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("transcribe", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("tts", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("image", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("search", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("moderation", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("instruct", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("embedding", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsImages(string id) =>
        !id.StartsWith("o1-mini", StringComparison.OrdinalIgnoreCase)
        && !id.StartsWith("o3-mini", StringComparison.OrdinalIgnoreCase);

    private static string? ReadOpenAiOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && part.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static string ReadSafeError(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString() ?? fallback;
        }

        return fallback;
    }

    private static ExternalServiceException InvalidResponse() =>
        new("OpenAI", "invalid_response", "OpenAI trả về phản hồi không đúng định dạng.");
}
