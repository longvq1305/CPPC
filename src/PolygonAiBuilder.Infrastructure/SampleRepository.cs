using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class SampleRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : ISampleRepository
{
    public async Task<LocalSampleSnapshot?> GetAsync(
        Guid projectId,
        int testIndex,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sample = await db.Samples.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProblemProjectId == projectId && item.TestIndex == testIndex,
            cancellationToken);
        return sample is null ? null : Map(sample);
    }

    public async Task<LocalSampleSnapshot> SaveGeneratedAsync(
        Guid projectId,
        int testIndex,
        string input,
        string output,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var versions = await db.CodeArtifacts.AsNoTracking()
            .Where(item => item.ProblemProjectId == projectId)
            .Select(item => new { item.Type, item.Id, item.Version })
            .ToArrayAsync(cancellationToken);
        var solutionArtifact = versions.SingleOrDefault(item => item.Type == CodeArtifactType.Solution)
            ?? throw new InvalidOperationException("Chưa có solution.cpp.");
        var generatorArtifact = versions.SingleOrDefault(item => item.Type == CodeArtifactType.Generator)
            ?? throw new InvalidOperationException("Chưa có generate.cpp.");
        var solutionVersionId = await db.CodeArtifactVersions.AsNoTracking()
            .Where(item => item.CodeArtifactId == solutionArtifact.Id && item.VersionNumber == solutionArtifact.Version)
            .Select(item => item.Id).SingleAsync(cancellationToken);
        var generatorVersionId = await db.CodeArtifactVersions.AsNoTracking()
            .Where(item => item.CodeArtifactId == generatorArtifact.Id && item.VersionNumber == generatorArtifact.Version)
            .Select(item => item.Id).SingleAsync(cancellationToken);

        var sample = await db.Samples.SingleOrDefaultAsync(
            item => item.ProblemProjectId == projectId && item.TestIndex == testIndex,
            cancellationToken);
        if (sample is null)
        {
            sample = new Sample { Id = Guid.NewGuid(), ProblemProjectId = projectId, TestIndex = testIndex };
            db.Samples.Add(sample);
        }

        var contentChanged = !string.Equals(sample.Input, Normalize(input), StringComparison.Ordinal)
            || !string.Equals(sample.Output, Normalize(output), StringComparison.Ordinal);
        sample.Input = Normalize(input);
        sample.Output = Normalize(output);
        sample.GeneratedAt = timeProvider.GetUtcNow();
        sample.SolutionVersionId = solutionVersionId;
        sample.GeneratorVersionId = generatorVersionId;
        sample.InputIsStale = false;
        sample.OutputIsStale = false;
        sample.WasManuallyEdited = false;
        if (contentChanged)
        {
            var project = await db.ProblemProjects.SingleAsync(item => item.Id == projectId, cancellationToken);
            project.InvalidateSync(PolygonSyncPhase.PointsEnabled, sample.GeneratedAt);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Map(sample);
    }

    public async Task<LocalSampleSnapshot> SaveManualAsync(
        Guid projectId,
        int testIndex,
        string input,
        string output,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sample = await db.Samples.SingleOrDefaultAsync(
            item => item.ProblemProjectId == projectId && item.TestIndex == testIndex,
            cancellationToken) ?? throw new KeyNotFoundException("Chưa có Sample 1 để chỉnh sửa.");
        var contentChanged = !string.Equals(sample.Input, Normalize(input), StringComparison.Ordinal)
            || !string.Equals(sample.Output, Normalize(output), StringComparison.Ordinal);
        sample.Input = Normalize(input);
        sample.Output = Normalize(output);
        sample.WasManuallyEdited = true;
        if (contentChanged)
        {
            var project = await db.ProblemProjects.SingleAsync(item => item.Id == projectId, cancellationToken);
            project.InvalidateSync(PolygonSyncPhase.PointsEnabled, timeProvider.GetUtcNow());
        }
        await db.SaveChangesAsync(cancellationToken);
        return Map(sample);
    }

    private static LocalSampleSnapshot Map(Sample sample) => new(
        sample.Id, sample.TestIndex, sample.Input, sample.Output, sample.GeneratedAt,
        sample.SolutionVersionId, sample.GeneratorVersionId, sample.InputIsStale,
        sample.OutputIsStale, sample.WasManuallyEdited);

    private static string Normalize(string value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
