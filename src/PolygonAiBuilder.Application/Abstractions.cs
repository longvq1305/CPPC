using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public interface IProjectRepository
{
    Task<IReadOnlyList<ProblemProject>> ListAsync(CancellationToken cancellationToken);
    Task<ProblemProject?> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(ProblemProject project, CancellationToken cancellationToken);
    Task UpdateAsync(ProblemProject project, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken);
}

public interface IApplicationSettingsRepository
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken);
    Task SetManyAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken);
}

public interface ISecretStore
{
    string FilePath { get; }
    Task<SecretBundle> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(SecretBundle secrets, CancellationToken cancellationToken);
}

public interface IProjectService
{
    Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<ProjectDetails?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectDetails> CreateAsync(string internalName, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<bool> SetCurrentScreenAsync(Guid projectId, int screen, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SettingsUpdate update, CancellationToken cancellationToken = default);
}
