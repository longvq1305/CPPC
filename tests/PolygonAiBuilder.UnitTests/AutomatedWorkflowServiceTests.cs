using System.Runtime.CompilerServices;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class AutomatedWorkflowServiceTests
{
    [Fact]
    public async Task Run_ConfiguresAndExecutesCompletePipelineInOrder()
    {
        var projectId = Guid.NewGuid();
        var calls = new List<string>();
        var settings = new MemorySettingsRepository();
        var tests = new RecordingTestConfigurationService(calls);
        var code = new RecordingCodeGenerationService(projectId, calls);
        var project = new RecordingProjectService(calls);
        var service = new AutomatedWorkflowService(
            settings,
            new StatementRepository(projectId, ValidStatement()),
            new LatexValidator([]),
            tests,
            code,
            new RecordingSampleService(calls),
            new RecordingPolygonSyncService(projectId, calls),
            project);
        await service.SetEnabledAsync(projectId, true);

        var progress = await CollectAsync(service.RunAsync(projectId));

        Assert.Equal(
            [
                "config:ncmp.cpp:100:1:True",
                "generate",
                "config:wcmp.cpp:100:1:True",
                "fix:Solution",
                "fix:Generator",
                "sample:1",
                "sync",
                "screen:5",
            ],
            calls);
        Assert.All(tests.Updates, update =>
        {
            Assert.Equal("tests", update.TestsetName);
            Assert.Equal(100, update.TestCount);
            Assert.Equal(1m, update.ScorePerTest);
            Assert.True(update.UseSampleInStatement);
            Assert.Contains("1..100", update.Script, StringComparison.Ordinal);
        });
        Assert.Equal("ncmp.cpp", tests.Updates[0].Checker);
        Assert.Equal("wcmp.cpp", tests.Updates[1].Checker);
        Assert.Contains(progress, item => item.Stage == AutomatedWorkflowStage.Completed
            && item.Status == SyncOperationStatus.Succeeded
            && item.Package?.State == "READY");
    }

    [Fact]
    public async Task Run_StopsBeforeCodeOrRemoteWritesWhenStatementIsInvalid()
    {
        var projectId = Guid.NewGuid();
        var calls = new List<string>();
        var settings = new MemorySettingsRepository();
        var service = new AutomatedWorkflowService(
            settings,
            new StatementRepository(projectId, ValidStatement() with
            {
                Content = ValidStatement().Content with { Output = "" },
            }),
            new LatexValidator([]),
            new RecordingTestConfigurationService(calls),
            new RecordingCodeGenerationService(projectId, calls),
            new RecordingSampleService(calls),
            new RecordingPolygonSyncService(projectId, calls),
            new RecordingProjectService(calls));
        await service.SetEnabledAsync(projectId, true);

        var progress = await CollectAsync(service.RunAsync(projectId));

        var failure = Assert.Single(progress);
        Assert.Equal(AutomatedWorkflowStage.StatementValidated, failure.Stage);
        Assert.Equal(SyncOperationStatus.Failed, failure.Status);
        Assert.Contains("Output", failure.Message, StringComparison.Ordinal);
        Assert.Empty(calls);
    }

    private static StatementSnapshot ValidStatement()
    {
        var projectId = Guid.NewGuid();
        var content = new StatementContent("A", "Legend", "Input", "Output", "Note");
        return new StatementSnapshot(projectId, "english", 3, content, true, DateTimeOffset.UtcNow, []);
    }

    private static async Task<List<AutomatedWorkflowProgress>> CollectAsync(
        IAsyncEnumerable<AutomatedWorkflowProgress> source)
    {
        var result = new List<AutomatedWorkflowProgress>();
        await foreach (var item in source) result.Add(item);
        return result;
    }

    private sealed class MemorySettingsRepository : IApplicationSettingsRepository
    {
        private readonly Dictionary<string, string> values = [];

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(values);

        public Task SetManyAsync(
            IReadOnlyDictionary<string, string> settings,
            CancellationToken cancellationToken)
        {
            foreach (var item in settings) values[item.Key] = item.Value;
            return Task.CompletedTask;
        }
    }

    private sealed class StatementRepository(Guid projectId, StatementSnapshot statement) : IStatementRepository
    {
        public Task<StatementSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<StatementSnapshot?>(id == projectId ? statement with { ProjectId = projectId } : null);

        public Task<StatementSnapshot> SaveAsync(
            Guid id,
            StatementContent content,
            ChangeSource source,
            string? provider,
            string? model,
            Guid? messageId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatementSnapshot> RestoreAsync(
            Guid id,
            int versionNumber,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LatexValidator(IReadOnlyList<LatexIssue> issues) : ILatexValidator
    {
        public IReadOnlyList<LatexIssue> Validate(StatementContent content) => issues;
    }

    private sealed class RecordingTestConfigurationService(List<string> calls) : ITestConfigurationService
    {
        public List<TestConfigurationUpdate> Updates { get; } = [];

        public Task<TestConfigurationSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TestConfigurationSnapshot?>(null);

        public Task<TestConfigurationSnapshot> SaveAsync(
            Guid projectId,
            TestConfigurationUpdate update,
            CancellationToken cancellationToken = default)
        {
            Updates.Add(update);
            calls.Add($"config:{update.Checker}:{update.TestCount}:{update.ScorePerTest}:{update.UseSampleInStatement}");
            return Task.FromResult(new TestConfigurationSnapshot(
                projectId,
                update.TestsetName,
                update.TestCount,
                update.ScorePerTest,
                true,
                update.Checker,
                update.Script,
                1,
                update.UseSampleInStatement,
                update.CommitMessage,
                DateTimeOffset.UtcNow));
        }

        public Task<TestConfigurationSnapshot> RegenerateScriptAsync(
            Guid projectId,
            TestConfigurationUpdate update,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public string CreateDefaultScript(int testCount) =>
            $"<#list 1..{testCount} as i>\ngen ${{i}} > $\n</#list>";
    }

    private sealed class RecordingCodeGenerationService(Guid projectId, List<string> calls) : ICodeGenerationService
    {
        private readonly CodeWorkspaceSnapshot workspace = new(projectId, 3, false, null, null);

        public Task<CodeWorkspaceSnapshot?> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CodeWorkspaceSnapshot?>(workspace);

        public Task<CodeGenerationResult> GenerateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            calls.Add("generate");
            return Task.FromResult(new CodeGenerationResult(
                true,
                workspace,
                "generated",
                [],
                "algorithm",
                "O(n)",
                "O(1)",
                "wcmp.cpp",
                []));
        }

        public Task<AutoFixResult> AutoFixAsync(
            Guid id,
            CodeArtifactType type,
            CancellationToken cancellationToken = default)
        {
            calls.Add($"fix:{type}");
            var compile = new CompileArtifactResult(
                type,
                CompileStatus.Succeeded,
                type == CodeArtifactType.Solution ? "solution.cpp" : "generate.cpp",
                "ok",
                TimeSpan.Zero,
                "artifact.exe");
            return Task.FromResult(new AutoFixResult(true, workspace, compile, 0, "compiled"));
        }

        public Task<CodeWorkspaceSnapshot> SaveUserEditAsync(
            Guid id,
            CodeArtifactType type,
            string content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CodeWorkspaceSnapshot> RestoreAsync(
            Guid id,
            CodeArtifactType type,
            int versionNumber,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingSampleService(List<string> calls) : ILocalSampleService
    {
        public Task<LocalSampleSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalSampleSnapshot?>(null);

        public Task<SampleGenerationResult> GenerateAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            calls.Add("sample:1");
            var sample = new LocalSampleSnapshot(
                Guid.NewGuid(), 1, "1", "1", DateTimeOffset.UtcNow,
                Guid.NewGuid(), Guid.NewGuid(), false, false, false);
            return Task.FromResult(new SampleGenerationResult(true, sample, "generated", null, null));
        }

        public Task<LocalSampleSnapshot> SaveManualAsync(
            Guid projectId,
            string input,
            string output,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingPolygonSyncService(Guid projectId, List<string> calls) : IPolygonSyncService
    {
        public Task<PolygonSyncSnapshot?> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PolygonSyncSnapshot?>(null);

        public async IAsyncEnumerable<PolygonSyncProgress> SyncAsync(
            Guid id,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            calls.Add("sync");
            await Task.Yield();
            var snapshot = new PolygonSyncSnapshot(
                projectId, 123, 7, PolygonSyncPhase.PackageReady, ProjectStatus.Synced, []);
            var package = new PolygonPackage(44, 7, 1, "READY", "standard", "standard");
            yield return new PolygonSyncProgress(
                PolygonSyncPhase.PackageReady,
                SyncOperationStatus.Succeeded,
                "ready",
                snapshot,
                package);
        }
    }

    private sealed class RecordingProjectService(List<string> calls) : IProjectService
    {
        public Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectSummary>>([]);

        public Task<ProjectDetails?> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProjectDetails?>(null);

        public Task<ProjectDetails> CreateAsync(string internalName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SetCurrentScreenAsync(
            Guid projectId,
            int screen,
            CancellationToken cancellationToken = default)
        {
            calls.Add($"screen:{screen}");
            return Task.FromResult(true);
        }
    }
}
