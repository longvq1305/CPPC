using PolygonAiBuilder.Web;

namespace PolygonAiBuilder.E2ETests;

public sealed class StartupBrowserLauncherTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5187", "http://127.0.0.1:5187/", "http://127.0.0.1:5187/health")]
    [InlineData("http://localhost:9000", "http://localhost:9000/", "http://localhost:9000/health")]
    public void TryGetLoopbackUris_AcceptsLocalHttpEndpoints(
        string configuredUrl,
        string expectedApplicationUrl,
        string expectedHealthUrl)
    {
        var valid = StartupBrowserLauncher.TryGetLoopbackUris(
            configuredUrl,
            out var applicationUri,
            out var healthUri);

        Assert.True(valid);
        Assert.Equal(expectedApplicationUrl, applicationUri.AbsoluteUri);
        Assert.Equal(expectedHealthUrl, healthUri.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("https://example.com")]
    [InlineData("file:///C:/temp/index.html")]
    public void TryGetLoopbackUris_RejectsInvalidOrNonLocalEndpoints(string? configuredUrl)
    {
        var valid = StartupBrowserLauncher.TryGetLoopbackUris(
            configuredUrl,
            out _,
            out _);

        Assert.False(valid);
    }
}
