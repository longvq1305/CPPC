using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class StatementServiceTests
{
    [Fact]
    public async Task ApplyAiUpdate_MergesNullFieldsAndPersistsAiMetadata()
    {
        var projectId = Guid.NewGuid();
        var initial = new StatementContent("Old", "Legend", "Input", "Output", "Note");
        var repository = new MemoryStatementRepository(projectId, initial);
        var service = CreateService(repository);

        var result = await service.ApplyAiUpdateAsync(
            projectId,
            new("New title", null, null, "New output", null, "refined title and output"),
            "Gemini",
            "gemini-test");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Statement);
        Assert.Equal("New title", result.Statement.Content.Title);
        Assert.Equal("Legend", result.Statement.Content.Legend);
        Assert.Equal("New output", result.Statement.Content.Output);
        Assert.Equal(ChangeSource.AI, repository.LastSource);
        Assert.Equal("Gemini", repository.LastProvider);
        Assert.Equal("gemini-test", repository.LastModel);
    }

    [Fact]
    public async Task ApplyAiUpdate_WithInvalidLatexRefusesToPersist()
    {
        var projectId = Guid.NewGuid();
        var repository = new MemoryStatementRepository(projectId, new("Title", "Legend", "Input", "Output", ""));
        var service = CreateService(repository);

        var result = await service.ApplyAiUpdateAsync(
            projectId,
            new(null, "Broken $formula", null, null, null, "bad update"),
            "OpenAI",
            "gpt-test");

        Assert.False(result.Succeeded);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Contains(result.Issues, issue => issue.Severity == LatexIssueSeverity.Error);
    }

    [Fact]
    public void Compare_ReportsOnlyChangedFields()
    {
        var service = CreateService(new MemoryStatementRepository(Guid.NewGuid(), StatementContent.Empty));

        var diff = service.Compare(
            new("A", "B", "C", "D", "E"),
            new("A", "Changed", "C", "D", ""));

        Assert.True(diff.HasChanges);
        Assert.Equal(["Legend", "Note"], diff.Fields.Where(item => item.Changed).Select(item => item.Field));
    }

    private static StatementService CreateService(IStatementRepository repository) =>
        new(repository, null!, null!, new LatexValidator(), []);

    private sealed class MemoryStatementRepository(Guid projectId, StatementContent content) : IStatementRepository
    {
        private StatementSnapshot snapshot = new(projectId, "english", 0, content, false, DateTimeOffset.UtcNow, []);

        public int SaveCalls { get; private set; }
        public ChangeSource? LastSource { get; private set; }
        public string? LastProvider { get; private set; }
        public string? LastModel { get; private set; }

        public Task<StatementSnapshot?> GetAsync(Guid requestedProjectId, CancellationToken cancellationToken) =>
            Task.FromResult<StatementSnapshot?>(requestedProjectId == projectId ? snapshot : null);

        public Task<StatementSnapshot> SaveAsync(
            Guid requestedProjectId,
            StatementContent newContent,
            ChangeSource source,
            string? provider,
            string? model,
            Guid? messageId,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            LastSource = source;
            LastProvider = provider;
            LastModel = model;
            snapshot = snapshot with { CurrentVersion = snapshot.CurrentVersion + 1, Content = newContent };
            return Task.FromResult(snapshot);
        }

        public Task<StatementSnapshot> RestoreAsync(Guid requestedProjectId, int versionNumber, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }
}
