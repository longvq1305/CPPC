using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class CodeGenerationValidationTests
{
    [Fact]
    public void Validate_AcceptsRequiredSolutionAndTestIdGeneratorWorkflow()
    {
        var output = new CodeGenerationOutput(
            "#include <bits/stdc++.h>\nusing namespace std;\nint main(){ios_base::sync_with_stdio(false);cin.tie(NULL);return 0;}",
            "#include <bits/stdc++.h>\n#include \"testlib.h\"\nusing namespace std;\nint main(int argc,char** argv){registerGen(argc, argv, 1); int test_id=stoi(argv[1]); mt19937_64 gen(test_id); return 0;}",
            "summary", "O(1)", "O(1)", "ncmp.cpp", []);

        Assert.Empty(CodeGenerationService.Validate(output));
    }

    [Fact]
    public void Validate_RejectsMarkdownAndIncompleteGenerator()
    {
        var errors = CodeGenerationService.Validate(CodeArtifactType.Generator,
            "```cpp\n#include <bits/stdc++.h>\nusing namespace std;\nint main(){return 0;}\n```");

        Assert.Contains(errors, item => item.Contains("Markdown", StringComparison.Ordinal));
        Assert.Contains(errors, item => item.Contains("testlib.h", StringComparison.Ordinal));
        Assert.Contains(errors, item => item.Contains("mt19937_64", StringComparison.Ordinal));
        Assert.Contains(errors, item => item.Contains("registerGen", StringComparison.Ordinal));
        Assert.Contains(errors, item => item.Contains("test_id", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("registerGen(argc, argv, 0)")]
    [InlineData("registerGen(argc, argv)")]
    public void Validate_RequiresExactTestlibRegistration(string registration)
    {
        var code = $"#include <bits/stdc++.h>\n#include \"testlib.h\"\nusing namespace std;\nint main(int argc,char** argv){{{registration}; int test_id=0; mt19937_64 gen(test_id); return argv[1][0];}}";

        var errors = CodeGenerationService.Validate(CodeArtifactType.Generator, code);

        Assert.Contains(errors, item => item.Contains("registerGen", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsEquivalentCppWhitespaceFormatting()
    {
        var solution = "# include<bits/stdc++.h>\nusing  namespace std ;\nint main(){ios :: sync_with_stdio(false);cin . tie(nullptr);return 0;}";

        Assert.Empty(CodeGenerationService.Validate(CodeArtifactType.Solution, solution));
    }
}
