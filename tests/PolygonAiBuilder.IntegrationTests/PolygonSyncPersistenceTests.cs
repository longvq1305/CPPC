using Microsoft.Extensions.DependencyInjection;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class PolygonSyncPersistenceTests
{
    [Fact]
    public async Task CreatedProblemIdAndLastSuccessfulPhaseSurviveFailureAndResume()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();
        await provider.MigratePolygonAiBuilderDatabaseAsync();
        await using var scope = provider.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var sync = scope.ServiceProvider.GetRequiredService<IPolygonSyncRepository>();
        var project = await projects.CreateAsync("sync-resume-test");

        var createOperation = await sync.StartOperationAsync(project.Id, PolygonSyncPhase.ProblemCreated,
            "problem.create", "fingerprint");
        var created = await sync.CompleteOperationAsync(createOperation, project.Id,
            PolygonSyncPhase.ProblemCreated, "created", createdProblemId: 9123);
        Assert.Equal(9123, created.ProblemId);
        Assert.Equal(PolygonSyncPhase.ProblemCreated, created.Phase);

        var failedOperation = await sync.StartOperationAsync(project.Id, PolygonSyncPhase.GeneralInfoSaved,
            "problem.updateInfo", "fingerprint-2");
        var failed = await sync.FailOperationAsync(failedOperation, project.Id,
            PolygonSyncPhase.GeneralInfoSaved, "network_error", "offline", 3);
        Assert.Equal(9123, failed.ProblemId);
        Assert.Equal(PolygonSyncPhase.ProblemCreated, failed.Phase);
        Assert.Equal(ProjectStatus.SyncFailed, failed.Status);

        var resumeOperation = await sync.StartOperationAsync(project.Id, PolygonSyncPhase.GeneralInfoSaved,
            "problem.updateInfo", "fingerprint-2");
        var resumed = await sync.CompleteOperationAsync(resumeOperation, project.Id,
            PolygonSyncPhase.GeneralInfoSaved, "saved", retryCount: 0);
        Assert.Equal(PolygonSyncPhase.GeneralInfoSaved, resumed.Phase);
        Assert.Contains(resumed.Operations, item => item.Status == SyncOperationStatus.Failed && item.RetryCount == 3);
    }

    [Fact]
    public async Task StatementChangeInvalidatesOnlyDownstreamRemotePhases()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();
        await provider.MigratePolygonAiBuilderDatabaseAsync();
        await using var scope = provider.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var sync = scope.ServiceProvider.GetRequiredService<IPolygonSyncRepository>();
        var statements = scope.ServiceProvider.GetRequiredService<IStatementRepository>();
        var project = await projects.CreateAsync("sync-invalidation-test");
        var create = await sync.StartOperationAsync(project.Id, PolygonSyncPhase.ProblemCreated, "problem.create", "x");
        await sync.CompleteOperationAsync(create, project.Id, PolygonSyncPhase.ProblemCreated, "created", createdProblemId: 99);
        foreach (var phase in new[] { PolygonSyncPhase.GeneralInfoSaved, PolygonSyncPhase.StatementSaved, PolygonSyncPhase.SolutionSaved })
        {
            var operation = await sync.StartOperationAsync(project.Id, phase, phase.ToString(), "x");
            await sync.CompleteOperationAsync(operation, project.Id, phase, "ok");
        }

        await statements.SaveAsync(project.Id, new("Title", "Legend", "Input", "Output", ""),
            ChangeSource.User, null, null, null, CancellationToken.None);

        var invalidated = await sync.GetAsync(project.Id);
        Assert.NotNull(invalidated);
        Assert.Equal(PolygonSyncPhase.GeneralInfoSaved, invalidated.Phase);
        Assert.Equal(99, invalidated.ProblemId);
    }
}
