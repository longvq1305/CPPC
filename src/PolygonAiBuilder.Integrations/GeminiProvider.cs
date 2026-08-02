using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Integrations;

public sealed class GeminiProvider(
    HttpClient httpClient,
    ISecretStore secretStore,
    TimeProvider timeProvider) : IAiProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    public AiProviderKind Kind => AiProviderKind.Gemini;

    public async Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "v1beta/models?pageSize=1000", null, cancellationToken);
        using var response = await SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("models", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        var now = timeProvider.GetUtcNow();
        return data.EnumerateArray()
            .Select(item => new
            {
                Id = item.TryGetProperty("name", out var name)
                    ? name.GetString()?.Replace("models/", string.Empty, StringComparison.Ordinal)
                    : null,
                Display = item.TryGetProperty("displayName", out var display) ? display.GetString() : null,
            })
            .Where(model => !string.IsNullOrWhiteSpace(model.Id) && IsChatModel(model.Id!))
            .Select(model => new AiModelInfo(
                Kind,
                model.Id!,
                string.IsNullOrWhiteSpace(model.Display) ? model.Id! : model.Display!,
                true,
                true,
                true,
                now))
            .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async IAsyncEnumerable<AiStreamEvent> StreamChatAsync(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request, stream: true);
        using var httpRequest = await CreateRequestAsync(HttpMethod.Post, "v1/interactions", body, cancellationToken);
        using var response = await SendAsync(httpRequest, cancellationToken);
        string? responseId = null;
        await foreach (var document in SseJsonReader.ReadAsync(response.Content, cancellationToken))
        {
            using (document)
            {
                var root = document.RootElement;
                var eventType = root.TryGetProperty("event_type", out var eventTypeProperty)
                    ? eventTypeProperty.GetString()
                    : null;
                if (string.Equals(eventType, "interaction.created", StringComparison.Ordinal)
                    && root.TryGetProperty("interaction", out var interaction)
                    && interaction.TryGetProperty("id", out var id))
                {
                    responseId = id.GetString();
                    yield return new(AiStreamEventKind.ResponseStarted, ProviderResponseId: responseId);
                }
                else if (string.Equals(eventType, "step.delta", StringComparison.Ordinal)
                         && root.TryGetProperty("delta", out var delta)
                         && delta.TryGetProperty("type", out var deltaType)
                         && deltaType.GetString() == "text"
                         && delta.TryGetProperty("text", out var text))
                {
                    yield return new(AiStreamEventKind.TextDelta, text.GetString() ?? string.Empty, responseId);
                }
                else if (string.Equals(eventType, "interaction.completed", StringComparison.Ordinal))
                {
                    if (root.TryGetProperty("interaction", out var completed)
                        && completed.TryGetProperty("id", out var completedId))
                    {
                        responseId = completedId.GetString() ?? responseId;
                    }

                    yield return new(AiStreamEventKind.Completed, ProviderResponseId: responseId);
                }
                else if (string.Equals(eventType, "error", StringComparison.Ordinal))
                {
                    throw new ExternalServiceException(
                        "Gemini",
                        "stream_error",
                        ReadSafeError(root, "Gemini không thể hoàn tất phản hồi."));
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
            ["input"] = request.Prompt,
            ["system_instruction"] = request.SystemInstruction,
            ["store"] = false,
            ["response_format"] = new JsonObject
            {
                ["type"] = "text",
                ["mime_type"] = "application/json",
                ["schema"] = schema,
            },
        };
        using var httpRequest = await CreateRequestAsync(HttpMethod.Post, "v1/interactions", body, cancellationToken);
        using var response = await SendAsync(httpRequest, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = ReadGeminiOutputText(document.RootElement) ?? throw InvalidResponse();
        try
        {
            return JsonSerializer.Deserialize<T>(text, SerializerOptions)
                ?? throw new JsonException("Structured output was null.");
        }
        catch (JsonException exception)
        {
            throw new ExternalServiceException(
                "Gemini",
                "invalid_structured_output",
                "Gemini trả về dữ liệu có cấu trúc không hợp lệ.",
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
            $"Kết nối Gemini thành công; tìm thấy {models.Count} model chat phù hợp.",
            stopwatch.Elapsed);
    }

    private static JsonObject BuildRequestBody(AiChatRequest request, bool stream)
    {
        var input = new JsonArray();
        foreach (var turn in request.Turns)
        {
            var content = new JsonArray();
            if (!string.IsNullOrWhiteSpace(turn.Content))
            {
                content.Add(new JsonObject { ["type"] = "text", ["text"] = turn.Content });
            }

            foreach (var attachment in turn.Attachments)
            {
                if (attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["data"] = Convert.ToBase64String(attachment.Data),
                        ["mime_type"] = attachment.MimeType,
                    });
                }
                else if (string.Equals(attachment.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "document",
                        ["data"] = Convert.ToBase64String(attachment.Data),
                        ["mime_type"] = attachment.MimeType,
                    });
                }
                else if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"\n<attachment name=\"{attachment.OriginalFileName}\">\n{attachment.ExtractedText}\n</attachment>",
                    });
                }
            }

            if (content.Count > 0)
            {
                input.Add(new JsonObject
                {
                    ["type"] = turn.Role == MessageRole.Assistant ? "model_output" : "user_input",
                    ["content"] = content,
                });
            }
        }

        return new JsonObject
        {
            ["model"] = request.Model,
            ["input"] = input,
            ["system_instruction"] = request.SystemInstruction,
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
        if (string.IsNullOrWhiteSpace(secrets.GeminiApiKey))
        {
            throw new IntegrationConfigurationException("Gemini API key chưa được cấu hình trong Settings.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("x-goog-api-key", secrets.GeminiApiKey);
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
                "Gemini",
                "network_error",
                "Không thể kết nối tới Gemini. Hãy kiểm tra mạng và thử lại.",
                exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "Gemini từ chối API key. Hãy kiểm tra credential trong Settings."
                : await ReadHttpErrorAsync(response, cancellationToken)
                    ?? $"Gemini trả về HTTP {(int)response.StatusCode}.";
            throw new ExternalServiceException("Gemini", $"http_{(int)response.StatusCode}", message);
        }
    }

    private static bool IsChatModel(string id) =>
        id.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("embedding", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("image", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("tts", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("audio", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("live", StringComparison.OrdinalIgnoreCase);

    private static string? ReadGeminiOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var step in steps.EnumerateArray().Reverse())
        {
            if (!step.TryGetProperty("type", out var type)
                || type.GetString() != "model_output"
                || !step.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var partType)
                    && partType.GetString() == "text"
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

    private static async Task<string?> ReadHttpErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadSafeError(document.RootElement, string.Empty) is { Length: > 0 } message
                ? message
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExternalServiceException InvalidResponse() =>
        new("Gemini", "invalid_response", "Gemini trả về phản hồi không đúng định dạng.");
}
