using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class DpapiSecretStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsWithoutPlaintextOnDisk()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var paths = RuntimePaths.Create(temporary.Path);
        var store = new DpapiSecretStore(paths, NullLogger<DpapiSecretStore>.Instance);
        var expected = new SecretBundle(
            "openai-super-secret",
            "gemini-super-secret",
            "polygon-key-secret",
            "polygon-api-secret");

        await store.SaveAsync(expected, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        var diskContent = await File.ReadAllTextAsync(
            store.FilePath,
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(expected, loaded);
        Assert.DoesNotContain(expected.OpenAiApiKey, diskContent, StringComparison.Ordinal);
        Assert.DoesNotContain(expected.GeminiApiKey, diskContent, StringComparison.Ordinal);
        Assert.DoesNotContain(expected.PolygonApiSecret, diskContent, StringComparison.Ordinal);
        Assert.Contains("openAiApiKeyEncrypted", diskContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_WithMalformedCiphertext_ReturnsSafeStoreError()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var paths = RuntimePaths.Create(temporary.Path);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(
            paths.SecretsPath,
            """
            {
              "version": 1,
              "openAiApiKeyEncrypted": "not-base64!"
            }
            """,
            CancellationToken.None);
        var store = new DpapiSecretStore(paths, NullLogger<DpapiSecretStore>.Instance);

        var exception = await Assert.ThrowsAsync<SecretStoreException>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.DoesNotContain("not-base64", exception.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(exception.InnerException);
    }
}
