using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class StatementRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : IStatementRepository
{
    public async Task<StatementSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var statement = await db.Statements
            .AsNoTracking()
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId, cancellationToken);
        return statement is null ? null : Map(statement);
    }

    public async Task<StatementSnapshot> SaveAsync(
        Guid projectId,
        StatementContent content,
        ChangeSource source,
        string? provider,
        string? model,
        Guid? messageId,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var statement = await db.Statements
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        var current = ToContent(statement);
        if (current == content)
        {
            return Map(statement);
        }

        var now = timeProvider.GetUtcNow();
        var versionNumber = statement.Versions.Count == 0
            ? 1
            : statement.Versions.Max(version => version.VersionNumber) + 1;
        Apply(statement, content, versionNumber, now);
        await db.StatementVersions.AddAsync(new StatementVersion
        {
            Id = Guid.NewGuid(),
            StatementId = statement.Id,
            VersionNumber = versionNumber,
            Title = content.Title,
            Legend = content.Legend,
            Input = content.Input,
            Output = content.Output,
            Note = content.Note,
            ChangedBy = source,
            Provider = provider,
            Model = model,
            MessageId = messageId,
            CreatedAt = now,
        }, cancellationToken);
        var code = await db.CodeArtifacts
            .Where(artifact => artifact.ProblemProjectId == projectId)
            .ToArrayAsync(cancellationToken);
        foreach (var artifact in code)
        {
            artifact.IsStale = true;
        }

        statement.IsCodeStale = code.Length > 0;
        await db.SaveChangesAsync(cancellationToken);
        return await GetRequiredAsync(db, projectId, cancellationToken);
    }

    public async Task<StatementSnapshot> RestoreAsync(
        Guid projectId,
        int versionNumber,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var statement = await db.Statements
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        var target = statement.Versions.SingleOrDefault(version => version.VersionNumber == versionNumber)
            ?? throw new KeyNotFoundException($"Không tìm thấy statement version {versionNumber}.");
        var content = new StatementContent(target.Title, target.Legend, target.Input, target.Output, target.Note);
        if (ToContent(statement) == content)
        {
            return Map(statement);
        }

        var now = timeProvider.GetUtcNow();
        var newVersion = statement.Versions.Max(version => version.VersionNumber) + 1;
        Apply(statement, content, newVersion, now);
        await db.StatementVersions.AddAsync(new StatementVersion
        {
            Id = Guid.NewGuid(),
            StatementId = statement.Id,
            VersionNumber = newVersion,
            Title = content.Title,
            Legend = content.Legend,
            Input = content.Input,
            Output = content.Output,
            Note = content.Note,
            ChangedBy = ChangeSource.User,
            Provider = target.Provider,
            Model = target.Model,
            CreatedAt = now,
        }, cancellationToken);
        var code = await db.CodeArtifacts
            .Where(artifact => artifact.ProblemProjectId == projectId)
            .ToArrayAsync(cancellationToken);
        foreach (var artifact in code)
        {
            artifact.IsStale = true;
        }

        statement.IsCodeStale = code.Length > 0;
        await db.SaveChangesAsync(cancellationToken);
        return await GetRequiredAsync(db, projectId, cancellationToken);
    }

    private static void Apply(
        Statement statement,
        StatementContent content,
        int versionNumber,
        DateTimeOffset now)
    {
        statement.Title = content.Title;
        statement.Legend = content.Legend;
        statement.Input = content.Input;
        statement.Output = content.Output;
        statement.Note = content.Note;
        statement.CurrentVersion = versionNumber;
        statement.UpdatedAt = now;
    }

    private static StatementContent ToContent(Statement statement) =>
        new(statement.Title, statement.Legend, statement.Input, statement.Output, statement.Note);

    private static StatementSnapshot Map(Statement statement) => new(
        statement.ProblemProjectId,
        statement.Language,
        statement.CurrentVersion,
        ToContent(statement),
        statement.IsCodeStale,
        statement.UpdatedAt,
        statement.Versions
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => new StatementVersionInfo(
                version.VersionNumber,
                new(version.Title, version.Legend, version.Input, version.Output, version.Note),
                version.ChangedBy,
                version.Provider,
                version.Model,
                version.CreatedAt))
            .ToArray());

    private static async Task<StatementSnapshot> GetRequiredAsync(
        BuilderDbContext db,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var statement = await db.Statements
            .AsNoTracking()
            .Include(item => item.Versions)
            .SingleAsync(item => item.ProblemProjectId == projectId, cancellationToken);
        return Map(statement);
    }
}
