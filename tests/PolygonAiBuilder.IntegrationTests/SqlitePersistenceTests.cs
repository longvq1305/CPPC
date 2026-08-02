using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task MigrationAndRepositories_PersistProjectAcrossScopes()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();

        await provider.MigratePolygonAiBuilderDatabaseAsync(CancellationToken.None);

        Guid projectId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProjectService>();
            var created = await service.CreateAsync("persisted-problem", CancellationToken.None);
            projectId = created.Id;
            Assert.True(await service.SetCurrentScreenAsync(projectId, 3, CancellationToken.None));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProjectService>();
            var loaded = await service.GetAsync(projectId, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal("persisted-problem", loaded.InternalName);
            Assert.Equal(3, loaded.CurrentScreen);
            Assert.Equal("stdin", loaded.InputFile);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BuilderDbContext>>();
            await using var database = await factory.CreateDbContextAsync(CancellationToken.None);
            var migrations = await database.Database.GetAppliedMigrationsAsync(CancellationToken.None);
            Assert.Contains(migrations, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        }
    }
}
