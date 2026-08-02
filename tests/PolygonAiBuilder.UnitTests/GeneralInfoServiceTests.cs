using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class GeneralInfoServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckNameAndContinue_WhenAvailable_MarksCheckAndMovesToStepTwo()
    {
        var project = ProblemProject.Create("new-problem", Now);
        var repository = new MemoryProjectRepository(project);
        var polygon = new FakePolygonClient([]);
        var service = new GeneralInfoService(repository, polygon, new FixedTimeProvider(Now));

        var result = await service.CheckNameAndContinueAsync(
            project.Id,
            new("new-problem", "stdin", "stdout", 1000, 256));

        Assert.True(result.Succeeded);
        Assert.True(result.IsAvailable);
        Assert.Equal(2, project.CurrentScreen);
        Assert.NotNull(project.NameAvailableCheckedAt);
        Assert.Equal(1, polygon.ListCallCount);
    }

    [Fact]
    public async Task CheckNameAndContinue_WhenExactRemoteNameExists_DoesNotAdvance()
    {
        var project = ProblemProject.Create("existing-problem", Now);
        var service = new GeneralInfoService(
            new MemoryProjectRepository(project),
            new FakePolygonClient([new(42, "existing-problem", "owner", false)]),
            new FixedTimeProvider(Now));

        var result = await service.CheckNameAndContinueAsync(
            project.Id,
            new("existing-problem", "stdin", "stdout", 1000, 256));

        Assert.True(result.Succeeded);
        Assert.False(result.IsAvailable);
        Assert.Equal(1, project.CurrentScreen);
        Assert.Null(project.NameAvailableCheckedAt);
    }

    [Fact]
    public async Task SaveGeneralInfo_WhenInvalid_DoesNotPersistOrCallPolygon()
    {
        var project = ProblemProject.Create("draft", Now);
        var repository = new MemoryProjectRepository(project);
        var polygon = new FakePolygonClient([]);
        var service = new GeneralInfoService(repository, polygon, new FixedTimeProvider(Now));

        var result = await service.SaveGeneralInfoAsync(
            project.Id,
            new("draft", "same", "SAME", 275, 2));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Issues);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(0, polygon.ListCallCount);
    }

    private sealed class MemoryProjectRepository(ProblemProject project) : IProjectRepository
    {
        public int UpdateCallCount { get; private set; }
        public Task<IReadOnlyList<ProblemProject>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProblemProject>>([project]);
        public Task<ProblemProject?> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<ProblemProject?>(project.Id == projectId ? project : null);
        public Task AddAsync(ProblemProject value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task UpdateAsync(ProblemProject value, CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePolygonClient(IReadOnlyList<PolygonProblem> problems) : IPolygonClient
    {
        public int ListCallCount { get; private set; }
        public Task<IReadOnlyList<PolygonProblem>> ListProblemsAsync(string? name, CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            return Task.FromResult(problems);
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult(true, "ok", TimeSpan.Zero));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
