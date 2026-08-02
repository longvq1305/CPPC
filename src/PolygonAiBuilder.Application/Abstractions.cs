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

public interface IStatementRepository
{
    Task<StatementSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<StatementSnapshot> SaveAsync(
        Guid projectId,
        StatementContent content,
        ChangeSource source,
        string? provider,
        string? model,
        Guid? messageId,
        CancellationToken cancellationToken);
    Task<StatementSnapshot> RestoreAsync(
        Guid projectId,
        int versionNumber,
        CancellationToken cancellationToken);
}

public interface ICodeRepository
{
    Task<CodeWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<CodeWorkspaceSnapshot> SaveGeneratedAsync(
        Guid projectId,
        CodeGenerationOutput output,
        int statementVersion,
        string provider,
        string model,
        CancellationToken cancellationToken);
    Task<CodeWorkspaceSnapshot> SaveArtifactAsync(
        Guid projectId,
        CodeArtifactType type,
        string content,
        ChangeSource source,
        int statementVersion,
        string? provider,
        string? model,
        CancellationToken cancellationToken);
    Task MarkCompileAsync(
        Guid projectId,
        CodeArtifactType type,
        CompileStatus status,
        string output,
        CancellationToken cancellationToken);
    Task<CodeWorkspaceSnapshot> RestoreAsync(
        Guid projectId,
        CodeArtifactType type,
        int versionNumber,
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

public interface IConnectionDiagnosticsService
{
    Task<ConnectionDiagnosticsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task RecordAiAsync(
        AiProviderKind provider,
        bool succeeded,
        string message,
        CancellationToken cancellationToken = default);
    Task RecordPolygonAsync(
        bool succeeded,
        string message,
        TimeSpan? serverTimeOffset = null,
        CancellationToken cancellationToken = default);
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

public interface ILatexValidator
{
    IReadOnlyList<LatexIssue> Validate(StatementContent content);
}

public interface IStatementService
{
    Task<StatementSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<StatementSaveResult> SaveUserEditAsync(
        Guid projectId,
        StatementContent content,
        CancellationToken cancellationToken = default);
    Task<StatementSaveResult> ApplyAiUpdateAsync(
        Guid projectId,
        StatementAiUpdate update,
        string provider,
        string model,
        Guid? messageId = null,
        CancellationToken cancellationToken = default);
    Task<StatementSaveResult> GenerateFromConversationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
    Task<StatementSnapshot> RestoreAsync(
        Guid projectId,
        int versionNumber,
        CancellationToken cancellationToken = default);
    StatementDiff Compare(StatementContent before, StatementContent after);
}

public interface ICodeGenerationService
{
    Task<CodeWorkspaceSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<CodeGenerationResult> GenerateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<CodeWorkspaceSnapshot> SaveUserEditAsync(
        Guid projectId,
        CodeArtifactType type,
        string content,
        CancellationToken cancellationToken = default);
    Task<CodeWorkspaceSnapshot> RestoreAsync(
        Guid projectId,
        CodeArtifactType type,
        int versionNumber,
        CancellationToken cancellationToken = default);
    Task<AutoFixResult> AutoFixAsync(
        Guid projectId,
        CodeArtifactType type,
        CancellationToken cancellationToken = default);
}

public interface ICodeCompileService
{
    Task<CompileWorkspaceResult> CompileAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<CompileArtifactResult> CompileArtifactAsync(
        Guid projectId,
        CodeArtifactType type,
        CancellationToken cancellationToken = default);
}

public interface IToolchainService
{
    Task<ToolchainStatus> VerifyAsync(CancellationToken cancellationToken = default);
    Task<ToolchainStatus> RepairAsync(CancellationToken cancellationToken = default);
}

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default);
}

public interface ISampleRepository
{
    Task<LocalSampleSnapshot?> GetAsync(Guid projectId, int testIndex, CancellationToken cancellationToken = default);
    Task<LocalSampleSnapshot> SaveGeneratedAsync(
        Guid projectId,
        int testIndex,
        string input,
        string output,
        CancellationToken cancellationToken = default);
    Task<LocalSampleSnapshot> SaveManualAsync(
        Guid projectId,
        int testIndex,
        string input,
        string output,
        CancellationToken cancellationToken = default);
}

public interface ILocalSampleService
{
    Task<LocalSampleSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<SampleGenerationResult> GenerateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<LocalSampleSnapshot> SaveManualAsync(
        Guid projectId,
        string input,
        string output,
        CancellationToken cancellationToken = default);
}

public interface ITestConfigurationRepository
{
    Task<TestConfigurationSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TestConfigurationSnapshot> SaveAsync(
        Guid projectId,
        TestConfigurationUpdate update,
        CancellationToken cancellationToken = default);
}

public interface ITestConfigurationService
{
    Task<TestConfigurationSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TestConfigurationSnapshot> SaveAsync(
        Guid projectId,
        TestConfigurationUpdate update,
        CancellationToken cancellationToken = default);
    Task<TestConfigurationSnapshot> RegenerateScriptAsync(
        Guid projectId,
        TestConfigurationUpdate update,
        CancellationToken cancellationToken = default);
    string CreateDefaultScript(int testCount);
}

public interface ISelfAuditService
{
    Task<SelfAuditResult> RunAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public interface IPolygonSyncRepository
{
    Task<PolygonSyncSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<Guid> StartOperationAsync(
        Guid projectId,
        PolygonSyncPhase phase,
        string endpoint,
        string requestFingerprint,
        CancellationToken cancellationToken = default);
    Task<PolygonSyncSnapshot> CompleteOperationAsync(
        Guid operationId,
        Guid projectId,
        PolygonSyncPhase phase,
        string summary,
        long? createdProblemId = null,
        int? packageRevision = null,
        int retryCount = 0,
        CancellationToken cancellationToken = default);
    Task<PolygonSyncSnapshot> FailOperationAsync(
        Guid operationId,
        Guid projectId,
        PolygonSyncPhase phase,
        string errorCode,
        string errorMessage,
        int retryCount,
        CancellationToken cancellationToken = default);
}

public interface ICheckerSourceStore
{
    Task<string> ReadAsync(string checkerName, CancellationToken cancellationToken = default);
}

public interface IPolygonSyncService
{
    Task<PolygonSyncSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<PolygonSyncProgress> SyncAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

public interface IPolygonClient
{
    Task<IReadOnlyList<PolygonProblem>> ListProblemsAsync(
        string? name,
        CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<PolygonProblem> CreateProblemAsync(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task UpdateInfoAsync(
        long problemId,
        string inputFile,
        string outputFile,
        int timeLimitMs,
        int memoryLimitMb,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task SaveStatementAsync(long problemId, PolygonStatementPayload statement, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task SaveSolutionAsync(long problemId, string source, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task SaveSourceFileAsync(
        long problemId,
        string name,
        string source,
        string sourceType,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task SetCheckerAsync(long problemId, string checkerName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task SaveScriptAsync(long problemId, string testset, string source, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task EnablePointsAsync(long problemId, bool enabled, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task SaveTestMetadataAsync(long problemId, PolygonTestMetadata metadata, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<RenderStatementsResult> RenderStatementsAsync(
        long problemId,
        bool includeContent,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<PolygonCommitResult> CommitAsync(
        long problemId,
        string? message,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task BuildStandardPackageAsync(long problemId, bool verify, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<IReadOnlyList<PolygonPackage>> ListPackagesAsync(long problemId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<PolygonCautions> GetCautionsAsync(long problemId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
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
