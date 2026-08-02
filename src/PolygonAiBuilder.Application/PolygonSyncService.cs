using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed class PolygonSyncService(
    IPolygonSyncRepository syncRepository,
    IProjectService projectService,
    IStatementRepository statementRepository,
    ICodeRepository codeRepository,
    ISampleRepository sampleRepository,
    ITestConfigurationRepository testConfigurationRepository,
    ISelfAuditService selfAuditService,
    ICheckerSourceStore checkerSourceStore,
    IPolygonClient polygonClient,
    TimeProvider timeProvider) : IPolygonSyncService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ProjectLocks = new();

    public Task<PolygonSyncSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        syncRepository.GetAsync(projectId, cancellationToken);

    public async IAsyncEnumerable<PolygonSyncProgress> SyncAsync(
        Guid projectId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var gate = ProjectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            var busySnapshot = await RequiredSnapshotAsync(projectId, cancellationToken);
            yield return new(busySnapshot.Phase, SyncOperationStatus.Failed,
                "Dự án này đang có một sync operation khác chạy.", busySnapshot);
            yield break;
        }

        try
        {
            var snapshot = await RequiredSnapshotAsync(projectId, cancellationToken);
            var project = await projectService.GetAsync(projectId, cancellationToken)
                ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
            var statement = await statementRepository.GetAsync(projectId, cancellationToken)
                ?? throw new InvalidOperationException("Thiếu statement.");
            var code = await codeRepository.GetAsync(projectId, cancellationToken)
                ?? throw new InvalidOperationException("Thiếu code workspace.");
            var sample = await sampleRepository.GetAsync(projectId, 1, cancellationToken)
                ?? throw new InvalidOperationException("Thiếu Sample 1.");
            var configuration = await testConfigurationRepository.GetAsync(projectId, cancellationToken)
                ?? throw new InvalidOperationException("Thiếu test configuration.");

            var audit = await selfAuditService.RunAsync(projectId, cancellationToken);
            if (!audit.Passed)
            {
                yield return new(snapshot.Phase, SyncOperationStatus.Failed,
                    "Self-Audit FAILED. Không có thay đổi nào được gửi lên Polygon.", snapshot);
                yield break;
            }

            await polygonClient.TestConnectionAsync(cancellationToken);

            if (snapshot.ProblemId is null && snapshot.Phase < PolygonSyncPhase.NameRechecked)
            {
                var step = await ExecuteAsync(projectId, PolygonSyncPhase.NameRechecked, PolygonSyncPhase.NameRechecked,
                    "problems.list", Fingerprint(project.InternalName), async ct =>
                    {
                        var matches = await polygonClient.ListProblemsAsync(project.InternalName, ct);
                        if (matches.Any(item => !item.Deleted && string.Equals(item.Name, project.InternalName, StringComparison.OrdinalIgnoreCase)))
                            throw new ExternalServiceException("Polygon", "duplicate_name",
                                "Tên problem đã tồn tại trên Polygon; chưa tạo problem mới.");
                        return true;
                    }, _ => "Tên problem vẫn khả dụng.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.NameRechecked);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.ProblemId is null)
            {
                var step = await ExecuteAsync(projectId, PolygonSyncPhase.ProblemCreated, PolygonSyncPhase.ProblemCreated,
                    "problem.create", Fingerprint(project.InternalName),
                    ct => polygonClient.CreateProblemAsync(project.InternalName, ct),
                    problem => $"Đã tạo Polygon problem {problem.Id}.", cancellationToken,
                    createdProblemId: problem => problem.Id);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.ProblemCreated);
                if (!step.Succeeded) yield break;
            }

            var problemId = snapshot.ProblemId!.Value;
            if (snapshot.Phase < PolygonSyncPhase.GeneralInfoSaved)
            {
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.GeneralInfoSaved,
                    "problem.updateInfo", Fingerprint(project.InputFile, project.OutputFile, project.TimeLimitMs, project.MemoryLimitMb),
                    ct => polygonClient.UpdateInfoAsync(problemId, project.InputFile, project.OutputFile,
                        project.TimeLimitMs, project.MemoryLimitMb, ct), "Đã lưu General Info.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.GeneralInfoSaved);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.StatementSaved)
            {
                var payload = new PolygonStatementPayload(statement.Language, statement.Content.Title,
                    statement.Content.Legend, statement.Content.Input, statement.Content.Output, statement.Content.Note);
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.StatementSaved,
                    "problem.saveStatement", Fingerprint(statement.CurrentVersion, statement.Content),
                    ct => polygonClient.SaveStatementAsync(problemId, payload, ct),
                    "Đã lưu statement language=english.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.StatementSaved);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.SolutionSaved)
            {
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.SolutionSaved,
                    "problem.saveSolution", Fingerprint(code.Solution!.Version, code.Solution.Content),
                    ct => polygonClient.SaveSolutionAsync(problemId, code.Solution.Content, ct),
                    "Đã lưu solution.cpp · cpp.g++17 · MA.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.SolutionSaved);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.GeneratorSaved)
            {
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.GeneratorSaved,
                    "problem.saveFile", Fingerprint(code.Generator!.Version, code.Generator.Content),
                    ct => polygonClient.SaveSourceFileAsync(problemId, "gen.cpp", code.Generator.Content, "cpp.g++17", ct),
                    "Đã map generate.cpp local thành gen.cpp remote.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.GeneratorSaved);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.CheckerUploaded)
            {
                var checkerSource = await checkerSourceStore.ReadAsync(configuration.Checker, cancellationToken);
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.CheckerUploaded,
                    "problem.saveFile", Fingerprint(configuration.Checker, checkerSource),
                    ct => polygonClient.SaveSourceFileAsync(problemId, configuration.Checker, checkerSource, "cpp.g++17", ct),
                    $"Đã upload bundled {configuration.Checker}.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.CheckerUploaded);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.CheckerSelected)
            {
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.CheckerSelected,
                    "problem.setChecker", Fingerprint(configuration.Checker),
                    ct => polygonClient.SetCheckerAsync(problemId, configuration.Checker, ct),
                    $"Đã chọn {configuration.Checker}.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.CheckerSelected);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.ScriptSaved)
            {
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.ScriptSaved,
                    "problem.saveScript", Fingerprint(configuration.TestsetName, configuration.Script),
                    ct => polygonClient.SaveScriptAsync(problemId, configuration.TestsetName, configuration.Script, ct),
                    $"Đã lưu script testset {configuration.TestsetName}.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.ScriptSaved);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.PointsEnabled)
            {
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.PointsEnabled,
                    "problem.enablePoints", Fingerprint(true),
                    ct => polygonClient.EnablePointsAsync(problemId, true, ct),
                    "Đã bật points.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.PointsEnabled);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.TestMetadataSaved)
            {
                var metadata = new PolygonTestMetadata(configuration.TestsetName, configuration.TestCount,
                    configuration.ScorePerTest, 1, configuration.UseSampleInStatement, sample.Input, sample.Output);
                var step = await ExecuteVoidAsync(projectId, PolygonSyncPhase.TestMetadataSaved,
                    "problem.saveTest", Fingerprint(configuration.TestCount, configuration.ScorePerTest,
                        configuration.UseSampleInStatement, sample.Input, sample.Output),
                    ct => polygonClient.SaveTestMetadataAsync(problemId, metadata, ct),
                    $"Đã lưu points cho {configuration.TestCount} test và Sample 1 metadata.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.TestMetadataSaved);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.StatementRendered)
            {
                var step = await ExecuteAsync(projectId, PolygonSyncPhase.StatementRendered, PolygonSyncPhase.StatementRendered,
                    "problem.renderStatements", Fingerprint(statement.CurrentVersion),
                    async ct =>
                    {
                        await EnsureVietnameseStatementTemplateAsync(problemId, statement.Content, ct);
                        var render = await polygonClient.RenderStatementsAsync(problemId, true, ct);
                        if (!render.Succeeded)
                        {
                            var errors = render.Statements.SelectMany(item => new[] { item.Html, item.Pdf })
                                .Where(item => !item.Succeeded).Select(item => item.Message).Where(item => item is not null);
                            throw new ExternalServiceException("Polygon", "statement_render_failed",
                                "Polygon render statement thất bại: " + string.Join("; ", errors));
                        }
                        return render;
                    }, render => $"Statement render OK ở working-copy revision {render.Revision}.", cancellationToken);
                snapshot = step.Snapshot;
                yield return Progress(step, PolygonSyncPhase.StatementRendered);
                if (!step.Succeeded) yield break;
            }

            if (snapshot.Phase < PolygonSyncPhase.Committed)
            {
                var cautionStep = await ExecuteAsync(projectId, PolygonSyncPhase.StatementRendered, PolygonSyncPhase.StatementRendered,
                    "problem.cautions", Fingerprint(problemId, snapshot.Phase), async ct =>
                    {
                        var cautions = await polygonClient.GetCautionsAsync(problemId, ct);
                        if (cautions.HasBlockingIssues)
                        {
                            var messages = cautions.Cautions.Where(item => item.Severity == "HARD").Select(item => item.Message)
                                .Concat(cautions.PackageReadinessIssues.Select(item => item.Message));
                            throw new ExternalServiceException("Polygon", "blocking_cautions",
                                "Polygon báo blocking issues: " + string.Join("; ", messages));
                        }
                        return cautions;
                    }, value => $"Cautions checked: {value.Cautions.Count} caution(s), không có blocker.", cancellationToken);
                snapshot = cautionStep.Snapshot;
                yield return new(snapshot.Phase, cautionStep.Succeeded ? SyncOperationStatus.Succeeded : SyncOperationStatus.Failed,
                    cautionStep.Message, snapshot, Cautions: cautionStep.Value);
                if (!cautionStep.Succeeded) yield break;

                var commitStep = await ExecuteAsync(projectId, PolygonSyncPhase.Committed, PolygonSyncPhase.Committed,
                    "problem.commitChanges", Fingerprint(configuration.CommitMessage),
                    ct => polygonClient.CommitAsync(problemId, configuration.CommitMessage, ct),
                    value => $"Commit: {value.Message}", cancellationToken);
                snapshot = commitStep.Snapshot;
                yield return Progress(commitStep, PolygonSyncPhase.Committed);
                if (!commitStep.Succeeded) yield break;
            }

            var recordedPackageBaseline = ReadPackageBaseline(snapshot);
            long previousPackageId;
            if (snapshot.Phase < PolygonSyncPhase.PackageBuildStarted
                || snapshot.Phase == PolygonSyncPhase.PackageFailed
                || recordedPackageBaseline is null)
            {
                var packages = await polygonClient.ListPackagesAsync(problemId, cancellationToken);
                previousPackageId = packages.Count == 0 ? 0 : packages.Max(item => item.Id);
                var buildStep = await ExecuteVoidAsync(projectId, PolygonSyncPhase.PackageBuildStarted,
                    "problem.buildPackage", Fingerprint(problemId, "standard", true),
                    ct => polygonClient.BuildStandardPackageAsync(problemId, true, ct),
                    $"baselinePackageId={previousPackageId}; Đã bắt đầu standard package build với verify=true.", cancellationToken);
                snapshot = buildStep.Snapshot;
                yield return Progress(buildStep, PolygonSyncPhase.PackageBuildStarted);
                if (!buildStep.Succeeded) yield break;
            }
            else
            {
                previousPackageId = recordedPackageBaseline.Value;
            }

            if (snapshot.Phase == PolygonSyncPhase.PackageBuildStarted)
            {
                var pollStep = await ExecuteAsync(projectId, PolygonSyncPhase.PackageReady, PolygonSyncPhase.PackageFailed,
                    "problem.packages", Fingerprint(problemId, previousPackageId),
                    ct => PollPackageAsync(problemId, previousPackageId, ct),
                    package => $"Package {package.Id} revision {package.Revision}: {package.State}.", cancellationToken,
                    packageRevision: package => package.Revision);
                snapshot = pollStep.Snapshot;
                yield return new(snapshot.Phase, pollStep.Succeeded ? SyncOperationStatus.Succeeded : SyncOperationStatus.Failed,
                    pollStep.Message, snapshot, pollStep.Value);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureVietnameseStatementTemplateAsync(
        long problemId,
        StatementContent content,
        CancellationToken cancellationToken)
    {
        if (!PolygonStatementTemplate.NeedsVietnameseFontEncoding(content))
        {
            return;
        }

        const string templateName = "statements.ftl";
        var currentTemplate = await polygonClient.ViewResourceFileAsync(
            problemId,
            templateName,
            cancellationToken);
        var updatedTemplate = PolygonStatementTemplate.EnableVietnameseFontEncoding(currentTemplate);
        if (!string.Equals(currentTemplate, updatedTemplate, StringComparison.Ordinal))
        {
            await polygonClient.SaveResourceFileAsync(
                problemId,
                templateName,
                updatedTemplate,
                cancellationToken);
        }
    }

    private async Task<PolygonPackage> PollPackageAsync(
        long problemId,
        long previousPackageId,
        CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow().AddMinutes(10);
        var delay = TimeSpan.FromSeconds(2);
        while (timeProvider.GetUtcNow() < deadline)
        {
            var packages = await polygonClient.ListPackagesAsync(problemId, cancellationToken);
            var package = packages.Where(item => item.Type == "standard" && item.Id > previousPackageId)
                .OrderByDescending(item => item.Id).FirstOrDefault();
            if (package?.State == "READY") return package;
            if (package?.State == "FAILED")
                throw new ExternalServiceException("Polygon", "package_failed",
                    $"Polygon package {package.Id} build FAILED: {package.Comment}");
            await Task.Delay(delay, timeProvider, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(10, delay.TotalSeconds * 1.6));
        }
        throw new ExternalServiceException("Polygon", "package_timeout",
            "Hết thời gian chờ Polygon package sau 10 phút. Có thể bấm Tiếp tục đồng bộ để poll lại.");
    }

    private Task<StepResult<Unit>> ExecuteVoidAsync(
        Guid projectId,
        PolygonSyncPhase targetPhase,
        string endpoint,
        string fingerprint,
        Func<CancellationToken, Task> action,
        string summary,
        CancellationToken cancellationToken) =>
        ExecuteAsync(projectId, targetPhase, targetPhase, endpoint, fingerprint, async ct =>
        {
            await action(ct);
            return Unit.Value;
        }, _ => summary, cancellationToken);

    private async Task<StepResult<T>> ExecuteAsync<T>(
        Guid projectId,
        PolygonSyncPhase targetPhase,
        PolygonSyncPhase failurePhase,
        string endpoint,
        string fingerprint,
        Func<CancellationToken, Task<T>> action,
        Func<T, string> summary,
        CancellationToken cancellationToken,
        Func<T, long?>? createdProblemId = null,
        Func<T, int?>? packageRevision = null)
    {
        var operationId = await syncRepository.StartOperationAsync(
            projectId, targetPhase, endpoint, fingerprint, cancellationToken);
        var retries = 0;
        try
        {
            while (true)
            {
                try
                {
                    var value = await action(cancellationToken);
                    var message = summary(value);
                    var snapshot = await syncRepository.CompleteOperationAsync(operationId, projectId,
                        targetPhase, message, createdProblemId?.Invoke(value), packageRevision?.Invoke(value),
                        retries, cancellationToken);
                    return new(true, value, snapshot, message);
                }
                catch (ExternalServiceException exception) when (IsTransient(exception) && retries < 3)
                {
                    retries++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), timeProvider, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await syncRepository.FailOperationAsync(operationId, projectId, failurePhase,
                "cancelled", "Đã hủy sync.", retries, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            var code = exception is ExternalServiceException external ? external.Code : "local_error";
            var snapshot = await syncRepository.FailOperationAsync(operationId, projectId, failurePhase,
                code, exception.Message, retries, CancellationToken.None);
            return new(false, default, snapshot, exception.Message);
        }
    }

    private static bool IsTransient(ExternalServiceException exception) =>
        exception.Code is "network_error" or "http_408" or "http_429"
        || exception.Code.StartsWith("http_5", StringComparison.Ordinal);

    private async Task<PolygonSyncSnapshot> RequiredSnapshotAsync(Guid projectId, CancellationToken cancellationToken) =>
        await syncRepository.GetAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException("Không tìm thấy sync state.");

    private static PolygonSyncProgress Progress<T>(StepResult<T> step, PolygonSyncPhase phase) =>
        new(phase, step.Succeeded ? SyncOperationStatus.Succeeded : SyncOperationStatus.Failed,
            step.Message, step.Snapshot);

    private static string Fingerprint(params object?[] values)
    {
        var source = string.Join("\u001f", values.Select(value => value?.ToString() ?? "<null>"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    internal static long? ReadPackageBaseline(PolygonSyncSnapshot snapshot)
    {
        const string marker = "baselinePackageId=";
        var summary = snapshot.Operations
            .Where(item => item.Phase == PolygonSyncPhase.PackageBuildStarted
                && item.Status == SyncOperationStatus.Succeeded)
            .OrderByDescending(item => item.CompletedAt)
            .Select(item => item.RemoteResultSummary)
            .FirstOrDefault(item => item.StartsWith(marker, StringComparison.Ordinal));
        if (summary is null)
        {
            return null;
        }

        var separator = summary.IndexOf(';', marker.Length);
        var value = separator < 0 ? summary[marker.Length..] : summary[marker.Length..separator];
        return long.TryParse(value, out var baseline) && baseline >= 0 ? baseline : null;
    }

    private sealed record StepResult<T>(bool Succeeded, T? Value, PolygonSyncSnapshot Snapshot, string Message);
    private readonly record struct Unit
    {
        public static Unit Value => new();
    }
}
