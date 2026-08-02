namespace PolygonAiBuilder.Domain;

public sealed class ProblemProject
{
    private ProblemProject()
    {
    }

    public Guid Id { get; private set; }
    public string InternalName { get; private set; } = string.Empty;
    public ProjectStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int CurrentScreen { get; private set; }
    public long? PolygonProblemId { get; private set; }
    public int? PolygonRevision { get; private set; }
    public PolygonSyncPhase PolygonSyncPhase { get; private set; }
    public AiProviderKind SelectedProvider { get; private set; }
    public string SelectedModel { get; private set; } = string.Empty;
    public DateTimeOffset? NameAvailableCheckedAt { get; private set; }

    public GeneralInfo GeneralInfo { get; private set; } = null!;
    public Statement Statement { get; private set; } = null!;
    public Conversation Conversation { get; private set; } = null!;
    public TestConfiguration TestConfiguration { get; private set; } = null!;
    public ICollection<CodeArtifact> CodeArtifacts { get; } = [];
    public ICollection<Attachment> Attachments { get; } = [];
    public ICollection<Sample> Samples { get; } = [];
    public ICollection<SyncOperationLog> SyncOperations { get; } = [];

    public static ProblemProject Create(string internalName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);
        var normalizedName = internalName.Trim();
        if (normalizedName.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(internalName), "Problem name must not exceed 128 characters.");
        }

        var projectId = Guid.NewGuid();
        var project = new ProblemProject
        {
            Id = projectId,
            InternalName = normalizedName,
            Status = ProjectStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            CurrentScreen = 1,
            PolygonSyncPhase = PolygonSyncPhase.NotCreated,
            SelectedProvider = AiProviderKind.OpenAI,
        };

        project.GeneralInfo = GeneralInfo.Create(projectId);
        project.Statement = Statement.Create(projectId, now);
        project.Conversation = Conversation.Create(projectId, now);
        project.TestConfiguration = TestConfiguration.Create(projectId, now);
        return project;
    }

    public void SetCurrentScreen(int screen, DateTimeOffset now)
    {
        if (screen is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(screen), "Workflow screen must be between 1 and 5.");
        }

        CurrentScreen = screen;
        UpdatedAt = now;
    }

    public void SetModel(AiProviderKind provider, string model, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        SelectedProvider = provider;
        SelectedModel = model.Trim();
        UpdatedAt = now;
    }

    public void MarkNameAvailable(DateTimeOffset now)
    {
        NameAvailableCheckedAt = now;
        UpdatedAt = now;
    }

    public void UpdateGeneralInfo(
        string internalName,
        string inputFile,
        string outputFile,
        int timeLimitMs,
        int memoryLimitMb,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);

        var normalizedName = internalName.Trim();
        var normalizedInput = inputFile.Trim();
        var normalizedOutput = outputFile.Trim();
        var remoteInfoChanged = !string.Equals(GeneralInfo.InputFile, normalizedInput, StringComparison.Ordinal)
            || !string.Equals(GeneralInfo.OutputFile, normalizedOutput, StringComparison.Ordinal)
            || GeneralInfo.TimeLimitMs != timeLimitMs
            || GeneralInfo.MemoryLimitMb != memoryLimitMb;
        if (!string.Equals(InternalName, normalizedName, StringComparison.Ordinal))
        {
            InternalName = normalizedName;
            NameAvailableCheckedAt = null;
        }

        GeneralInfo.Update(normalizedInput, normalizedOutput, timeLimitMs, memoryLimitMb);
        if (remoteInfoChanged) InvalidateSync(PolygonSyncPhase.ProblemCreated, now);
        UpdatedAt = now;
    }

    public void LinkPolygonProblem(long problemId, DateTimeOffset now)
    {
        if (problemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(problemId));
        }

        if (PolygonProblemId is not null && PolygonProblemId != problemId)
        {
            throw new InvalidOperationException("This project is already linked to another Polygon problem.");
        }

        PolygonProblemId = problemId;
        PolygonSyncPhase = PolygonSyncPhase.ProblemCreated;
        Status = ProjectStatus.Syncing;
        UpdatedAt = now;
    }

    public void AdvanceSync(PolygonSyncPhase phase, DateTimeOffset now)
    {
        if (phase < PolygonSyncPhase
            && !(PolygonSyncPhase == PolygonSyncPhase.PackageFailed
                 && phase is PolygonSyncPhase.Committed or PolygonSyncPhase.PackageBuildStarted or PolygonSyncPhase.PackageReady))
        {
            throw new InvalidOperationException("Polygon synchronization cannot move backwards without explicit invalidation.");
        }

        PolygonSyncPhase = phase;
        Status = phase switch
        {
            PolygonSyncPhase.PackageReady => ProjectStatus.Synced,
            PolygonSyncPhase.PackageFailed => ProjectStatus.SyncFailed,
            _ => ProjectStatus.Syncing,
        };
        UpdatedAt = now;
    }

    public void InvalidateSync(PolygonSyncPhase lastValidPhase, DateTimeOffset now)
    {
        if (PolygonProblemId is null || PolygonSyncPhase <= lastValidPhase) return;
        PolygonSyncPhase = lastValidPhase;
        Status = ProjectStatus.Syncing;
        UpdatedAt = now;
    }

    public void MarkPackageReady(int revision, DateTimeOffset now)
    {
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        PolygonRevision = revision;
        AdvanceSync(PolygonSyncPhase.PackageReady, now);
    }

    public void MarkSyncFailed(DateTimeOffset now)
    {
        if (PolygonSyncPhase != PolygonSyncPhase.PackageReady)
        {
            Status = ProjectStatus.SyncFailed;
            UpdatedAt = now;
        }
    }
}

