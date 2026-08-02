using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class ProblemProjectTests
{
    [Fact]
    public void Create_InitializesOneProjectWorkflowAggregate()
    {
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        var project = ProblemProject.Create("  sample-problem  ", now);

        Assert.Equal("sample-problem", project.InternalName);
        Assert.Equal(1, project.CurrentScreen);
        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.Equal(PolygonSyncPhase.NotCreated, project.PolygonSyncPhase);
        Assert.Equal("stdin", project.GeneralInfo.InputFile);
        Assert.Equal("stdout", project.GeneralInfo.OutputFile);
        Assert.Equal("english", project.Statement.Language);
        Assert.Equal(100, project.TestConfiguration.TestCount);
        Assert.Equal(project.Id, project.Conversation.ProblemProjectId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void SetCurrentScreen_RejectsOutOfRangeValues(int screen)
    {
        var project = ProblemProject.Create("sample", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => project.SetCurrentScreen(screen, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void LinkPolygonProblem_PreventsLinkingASecondProblem()
    {
        var project = ProblemProject.Create("sample", DateTimeOffset.UtcNow);
        project.LinkPolygonProblem(123, DateTimeOffset.UtcNow);

        var exception = Assert.Throws<InvalidOperationException>(
            () => project.LinkPolygonProblem(456, DateTimeOffset.UtcNow));

        Assert.Contains("already linked", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(123, project.PolygonProblemId);
    }
}
