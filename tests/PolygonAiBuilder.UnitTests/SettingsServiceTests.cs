using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.UnitTests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SaveAsync_PreservesExistingSecretWhenReplacementIsBlank()
    {
        var secretStore = new MemorySecretStore
        {
            Secrets = new("existing-openai", "existing-gemini", "polygon-key", "polygon-secret"),
        };
        var settingsRepository = new MemorySettingsRepository();
        var service = new SettingsService(secretStore, settingsRepository);

        await service.SaveAsync(new(
            " ", false,
            "new-gemini", false,
            null, false,
            null, true,
            "openai-model", "gemini-model"));

        Assert.Equal("existing-openai", secretStore.Secrets.OpenAiApiKey);
        Assert.Equal("new-gemini", secretStore.Secrets.GeminiApiKey);
        Assert.Equal("polygon-key", secretStore.Secrets.PolygonApiKey);
        Assert.Empty(secretStore.Secrets.PolygonApiSecret);
        Assert.Equal("openai-model", settingsRepository.Settings[SettingsService.OpenAiDefaultModelKey]);
    }

    [Fact]
    public async Task LoadAsync_ExposesOnlyPresenceAndMasks()
    {
        var secretStore = new MemorySecretStore
        {
            Secrets = new("sensitive-openai", string.Empty, "sensitive-polygon", "sensitive-secret"),
        };
        var service = new SettingsService(secretStore, new MemorySettingsRepository());

        var snapshot = await service.LoadAsync();

        Assert.True(snapshot.HasOpenAiApiKey);
        Assert.False(snapshot.HasGeminiApiKey);
        Assert.Equal("••••••••", snapshot.OpenAiMasked);
        Assert.DoesNotContain("sensitive", snapshot.OpenAiMasked, StringComparison.Ordinal);
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        public string FilePath => "memory";
        public SecretBundle Secrets { get; set; } = SecretBundle.Empty;
        public Task<SecretBundle> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Secrets);

        public Task SaveAsync(SecretBundle secrets, CancellationToken cancellationToken)
        {
            Secrets = secrets;
            return Task.CompletedTask;
        }
    }

    private sealed class MemorySettingsRepository : IApplicationSettingsRepository
    {
        public Dictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(Settings);

        public Task SetManyAsync(
            IReadOnlyDictionary<string, string> settings,
            CancellationToken cancellationToken)
        {
            foreach (var pair in settings)
            {
                Settings[pair.Key] = pair.Value;
            }

            return Task.CompletedTask;
        }
    }
}
