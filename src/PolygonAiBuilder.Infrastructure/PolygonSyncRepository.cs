using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class PolygonSyncRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : IPolygonSyncRepository
{
    public async Task<PolygonSyncSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.ProblemProjects.AsNoTracking()
            .Include(item => item.SyncOperations)
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
        return project is null ? null : Map(project);
    }

    public async Task<Guid> StartOperationAsync(
        Guid projectId,
        PolygonSyncPhase phase,
        string endpoint,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.ProblemProjects.AnyAsync(item => item.Id == projectId, cancellationToken))
            throw new KeyNotFoundException("Không tìm thấy dự án để sync.");
        var operation = new SyncOperationLog
        {
            Id = Guid.NewGuid(),
            ProblemProjectId = projectId,
            Phase = phase,
            Endpoint = endpoint,
            Status = SyncOperationStatus.Started,
            StartedAt = timeProvider.GetUtcNow(),
            RequestFingerprint = requestFingerprint,
            RemoteResultSummary = string.Empty,
        };
        db.SyncOperationLogs.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
        return operation.Id;
    }

    public async Task<PolygonSyncSnapshot> CompleteOperationAsync(
        Guid operationId,
        Guid projectId,
        PolygonSyncPhase phase,
        string summary,
        long? createdProblemId = null,
        int? packageRevision = null,
        int retryCount = 0,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.ProblemProjects.Include(item => item.SyncOperations)
            .SingleAsync(item => item.Id == projectId, cancellationToken);
        var operation = project.SyncOperations.Single(item => item.Id == operationId);
        var now = timeProvider.GetUtcNow();
        if (createdProblemId is { } problemId) project.LinkPolygonProblem(problemId, now);
        if (packageRevision is { } revision) project.MarkPackageReady(revision, now);
        else project.AdvanceSync(phase, now);
        operation.Status = SyncOperationStatus.Succeeded;
        operation.CompletedAt = now;
        operation.RemoteResultSummary = Limit(summary, 4_000);
        operation.RetryCount = retryCount;
        await db.SaveChangesAsync(cancellationToken);
        return Map(project);
    }

    public async Task<PolygonSyncSnapshot> FailOperationAsync(
        Guid operationId,
        Guid projectId,
        PolygonSyncPhase phase,
        string errorCode,
        string errorMessage,
        int retryCount,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.ProblemProjects.Include(item => item.SyncOperations)
            .SingleAsync(item => item.Id == projectId, cancellationToken);
        var operation = project.SyncOperations.Single(item => item.Id == operationId);
        var now = timeProvider.GetUtcNow();
        if (phase == PolygonSyncPhase.PackageFailed) project.AdvanceSync(PolygonSyncPhase.PackageFailed, now);
        else project.MarkSyncFailed(now);
        operation.Status = SyncOperationStatus.Failed;
        operation.CompletedAt = now;
        operation.ErrorCode = Limit(errorCode, 100);
        operation.ErrorMessage = Limit(errorMessage, 4_000);
        operation.RetryCount = retryCount;
        await db.SaveChangesAsync(cancellationToken);
        return Map(project);
    }

    private static PolygonSyncSnapshot Map(ProblemProject project) => new(
        project.Id, project.PolygonProblemId, project.PolygonRevision, project.PolygonSyncPhase,
        project.Status, project.SyncOperations.OrderByDescending(item => item.StartedAt).Select(item => new SyncOperationInfo(
            item.Id, item.Phase, item.Endpoint, item.Status, item.StartedAt, item.CompletedAt,
            item.RemoteResultSummary, item.ErrorCode, item.ErrorMessage, item.RetryCount)).ToArray());

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