public sealed class GeneralInfo
{
    private GeneralInfo()
    {
    }

    public Guid ProblemProjectId { get; private set; }
    public string InputFile { get; set; } = "stdin";
    public string OutputFile { get; set; } = "stdout";
    public int TimeLimitMs { get; set; } = 1000;
    public int MemoryLimitMb { get; set; } = 256;
    public ProblemProject ProblemProject { get; private set; } = null!;

    internal static GeneralInfo Create(Guid projectId) => new() { ProblemProjectId = projectId };

    internal void Update(string inputFile, string outputFile, int timeLimitMs, int memoryLimitMb)
    {
        InputFile = inputFile;
        OutputFile = outputFile;
        TimeLimitMs = timeLimitMs;
        MemoryLimitMb = memoryLimitMb;
    }
}

public sealed class Statement
{
    private Statement()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProblemProjectId { get; private set; }
    public string Language { get; private set; } = "english";
    public string Title { get; set; } = string.Empty;
    public string Legend { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int CurrentVersion { get; set; }
    public bool IsCodeStale { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ProblemProject ProblemProject { get; private set; } = null!;
    public ICollection<StatementVersion> Versions { get; } = [];

    internal static Statement Create(Guid projectId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProblemProjectId = projectId,
        UpdatedAt = now,
    };
}

public sealed class StatementVersion
{
    public Guid Id { get; set; }
    public Guid StatementId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Legend { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public ChangeSource ChangedBy { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public Guid? MessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Statement Statement { get; set; } = null!;
}

public sealed class Conversation
{
    private Conversation()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProblemProjectId { get; private set; }
    public string RollingSummary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ProblemProject ProblemProject { get; private set; } = null!;
    public ICollection<ConversationMessage> Messages { get; } = [];

    internal static Conversation Create(Guid projectId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProblemProjectId = projectId,
        CreatedAt = now,
        UpdatedAt = now,
    };
}

public sealed class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string ContentMarkdown { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public MessageStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ParentMessageId { get; set; }
    public string? ProviderResponseId { get; set; }
    public string? StructuredActionsJson { get; set; }
    public Guid? StatementVersionId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetails { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public ICollection<Attachment> Attachments { get; } = [];
}

public sealed class Attachment
{
    public Guid Id { get; set; }
    public Guid ProblemProjectId { get; set; }
    public Guid? MessageId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? ExtractedTextPath { get; set; }
    public string? ProviderFileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ProblemProject ProblemProject { get; set; } = null!;
    public ConversationMessage? Message { get; set; }
}

public sealed class CodeArtifact
{
    public Guid Id { get; set; }
    public Guid ProblemProjectId { get; set; }
    public CodeArtifactType Type { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Version { get; set; }
    public int GeneratedFromStatementVersion { get; set; }
    public bool IsStale { get; set; }
    public CompileStatus LastCompileStatus { get; set; }
    public string LastCompileOutput { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public ProblemProject ProblemProject { get; set; } = null!;
    public ICollection<CodeArtifactVersion> Versions { get; } = [];
}

public sealed class CodeArtifactVersion
{
    public Guid Id { get; set; }
    public Guid CodeArtifactId { get; set; }
    public int VersionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public ChangeSource Source { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public CompileStatus CompileStatus { get; set; }
    public string CompilerOutput { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public CodeArtifact CodeArtifact { get; set; } = null!;
}

public sealed class TestConfiguration
{
    private TestConfiguration()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProblemProjectId { get; private set; }
    public string TestsetName { get; set; } = "tests";
    public int TestCount { get; set; } = 100;
    public decimal ScorePerTest { get; set; } = 1m;
    public bool PointsEnabled { get; set; } = true;
    public string Checker { get; set; } = "ncmp.cpp";
    public string Script { get; set; } = DefaultScript;
    public int SampleTestIndex { get; set; } = 1;
    public bool UseSampleInStatement { get; set; } = true;
    public string CommitMessage { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public ProblemProject ProblemProject { get; private set; } = null!;

    public const string DefaultScript = "<#list 1..100 as i>\n    gen ${i} > $\n</#list>";

    internal static TestConfiguration Create(Guid projectId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProblemProjectId = projectId,
        UpdatedAt = now,
    };
}

public sealed class Sample
{
    public Guid Id { get; set; }
    public Guid ProblemProjectId { get; set; }
    public int TestIndex { get; set; }
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public Guid SolutionVersionId { get; set; }
    public Guid GeneratorVersionId { get; set; }
    public bool InputIsStale { get; set; }
    public bool OutputIsStale { get; set; }
    public bool WasManuallyEdited { get; set; }
    public ProblemProject ProblemProject { get; set; } = null!;
}

public sealed class SyncOperationLog
{
    public Guid Id { get; set; }
    public Guid ProblemProjectId { get; set; }
    public PolygonSyncPhase Phase { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public SyncOperationStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string RemoteResultSummary { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public ProblemProject ProblemProject { get; set; } = null!;
}

public sealed class ApplicationSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ModelCacheEntry
{
    public Guid Id { get; set; }
    public AiProviderKind Provider { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CapabilitiesJson { get; set; } = "{}";
    public DateTimeOffset RefreshedAt { get; set; }
}
