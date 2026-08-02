namespace PolygonAiBuilder.Domain;

public enum ProjectStatus
{
    Draft,
    Ready,
    Syncing,
    Synced,
    SyncFailed,
}

public enum AiProviderKind
{
    OpenAI,
    Gemini,
}

public enum PolygonSyncPhase
{
    NotCreated,
    NameRechecked,
    ProblemCreated,
    GeneralInfoSaved,
    StatementSaved,
    SolutionSaved,
    GeneratorSaved,
    CheckerUploaded,
    CheckerSelected,
    ScriptSaved,
    PointsEnabled,
    TestMetadataSaved,
    StatementRendered,
    Committed,
    PackageBuildStarted,
    PackageReady,
    PackageFailed,
}

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool,
}

public enum MessageStatus
{
    Streaming,
    Completed,
    Failed,
    Cancelled,
}

public enum ChangeSource
{
    User,
    AI,
    System,
}

public enum CodeArtifactType
{
    Solution,
    Generator,
}

public enum CompileStatus
{
    NotCompiled,
    Compiling,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
}

public enum SyncOperationStatus
{
    Started,
    Succeeded,
    Failed,
}
