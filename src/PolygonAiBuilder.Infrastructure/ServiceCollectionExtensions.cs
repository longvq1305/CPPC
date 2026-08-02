using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPolygonAiBuilderInfrastructure(
        this IServiceCollection services,
        RuntimePaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureDirectories();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString();

        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<BuilderDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IApplicationSettingsRepository, ApplicationSettingsRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IModelCacheRepository, ModelCacheRepository>();
        services.AddScoped<IStatementRepository, StatementRepository>();
        services.AddScoped<IAttachmentStore, AttachmentStore>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IGeneralInfoService, GeneralInfoService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IModelCatalogService, ModelCatalogService>();
        services.AddScoped<IAiWorkspaceService, AiWorkspaceService>();
        services.AddSingleton<ILatexValidator, LatexValidator>();
        services.AddScoped<IStatementService, StatementService>();
        return services;
    }

    public static async Task MigratePolygonAiBuilderDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BuilderDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
