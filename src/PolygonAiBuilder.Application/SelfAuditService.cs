using System.Text;
using System.Text.RegularExpressions;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed partial class SelfAuditService(
    IStatementRepository statementRepository,
    ICodeRepository codeRepository,
    ISampleRepository sampleRepository,
    ITestConfigurationRepository testConfigurationRepository,
    IConversationRepository conversationRepository,
    ILatexValidator latexValidator,
    IEnumerable<IAiProvider> providers,
    TimeProvider timeProvider) : ISelfAuditService
{
    private const string AuditSchema = """
        {
          "type": "object",
          "properties": {
            "inputOutputDescriptionsMatchCode": { "type": "boolean" },
            "testOneIsSample": { "type": "boolean" },
            "generatorCoversConfiguredRange": { "type": "boolean" },
            "checkerIsAppropriate": { "type": "boolean" },
            "complexityIsReasonable": { "type": "boolean" },
            "overflowReviewed": { "type": "boolean" },
            "findings": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["inputOutputDescriptionsMatchCode", "testOneIsSample", "generatorCoversConfiguredRange", "checkerIsAppropriate", "complexityIsReasonable", "overflowReviewed", "findings"],
          "additionalProperties": false
        }
        """;

    public async Task<SelfAuditResult> RunAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var statement = await statementRepository.GetAsync(projectId, cancellationToken);
        var code = await codeRepository.GetAsync(projectId, cancellationToken);
        var sample = await sampleRepository.GetAsync(projectId, 1, cancellationToken);
        var configuration = await testConfigurationRepository.GetAsync(projectId, cancellationToken);
        var workspace = await conversationRepository.GetAsync(projectId, cancellationToken);
        var issues = new List<AuditIssue>();

        if (statement is null || configuration is null || code is null || workspace is null)
        {
            return new(false, timeProvider.GetUtcNow(), [new("Local data", AuditSeverity.Error, "Dữ liệu dự án chưa đầy đủ.")]);
        }

        AddRequiredStatementChecks(statement, issues);
        foreach (var issue in latexValidator.Validate(statement.Content))
        {
            issues.Add(new("LaTeX", issue.Severity == LatexIssueSeverity.Error ? AuditSeverity.Error : AuditSeverity.Warning,
                $"{issue.Field}: {issue.Message}"));
        }

        CheckArtifact("Solution compile", code.Solution, issues);
        CheckArtifact("Generator compile", code.Generator, issues);
        if (code.HasStaleCode) issues.Add(new("Code freshness", AuditSeverity.Error, "Code đang stale so với statement/test config."));
        if (sample is null)
        {
            issues.Add(new("Sample 1", AuditSeverity.Error, "Chưa có input/output Sample 1 từ local run."));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sample.Input) || string.IsNullOrWhiteSpace(sample.Output))
                issues.Add(new("Sample 1", AuditSeverity.Error, "Sample 1 input/output không được rỗng."));
            if (sample.InputIsStale || sample.OutputIsStale)
                issues.Add(new("Sample 1", AuditSeverity.Error, "Sample 1 đã stale và phải chạy lại."));
            if (sample.WasManuallyEdited)
                issues.Add(new("Sample 1", AuditSeverity.Warning, "Sample hiển thị đã được sửa thủ công; hãy đối chiếu với output chạy thực tế."));
        }

        if (!ScriptMatches(configuration.Script, configuration.TestCount))
            issues.Add(new("Test script", AuditSeverity.Error,
                $"Script phải gọi remote generator 'gen' cho đúng 1..{configuration.TestCount}."));
        if (configuration.TestCount != 100)
            issues.Add(new("Generator range", AuditSeverity.Warning,
                "Test count khác 100; generator phải được AI tạo lại để bao phủ đầy đủ range mới."));
        if (configuration.Checker is not ("ncmp.cpp" or "wcmp.cpp"))
            issues.Add(new("Checker", AuditSeverity.Error, "Checker không nằm trong bundle cho phép."));

        if (issues.All(item => item.Severity != AuditSeverity.Error))
        {
            if (string.IsNullOrWhiteSpace(workspace.SelectedModel))
            {
                issues.Add(new("AI review", AuditSeverity.Error, "Chưa chọn provider/model để thực hiện semantic self-audit."));
            }
            else
            {
                try
                {
                    var provider = providers.Single(item => item.Kind == workspace.SelectedProvider);
                    var ai = await provider.GenerateStructuredAsync<SelfAuditAiOutput>(new(
                        workspace.SelectedModel,
                        "Review this competitive-programming project conservatively. Do not claim correctness without evidence. Return only the requested structured audit.",
                        BuildPrompt(statement, code, sample!, configuration),
                        "self_audit",
                        AuditSchema), cancellationToken);
                    AddAiResult(ai, issues);
                }
                catch (Exception exception) when (exception is IntegrationConfigurationException or ExternalServiceException or InvalidOperationException)
                {
                    issues.Add(new("AI review", AuditSeverity.Error, $"Không hoàn thành được AI self-audit: {exception.Message}"));
                }
            }
        }

        return new(issues.All(item => item.Severity != AuditSeverity.Error), timeProvider.GetUtcNow(), issues);
    }

    private static void AddRequiredStatementChecks(StatementSnapshot statement, ICollection<AuditIssue> issues)
    {
        var fields = new[]
        {
            ("Title", statement.Content.Title), ("Legend", statement.Content.Legend),
            ("Input", statement.Content.Input), ("Output", statement.Content.Output),
        };
        foreach (var (name, value) in fields.Where(item => string.IsNullOrWhiteSpace(item.Item2)))
            issues.Add(new("Statement", AuditSeverity.Error, $"Trường {name} đang rỗng."));
    }

    private static void CheckArtifact(string check, CodeArtifactInfo? artifact, ICollection<AuditIssue> issues)
    {
        if (artifact is null) issues.Add(new(check, AuditSeverity.Error, "Chưa có source file."));
        else if (artifact.LastCompileStatus != CompileStatus.Succeeded)
            issues.Add(new(check, AuditSeverity.Error, $"Trạng thái compile là {artifact.LastCompileStatus}."));
    }

    private static bool ScriptMatches(string script, int testCount)
    {
        var match = ScriptRangeRegex().Match(script);
        return match.Success
            && int.TryParse(match.Groups[1].Value, out var count)
            && count == testCount
            && GeneratorCallRegex().IsMatch(script);
    }

    private static string BuildPrompt(
        StatementSnapshot statement,
        CodeWorkspaceSnapshot code,
        LocalSampleSnapshot sample,
        TestConfigurationSnapshot configuration)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Configured test range: 1..{configuration.TestCount}; checker: {configuration.Checker}");
        builder.AppendLine($"Title: {statement.Content.Title}\nLegend: {statement.Content.Legend}\nInput: {statement.Content.Input}\nOutput: {statement.Content.Output}\nNote: {statement.Content.Note}");
        builder.AppendLine($"solution.cpp:\n{code.Solution!.Content}");
        builder.AppendLine($"generate.cpp:\n{code.Generator!.Content}");
        builder.AppendLine($"Sample 1 input:\n{sample.Input}\nSample 1 output:\n{sample.Output}");
        builder.AppendLine("Check description/code compatibility, confirm generator test_id 1 is exactly the shown sample, range coverage, checker choice, complexity versus limits, and integer overflow risks.");
        return builder.ToString();
    }

    private static void AddAiResult(SelfAuditAiOutput ai, ICollection<AuditIssue> issues)
    {
        AddBoolean("Input/output review", ai.InputOutputDescriptionsMatchCode, "Mô tả input/output không khớp code theo AI review.", issues);
        AddBoolean("Sample 1 review", ai.TestOneIsSample, "AI không xác nhận test_id 1 chính xác là Sample 1.", issues);
        AddBoolean("Generator range", ai.GeneratorCoversConfiguredRange, "Generator không bao phủ đầy đủ test id đã cấu hình.", issues);
        AddBoolean("Checker review", ai.CheckerIsAppropriate, "Checker có thể không phù hợp kiểu output.", issues);
        AddBoolean("Complexity review", ai.ComplexityIsReasonable, "Complexity có thể không đáp ứng giới hạn.", issues);
        AddBoolean("Overflow review", ai.OverflowReviewed, "Chưa xử lý đầy đủ nguy cơ integer overflow.", issues);
        foreach (var finding in ai.Findings ?? [])
            issues.Add(new("AI finding", AuditSeverity.Warning, finding));
    }

    private static void AddBoolean(string check, bool passed, string message, ICollection<AuditIssue> issues)
    {
        if (!passed) issues.Add(new(check, AuditSeverity.Error, message));
    }

    [GeneratedRegex(@"<#list\s+1\.\.(\d+)\s+as\s+i>", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptRangeRegex();

    [GeneratedRegex(@"(?m)^\s*gen\s+\$\{i\}\s*>\s*\$\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratorCallRegex();
}
