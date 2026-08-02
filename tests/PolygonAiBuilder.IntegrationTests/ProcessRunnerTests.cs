using PolygonAiBuilder.Application;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class ProcessRunnerTests
{
    private static readonly string PowerShellPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task RunAsync_UsesArgumentListAndCapturesOutput()
    {
        using var temporary = new TemporaryDirectory();
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new(
            PowerShellPath,
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write('safe output')"],
            temporary.Path,
            TimeSpan.FromSeconds(10),
            1024));

        Assert.True(result.Succeeded);
        Assert.Equal("safe output", result.StandardOutput);
        Assert.False(result.OutputTruncated);
    }

    [Fact]
    public async Task RunAsync_KillsTimedOutProcessTree()
    {
        using var temporary = new TemporaryDirectory();
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new(
            PowerShellPath,
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 10"],
            temporary.Path,
            TimeSpan.FromMilliseconds(250),
            1024));

        Assert.True(result.Started);
        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_StopsWhenCombinedOutputExceedsLimit()
    {
        using var temporary = new TemporaryDirectory();
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new(
            PowerShellPath,
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write('x' * 20000)"],
            temporary.Path,
            TimeSpan.FromSeconds(10),
            1024));

        Assert.True(result.OutputTruncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.StandardOutput + result.StandardError) <= 1024);
    }
}
