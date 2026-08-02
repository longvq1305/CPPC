namespace PolygonAiBuilder.Application;

public sealed class GeneralInfoService(
    IProjectRepository repository,
    IPolygonClient polygonClient,
    TimeProvider timeProvider) : IGeneralInfoService
{
    public async Task<GeneralInfoSaveResult> SaveGeneralInfoAsync(
        Guid projectId,
        GeneralInfoDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = GeneralInfoValidator.Validate(
            draft.InternalName,
            draft.InputFile,
            draft.OutputFile,
            draft.TimeLimitMs,
            draft.MemoryLimitMb);
        if (issues.Count > 0)
        {
            return GeneralInfoSaveResult.Invalid(issues);
        }

        var project = await repository.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return GeneralInfoSaveResult.Invalid(
                [new ValidationIssue("Project", "Dự án không còn tồn tại.")]);
        }

        var normalizedName = draft.InternalName.Trim();
        var projects = await repository.ListAsync(cancellationToken);
        if (projects.Any(candidate =>
                candidate.Id != projectId
                && string.Equals(candidate.InternalName, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            return GeneralInfoSaveResult.Invalid(
                [new ValidationIssue("InternalName", "Tên này đã được một dự án local khác sử dụng.")]);
        }

        project.UpdateGeneralInfo(
            normalizedName,
            draft.InputFile,
            draft.OutputFile,
            draft.TimeLimitMs,
            draft.MemoryLimitMb,
            timeProvider.GetUtcNow());
        await repository.UpdateAsync(project, cancellationToken);
        return GeneralInfoSaveResult.Saved(ProjectService.MapDetails(project));
    }

    public async Task<NameAvailabilityResult> CheckNameAndContinueAsync(
        Guid projectId,
        GeneralInfoDraft draft,
        CancellationToken cancellationToken = default)
    {
        var saveResult = await SaveGeneralInfoAsync(projectId, draft, cancellationToken);
        if (!saveResult.Succeeded || saveResult.Project is null)
        {
            return new(false, false, "Vui lòng sửa các trường chưa hợp lệ.", saveResult.Issues, null);
        }

        var normalizedName = saveResult.Project.InternalName;
        var remoteProblems = await polygonClient.ListProblemsAsync(normalizedName, cancellationToken);
        if (remoteProblems.Any(problem =>
                !problem.Deleted
                && string.Equals(problem.Name.Trim(), normalizedName, StringComparison.Ordinal)))
        {
            return new(
                true,
                false,
                $"Problem “{normalizedName}” đã tồn tại trên Polygon. Tool này chỉ tạo problem mới. Vui lòng chọn tên khác.",
                [],
                saveResult.Project);
        }

        var project = await repository.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new(false, false, "Dự án không còn tồn tại.", [], null);
        }

        var now = timeProvider.GetUtcNow();
        project.MarkNameAvailable(now);
        project.SetCurrentScreen(2, now);
        await repository.UpdateAsync(project, cancellationToken);
        return new(
            true,
            true,
            "Tên chưa tồn tại trên Polygon. Không có problem remote nào được tạo ở bước này.",
            [],
            ProjectService.MapDetails(project));
    }
}
