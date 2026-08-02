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

public sealed record ConnectionTestResult(bool Succeeded, string Message, TimeSpan Duration);

public sealed record NameAvailabilityResult(
    bool Succeeded,
    bool IsAvailable,
    string Message,
    IReadOnlyList<ValidationIssue> Issues,
    ProjectDetails? Project);
