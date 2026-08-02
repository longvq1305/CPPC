using System.Text;
using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class CodeRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    RuntimePaths paths,
    TimeProvider timeProvider) : ICodeRepository
{
    private const int MaximumSourceLength = 2 * 1024 * 1024;

    public async Task<CodeWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var statement = await db.Statements.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId, cancellationToken);
        if (statement is null)
        {
            return null;
        }

        var artifacts = await db.CodeArtifacts.AsNoTracking()
            .Include(item => item.Versions)
            .Where(item => item.ProblemProjectId == projectId)
            .ToArrayAsync(cancellationToken);
        return Map(projectId, statement, artifacts);
    }

    public async Task<CodeWorkspaceSnapshot> SaveGeneratedAsync(
        Guid projectId,
        CodeGenerationOutput output,
        int statementVersion,
        string provider,
        string model,
        CancellationToken cancellationToken)
    {
        var solution = Normalize(output.SolutionCpp);
        var generator = Normalize(output.GeneratorCpp);
        ValidateLength(solution);
        ValidateLength(generator);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var statement = await db.Statements
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        if (statement.CurrentVersion != statementVersion)
        {
            throw new InvalidOperationException("Statement đã thay đổi trong khi AI tạo code. Hãy tạo lại từ version mới nhất.");
        }

        var artifacts = await db.CodeArtifacts
            .Include(item => item.Versions)
            .Where(item => item.ProblemProjectId == projectId)
            .ToListAsync(cancellationToken);
        var isFirstGeneration = artifacts.Count == 0;
        var solutionChanged = HasChanged(artifacts, CodeArtifactType.Solution, solution);
        var generatorChanged = HasChanged(artifacts, CodeArtifactType.Generator, generator);
        Upsert(db, artifacts, projectId, CodeArtifactType.Solution, "solution.cpp", solution,
            ChangeSource.AI, statementVersion, provider, model, clearStale: true);
        Upsert(db, artifacts, projectId, CodeArtifactType.Generator, "generate.cpp", generator,
            ChangeSource.AI, statementVersion, provider, model, clearStale: true);
        statement.IsCodeStale = false;
        if (solutionChanged || generatorChanged)
        {
            var project = await db.ProblemProjects.SingleAsync(item => item.Id == projectId, cancellationToken);
            project.InvalidateSync(PolygonSyncPhase.StatementSaved, timeProvider.GetUtcNow());
        }
        if (isFirstGeneration && output.RecommendedChecker is "ncmp.cpp" or "wcmp.cpp")
        {
            var configuration = await db.TestConfigurations.SingleAsync(
                item => item.ProblemProjectId == projectId, cancellationToken);
            configuration.Checker = output.RecommendedChecker;
            configuration.UpdatedAt = timeProvider.GetUtcNow();
        }
        await MarkSamplesStaleAsync(db, projectId, solutionChanged, generatorChanged, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await WriteSourceAsync(projectId, "solution.cpp", solution, cancellationToken);
        await WriteSourceAsync(projectId, "generate.cpp", generator, cancellationToken);
        return (await GetAsync(projectId, cancellationToken))!;
    }

    public async Task<CodeWorkspaceSnapshot> SaveArtifactAsync(
        Guid projectId,
        CodeArtifactType type,
        string content,
        ChangeSource source,
        int statementVersion,
        string? provider,
        string? model,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(content);
        ValidateLength(normalized);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var statement = await db.Statements
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        var artifacts = await db.CodeArtifacts
            .Include(item => item.Versions)
            .Where(item => item.ProblemProjectId == projectId)
            .ToListAsync(cancellationToken);
        var existing = artifacts.SingleOrDefault(item => item.Type == type);
        var changed = existing is null || !string.Equals(existing.Content, normalized, StringComparison.Ordinal);
        var provenanceVersion = existing?.GeneratedFromStatementVersion ?? statementVersion;
        Upsert(db, artifacts, projectId, type, FileName(type), normalized, source,
            provenanceVersion, provider, model, clearStale: false);
        await MarkSamplesStaleAsync(db, projectId,
            solutionChanged: changed && type == CodeArtifactType.Solution,
            generatorChanged: changed && type == CodeArtifactType.Generator,
            cancellationToken);
        if (changed)
        {
            var project = await db.ProblemProjects.SingleAsync(item => item.Id == projectId, cancellationToken);
            project.InvalidateSync(type == CodeArtifactType.Solution
                ? PolygonSyncPhase.StatementSaved
                : PolygonSyncPhase.SolutionSaved, timeProvider.GetUtcNow());
        }
        await db.SaveChangesAsync(cancellationToken);
        await WriteSourceAsync(projectId, FileName(type), normalized, cancellationToken);
        return (await GetAsync(projectId, cancellationToken))!;
    }

    public async Task MarkCompileAsync(
        Guid projectId,
        CodeArtifactType type,
        CompileStatus status,
        string output,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var artifact = await db.CodeArtifacts.Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId && item.Type == type, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy {FileName(type)}.");
        artifact.LastCompileStatus = status;
        artifact.LastCompileOutput = LimitCompilerOutput(output);
        artifact.UpdatedAt = timeProvider.GetUtcNow();
        var version = artifact.Versions.SingleOrDefault(item => item.VersionNumber == artifact.Version);
        if (version is not null)
        {
            version.CompileStatus = status;
            version.CompilerOutput = artifact.LastCompileOutput;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CodeWorkspaceSnapshot> RestoreAsync(
        Guid projectId,
        CodeArtifactType type,
        int versionNumber,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var artifact = await db.CodeArtifacts.Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.ProblemProjectId == projectId && item.Type == type, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy {FileName(type)}.");
        var target = artifact.Versions.SingleOrDefault(item => item.VersionNumber == versionNumber)
            ?? throw new KeyNotFoundException($"Không tìm thấy version {versionNumber} của {artifact.FileName}.");
        if (string.Equals(artifact.Content, target.Content, StringComparison.Ordinal))
        {
            return (await GetAsync(projectId, cancellationToken))!;
        }

        var now = timeProvider.GetUtcNow();
        artifact.Content = target.Content;
        artifact.Version++;
        artifact.LastCompileStatus = CompileStatus.NotCompiled;
        artifact.LastCompileOutput = string.Empty;
        artifact.UpdatedAt = now;
        db.CodeArtifactVersions.Add(new CodeArtifactVersion
        {
            Id = Guid.NewGuid(),
            CodeArtifactId = artifact.Id,
            VersionNumber = artifact.Version,
            Content = target.Content,
            Source = ChangeSource.User,
            Provider = target.Provider,
            Model = target.Model,
            CompileStatus = CompileStatus.NotCompiled,
            CreatedAt = now,
        });
        await MarkSamplesStaleAsync(db, projectId,
            solutionChanged: type == CodeArtifactType.Solution,
            generatorChanged: type == CodeArtifactType.Generator,
            cancellationToken);
        var project = await db.ProblemProjects.SingleAsync(item => item.Id == projectId, cancellationToken);
        project.InvalidateSync(type == CodeArtifactType.Solution
            ? PolygonSyncPhase.StatementSaved
            : PolygonSyncPhase.SolutionSaved, now);
        await db.SaveChangesAsync(cancellationToken);
        await WriteSourceAsync(projectId, artifact.FileName, artifact.Content, cancellationToken);
        return (await GetAsync(projectId, cancellationToken))!;
    }

    private void Upsert(
        BuilderDbContext db,
        ICollection<CodeArtifact> artifacts,
        Guid projectId,
        CodeArtifactType type,
        string fileName,
        string content,
        ChangeSource source,
        int statementVersion,
        string? provider,
        string? model,
        bool clearStale)
    {
        var artifact = artifacts.SingleOrDefault(item => item.Type == type);
        if (artifact is not null && string.Equals(artifact.Content, content, StringComparison.Ordinal))
        {
            if (clearStale)
            {
                artifact.IsStale = false;
                artifact.GeneratedFromStatementVersion = statementVersion;
            }
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (artifact is null)
        {
            artifact = new CodeArtifact
            {
                Id = Guid.NewGuid(),
                ProblemProjectId = projectId,
                Type = type,
                FileName = fileName,
                Content = content,
                Version = 1,
                GeneratedFromStatementVersion = statementVersion,
                LastCompileStatus = CompileStatus.NotCompiled,
                UpdatedAt = now,
            };
            db.CodeArtifacts.Add(artifact);
            artifacts.Add(artifact);
        }
        else
        {
            artifact.Content = content;
            artifact.Version++;
            artifact.GeneratedFromStatementVersion = clearStale ? statementVersion : artifact.GeneratedFromStatementVersion;
            artifact.IsStale = clearStale ? false : artifact.IsStale;
            artifact.LastCompileStatus = CompileStatus.NotCompiled;
            artifact.LastCompileOutput = string.Empty;
            artifact.UpdatedAt = now;
        }

        db.CodeArtifactVersions.Add(new CodeArtifactVersion
        {
            Id = Guid.NewGuid(),
            CodeArtifactId = artifact.Id,
            VersionNumber = artifact.Version,
            Content = content,
            Source = source,
            Provider = provider,
            Model = model,
            CompileStatus = CompileStatus.NotCompiled,
            CreatedAt = now,
        });
    }

    private async Task WriteSourceAsync(
        Guid projectId,
        string fileName,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(paths.ProjectsPath, projectId.ToString("N"), "code");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, fileName);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static CodeWorkspaceSnapshot Map(Guid projectId, Statement statement, IReadOnlyCollection<CodeArtifact> artifacts) =>
        new(projectId, statement.CurrentVersion, statement.IsCodeStale,
            MapArtifact(artifacts.SingleOrDefault(item => item.Type == CodeArtifactType.Solution)),
            MapArtifact(artifacts.SingleOrDefault(item => item.Type == CodeArtifactType.Generator)));

    private static CodeArtifactInfo? MapArtifact(CodeArtifact? artifact) => artifact is null ? null : new(
        artifact.Type,
        artifact.FileName,
        artifact.Content,
        artifact.Version,
        artifact.GeneratedFromStatementVersion,
        artifact.IsStale,
        artifact.LastCompileStatus,
        artifact.LastCompileOutput,
        artifact.UpdatedAt,
        artifact.Versions.OrderByDescending(item => item.VersionNumber)
            .Select(item => new CodeArtifactVersionInfo(
                item.Id,
                item.VersionNumber,
                item.Content,
                item.Source,
                item.Provider,
                item.Model,
                item.CompileStatus,
                item.CompilerOutput,
                item.CreatedAt))
            .ToArray());

    private static string Normalize(string content) =>
        (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimStart('\uFEFF');

    private static void ValidateLength(string content)
    {
        if (content.Length > MaximumSourceLength)
        {
            throw new ArgumentException("Mỗi source file không được vượt quá 2 MB.", nameof(content));
        }
    }

    private static string FileName(CodeArtifactType type) =>
        type == CodeArtifactType.Solution ? "solution.cpp" : "generate.cpp";

    private static string LimitCompilerOutput(string value) => value.Length <= 200_000
        ? value
        : value[..200_000] + "\n… compiler output đã được rút gọn trong database.";

    private static bool HasChanged(
        IEnumerable<CodeArtifact> artifacts,
        CodeArtifactType type,
        string content) =>
        artifacts.SingleOrDefault(item => item.Type == type) is not { } artifact
        || !string.Equals(artifact.Content, content, StringComparison.Ordinal);

    private static async Task MarkSamplesStaleAsync(
        BuilderDbContext db,
        Guid projectId,
        bool solutionChanged,
        bool generatorChanged,
        CancellationToken cancellationToken)
    {
        if (!solutionChanged && !generatorChanged) return;
        var samples = await db.Samples.Where(item => item.ProblemProjectId == projectId)
            .ToArrayAsync(cancellationToken);
        foreach (var sample in samples)
        {
            if (generatorChanged) sample.InputIsStale = true;
            if (solutionChanged || generatorChanged) sample.OutputIsStale = true;
        }
    }
}
