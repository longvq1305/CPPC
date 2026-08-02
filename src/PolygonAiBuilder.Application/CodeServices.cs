using System.Text;
using System.Text.RegularExpressions;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed partial class CodeGenerationService(
    ICodeRepository codeRepository,
    IStatementRepository statementRepository,
    IConversationRepository conversationRepository,
    IProjectService projectService,
    ICodeCompileService compileService,
    IEnumerable<IAiProvider> providers) : ICodeGenerationService
{
    private const string GenerationSchema = """
        {
          "type": "object",
          "properties": {
            "solutionCpp": { "type": "string" },
            "generatorCpp": { "type": "string" },
            "algorithmSummary": { "type": "string" },
            "timeComplexity": { "type": "string" },
            "memoryComplexity": { "type": "string" },
            "recommendedChecker": { "type": "string", "enum": ["ncmp.cpp", "wcmp.cpp"] },
            "auditNotes": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["solutionCpp", "generatorCpp", "algorithmSummary", "timeComplexity", "memoryComplexity", "recommendedChecker", "auditNotes"],
          "additionalProperties": false
        }
        """;

    private const string RepairSchema = """
        {
          "type": "object",
          "properties": {
            "replacementCode": { "type": "string" },
            "summary": { "type": "string" }
          },
          "required": ["replacementCode", "summary"],
          "additionalProperties": false
        }
        """;

    public Task<CodeWorkspaceSnapshot?> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        codeRepository.GetAsync(projectId, cancellationToken);

    public async Task<CodeGenerationResult> GenerateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(projectId, cancellationToken);
        CodeGenerationOutput output = default!;
        IReadOnlyList<string> errors = [];
        var validationFeedback = string.Empty;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            output = await context.Provider.GenerateStructuredAsync<CodeGenerationOutput>(new(
                context.Workspace.SelectedModel,
                GenerationSystemInstruction,
                BuildGenerationPrompt(context) + validationFeedback,
                "generate_code",
                GenerationSchema), cancellationToken);
            output = Normalize(output);
            errors = Validate(output);
            if (errors.Count == 0) break;
            validationFeedback = "\nThe previous response was rejected locally. Return a fresh complete response and fix every item: "
                + string.Join("; ", errors);
        }
        if (errors.Count > 0)
        {
            return new(false, context.Code, "AI trả code không đạt các guardrail bắt buộc nên chưa ghi đè file hiện tại.",
                errors, output.AlgorithmSummary, output.TimeComplexity, output.MemoryComplexity,
                output.RecommendedChecker, output.AuditNotes);
        }

        var saved = await codeRepository.SaveGeneratedAsync(
            projectId,
            output,
            context.Statement.CurrentVersion,
            context.Workspace.SelectedProvider.ToString(),
            context.Workspace.SelectedModel,
            cancellationToken);
        return new(true, saved, "Đã tạo solution.cpp và generate.cpp thành version mới.", [],
            output.AlgorithmSummary, output.TimeComplexity, output.MemoryComplexity,
            output.RecommendedChecker, output.AuditNotes);
    }

    public async Task<CodeWorkspaceSnapshot> SaveUserEditAsync(
        Guid projectId,
        CodeArtifactType type,
        string content,
        CancellationToken cancellationToken = default)
    {
        var statement = await statementRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        return await codeRepository.SaveArtifactAsync(
            projectId, type, content, ChangeSource.User, statement.CurrentVersion, null, null, cancellationToken);
    }

    public Task<CodeWorkspaceSnapshot> RestoreAsync(
        Guid projectId,
        CodeArtifactType type,
        int versionNumber,
        CancellationToken cancellationToken = default) =>
        codeRepository.RestoreAsync(projectId, type, versionNumber, cancellationToken);

    public async Task<AutoFixResult> AutoFixAsync(
        Guid projectId,
        CodeArtifactType type,
        CancellationToken cancellationToken = default)
    {
        var compile = await compileService.CompileArtifactAsync(projectId, type, cancellationToken);
        var workspace = await RequiredWorkspaceAsync(projectId, cancellationToken);
        if (compile.Succeeded)
        {
            return new(true, workspace, compile, 0, $"{compile.FileName} đã compile thành công; không cần auto-fix.");
        }

        var context = await LoadContextAsync(projectId, cancellationToken);
        var validationFeedback = string.Empty;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var artifact = Select(workspace, type)
                ?? throw new InvalidOperationException($"Chưa có {FileName(type)} để sửa.");
            var repair = await context.Provider.GenerateStructuredAsync<CodeRepairOutput>(new(
                context.Workspace.SelectedModel,
                RepairSystemInstruction(type),
                BuildRepairPrompt(context, artifact.Content, compile.Output, validationFeedback),
                "repair_code",
                RepairSchema), cancellationToken);
            repair = repair with { ReplacementCode = NormalizeCode(repair.ReplacementCode) };
            var errors = Validate(type, repair.ReplacementCode);
            if (errors.Count > 0)
            {
                validationFeedback = "The previous replacement was rejected locally: " + string.Join("; ", errors);
                continue;
            }

            workspace = await codeRepository.SaveArtifactAsync(
                projectId,
                type,
                repair.ReplacementCode,
                ChangeSource.AI,
                artifact.GeneratedFromStatementVersion,
                context.Workspace.SelectedProvider.ToString(),
                context.Workspace.SelectedModel,
                cancellationToken);
            compile = await compileService.CompileArtifactAsync(projectId, type, cancellationToken);
            workspace = await RequiredWorkspaceAsync(projectId, cancellationToken);
            if (compile.Succeeded)
            {
                return new(true, workspace, compile, attempt,
                    $"Auto-fix thành công sau {attempt} lần: {repair.Summary.Trim()}");
            }

            validationFeedback = string.Empty;
        }

        return new(false, workspace, compile, 3,
            "Không thể tự động sửa code sau 3 lần. Hãy xem lỗi, chỉnh thủ công hoặc chủ động yêu cầu AI thử lại.");
    }

    private async Task<CodeContext> LoadContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var statement = await statementRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        if (statement.CurrentVersion <= 0
            || string.IsNullOrWhiteSpace(statement.Content.Title)
            || string.IsNullOrWhiteSpace(statement.Content.Legend)
            || string.IsNullOrWhiteSpace(statement.Content.Input)
            || string.IsNullOrWhiteSpace(statement.Content.Output))
        {
            throw new InvalidOperationException("Statement chưa đủ bốn trường bắt buộc để tạo code.");
        }

        var workspace = await conversationRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy conversation của dự án.");
        if (string.IsNullOrWhiteSpace(workspace.SelectedModel))
        {
            throw new InvalidOperationException("Hãy chọn provider/model ở AI Workspace trước khi tạo code.");
        }

        var project = await projectService.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        var code = await codeRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy code workspace.");
        var provider = providers.Single(item => item.Kind == workspace.SelectedProvider);
        return new(project, statement, workspace, code, provider);
    }

    private async Task<CodeWorkspaceSnapshot> RequiredWorkspaceAsync(Guid projectId, CancellationToken cancellationToken) =>
        await codeRepository.GetAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException("Không tìm thấy code workspace.");

    private static string BuildGenerationPrompt(CodeContext context)
    {
        var builder = CommonContext(context);
        if (context.Code.Solution is not null || context.Code.Generator is not null)
        {
            builder.AppendLine("Current code may contain user edits. Preserve sound choices unless the statement requires change.");
            if (context.Code.Solution is not null) builder.AppendLine($"Current solution.cpp:\n{context.Code.Solution.Content}");
            if (context.Code.Generator is not null) builder.AppendLine($"Current generate.cpp:\n{context.Code.Generator.Content}");
        }

        builder.AppendLine("Return the two complete source files as JSON string fields, without Markdown fences.");
        return builder.ToString();
    }

    private static string BuildRepairPrompt(
        CodeContext context,
        string code,
        string compilerOutput,
        string validationFeedback)
    {
        var builder = CommonContext(context);
        builder.AppendLine("Current source:");
        builder.AppendLine(code);
        builder.AppendLine("Compiler output:");
        builder.AppendLine(compilerOutput);
        if (validationFeedback.Length > 0) builder.AppendLine(validationFeedback);
        builder.AppendLine("Return the complete replacement source only in replacementCode.");
        return builder.ToString();
    }

    private static StringBuilder CommonContext(CodeContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Input/output files: {context.Project.InputFile} / {context.Project.OutputFile}");
        builder.AppendLine($"Limits: {context.Project.TimeLimitMs} ms, {context.Project.MemoryLimitMb} MB");
        builder.AppendLine($"Title: {context.Statement.Content.Title}");
        builder.AppendLine($"Legend:\n{context.Statement.Content.Legend}");
        builder.AppendLine($"Input:\n{context.Statement.Content.Input}");
        builder.AppendLine($"Output:\n{context.Statement.Content.Output}");
        builder.AppendLine($"Note:\n{context.Statement.Content.Note}");
        builder.AppendLine("Recent completed conversation:");
        foreach (var message in context.Workspace.Messages
                     .Where(item => item.Status == MessageStatus.Completed)
                     .TakeLast(16))
        {
            builder.AppendLine($"[{message.Role}] {message.ContentMarkdown}");
        }

        return builder;
    }

    internal static IReadOnlyList<string> Validate(CodeGenerationOutput output) =>
        Validate(CodeArtifactType.Solution, output.SolutionCpp)
            .Concat(Validate(CodeArtifactType.Generator, output.GeneratorCpp))
            .ToArray();

    internal static IReadOnlyList<string> Validate(CodeArtifactType type, string code)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(code)) errors.Add($"{FileName(type)} rỗng.");
        if (code.Contains("```", StringComparison.Ordinal)) errors.Add($"{FileName(type)} chứa Markdown code fence.");
        if (!BitsIncludeRegex().IsMatch(code))
            errors.Add($"{FileName(type)} phải include <bits/stdc++.h>.");
        if (!UsingNamespaceRegex().IsMatch(code))
            errors.Add($"{FileName(type)} phải khai báo using namespace std;.");
        if (!MainRegex().IsMatch(code)) errors.Add($"{FileName(type)} thiếu hàm main.");
        if (type == CodeArtifactType.Solution)
        {
            if (!FastIoRegex().IsMatch(code))
                errors.Add("solution.cpp phải cấu hình fast iostream.");
            if (!CinTieRegex().IsMatch(code)) errors.Add("solution.cpp phải gọi cin.tie(NULL) hoặc cin.tie(nullptr).");
        }
        if (type == CodeArtifactType.Generator)
        {
            if (!TestlibIncludeRegex().IsMatch(code))
                errors.Add("generate.cpp phải include testlib.h.");
            if (!code.Contains("mt19937_64", StringComparison.Ordinal))
                errors.Add("generate.cpp phải dùng mt19937_64.");
            if (!RegisterGenRegex().IsMatch(code))
                errors.Add("generate.cpp phải gọi registerGen(argc, argv, 1).");
            if (!code.Contains("test_id", StringComparison.Ordinal) || !code.Contains("argv[1]", StringComparison.Ordinal))
                errors.Add("generate.cpp phải đọc test_id từ argv[1].");
        }

        return errors;
    }

    private static CodeGenerationOutput Normalize(CodeGenerationOutput output) => output with
    {
        SolutionCpp = NormalizeCode(output.SolutionCpp),
        GeneratorCpp = NormalizeCode(output.GeneratorCpp),
        AlgorithmSummary = output.AlgorithmSummary?.Trim() ?? string.Empty,
        TimeComplexity = output.TimeComplexity?.Trim() ?? string.Empty,
        MemoryComplexity = output.MemoryComplexity?.Trim() ?? string.Empty,
        RecommendedChecker = output.RecommendedChecker?.Trim() ?? string.Empty,
        AuditNotes = output.AuditNotes ?? [],
    };

    private static string NormalizeCode(string value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static CodeArtifactInfo? Select(CodeWorkspaceSnapshot workspace, CodeArtifactType type) =>
        type == CodeArtifactType.Solution ? workspace.Solution : workspace.Generator;

    private static string FileName(CodeArtifactType type) =>
        type == CodeArtifactType.Solution ? "solution.cpp" : "generate.cpp";

    private const string GenerationSystemInstruction = """
        Generate exactly two complete GNU C++17 source files for the provided competitive-programming statement.
        solution.cpp must be the intended correct main solution, meet the limits, use the configured input/output filenames, and contain the exact directives '#include <bits/stdc++.h>' and 'using namespace std;' plus fast iostream setup with sync_with_stdio(false) and cin.tie(NULL or nullptr).
        generate.cpp must contain the exact directives '#include <bits/stdc++.h>', '#include "testlib.h"', and 'using namespace std;', call registerGen(argc, argv, 1), read integer test_id from argv[1], seed and use mt19937_64 based on test_id, emit exactly one valid test to stdout, end with newline, make test 1 the sample/corner case, use 1-10 for sample/edges, 11-40 for medium branch coverage, and 41-100 for full-bound random cases.
        Do not create a validator, brute-force solution, wrong solution, stress harness, checker, script, or extra artifact. Do not use testlib::rnd as a substitute for the required mt19937_64 workflow.
        Never invent missing important constraints. Recommend only ncmp.cpp for numeric-token output or wcmp.cpp for whitespace-token output. Return raw source in the JSON fields with no Markdown fences or prose mixed into code.
        """;

    private static string RepairSystemInstruction(CodeArtifactType type) => $"""
        Repair the provided {FileName(type)} so it compiles as GNU C++17 while preserving the intended algorithm and statement semantics.
        Return the complete replacement file in structured JSON, without Markdown fences. Do not add validators, brute-force solutions, wrong solutions, stress tools, or unrelated files.
        {(type == CodeArtifactType.Generator ? "The generator must still use testlib registration, argv[1] test_id, deterministic test_id-based mt19937_64, and emit exactly one valid test." : "The solution must remain the intended correct main solution.")}
        """;

    [GeneratedRegex(@"\b(?:int|signed)\s+main\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex MainRegex();

    [GeneratedRegex(@"#\s*include\s*<\s*bits/stdc\+\+\.h\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex BitsIncludeRegex();

    [GeneratedRegex("#\\s*include\\s*[<\\\"]\\s*testlib\\.h\\s*[>\\\"]", RegexOptions.CultureInvariant)]
    private static partial Regex TestlibIncludeRegex();

    [GeneratedRegex(@"\busing\s+namespace\s+std\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex UsingNamespaceRegex();

    [GeneratedRegex(@"\b(?:ios_base|ios)\s*::\s*sync_with_stdio\s*\(\s*false\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex FastIoRegex();

    [GeneratedRegex(@"registerGen\s*\(\s*argc\s*,\s*argv\s*,\s*1\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex RegisterGenRegex();

    [GeneratedRegex(@"cin\s*\.\s*tie\s*\(\s*(?:NULL|nullptr|0)\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex CinTieRegex();

    private sealed record CodeContext(
        ProjectDetails Project,
        StatementSnapshot Statement,
        AiWorkspaceSnapshot Workspace,
        CodeWorkspaceSnapshot Code,
        IAiProvider Provider);
}
