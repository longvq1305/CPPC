using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed class AutomatedWorkflowService(
    IApplicationSettingsRepository settingsRepository,
    IStatementRepository statementRepository,
    ILatexValidator latexValidator,
    ITestConfigurationService testConfigurationService,
    ICodeGenerationService codeGenerationService,
    ILocalSampleService localSampleService,
    IPolygonSyncService polygonSyncService,
    IProjectService projectService) : IAutomatedWorkflowService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ProjectLocks = new();

    public async Task<bool> IsEnabledAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepository.GetAllAsync(cancellationToken);
        return settings.TryGetValue(PreferenceKey(projectId), out var value)
            && bool.TryParse(value, out var enabled)
            && enabled;
    }

    public Task SetEnabledAsync(
        Guid projectId,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        settingsRepository.SetManyAsync(
            new Dictionary<string, string> { [PreferenceKey(projectId)] = enabled.ToString() },
            cancellationToken);

    public async IAsyncEnumerable<AutomatedWorkflowProgress> RunAsync(
        Guid projectId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync(projectId, cancellationToken))
        {
            throw new InvalidOperationException(
                "Hãy bật chế độ tự động trước khi chốt đề ở bước 2.");
        }

        var gate = ProjectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            yield return Failed(
                AutomatedWorkflowStage.StatementValidated,
                "Project này đang có một quy trình tự động khác chạy.");
            yield break;
        }

        try
        {
            var statement = await statementRepository.GetAsync(projectId, cancellationToken)
                ?? throw new InvalidOperationException("Thiếu statement để chốt đề.");
            var missingFields = RequiredFields(statement.Content)
                .Where(field => string.IsNullOrWhiteSpace(field.Value))
                .Select(field => field.Name)
                .ToArray();
            var latexErrors = latexValidator.Validate(statement.Content)
                .Where(issue => issue.Severity == LatexIssueSeverity.Error)
                .ToArray();
            if (missingFields.Length > 0 || latexErrors.Length > 0)
            {
                var details = missingFields.Length > 0
                    ? $"Thiếu trường bắt buộc: {string.Join(", ", missingFields)}."
                    : string.Join("; ", latexErrors.Select(issue => $"{issue.Field}: {issue.Message}"));
                yield return Failed(AutomatedWorkflowStage.StatementValidated, details);
                yield break;
            }

            yield return Succeeded(
                AutomatedWorkflowStage.StatementValidated,
                $"Đã chốt statement version {statement.CurrentVersion}.");

            const int testCount = 100;
            const decimal scorePerTest = 1m;
            var defaultScript = testConfigurationService.CreateDefaultScript(testCount);
            await testConfigurationService.SaveAsync(projectId, new(
                "tests",
                testCount,
                scorePerTest,
                "ncmp.cpp",
                defaultScript,
                true,
                string.Empty), cancellationToken);
            yield return Succeeded(
                AutomatedWorkflowStage.TestsConfigured,
                "Đã đặt 100 test, mỗi test 1 điểm và dùng test 1 làm sample.");

            yield return Started(AutomatedWorkflowStage.CodeGenerated, "Đang sinh solution.cpp và generate.cpp…");
            var generation = await codeGenerationService.GenerateAsync(projectId, cancellationToken);
            if (!generation.Succeeded)
            {
                var details = generation.ValidationErrors.Count == 0
                    ? generation.Message
                    : $"{generation.Message} {string.Join("; ", generation.ValidationErrors)}";
                yield return Failed(AutomatedWorkflowStage.CodeGenerated, details);
                yield break;
            }

            var checker = generation.RecommendedChecker is "ncmp.cpp" or "wcmp.cpp"
                ? generation.RecommendedChecker
                : "ncmp.cpp";
            await testConfigurationService.SaveAsync(projectId, new(
                "tests",
                testCount,
                scorePerTest,
                checker,
                defaultScript,
                true,
                string.Empty), cancellationToken);
            yield return Succeeded(
                AutomatedWorkflowStage.CodeGenerated,
                $"Đã sinh code và tự chọn checker {checker}.");

            var solution = await CompileAndRepairAsync(
                projectId,
                CodeArtifactType.Solution,
                AutomatedWorkflowStage.SolutionCompiled,
                cancellationToken);
            yield return solution;
            if (solution.Status == SyncOperationStatus.Failed) yield break;

            var generator = await CompileAndRepairAsync(
                projectId,
                CodeArtifactType.Generator,
                AutomatedWorkflowStage.GeneratorCompiled,
                cancellationToken);
            yield return generator;
            if (generator.Status == SyncOperationStatus.Failed) yield break;

            yield return Started(AutomatedWorkflowStage.SampleGenerated, "Đang chạy test_id 1 để tạo sample…");
            var sample = await localSampleService.GenerateAsync(projectId, cancellationToken);
            if (!sample.Succeeded || sample.Sample is null)
            {
                yield return Failed(AutomatedWorkflowStage.SampleGenerated, sample.Message);
                yield break;
            }
            yield return Succeeded(
                AutomatedWorkflowStage.SampleGenerated,
                "Sample 1 đã được tạo từ generate.exe 1 và solution.exe.");

            yield return Started(
                AutomatedWorkflowStage.PolygonSync,
                "Đang Self-Audit; chỉ khi PASSED mới tạo/cập nhật problem trên Polygon…");
            PolygonSyncSnapshot? lastSnapshot = null;
            PolygonPackage? package = null;
            await foreach (var progress in polygonSyncService.SyncAsync(projectId, cancellationToken))
            {
                lastSnapshot = progress.Snapshot;
                package = progress.Package ?? package;
                yield return new(
                    AutomatedWorkflowStage.PolygonSync,
                    progress.Status,
                    progress.Message,
                    progress.Snapshot,
                    progress.Package);
                if (progress.Status == SyncOperationStatus.Failed)
                {
                    yield break;
                }
            }

            if (lastSnapshot?.Phase != PolygonSyncPhase.PackageReady)
            {
                yield return Failed(
                    AutomatedWorkflowStage.PolygonSync,
                    "Polygon chưa xác nhận standard package ở trạng thái READY.",
                    lastSnapshot);
                yield break;
            }

            await projectService.SetCurrentScreenAsync(projectId, 5, cancellationToken);
            yield return new(
                AutomatedWorkflowStage.Completed,
                SyncOperationStatus.Succeeded,
                "Hoàn tất tự động. Standard package đã READY; hãy mở Manage Access và thêm codeforces cùng @gia-su-yb.",
                lastSnapshot,
                package);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AutomatedWorkflowProgress> CompileAndRepairAsync(
        Guid projectId,
        CodeArtifactType artifactType,
        AutomatedWorkflowStage stage,
        CancellationToken cancellationToken)
    {
        var result = await codeGenerationService.AutoFixAsync(
            projectId,
            artifactType,
            cancellationToken);
        return result.Succeeded
            ? Succeeded(stage, result.Message)
            : Failed(stage, result.Message);
    }

    private static IEnumerable<(string Name, string Value)> RequiredFields(StatementContent content)
    {
        yield return ("Title", content.Title);
        yield return ("Legend", content.Legend);
        yield return ("Input", content.Input);
        yield return ("Output", content.Output);
    }

    private static string PreferenceKey(Guid projectId) =>
        $"Automation.FullPipeline.{projectId:N}";

    private static AutomatedWorkflowProgress Started(AutomatedWorkflowStage stage, string message) =>
        new(stage, SyncOperationStatus.Started, message);

    private static AutomatedWorkflowProgress Succeeded(AutomatedWorkflowStage stage, string message) =>
        new(stage, SyncOperationStatus.Succeeded, message);

    private static AutomatedWorkflowProgress Failed(
        AutomatedWorkflowStage stage,
        string message,
        PolygonSyncSnapshot? snapshot = null) =>
        new(stage, SyncOperationStatus.Failed, message, snapshot);
}
