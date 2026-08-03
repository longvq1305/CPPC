using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed record ProjectSummary(
    Guid Id,
    string InternalName,
    int CurrentScreen,
    ProjectStatus Status,
    PolygonSyncPhase SyncPhase,
    long? PolygonProblemId,
    DateTimeOffset UpdatedAt);

public sealed record ProjectDetails(
    Guid Id,
    string InternalName,
    int CurrentScreen,
    ProjectStatus Status,
    PolygonSyncPhase SyncPhase,
    long? PolygonProblemId,
    string InputFile,
    string OutputFile,
    int TimeLimitMs,
    int MemoryLimitMb,
    AiProviderKind SelectedProvider,
    string SelectedModel,
    DateTimeOffset? NameAvailableCheckedAt,
    DateTimeOffset UpdatedAt);

public sealed record SecretBundle(
    string OpenAiApiKey,
    string GeminiApiKey,
    string PolygonApiKey,
    string PolygonApiSecret)
{
    public static SecretBundle Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

public sealed record SettingsSnapshot(
    bool HasOpenAiApiKey,
    bool HasGeminiApiKey,
    bool HasPolygonApiKey,
    bool HasPolygonApiSecret,
    string OpenAiMasked,
    string GeminiMasked,
    string PolygonApiKeyMasked,
    string PolygonApiSecretMasked,
    string OpenAiDefaultModel,
    string GeminiDefaultModel);

public sealed record SettingsUpdate(
    string? OpenAiApiKey,
    bool ClearOpenAiApiKey,
    string? GeminiApiKey,
    bool ClearGeminiApiKey,
    string? PolygonApiKey,
    bool ClearPolygonApiKey,
    string? PolygonApiSecret,
    bool ClearPolygonApiSecret,
    string OpenAiDefaultModel,
    string GeminiDefaultModel);

public sealed record ValidationIssue(string Field, string Message);

public sealed record GeneralInfoDraft(
    string InternalName,
    string InputFile,
    string OutputFile,
    int TimeLimitMs,
    int MemoryLimitMb);

public sealed record GeneralInfoSaveResult(
    bool Succeeded,
    IReadOnlyList<ValidationIssue> Issues,
    ProjectDetails? Project)
{
    public static GeneralInfoSaveResult Invalid(IReadOnlyList<ValidationIssue> issues) =>
        new(false, issues, null);

    public static GeneralInfoSaveResult Saved(ProjectDetails project) =>
        new(true, [], project);
}

public sealed record PolygonProblem(long Id, string Name, string Owner, bool Deleted);

public sealed record PolygonStatementPayload(
    string Language,
    string Title,
    string Legend,
    string Input,
    string Output,
    string Note);

public sealed record PolygonTestMetadata(
    string Testset,
    int TestCount,
    decimal PointsPerTest,
    int SampleTestIndex,
    bool UseSampleInStatements,
    string SampleInput,
    string SampleOutput);

public sealed record PolygonRenderResult(string Status, string? Message, string? Sha256, long? SizeBytes)
{
    public bool Succeeded => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);
}

public sealed record PolygonRenderedStatement(
    string Language,
    PolygonRenderResult Html,
    PolygonRenderResult Pdf);

public sealed record RenderStatementsResult(
    int Revision,
    long RenderingTimeSeconds,
    IReadOnlyList<PolygonRenderedStatement> Statements)
{
    public bool Succeeded => Statements.Count > 0
        && Statements.All(item => item.Html.Succeeded && item.Pdf.Succeeded);
}

public sealed record PolygonCommitResult(bool Committed, bool ConflictOccurred, string Message);

public sealed record PolygonPackage(
    long Id,
    int Revision,
    long CreationTimeSeconds,
    string State,
    string Comment,
    string Type)
{
    public bool IsTerminal => State is "READY" or "FAILED";
}

public sealed record PolygonCaution(string Type, string Severity, string Category, string Message);
public sealed record PolygonPackageReadinessIssue(string Type, string? Reason, string Message);
public sealed record PolygonCautions(
    IReadOnlyList<PolygonCaution> Cautions,
    IReadOnlyList<PolygonPackageReadinessIssue> PackageReadinessIssues,
    IReadOnlyList<string> LatestPackageWarnings)
{
    public bool HasBlockingIssues => Cautions.Any(item => item.Severity == "HARD") || PackageReadinessIssues.Count > 0;
}

public sealed record SyncOperationInfo(
    Guid Id,
    PolygonSyncPhase Phase,
    string Endpoint,
    SyncOperationStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string RemoteResultSummary,
    string? ErrorCode,
    string? ErrorMessage,
    int RetryCount);

public sealed record PolygonSyncSnapshot(
    Guid ProjectId,
    long? ProblemId,
    int? Revision,
    PolygonSyncPhase Phase,
    ProjectStatus Status,
    IReadOnlyList<SyncOperationInfo> Operations);

public sealed record PolygonSyncProgress(
    PolygonSyncPhase Phase,
    SyncOperationStatus Status,
    string Message,
    PolygonSyncSnapshot Snapshot,
    PolygonPackage? Package = null,
    PolygonCautions? Cautions = null);

