using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.UnitTests;

public sealed class TestConfigurationServiceTests
{
    [Fact]
    public async Task RegenerateScript_UsesConfiguredRangeAndRemoteGenName()
    {
        var repository = new MemoryRepository();
        var service = new TestConfigurationService(repository);
        var update = new TestConfigurationUpdate("tests", 37, 2m, "ncmp.cpp", "custom", true, "");

        var saved = await service.RegenerateScriptAsync(Guid.NewGuid(), update);

        Assert.Contains("1..37", saved.Script, StringComparison.Ordinal);
        Assert.Contains("gen ${i} > $", saved.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("generate", saved.Script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validator.cpp")]
    [InlineData("custom.cpp")]
    public async Task Save_RejectsUnsupportedChecker(string checker)
    {
        var service = new TestConfigurationService(new MemoryRepository());
        var update = new TestConfigurationUpdate("tests", 100, 1m, checker, "gen 1 > $", true, "");

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Guid.NewGuid(), update));
    }

    private sealed class MemoryRepository : ITestConfigurationRepository
    {
        public Task<TestConfigurationSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TestConfigurationSnapshot?>(null);

        public Task<TestConfigurationSnapshot> SaveAsync(
            Guid projectId,
            TestConfigurationUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TestConfigurationSnapshot(projectId, update.TestsetName, update.TestCount,
                update.ScorePerTest, true, update.Checker, update.Script, 1,
                update.UseSampleInStatement, update.CommitMessage, DateTimeOffset.UtcNow));
    }
}
