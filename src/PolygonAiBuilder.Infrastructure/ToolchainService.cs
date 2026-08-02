using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Infrastructure;

public sealed class ToolchainService(
    RuntimePaths paths,
    IProcessRunner processRunner,
    IHttpClientFactory httpClientFactory) : IToolchainService
{
    private const int ProcessOutputLimit = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ToolchainStatus> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(cancellationToken);
        var compilerPath = Path.Combine(paths.ToolchainPath, "mingw64", "bin", "g++.exe");
        var issues = new List<string>();
        var hasTestlib = await VerifyFileAsync(manifest, "testlib/testlib.h", cancellationToken);
        var hasTestlibLicense = await VerifyFileAsync(manifest, "testlib/LICENSE", cancellationToken);
        var hasNcmp = await VerifyFileAsync(manifest, "checkers/ncmp.cpp", cancellationToken);
        var hasWcmp = await VerifyFileAsync(manifest, "checkers/wcmp.cpp", cancellationToken);
        if (!hasTestlib) issues.Add("testlib.h thiếu hoặc checksum không khớp.");
        if (!hasTestlibLicense) issues.Add("testlib LICENSE thiếu hoặc checksum không khớp.");
        if (!hasNcmp) issues.Add("ncmp.cpp thiếu hoặc checksum không khớp.");
        if (!hasWcmp) issues.Add("wcmp.cpp thiếu hoặc checksum không khớp.");
        if (!File.Exists(compilerPath))
        {
            issues.Add("Chưa có bundled g++.exe.");
            return new(false, compilerPath, "", false, hasTestlib, hasNcmp, hasWcmp,
                "Toolchain chưa sẵn sàng. Bấm Repair toolchain để tải bản đã pin.", issues);
        }

        var versionResult = await processRunner.RunAsync(new(
            compilerPath,
            ["--version"],
            paths.ToolchainPath,
            TimeSpan.FromSeconds(10),
            256 * 1024,
            Environment: CompilerEnvironment(compilerPath)), cancellationToken);
        var version = FirstLine(versionResult.StandardOutput.Length > 0
            ? versionResult.StandardOutput
            : versionResult.StandardError);
        if (!versionResult.Succeeded)
        {
            issues.Add("g++.exe không chạy được: " + SafeProcessError(versionResult));
            return new(false, compilerPath, version, false, hasTestlib, hasNcmp, hasWcmp,
                "Không thể chạy bundled compiler.", issues);
        }

        var supports = await CompileSmokeAsync(compilerPath, cancellationToken);
        if (!supports)
        {
            issues.Add("Compiler không vượt qua smoke test -std=gnu++17.");
        }

        var ready = supports && hasTestlib && hasTestlibLicense && hasNcmp && hasWcmp;
        return new(ready, compilerPath, version, supports, hasTestlib, hasNcmp, hasWcmp,
            ready ? "Bundled GNU C++17 toolchain đã được xác minh." : "Toolchain còn thiếu hoặc không hợp lệ.", issues);
    }

    public async Task<ToolchainStatus> RepairAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(cancellationToken);
        Directory.CreateDirectory(paths.ToolchainPath);
        var compilerPath = Path.Combine(paths.ToolchainPath, "mingw64", "bin", "g++.exe");
        if (!File.Exists(compilerPath) || !await CompilerHealthyAsync(compilerPath, cancellationToken))
        {
            await AcquireCompilerAsync(manifest, cancellationToken);
        }

        foreach (var file in manifest.Testlib.Files)
        {
            if (!await VerifyFileAsync(file, cancellationToken))
            {
                await DownloadVerifiedFileAsync(file, cancellationToken);
            }
        }

        return await VerifyAsync(cancellationToken);
    }

    private async Task AcquireCompilerAsync(ToolchainManifest manifest, CancellationToken cancellationToken)
    {
        var downloads = Path.Combine(paths.ToolchainPath, "downloads");
        Directory.CreateDirectory(downloads);
        var archive = Path.Combine(downloads, manifest.Compiler.ArchiveFileName);
        if (!await HasHashAsync(archive, manifest.Compiler.Sha256, cancellationToken))
        {
            var temporary = archive + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await DownloadAsync(manifest.Compiler.ArchiveUrl, temporary, cancellationToken);
                if (!await HasHashAsync(temporary, manifest.Compiler.Sha256, cancellationToken))
                {
                    throw new InvalidDataException("Checksum archive compiler không khớp manifest.");
                }

                File.Move(temporary, archive, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        var staging = Path.Combine(paths.ToolchainPath, $"staging-{Guid.NewGuid():N}");
        var target = Path.Combine(paths.ToolchainPath, "mingw64");
        var backup = Path.Combine(paths.ToolchainPath, $"mingw64-backup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            await ExtractSafeAsync(archive, staging, cancellationToken);
            var stagedRoot = Path.Combine(staging, manifest.Compiler.ArchiveRoot);
            if (!File.Exists(Path.Combine(stagedRoot, "bin", "g++.exe")))
            {
                throw new InvalidDataException("Archive compiler không chứa mingw64/bin/g++.exe như manifest.");
            }

            if (Directory.Exists(target)) Directory.Move(target, backup);
            try
            {
                Directory.Move(stagedRoot, target);
            }
            catch
            {
                if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }

            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private async Task DownloadVerifiedFileAsync(ToolchainFile file, CancellationToken cancellationToken)
    {
        var destination = ResolveToolchainPath(file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await DownloadAsync(file.Url, temporary, cancellationToken);
            if (!await HasHashAsync(temporary, file.Sha256, cancellationToken))
            {
                throw new InvalidDataException($"Checksum không khớp cho {file.RelativePath}.");
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("toolchain-download");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private async Task<bool> CompileSmokeAsync(string compilerPath, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(paths.ToolchainPath, $"verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "smoke.cpp");
            await File.WriteAllTextAsync(source,
                "#include <optional>\n#include <iostream>\nint main(){std::optional<int> x=17; std::cout<<*x<<'\\n';}\n",
                new UTF8Encoding(false), cancellationToken);
            var result = await processRunner.RunAsync(new(
                compilerPath,
                [source, "-std=gnu++17", "-O2", "-pipe", "-Wall", "-Wextra", "-o", Path.Combine(directory, "smoke.exe")],
                directory,
                TimeSpan.FromSeconds(30),
                ProcessOutputLimit,
                Environment: CompilerEnvironment(compilerPath)), cancellationToken);
            return result.Succeeded && File.Exists(Path.Combine(directory, "smoke.exe"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<bool> CompilerHealthyAsync(string compilerPath, CancellationToken cancellationToken)
    {
        var version = await processRunner.RunAsync(new(
            compilerPath, ["--version"], paths.ToolchainPath, TimeSpan.FromSeconds(10),
            256 * 1024, Environment: CompilerEnvironment(compilerPath)), cancellationToken);
        return version.Succeeded && await CompileSmokeAsync(compilerPath, cancellationToken);
    }

    private async Task<ToolchainManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(paths.ToolchainPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Thiếu toolchain/manifest.json.", manifestPath);
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<ToolchainManifest>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException("Toolchain manifest không hợp lệ.");
    }

    private Task<bool> VerifyFileAsync(ToolchainManifest manifest, string relativePath, CancellationToken cancellationToken)
    {
        var file = manifest.Testlib.Files.Single(item =>
            string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        return VerifyFileAsync(file, cancellationToken);
    }

    private Task<bool> VerifyFileAsync(ToolchainFile file, CancellationToken cancellationToken) =>
        HasHashAsync(ResolveToolchainPath(file.RelativePath), file.Sha256, cancellationToken);

    private string ResolveToolchainPath(string relativePath)
    {
        var root = Path.GetFullPath(paths.ToolchainPath) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(paths.ToolchainPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Toolchain manifest chứa path không an toàn.");
        }

        return fullPath;
    }

    private static async Task<bool> HasHashAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExtractSafeAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Archive compiler chứa path traversal.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        }
    }

    private static IReadOnlyDictionary<string, string?> CompilerEnvironment(string compilerPath)
    {
        var compilerDirectory = Path.GetDirectoryName(compilerPath)!;
        return new Dictionary<string, string?>
        {
            ["PATH"] = compilerDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        };
    }

    private static string SafeProcessError(ProcessExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StartError))
        {
            return result.StartError;
        }

        var line = FirstLine(result.StandardError);
        return line.Length > 0 ? line : "unknown process error";
    }

    private static string FirstLine(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;

    private sealed record ToolchainManifest(int SchemaVersion, ToolchainCompiler Compiler, ToolchainSources Testlib);
    private sealed record ToolchainCompiler(string Name, string ArchiveUrl, string ArchiveFileName, string Sha256, string ArchiveRoot);
    private sealed record ToolchainSources(string Revision, IReadOnlyList<ToolchainFile> Files);
    private sealed record ToolchainFile(string RelativePath, string Url, string Sha256);
}