public enum AutomatedWorkflowStage
{
    StatementValidated,
    TestsConfigured,
    CodeGenerated,
    SolutionCompiled,
    GeneratorCompiled,
    SampleGenerated,
    PolygonSync,
    Completed,
}

public sealed record AutomatedWorkflowProgress(
    AutomatedWorkflowStage Stage,
    SyncOperationStatus Status,
    string Message,
    PolygonSyncSnapshot? SyncSnapshot = null,
    PolygonPackage? Package = null);

public sealed record ConnectionTestResult(bool Succeeded, string Message, TimeSpan Duration);

public sealed record ConnectionDiagnostic(
    bool? Succeeded,
    string Message,
    DateTimeOffset? CheckedAt);

public sealed record ConnectionDiagnosticsSnapshot(
    ConnectionDiagnostic OpenAi,
    ConnectionDiagnostic Gemini,
    ConnectionDiagnostic Polygon,
    TimeSpan? PolygonServerTimeOffset);

public sealed record NameAvailabilityResult(
    bool Succeeded,
    bool IsAvailable,
    string Message,
    IReadOnlyList<ValidationIssue> Issues,
    ProjectDetails? Project);

public sealed record AiModelInfo(
    AiProviderKind Provider,
    string Id,
    string DisplayName,
    bool SupportsImages,
    bool SupportsDocuments,
    bool SupportsTools,
    DateTimeOffset RefreshedAt,
    bool IsAvailable = true);

public sealed record AttachmentInfo(
    Guid Id,
    string OriginalFileName,
    string MimeType,
    long SizeBytes,
    string Sha256);

public sealed record AiAttachmentContent(
    Guid Id,
    string OriginalFileName,
    string MimeType,
    byte[] Data,
    string? ExtractedText);

public sealed record ConversationMessageInfo(
    Guid Id,
    MessageRole Role,
    string ContentMarkdown,
    string? Provider,
    string? Model,
    MessageStatus Status,
    DateTimeOffset CreatedAt,
    string? ErrorCode,
    string? ErrorDetails,
    IReadOnlyList<AttachmentInfo> Attachments);

public sealed record AiWorkspaceSnapshot(
    Guid ProjectId,
    AiProviderKind SelectedProvider,
    string SelectedModel,
    string RollingSummary,
    IReadOnlyList<ConversationMessageInfo> Messages,
    IReadOnlyList<AttachmentInfo> PendingAttachments);

public sealed record AiTurnStart(
    Guid UserMessageId,
    Guid AssistantMessageId,
    AiWorkspaceSnapshot Workspace);

public sealed record AiChatTurn(
    MessageRole Role,
    string Content,
    IReadOnlyList<AiAttachmentContent> Attachments);

public sealed record AiChatRequest(
    string Model,
    string SystemInstruction,
    IReadOnlyList<AiChatTurn> Turns);

public sealed record AiStructuredRequest(
    string Model,
    string SystemInstruction,
    string Prompt,
    string SchemaName,
    string JsonSchema);

public enum AiStreamEventKind
{
    ResponseStarted,
    TextDelta,
    Completed,
}

public sealed record AiStreamEvent(
    AiStreamEventKind Kind,
    string Text = "",
    string? ProviderResponseId = null);

public sealed record AiChatProgress(
    Guid UserMessageId,
    Guid AssistantMessageId,
    string Delta,
    MessageStatus Status,
    string? ErrorMessage = null);

public sealed record StatementContent(
    string Title,
    string Legend,
    string Input,
    string Output,
    string Note)
{
    public static StatementContent Empty { get; } = new("", "", "", "", "");
}

public sealed record StatementVersionInfo(
    int VersionNumber,
    StatementContent Content,
    ChangeSource ChangedBy,
    string? Provider,
    string? Model,
    DateTimeOffset CreatedAt);

public sealed record StatementSnapshot(
    Guid ProjectId,
    string Language,
    int CurrentVersion,
    StatementContent Content,
    bool IsCodeStale,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<StatementVersionInfo> History);

public enum LatexIssueSeverity
{
    Warning,
    Error,
}

public sealed record LatexIssue(
    string Field,
    LatexIssueSeverity Severity,
    string Message);

public sealed record StatementSaveResult(
    bool Succeeded,
    StatementSnapshot? Statement,
    IReadOnlyList<LatexIssue> Issues,
    string Message)
{
    public bool CanContinue => Statement is not null
        && !string.IsNullOrWhiteSpace(Statement.Content.Title)
        && !string.IsNullOrWhiteSpace(Statement.Content.Legend)
        && !string.IsNullOrWhiteSpace(Statement.Content.Input)
        && !string.IsNullOrWhiteSpace(Statement.Content.Output)
        && Issues.All(issue => issue.Severity != LatexIssueSeverity.Error);
}

public sealed record StatementAiUpdate(
    string? Title,
    string? Legend,
    string? Input,
    string? Output,
    string? Note,
    string ChangeSummary);

public sealed record StatementFieldDiff(
    string Field,
    string Before,
    string After,
    bool Changed);

