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

public interface IConversationRepository
{
    Task<AiWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<AiTurnStart> StartTurnAsync(
        Guid projectId,
        string content,
        AiProviderKind provider,
        string model,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken);
    Task AppendAssistantAsync(
        Guid messageId,
        string delta,
        string? providerResponseId,
        CancellationToken cancellationToken);
    Task FinishAssistantAsync(
        Guid messageId,
        MessageStatus status,
        string? providerResponseId,
        string? errorCode,
        string? errorDetails,
        CancellationToken cancellationToken);
    Task SetSelectionAsync(
        Guid projectId,
        AiProviderKind provider,
        string model,
        CancellationToken cancellationToken);
}

public interface IModelCacheRepository
{
    Task<IReadOnlyList<AiModelInfo>> GetAsync(AiProviderKind provider, CancellationToken cancellationToken);
    Task ReplaceAsync(
        AiProviderKind provider,
        IReadOnlyList<AiModelInfo> models,
        CancellationToken cancellationToken);
}

public interface IAttachmentStore
{
    Task<AttachmentInfo> SaveAsync(
        Guid projectId,
        string originalFileName,
        string mimeType,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiAttachmentContent>> LoadContentsAsync(
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default);
    Task<bool> RemovePendingAsync(Guid attachmentId, CancellationToken cancellationToken = default);
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

public interface IGeneralInfoService
{
    Task<GeneralInfoSaveResult> SaveGeneralInfoAsync(
        Guid projectId,
        GeneralInfoDraft draft,
        CancellationToken cancellationToken = default);
    Task<NameAvailabilityResult> CheckNameAndContinueAsync(
        Guid projectId,
        GeneralInfoDraft draft,
        CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SettingsUpdate update, CancellationToken cancellationToken = default);
}

public interface IAiProvider
{
    AiProviderKind Kind { get; }
    Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<AiStreamEvent> StreamChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default);
    Task<T> GenerateStructuredAsync<T>(
        AiStructuredRequest request,
        CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IModelCatalogService
{
    Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(
        AiProviderKind provider,
        bool refresh,
        CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(
        AiProviderKind provider,
        CancellationToken cancellationToken = default);
}

public interface IAiWorkspaceService
{
    Task<AiWorkspaceSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task SetSelectionAsync(
        Guid projectId,
        AiProviderKind provider,
        string model,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<AiChatProgress> SendAsync(
        Guid projectId,
        string content,
        AiProviderKind provider,
        string model,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default);
}

public interface IPolygonClient
{
    Task<IReadOnlyList<PolygonProblem>> ListProblemsAsync(
        string? name,
        CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class IntegrationConfigurationException(string message) : Exception(message);

public sealed class AttachmentValidationException(string message) : Exception(message);

public sealed class ExternalServiceException(
    string service,
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Service { get; } = service;
    public string Code { get; } = code;
}
