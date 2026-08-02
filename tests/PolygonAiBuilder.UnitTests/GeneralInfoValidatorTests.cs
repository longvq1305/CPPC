using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.UnitTests;

public sealed class GeneralInfoValidatorTests
{
    [Fact]
    public void Validate_AcceptsOfficialPolygonBoundaryValues()
    {
        var minimum = GeneralInfoValidator.Validate("minimum", "stdin", "stdout", 250, 4);
        var maximum = GeneralInfoValidator.Validate("maximum", "input.txt", "output.txt", 15_000, 1_024);

        Assert.Empty(minimum);
        Assert.Empty(maximum);
    }

    [Theory]
    [InlineData(249)]
    [InlineData(15_001)]
    [InlineData(275)]
    public void Validate_RejectsInvalidTimeLimit(int timeLimit)
    {
        var issues = GeneralInfoValidator.Validate("problem", "stdin", "stdout", timeLimit, 256);

        Assert.Contains(issues, issue => issue.Field == "TimeLimitMs");
    }

    [Fact]
    public void Validate_RejectsEqualFileNamesIgnoringCase()
    {
        var issues = GeneralInfoValidator.Validate("problem", "DATA.IN", "data.in", 1_000, 256);

        Assert.Contains(issues, issue => issue.Field == "OutputFile");
    }

    [Theory]
    [InlineData("../input.txt")]
    [InlineData("..\\input.txt")]
    [InlineData("C:\\temp\\input.txt")]
    [InlineData("..")]
    public void Validate_RejectsPathsAndTraversal(string inputFile)
    {
        var issues = GeneralInfoValidator.Validate("problem", inputFile, "stdout", 1_000, 256);

        Assert.Contains(issues, issue => issue.Field == "InputFile");
    }
}
