using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class TestConfigurationRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : ITestConfigurationRepository
{
    public async Task<TestConfigurationSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await db.TestConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId, cancellationToken);
        return configuration is null ? null : Map(configuration);
    }

    public async Task<TestConfigurationSnapshot> SaveAsync(
        Guid projectId,
        TestConfigurationUpdate update,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await db.TestConfigurations.SingleOrDefaultAsync(
            item => item.ProblemProjectId == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy test configuration.");
        var testCountChanged = configuration.TestCount != update.TestCount;
        var syncRelevantChanged = testCountChanged
            || !string.Equals(configuration.TestsetName, update.TestsetName.Trim(), StringComparison.Ordinal)
            || configuration.ScorePerTest != update.ScorePerTest
            || !string.Equals(configuration.Checker, update.Checker, StringComparison.Ordinal)
            || !string.Equals(configuration.Script, Normalize(update.Script), StringComparison.Ordinal)
            || configuration.UseSampleInStatement != update.UseSampleInStatement;
        configuration.TestsetName = update.TestsetName.Trim();
        configuration.TestCount = update.TestCount;
        configuration.ScorePerTest = update.ScorePerTest;
        configuration.PointsEnabled = true;
        configuration.Checker = update.Checker;
        configuration.Script = Normalize(update.Script);
        configuration.SampleTestIndex = 1;
        configuration.UseSampleInStatement = update.UseSampleInStatement;
        configuration.CommitMessage = update.CommitMessage.Trim();
        configuration.UpdatedAt = timeProvider.GetUtcNow();
        if (syncRelevantChanged)
        {
            var project = await db.ProblemProjects.SingleAsync(item => item.Id == projectId, cancellationToken);
            project.InvalidateSync(PolygonSyncPhase.GeneratorSaved, configuration.UpdatedAt);
        }

        if (testCountChanged)
        {
            var generator = await db.CodeArtifacts.SingleOrDefaultAsync(
                item => item.ProblemProjectId == projectId && item.Type == CodeArtifactType.Generator,
                cancellationToken);
            if (generator is not null)
            {
                generator.IsStale = true;
                generator.LastCompileStatus = CompileStatus.NotCompiled;
                generator.LastCompileOutput = string.Empty;
            }
            var samples = await db.Samples.Where(item => item.ProblemProjectId == projectId).ToArrayAsync(cancellationToken);
            foreach (var sample in samples)
            {
                sample.InputIsStale = true;
                sample.OutputIsStale = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(configuration);
    }

    private static TestConfigurationSnapshot Map(TestConfiguration value) => new(
        value.ProblemProjectId, value.TestsetName, value.TestCount, value.ScorePerTest,
        value.PointsEnabled, value.Checker, value.Script, value.SampleTestIndex,
        value.UseSampleInStatement, value.CommitMessage, value.UpdatedAt);

    private static string Normalize(string value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
