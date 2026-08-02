using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class ConnectionDiagnosticsServiceTests
{
    [Fact]
    public async Task RecordsOnlyBoundedNonSecretDiagnosticMetadata()
    {
        var repository = new MemorySettingsRepository();
        var service = new ConnectionDiagnosticsService(repository, TimeProvider.System);

        await service.RecordAiAsync(AiProviderKind.OpenAI, false, new string('x', 800));
        await service.RecordPolygonAsync(true, "Polygon OK", TimeSpan.FromMilliseconds(123));
        var snapshot = await service.LoadAsync();

        Assert.False(snapshot.OpenAi.Succeeded);
        Assert.True(snapshot.OpenAi.Message.Length <= 501);
        Assert.True(snapshot.Polygon.Succeeded);
        Assert.Equal(TimeSpan.FromMilliseconds(123), snapshot.PolygonServerTimeOffset);
    }

    private sealed class MemorySettingsRepository : IApplicationSettingsRepository
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(values);

        public Task SetManyAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
        {
            foreach (var pair in settings) values[pair.Key] = pair.Value;
            return Task.CompletedTask;
        }
    }
}