public sealed record StatementDiff(IReadOnlyList<StatementFieldDiff> Fields)
{
    public bool HasChanges => Fields.Any(item => item.Changed);
}

public sealed record CodeArtifactVersionInfo(
    Guid Id,
    int VersionNumber,
    string Content,
    ChangeSource Source,
    string? Provider,
    string? Model,
    CompileStatus CompileStatus,
    string CompilerOutput,
    DateTimeOffset CreatedAt);

public sealed record CodeArtifactInfo(
    CodeArtifactType Type,
    string FileName,
    string Content,
    int Version,
    int GeneratedFromStatementVersion,
    bool IsStale,
    CompileStatus LastCompileStatus,
    string LastCompileOutput,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CodeArtifactVersionInfo> History);

public sealed record CodeWorkspaceSnapshot(
    Guid ProjectId,
    int StatementVersion,
    bool StatementMarksCodeStale,
    CodeArtifactInfo? Solution,
    CodeArtifactInfo? Generator)
{
    public bool HasBothArtifacts => Solution is not null && Generator is not null;
    public bool HasStaleCode => StatementMarksCodeStale || Solution?.IsStale == true || Generator?.IsStale == true;
}

public sealed record CodeGenerationOutput(
    string SolutionCpp,
    string GeneratorCpp,
    string AlgorithmSummary,
    string TimeComplexity,
    string MemoryComplexity,
    string RecommendedChecker,
    IReadOnlyList<string> AuditNotes);

public sealed record CodeGenerationResult(
    bool Succeeded,
    CodeWorkspaceSnapshot? Workspace,
    string Message,
    IReadOnlyList<string> ValidationErrors,
    string AlgorithmSummary,
    string TimeComplexity,
    string MemoryComplexity,
    string RecommendedChecker,
    IReadOnlyList<string> AuditNotes);

public sealed record CodeRepairOutput(
    string ReplacementCode,
    string Summary);

public sealed record AutoFixResult(
    bool Succeeded,
    CodeWorkspaceSnapshot Workspace,
    CompileArtifactResult LastCompile,
    int Attempts,
    string Message);

public sealed record ProcessExecutionRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    int OutputLimitBytes,
    string? StandardInput = null,
    IReadOnlyDictionary<string, string?>? Environment = null);

public sealed record ProcessExecutionResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Cancelled,
    bool OutputTruncated,
    TimeSpan Duration,
    string? StartError)
{
    public bool Succeeded => Started && ExitCode == 0 && !TimedOut && !Cancelled && StartError is null;
}

public sealed record CompileArtifactResult(
    CodeArtifactType Type,
    CompileStatus Status,
    string FileName,
    string Output,
    TimeSpan Duration,
    string? ExecutablePath)
{
    public bool Succeeded => Status == CompileStatus.Succeeded;
}

public sealed record CompileWorkspaceResult(
    CompileArtifactResult Solution,
    CompileArtifactResult Generator)
{
    public bool Succeeded => Solution.Succeeded && Generator.Succeeded;
}

public sealed record ToolchainStatus(
    bool IsReady,
    string CompilerPath,
    string CompilerVersion,
    bool SupportsGnuCpp17,
    bool HasTestlib,
    bool HasNcmp,
    bool HasWcmp,
    string Message,
    IReadOnlyList<string> Issues);

public sealed record LocalSampleSnapshot(
    Guid Id,
    int TestIndex,
    string Input,
    string Output,
    DateTimeOffset GeneratedAt,
    Guid SolutionVersionId,
    Guid GeneratorVersionId,
    bool InputIsStale,
    bool OutputIsStale,
    bool WasManuallyEdited);

public sealed record SampleGenerationResult(
    bool Succeeded,
    LocalSampleSnapshot? Sample,
    string Message,
    ProcessExecutionResult? GeneratorProcess,
    ProcessExecutionResult? SolutionProcess);

public sealed record TestConfigurationSnapshot(
    Guid ProjectId,
    string TestsetName,
    int TestCount,
    decimal ScorePerTest,
    bool PointsEnabled,
    string Checker,
    string Script,
    int SampleTestIndex,
    bool UseSampleInStatement,
    string CommitMessage,
    DateTimeOffset UpdatedAt);

public sealed record TestConfigurationUpdate(
    string TestsetName,
    int TestCount,
    decimal ScorePerTest,
    string Checker,
    string Script,
    bool UseSampleInStatement,
    string CommitMessage);

public enum AuditSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record AuditIssue(string Check, AuditSeverity Severity, string Message);

public sealed record SelfAuditResult(
    bool Passed,
    DateTimeOffset CompletedAt,
    IReadOnlyList<AuditIssue> Issues)
{
    public string Status => Passed ? "PASSED" : "FAILED";
}

public sealed record SelfAuditAiOutput(
    bool InputOutputDescriptionsMatchCode,
    bool TestOneIsSample,
    bool GeneratorCoversConfiguredRange,
    bool CheckerIsAppropriate,
    bool ComplexityIsReasonable,
    bool OverflowReviewed,
    IReadOnlyList<string> Findings);
