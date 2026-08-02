using System.Text;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed class StatementService(
    IStatementRepository statementRepository,
    IConversationRepository conversationRepository,
    IProjectService projectService,
    ILatexValidator latexValidator,
    IEnumerable<IAiProvider> providers) : IStatementService
{
    private const string UpdateSchema = """
        {
          "type": "object",
          "properties": {
            "title": { "type": ["string", "null"] },
            "legend": { "type": ["string", "null"] },
            "input": { "type": ["string", "null"] },
            "output": { "type": ["string", "null"] },
            "note": { "type": ["string", "null"] },
            "changeSummary": { "type": "string" }
          },
          "required": ["title", "legend", "input", "output", "note", "changeSummary"],
          "additionalProperties": false
        }
        """;

    public async Task<StatementSnapshot?> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        await statementRepository.GetAsync(projectId, cancellationToken);

    public async Task<StatementSaveResult> SaveUserEditAsync(
        Guid projectId,
        StatementContent content,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(content);
        var issues = latexValidator.Validate(normalized);
        var statement = await statementRepository.SaveAsync(
            projectId,
            normalized,
            ChangeSource.User,
            null,
            null,
            null,
            cancellationToken);
        return new(true, statement, issues,
            issues.Any(issue => issue.Severity == LatexIssueSeverity.Error)
                ? "Đã lưu local; hãy sửa lỗi LaTeX trước khi chuyển bước."
                : "Đã lưu statement local.");
    }

    public async Task<StatementSaveResult> ApplyAiUpdateAsync(
        Guid projectId,
        StatementAiUpdate update,
        string provider,
        string model,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var current = await statementRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        var merged = Normalize(new(
            update.Title ?? current.Content.Title,
            update.Legend ?? current.Content.Legend,
            update.Input ?? current.Content.Input,
            update.Output ?? current.Content.Output,
            update.Note ?? current.Content.Note));
        var issues = latexValidator.Validate(merged);
        if (issues.Any(issue => issue.Severity == LatexIssueSeverity.Error))
        {
            return new(false, current, issues,
                "AI đề xuất statement có lỗi LaTeX cơ bản nên chưa được áp dụng.");
        }

        var saved = await statementRepository.SaveAsync(
            projectId,
            merged,
            ChangeSource.AI,
            provider,
            model,
            messageId,
            cancellationToken);
        return new(true, saved, issues,
            string.IsNullOrWhiteSpace(update.ChangeSummary)
                ? "Statement đã được AI cập nhật."
                : $"Statement đã được AI cập nhật: {update.ChangeSummary.Trim()}");
    }

    public async Task<StatementSaveResult> GenerateFromConversationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var statement = await statementRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy statement của dự án.");
        var workspace = await conversationRepository.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy conversation của dự án.");
        var project = await projectService.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (string.IsNullOrWhiteSpace(workspace.SelectedModel))
        {
            throw new InvalidOperationException("Hãy chọn model ở AI Workspace trước khi cập nhật statement.");
        }

        var provider = providers.Single(candidate => candidate.Kind == workspace.SelectedProvider);
        var prompt = BuildPrompt(project, statement, workspace);
        var update = await provider.GenerateStructuredAsync<StatementAiUpdate>(new(
            workspace.SelectedModel,
            "You update exactly five Codeforces Polygon statement fields. Use Polygon-compatible LaTeX. Never add samples, scoring, tutorials, validators, or fields not in the schema. Return null for every field that should remain unchanged. Do not invent important missing constraints.",
            prompt,
            "update_statement",
            UpdateSchema), cancellationToken);
        return await ApplyAiUpdateAsync(
            projectId,
            update,
            workspace.SelectedProvider.ToString(),
            workspace.SelectedModel,
            cancellationToken: cancellationToken);
    }

    public async Task<StatementSnapshot> RestoreAsync(
        Guid projectId,
        int versionNumber,
        CancellationToken cancellationToken = default) =>
        await statementRepository.RestoreAsync(projectId, versionNumber, cancellationToken);

    public StatementDiff Compare(StatementContent before, StatementContent after) => new(
    [
        Field("Title", before.Title, after.Title),
        Field("Legend", before.Legend, after.Legend),
        Field("Input", before.Input, after.Input),
        Field("Output", before.Output, after.Output),
        Field("Note", before.Note, after.Note),
    ]);

    private static StatementFieldDiff Field(string name, string before, string after) =>
        new(name, before, after, !string.Equals(before, after, StringComparison.Ordinal));

    private static StatementContent Normalize(StatementContent content) => new(
        content.Title.Trim(),
        NormalizeBody(content.Legend),
        NormalizeBody(content.Input),
        NormalizeBody(content.Output),
        NormalizeBody(content.Note));

    private static string NormalizeBody(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static string BuildPrompt(
        ProjectDetails project,
        StatementSnapshot statement,
        AiWorkspaceSnapshot workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Internal problem name: {project.InternalName}");
        builder.AppendLine($"Input/output: {project.InputFile} / {project.OutputFile}");
        builder.AppendLine($"Limits: {project.TimeLimitMs} ms, {project.MemoryLimitMb} MB");
        builder.AppendLine("Current statement:");
        builder.AppendLine($"Title: {statement.Content.Title}");
        builder.AppendLine($"Legend:\n{statement.Content.Legend}");
        builder.AppendLine($"Input:\n{statement.Content.Input}");
        builder.AppendLine($"Output:\n{statement.Content.Output}");
        builder.AppendLine($"Note:\n{statement.Content.Note}");
        builder.AppendLine("Recent conversation:");
        foreach (var message in workspace.Messages
                     .Where(message => message.Status == MessageStatus.Completed)
                     .TakeLast(24))
        {
            builder.AppendLine($"[{message.Role}] {message.ContentMarkdown}");
        }

        builder.AppendLine("Create or update the five statement fields only when the conversation provides enough information.");
        return builder.ToString();
    }
}
