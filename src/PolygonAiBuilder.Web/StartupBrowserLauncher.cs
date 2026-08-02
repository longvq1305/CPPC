using System.ComponentModel;
using System.Diagnostics;

namespace PolygonAiBuilder.Web;

public sealed class StartupBrowserLauncher(
    IConfiguration configuration,
    ILogger<StartupBrowserLauncher> logger)
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    public void Register(IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!configuration.GetValue("Browser:OpenOnStartup", true))
        {
            return;
        }

        var configuredUrl = configuration["Kestrel:Endpoints:Http:Url"];
        if (!TryGetLoopbackUris(configuredUrl, out var applicationUri, out var healthUri))
        {
            logger.LogWarning(
                "The default browser was not opened because the configured HTTP endpoint is not a valid loopback URL.");
            return;
        }

        lifetime.ApplicationStarted.Register(() =>
            _ = OpenAfterHealthCheckAsync(applicationUri, healthUri, lifetime.ApplicationStopping));
    }

    public static bool TryGetLoopbackUris(
        string? configuredUrl,
        out Uri applicationUri,
        out Uri healthUri)
    {
        applicationUri = null!;
        healthUri = null!;
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var candidate)
            || !candidate.IsLoopback
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        applicationUri = new Uri(candidate.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
        healthUri = new Uri(applicationUri, "health");
        return true;
    }

    private async Task OpenAfterHealthCheckAsync(
        Uri applicationUri,
        Uri healthUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var deadline = DateTimeOffset.UtcNow + HealthCheckTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    using var response = await client.GetAsync(healthUri, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        using var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = applicationUri.AbsoluteUri,
                            UseShellExecute = true,
                        });

                        if (process is null)
                        {
                            logger.LogWarning("Windows did not return a process after opening the application URL.");
                        }

                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // The server may still be finishing startup; retry within the bounded window.
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A single health request timed out; retry until the overall deadline.
                }

                await Task.Delay(RetryDelay, cancellationToken);
            }

            logger.LogWarning("The default browser was not opened because the health check did not become ready in time.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown while the health check is pending.
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "The application is healthy, but the default browser could not be opened.");
        }
    }
}
