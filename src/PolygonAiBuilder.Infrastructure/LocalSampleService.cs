using System.Text;
using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Infrastructure;

public sealed class LocalSampleService(
    ISampleRepository sampleRepository,
    ICodeRepository codeRepository,
    ICodeCompileService compileService,
    IProjectService projectService,
    IProcessRunner processRunner,
    RuntimePaths paths) : ILocalSampleService
{
    private const int OutputLimit = 10 * 1024 * 1024;

    public Task<LocalSampleSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        sampleRepository.GetAsync(projectId, 1, cancellationToken);

    public async Task<SampleGenerationResult> GenerateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var code = await codeRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy code workspace.");
        var binDirectory = Path.Combine(paths.ProjectsPath, projectId.ToString("N"), "code", "bin");
        var solutionPath = Path.Combine(binDirectory, "solution.exe");
        var generatorPath = Path.Combine(binDirectory, "generate.exe");
        if (code.Solution?.LastCompileStatus != Domain.CompileStatus.Succeeded
            || code.Generator?.LastCompileStatus != Domain.CompileStatus.Succeeded
            || !File.Exists(solutionPath)
            || !File.Exists(generatorPath))
        {
            var compile = await compileService.CompileAsync(projectId, cancellationToken);
            if (!compile.Succeeded)
            {
                return new(false, null, "Không thể tạo Sample 1 vì hai source chưa compile thành công.", null, null);
            }
            solutionPath = compile.Solution.ExecutablePath!;
            generatorPath = compile.Generator.ExecutablePath!;
        }

        var project = await projectService.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        var workingDirectory = Path.Combine(paths.ProjectsPath, projectId.ToString("N"), "temp", $"sample-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var generator = await processRunner.RunAsync(new(
                generatorPath, ["1"], workingDirectory, TimeSpan.FromSeconds(5), OutputLimit), cancellationToken);
            if (!generator.Succeeded)
            {
                return new(false, null, ProcessFailure("generate.exe 1", generator), generator, null);
            }

            var sampleInput = generator.StandardOutput;
            string? standardInput = sampleInput;
            if (!string.Equals(project.InputFile, "stdin", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(Path.Combine(workingDirectory, project.InputFile), sampleInput,
                    new UTF8Encoding(false), cancellationToken);
                standardInput = null;
            }

            var solutionTimeout = TimeSpan.FromSeconds(Math.Max(5, project.TimeLimitMs * 2d / 1000d));
            var solution = await processRunner.RunAsync(new(
                solutionPath, [], workingDirectory, solutionTimeout, OutputLimit, standardInput), cancellationToken);
            if (!solution.Succeeded)
            {
                return new(false, null, ProcessFailure("solution.exe", solution), generator, solution);
            }

            var sampleOutput = solution.StandardOutput;
            if (!string.Equals(project.OutputFile, "stdout", StringComparison.OrdinalIgnoreCase))
            {
                var outputPath = Path.Combine(workingDirectory, project.OutputFile);
                if (!File.Exists(outputPath))
                {
                    return new(false, null, $"solution.exe không tạo file output đã cấu hình: {project.OutputFile}.", generator, solution);
                }
                sampleOutput = await File.ReadAllTextAsync(outputPath, cancellationToken);
                if (Encoding.UTF8.GetByteCount(sampleOutput) > OutputLimit)
                {
                    return new(false, null, "File output của Sample 1 vượt quá 10 MB.", generator, solution);
                }
            }

            await SaveGeneratedFilesAsync(projectId, sampleInput, sampleOutput, cancellationToken);
            var saved = await sampleRepository.SaveGeneratedAsync(
                projectId, 1, sampleInput, sampleOutput, cancellationToken);
            return new(true, saved,
                "Đã chạy generate.exe 1 và solution.exe; Sample 1 là smoke test local, không phải bằng chứng thuật toán đúng hoàn toàn.",
                generator, solution);
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }

    public Task<LocalSampleSnapshot> SaveManualAsync(
        Guid projectId,
        string input,
        string output,
        CancellationToken cancellationToken = default) =>
        sampleRepository.SaveManualAsync(projectId, 1, input, output, cancellationToken);

    private async Task SaveGeneratedFilesAsync(
        Guid projectId,
        string input,
        string output,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(paths.ProjectsPath, projectId.ToString("N"), "tests");
        Directory.CreateDirectory(directory);
        await AtomicWriteAsync(Path.Combine(directory, "sample-1.in"), input, cancellationToken);
        await AtomicWriteAsync(Path.Combine(directory, "sample-1.out"), output, cancellationToken);
        await AtomicWriteAsync(Path.Combine(directory, "sample-1.generated.in"), input, cancellationToken);
        await AtomicWriteAsync(Path.Combine(directory, "sample-1.generated.out"), output, cancellationToken);
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ProcessFailure(string name, ProcessExecutionResult result)
    {
        if (result.TimedOut) return $"{name} vượt quá thời gian cho phép.";
        if (result.OutputTruncated) return $"{name} sinh output vượt quá 10 MB.";
        if (result.Cancelled) return $"Đã hủy {name}.";
        if (!string.IsNullOrWhiteSpace(result.StartError)) return $"Không thể chạy {name}: {result.StartError}";
        return $"{name} kết thúc với exit code {result.ExitCode}. {result.StandardError}".Trim();
    }
}
