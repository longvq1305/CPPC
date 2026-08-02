using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class SelfAuditServiceTests
{
    [Fact]
    public async Task IncompleteLocalDataFailsWithoutCallingAi()
    {
        var provider = new AuditProvider(AllPassed());
        var service = new SelfAuditService(
            new StatementRepository(null),
            new CodeRepository(null),
            new SampleRepository(null),
            new ConfigurationRepository(null),
            new ConversationRepository(null),
            new LatexValidator(),
            [provider],
            TimeProvider.System);

        var result = await service.RunAsync(Guid.NewGuid());

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Severity == AuditSeverity.Error);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task SemanticFailurePreventsPassedStatusEvenWhenLocalChecksPass()
    {
        var projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var versionId = Guid.NewGuid();
        var statement = new StatementSnapshot(projectId, "english", 1,
            new("Sum", "Add two integers.", "Two integers.", "Their sum.", ""),
            false, now, []);
        var artifactHistory = new[]
        {
            new CodeArtifactVersionInfo(versionId, 1, "source", ChangeSource.User,
                null, null, CompileStatus.Succeeded, "OK", now),
        };
        var solution = new CodeArtifactInfo(CodeArtifactType.Solution, "solution.cpp", "source", 1, 1,
            false, CompileStatus.Succeeded, "OK", now, artifactHistory);
        var generator = new CodeArtifactInfo(CodeArtifactType.Generator, "generate.cpp", "source", 1, 1,
            false, CompileStatus.Succeeded, "OK", now, artifactHistory);
        var code = new CodeWorkspaceSnapshot(projectId, 1, false, solution, generator);
        var sample = new LocalSampleSnapshot(Guid.NewGuid(), 1, "1 2\n", "3\n", now,
            versionId, versionId, false, false, false);
        var configuration = new TestConfigurationSnapshot(projectId, "tests", 100, 1m, true,
            "ncmp.cpp", "<#list 1..100 as i>\ngen ${i} > $\n</#list>", 1, true, "", now);
        var workspace = new AiWorkspaceSnapshot(projectId, AiProviderKind.OpenAI, "gpt-test", "", [], []);
        var provider = new AuditProvider(AllPassed() with { OverflowReviewed = false });
        var service = new SelfAuditService(
            new StatementRepository(statement),
            new CodeRepository(code),
            new SampleRepository(sample),
            new ConfigurationRepository(configuration),
            new ConversationRepository(workspace),
            new LatexValidator(),
            [provider],
            TimeProvider.System);

        var result = await service.RunAsync(projectId);

        Assert.False(result.Passed);
        Assert.Equal(1, provider.Calls);
        Assert.Contains(result.Issues, issue => issue.Check == "Overflow review" && issue.Severity == AuditSeverity.Error);
    }

    private static SelfAuditAiOutput AllPassed() => new(true, true, true, true, true, true, []);

    private sealed class AuditProvider(SelfAuditAiOutput output) : IAiProvider
    {
        public int Calls { get; private set; }
        public AiProviderKind Kind => AiProviderKind.OpenAI;
        public Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<AiStreamEvent> StreamChatAsync(AiChatRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T> GenerateStructuredAsync<T>(AiStructuredRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult((T)(object)output);
        }
    }

    private sealed class StatementRepository(StatementSnapshot? value) : IStatementRepository
    {
        public Task<StatementSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken) => Task.FromResult(value);
        public Task<StatementSnapshot> SaveAsync(Guid projectId, StatementContent content, ChangeSource source, string? provider, string? model, Guid? messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StatementSnapshot> RestoreAsync(Guid projectId, int versionNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CodeRepository(CodeWorkspaceSnapshot? value) : ICodeRepository
    {
        public Task<CodeWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken) => Task.FromResult(value);
        public Task<CodeWorkspaceSnapshot> SaveGeneratedAsync(Guid projectId, CodeGenerationOutput output, int statementVersion, string provider, string model, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CodeWorkspaceSnapshot> SaveArtifactAsync(Guid projectId, CodeArtifactType type, string content, ChangeSource source, int statementVersion, string? provider, string? model, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkCompileAsync(Guid projectId, CodeArtifactType type, CompileStatus status, string output, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CodeWorkspaceSnapshot> RestoreAsync(Guid projectId, CodeArtifactType type, int versionNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SampleRepository(LocalSampleSnapshot? value) : ISampleRepository
    {
        public Task<LocalSampleSnapshot?> GetAsync(Guid projectId, int testIndex, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<LocalSampleSnapshot> SaveGeneratedAsync(Guid projectId, int testIndex, string input, string output, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocalSampleSnapshot> SaveManualAsync(Guid projectId, int testIndex, string input, string output, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ConfigurationRepository(TestConfigurationSnapshot? value) : ITestConfigurationRepository
    {
        public Task<TestConfigurationSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<TestConfigurationSnapshot> SaveAsync(Guid projectId, TestConfigurationUpdate update, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ConversationRepository(AiWorkspaceSnapshot? value) : IConversationRepository
    {
        public Task<AiWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken) => Task.FromResult(value);
        public Task<AiTurnStart> StartTurnAsync(Guid projectId, string content, AiProviderKind provider, string model, IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AppendAssistantAsync(Guid messageId, string delta, string? providerResponseId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FinishAssistantAsync(Guid messageId, MessageStatus status, string? providerResponseId, string? errorCode, string? errorDetails, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetSelectionAsync(Guid projectId, AiProviderKind provider, string model, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
