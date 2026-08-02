using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class ProjectRepository(IDbContextFactory<BuilderDbContext> contextFactory) : IProjectRepository
{
    public async Task<IReadOnlyList<ProblemProject>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProblemProjects
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProblemProject?> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProblemProjects
            .AsNoTracking()
            .Include(x => x.GeneralInfo)
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);
    }

    public async Task AddAsync(ProblemProject project, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.ProblemProjects.AddAsync(project, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ProblemProject project, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.ProblemProjects.Update(project);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.ProblemProjects
            .Where(x => x.Id == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }
}

public sealed class ApplicationSettingsRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : IApplicationSettingsRepository
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ApplicationSettings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.Ordinal, cancellationToken);
    }

    public async Task SetManyAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var keys = settings.Keys.ToArray();
        var existing = await db.ApplicationSettings
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, cancellationToken);
        var now = timeProvider.GetUtcNow();

        foreach (var pair in settings)
        {
            if (existing.TryGetValue(pair.Key, out var setting))
            {
                setting.Value = pair.Value;
                setting.UpdatedAt = now;
            }
            else
            {
                await db.ApplicationSettings.AddAsync(
                    new ApplicationSetting { Key = pair.Key, Value = pair.Value, UpdatedAt = now },
                    cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
