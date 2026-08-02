using Microsoft.Extensions.Logging;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.UnitTests;

public sealed class DailyFileLoggerProviderTests
{
    [Fact]
    public void RedactsRecognizableCredentialShapesBeforeWriting()
    {
        using var temporary = new TemporaryDirectory();
        using var provider = new DailyFileLoggerProvider(temporary.Path);
        var logger = provider.CreateLogger("SecurityTest");
        var openAiKey = "sk-exampleSecretValue123456";
        var googleKey = "AIzaExampleSecretValue1234567890";

        logger.LogInformation("Authorization: Bearer token-value apiSecret=polygon-secret {OpenAi} {Google}",
            openAiKey, googleKey);

        var log = File.ReadAllText(Directory.GetFiles(temporary.Path, "app-*.log").Single());
        Assert.DoesNotContain("token-value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("polygon-secret", log, StringComparison.Ordinal);
        Assert.DoesNotContain(openAiKey, log, StringComparison.Ordinal);
        Assert.DoesNotContain(googleKey, log, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", log, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"polygon-logger-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
