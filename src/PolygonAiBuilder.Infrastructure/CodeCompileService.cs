using System.Text;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class CodeCompileService(
    ICodeRepository codeRepository,
    IToolchainService toolchainService,
    IProcessRunner processRunner,
    RuntimePaths paths) : ICodeCompileService
{
    private const int OutputLimit = 10 * 1024 * 1024;

    public async Task<CompileWorkspaceResult> CompileAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var solution = await CompileArtifactAsync(projectId, CodeArtifactType.Solution, cancellationToken);
        var generator = await CompileArtifactAsync(projectId, CodeArtifactType.Generator, cancellationToken);
        return new(solution, generator);
    }

    public async Task<CompileArtifactResult> CompileArtifactAsync(
        Guid projectId,
        CodeArtifactType type,
        CancellationToken cancellationToken = default)
    {
        var workspace = await codeRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy code workspace.");
        var artifact = type == CodeArtifactType.Solution ? workspace.Solution : workspace.Generator;
        if (artifact is null || string.IsNullOrWhiteSpace(artifact.Content))
        {
            return new(type, CompileStatus.Failed, FileName(type), $"Chưa có {FileName(type)} để compile.", TimeSpan.Zero, null);
        }

        var toolchain = await toolchainService.VerifyAsync(cancellationToken);
        if (!toolchain.IsReady)
        {
            var output = toolchain.Message + "\n" + string.Join("\n", toolchain.Issues);
            await codeRepository.MarkCompileAsync(projectId, type, CompileStatus.Failed, output, cancellationToken);
            return new(type, CompileStatus.Failed, FileName(type), output, TimeSpan.Zero, null);
        }

        await codeRepository.MarkCompileAsync(projectId, type, CompileStatus.Compiling, string.Empty, cancellationToken);
        var directory = Path.Combine(paths.ProjectsPath, projectId.ToString("N"), "temp", $"compile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourceName = FileName(type);
            var sourcePath = Path.Combine(directory, sourceName);
            var executableName = type == CodeArtifactType.Solution ? "solution.exe" : "generate.exe";
            var temporaryExecutable = Path.Combine(directory, executableName);
            await File.WriteAllTextAsync(sourcePath, artifact.Content, new UTF8Encoding(false), cancellationToken);
            var arguments = new List<string>
            {
                sourceName,
                "-std=gnu++17",
                "-O2",
                "-pipe",
                "-Wall",
                "-Wextra",
            };
            if (type == CodeArtifactType.Generator)
            {
                arguments.Add("-I");
                arguments.Add(Path.Combine(paths.ToolchainPath, "testlib"));
            }
            arguments.Add("-o");
            arguments.Add(executableName);

            var process = await processRunner.RunAsync(new(
                toolchain.CompilerPath,
                arguments,
                directory,
                TimeSpan.FromSeconds(30),
                OutputLimit,
                Environment: CompilerEnvironment(toolchain.CompilerPath)), cancellationToken);
            var status = process.Cancelled ? CompileStatus.Cancelled
                : process.TimedOut ? CompileStatus.TimedOut
                : process.Succeeded ? CompileStatus.Succeeded
                : CompileStatus.Failed;
            var output = FormatOutput(arguments, process);
            string? savedExecutable = null;
            if (status == CompileStatus.Succeeded)
            {
                var binDirectory = Path.Combine(paths.ProjectsPath, projectId.ToString("N"), "code", "bin");
                Directory.CreateDirectory(binDirectory);
                savedExecutable = Path.Combine(binDirectory, executableName);
                File.Copy(temporaryExecutable, savedExecutable, overwrite: true);
            }

            await codeRepository.MarkCompileAsync(projectId, type, status, output, CancellationToken.None);
            return new(type, status, sourceName, output, process.Duration, savedExecutable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await codeRepository.MarkCompileAsync(projectId, type, CompileStatus.Cancelled, "Đã hủy compile.", CancellationToken.None);
            throw;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string FormatOutput(IReadOnlyCollection<string> arguments, ProcessExecutionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("g++.exe " + string.Join(" ", arguments.Select(DisplayArgument)));
        builder.AppendLine($"Duration: {result.Duration.TotalMilliseconds:N0} ms");
        if (result.StartError is not null) builder.AppendLine("Start error: " + result.StartError);
        if (result.StandardOutput.Length > 0) builder.AppendLine("stdout:\n" + result.StandardOutput);
        if (result.StandardError.Length > 0) builder.AppendLine("stderr:\n" + result.StandardError);
        if (result.TimedOut) builder.AppendLine("Compile timed out after 30 seconds.");
        if (result.OutputTruncated) builder.AppendLine("Output exceeded 10 MB and the process was terminated.");
        if (result.ExitCode is not null) builder.AppendLine($"Exit code: {result.ExitCode}");
        return builder.ToString().TrimEnd();
    }

    private static string DisplayArgument(string value) => value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

    private static IReadOnlyDictionary<string, string?> CompilerEnvironment(string compilerPath) =>
        new Dictionary<string, string?>
        {
            ["PATH"] = Path.GetDirectoryName(compilerPath) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        };

    private static string FileName(CodeArtifactType type) =>
        type == CodeArtifactType.Solution ? "solution.cpp" : "generate.cpp";
}
