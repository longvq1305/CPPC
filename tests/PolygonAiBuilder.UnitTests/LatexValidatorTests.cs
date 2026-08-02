using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.UnitTests;

public sealed class LatexValidatorTests
{
    private readonly LatexValidator validator = new();

    [Fact]
    public void Validate_AcceptsSupportedPolygonLatex()
    {
        var content = new StatementContent(
            "Range Sum",
            "Find $a+b$ and print \\textbf{the answer}.\\n\\begin{itemize}\\item Fast\\item Exact\\end{itemize}",
            "Two integers $a$ and $b$.",
            "Print $a+b$.",
            "\\begin{tabular}{cc}1 & 2 \\\\ 3 & 4\\end{tabular}");

        var issues = validator.Validate(content);

        Assert.DoesNotContain(issues, issue => issue.Severity == LatexIssueSeverity.Error);
    }

    [Fact]
    public void Validate_ReportsUnbalancedBracesEnvironmentAndMathDelimiter()
    {
        var content = new StatementContent(
            "Broken }",
            "\\begin{itemize}\\item x",
            "An unfinished $formula",
            "ok",
            string.Empty);

        var issues = validator.Validate(content);

        Assert.Contains(issues, issue => issue.Field == "Title" && issue.Severity == LatexIssueSeverity.Error);
        Assert.Contains(issues, issue => issue.Field == "Legend" && issue.Message.Contains("\\end", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Field == "Input" && issue.Severity == LatexIssueSeverity.Error);
    }

    [Fact]
    public void Validate_UnknownCommandIsWarningOnly()
    {
        var issues = validator.Validate(new("Title", "Use \\mystery{x}.", "in", "out", ""));

        var issue = Assert.Single(issues);
        Assert.Equal(LatexIssueSeverity.Warning, issue.Severity);
        Assert.Equal("Legend", issue.Field);
    }
}
