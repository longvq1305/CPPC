using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PolygonAiBuilder.Infrastructure;

public sealed partial class DailyFileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 14;
    private const int MaximumEntryLength = 32 * 1024;
    private readonly string logsPath;
    private readonly Lock writeLock = new();
    private DateOnly lastRetentionDate;

    public DailyFileLoggerProvider(string logsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsPath);
        this.logsPath = Path.GetFullPath(logsPath);
        Directory.CreateDirectory(this.logsPath);
    }

    public ILogger CreateLogger(string categoryName) => new DailyFileLogger(this, categoryName);
    public void Dispose() { }

    internal void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (level == LogLevel.None)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            var traceId = Activity.Current?.TraceId.ToString();
            var exceptionSummary = exception is null
                ? string.Empty
                : $" | {exception.GetType().Name}: {exception.Message}";
            var entry = $"{now:O} [{level}] {category} [{eventId.Id}]"
                + (string.IsNullOrWhiteSpace(traceId) ? string.Empty : $" trace={traceId}")
                + $" {message}{exceptionSummary}";
            entry = Redact(entry.ReplaceLineEndings(" "));
            if (entry.Length > MaximumEntryLength)
            {
                entry = entry[..MaximumEntryLength] + " …[truncated]";
            }

            lock (writeLock)
            {
                Directory.CreateDirectory(logsPath);
                File.AppendAllText(Path.Combine(logsPath, $"app-{now:yyyyMMdd}.log"), entry + Environment.NewLine);
                ApplyRetention(DateOnly.FromDateTime(now.LocalDateTime));
            }
        }
        catch (Exception loggingException) when (loggingException is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Daily file logging failed: {loggingException.GetType().Name}");
        }
    }

    internal static string Redact(string value)
    {
        var redacted = BearerRegex().Replace(value, "$1[REDACTED]");
        redacted = CredentialLabelRegex().Replace(redacted, "$1[REDACTED]");
        redacted = OpenAiKeyRegex().Replace(redacted, "[REDACTED_OPENAI_KEY]");
        return GoogleKeyRegex().Replace(redacted, "[REDACTED_GOOGLE_KEY]");
    }

    private void ApplyRetention(DateOnly today)
    {
        if (lastRetentionDate == today)
        {
            return;
        }

        lastRetentionDate = today;
        var cutoff = today.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(logsPath, "app-????????.log", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length == 12
                && DateOnly.TryParseExact(name[4..], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)
                && date < cutoff)
            {
                File.Delete(file);
            }
        }
    }

    [GeneratedRegex("(?i)\\b(apiKey|apiSecret|apiSig|x-goog-api-key|authorization)\\s*[:=]\\s*([^&\\s,;]+)")]
    private static partial Regex CredentialLabelRegex();

    [GeneratedRegex("(?i)(\\bBearer\\s+)[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex BearerRegex();

    [GeneratedRegex("\\bsk-[A-Za-z0-9_-]{8,}\\b")]
    private static partial Regex OpenAiKeyRegex();

    [GeneratedRegex("\\bAIza[A-Za-z0-9_-]{20,}\\b")]
    private static partial Regex GoogleKeyRegex();

    private sealed class DailyFileLogger(DailyFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
