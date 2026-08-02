using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class StatementPersistenceTests
{
    [Fact]
    public async Task VersionsAreImmutable_RestoreCreatesNewVersion_AndCodeBecomesStale()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();
        await provider.MigratePolygonAiBuilderDatabaseAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var statements = scope.ServiceProvider.GetRequiredService<IStatementRepository>();
        var created = await projects.CreateAsync("statement-version-test");

        var first = await statements.SaveAsync(
            created.Id,
            new("First", "Legend one", "Input", "Output", ""),
            ChangeSource.User,
            null,
            null,
            null,
            CancellationToken.None);
        Assert.Equal(1, first.CurrentVersion);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BuilderDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CodeArtifacts.Add(new CodeArtifact
            {
                Id = Guid.NewGuid(),
                ProblemProjectId = created.Id,
                Type = CodeArtifactType.Solution,
                FileName = "solution.cpp",
                Content = "int main(){}",
                Version = 1,
                GeneratedFromStatementVersion = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var second = await statements.SaveAsync(
            created.Id,
            new("Second", "Legend two", "Input", "Output", "Note"),
            ChangeSource.AI,
            "Gemini",
            "gemini-test",
            null,
            CancellationToken.None);

        Assert.Equal(2, second.CurrentVersion);
        Assert.True(second.IsCodeStale);
        Assert.Equal([2, 1], second.History.Select(version => version.VersionNumber));
        Assert.Equal("First", second.History.Single(version => version.VersionNumber == 1).Content.Title);

        var restored = await statements.RestoreAsync(created.Id, 1, CancellationToken.None);

        Assert.Equal(3, restored.CurrentVersion);
        Assert.Equal("First", restored.Content.Title);
        Assert.Equal(3, restored.History.Count);
        await using var verify = await factory.CreateDbContextAsync();
        Assert.True(await verify.CodeArtifacts.Where(item => item.ProblemProjectId == created.Id).AllAsync(item => item.IsStale));
    }
}
