using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed class ProjectService(IProjectRepository repository, TimeProvider timeProvider) : IProjectService
{
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var projects = await repository.ListAsync(cancellationToken);
        return projects.Select(MapSummary).ToArray();
    }

    public async Task<ProjectDetails?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await repository.GetAsync(projectId, cancellationToken);
        return project is null ? null : MapDetails(project);
    }

    public async Task<ProjectDetails> CreateAsync(
        string internalName,
        CancellationToken cancellationToken = default)
    {
        var project = ProblemProject.Create(internalName, timeProvider.GetUtcNow());
        await repository.AddAsync(project, cancellationToken);
        return MapDetails(project);
    }

    public Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(projectId, cancellationToken);

    public async Task<bool> SetCurrentScreenAsync(
        Guid projectId,
        int screen,
        CancellationToken cancellationToken = default)
    {
        var project = await repository.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return false;
        }

        project.SetCurrentScreen(screen, timeProvider.GetUtcNow());
        await repository.UpdateAsync(project, cancellationToken);
        return true;
    }

    internal static ProjectSummary MapSummary(ProblemProject project) => new(
        project.Id,
        project.InternalName,
        project.CurrentScreen,
        project.Status,
        project.PolygonSyncPhase,
        project.PolygonProblemId,
        project.UpdatedAt);

    internal static ProjectDetails MapDetails(ProblemProject project) => new(
        project.Id,
        project.InternalName,
        project.CurrentScreen,
        project.Status,
        project.PolygonSyncPhase,
        project.PolygonProblemId,
        project.GeneralInfo.InputFile,
        project.GeneralInfo.OutputFile,
        project.GeneralInfo.TimeLimitMs,
        project.GeneralInfo.MemoryLimitMb,
        project.SelectedProvider,
        project.SelectedModel,
        project.NameAvailableCheckedAt,
        project.UpdatedAt);
}
