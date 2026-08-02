using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.UnitTests;

public sealed class PolygonStatementTemplateTests
{
    [Fact]
    public void NeedsVietnameseFontEncoding_DetectsVietnameseStatement()
    {
        var content = new StatementContent(
            "Đếm bộ nghiệm",
            "Cho một số nguyên dương.",
            "Một dòng.",
            "In kết quả.",
            string.Empty);

        Assert.True(PolygonStatementTemplate.NeedsVietnameseFontEncoding(content));
        Assert.True(PolygonStatementTemplate.NeedsVietnameseFontEncoding(
            new("Nghie\u0302\u0323m", "", "", "", "")));
        Assert.False(PolygonStatementTemplate.NeedsVietnameseFontEncoding(
            new("Count solutions", "Given an integer.", "One line.", "Print the answer.", "")));
    }

    [Fact]
    public void EnableVietnameseFontEncoding_AddsT5AfterExistingEncoding()
    {
        const string template = "\\documentclass{article}\n\\usepackage [T2A] {fontenc}\n\\begin{document}\n";

        var updated = PolygonStatementTemplate.EnableVietnameseFontEncoding(template);

        Assert.Contains("\\usepackage [T2A,T5] {fontenc}", updated, StringComparison.Ordinal);
        Assert.Equal(updated, PolygonStatementTemplate.EnableVietnameseFontEncoding(updated));
    }

    [Fact]
    public void EnableVietnameseFontEncoding_RejectsUnknownTemplateShape()
    {
        var exception = Assert.Throws<ExternalServiceException>(
            () => PolygonStatementTemplate.EnableVietnameseFontEncoding("\\documentclass{article}"));

        Assert.Equal("statement_template_unsupported", exception.Code);
    }
}
