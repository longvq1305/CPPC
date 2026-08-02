using System.Globalization;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed class ConnectionDiagnosticsService(
    IApplicationSettingsRepository settingsRepository,
    TimeProvider timeProvider) : IConnectionDiagnosticsService
{
    private const string OpenAiPrefix = "Diagnostics.OpenAI";
    private const string GeminiPrefix = "Diagnostics.Gemini";
    private const string PolygonPrefix = "Diagnostics.Polygon";
    private const string PolygonOffsetKey = "Diagnostics.Polygon.ServerTimeOffsetMilliseconds";
    private const int MaximumMessageLength = 500;

    public async Task<ConnectionDiagnosticsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepository.GetAllAsync(cancellationToken);
        return new(
            Read(settings, OpenAiPrefix),
            Read(settings, GeminiPrefix),
            Read(settings, PolygonPrefix),
            ReadOffset(settings));
    }

    public Task RecordAiAsync(
        AiProviderKind provider,
        bool succeeded,
        string message,
        CancellationToken cancellationToken = default) =>
        RecordAsync(provider == AiProviderKind.OpenAI ? OpenAiPrefix : GeminiPrefix,
            succeeded, message, null, cancellationToken);

    public Task RecordPolygonAsync(
        bool succeeded,
        string message,
        TimeSpan? serverTimeOffset = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(PolygonPrefix, succeeded, message, serverTimeOffset, cancellationToken);

    private Task RecordAsync(
        string prefix,
        bool succeeded,
        string message,
        TimeSpan? serverTimeOffset,
        CancellationToken cancellationToken)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message) ? "Không có chi tiết." : message.Trim();
        if (safeMessage.Length > MaximumMessageLength)
        {
            safeMessage = safeMessage[..MaximumMessageLength] + "…";
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{prefix}.Succeeded"] = succeeded.ToString(CultureInfo.InvariantCulture),
            [$"{prefix}.Message"] = safeMessage,
            [$"{prefix}.CheckedAt"] = timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
        };
        if (serverTimeOffset is not null)
        {
            values[PolygonOffsetKey] = serverTimeOffset.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        return settingsRepository.SetManyAsync(values, cancellationToken);
    }

    private static ConnectionDiagnostic Read(IReadOnlyDictionary<string, string> settings, string prefix)
    {
        bool? succeeded = bool.TryParse(settings.GetValueOrDefault($"{prefix}.Succeeded"), out var parsed)
            ? parsed
            : null;
        DateTimeOffset? checkedAt = DateTimeOffset.TryParse(
            settings.GetValueOrDefault($"{prefix}.CheckedAt"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var timestamp)
            ? timestamp
            : null;
        return new(succeeded, settings.GetValueOrDefault($"{prefix}.Message", "Chưa chạy kiểm tra kết nối."), checkedAt);
    }

    private static TimeSpan? ReadOffset(IReadOnlyDictionary<string, string> settings) =>
        double.TryParse(settings.GetValueOrDefault(PolygonOffsetKey), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds)
            : null;
}
