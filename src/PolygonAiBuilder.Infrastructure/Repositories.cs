using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;
using System.Text.Json;

namespace PolygonAiBuilder.Infrastructure;

public sealed class ProjectRepository(IDbContextFactory<BuilderDbContext> contextFactory) : IProjectRepository
{
    public async Task<IReadOnlyList<ProblemProject>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProblemProjects
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProblemProject?> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProblemProjects
            .AsNoTracking()
            .Include(x => x.GeneralInfo)
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);
    }

    public async Task AddAsync(ProblemProject project, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.ProblemProjects.AddAsync(project, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ProblemProject project, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.ProblemProjects.Update(project);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.ProblemProjects
            .Where(x => x.Id == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }
}

public sealed class ApplicationSettingsRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : IApplicationSettingsRepository
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ApplicationSettings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.Ordinal, cancellationToken);
    }

    public async Task SetManyAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var keys = settings.Keys.ToArray();
        var existing = await db.ApplicationSettings
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, cancellationToken);
        var now = timeProvider.GetUtcNow();

        foreach (var pair in settings)
        {
            if (existing.TryGetValue(pair.Key, out var setting))
            {
                setting.Value = pair.Value;
                setting.UpdatedAt = now;
            }
            else
            {
                await db.ApplicationSettings.AddAsync(
                    new ApplicationSetting { Key = pair.Key, Value = pair.Value, UpdatedAt = now },
                    cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ConversationRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : IConversationRepository
{
    public async Task<AiWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.ProblemProjects
            .AsNoTracking()
            .Include(x => x.Conversation)
                .ThenInclude(x => x.Messages)
                    .ThenInclude(x => x.Attachments)
            .Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        return project is null ? null : Map(project);
    }

    public async Task<AiTurnStart> StartTurnAsync(
        Guid projectId,
        string content,
        AiProviderKind provider,
        string model,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.ProblemProjects
            .Include(x => x.Conversation)
                .ThenInclude(x => x.Messages)
                    .ThenInclude(x => x.Attachments)
            .Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        var requestedIds = attachmentIds.Distinct().ToArray();
        var attachments = project.Attachments
            .Where(attachment => requestedIds.Contains(attachment.Id))
            .ToArray();
        if (attachments.Length != requestedIds.Length
            || attachments.Any(attachment => attachment.MessageId is not null))
        {
            throw new AttachmentValidationException("Một hoặc nhiều tệp đính kèm không hợp lệ cho tin nhắn này.");
        }

        var totalSize = attachments.Sum(attachment => attachment.SizeBytes);
        if (totalSize > AttachmentStore.MaximumMessageBytes)
        {
            throw new AttachmentValidationException("Tổng tệp đính kèm của một tin nhắn không được vượt quá 50 MB.");
        }

        var now = timeProvider.GetUtcNow();
        var userMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = project.Conversation.Id,
            Role = MessageRole.User,
            ContentMarkdown = content,
            Provider = provider.ToString(),
            Model = model,
            Status = MessageStatus.Completed,
            CreatedAt = now,
        };
        var assistantMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = project.Conversation.Id,
            Role = MessageRole.Assistant,
            Provider = provider.ToString(),
            Model = model,
            Status = MessageStatus.Streaming,
            ParentMessageId = userMessage.Id,
            // DateTimeOffset is persisted as Unix milliseconds, so keep a full
            // millisecond between the paired rows to preserve deterministic order.
            CreatedAt = now.AddMilliseconds(1),
        };
        foreach (var attachment in attachments)
        {
            attachment.MessageId = userMessage.Id;
        }

        project.SetModel(provider, model, now);
        project.Conversation.UpdatedAt = now;
        await db.ConversationMessages.AddRangeAsync([userMessage, assistantMessage], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var workspace = Map(project);
        return new(userMessage.Id, assistantMessage.Id, workspace);
    }

    public async Task AppendAssistantAsync(
        Guid messageId,
        string delta,
        string? providerResponseId,
        CancellationToken cancellationToken)
    {
        if (delta.Length == 0 && providerResponseId is null)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var message = await db.ConversationMessages
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy phản hồi AI đang stream.");
        if (message.Status != MessageStatus.Streaming)
        {
            return;
        }

        message.ContentMarkdown += delta;
        message.ProviderResponseId = providerResponseId ?? message.ProviderResponseId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FinishAssistantAsync(
        Guid messageId,
        MessageStatus status,
        string? providerResponseId,
        string? errorCode,
        string? errorDetails,
        CancellationToken cancellationToken)
    {
        if (status == MessageStatus.Streaming)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var message = await db.ConversationMessages
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy phản hồi AI.");
        message.Status = status;
        message.ProviderResponseId = providerResponseId ?? message.ProviderResponseId;
        message.ErrorCode = errorCode;
        message.ErrorDetails = errorDetails;
        var conversation = await db.Conversations
            .SingleAsync(item => item.Id == message.ConversationId, cancellationToken);
        conversation.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetSelectionAsync(
        Guid projectId,
        AiProviderKind provider,
        string model,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.ProblemProjects
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        project.SetModel(provider, model, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AiWorkspaceSnapshot Map(ProblemProject project)
    {
        var messages = project.Conversation.Messages
            .OrderBy(message => message.CreatedAt)
            .Select(message => new ConversationMessageInfo(
                message.Id,
                message.Role,
                message.ContentMarkdown,
                message.Provider,
                message.Model,
                message.Status,
                message.CreatedAt,
                message.ErrorCode,
                message.ErrorDetails,
                message.Attachments.Select(MapAttachment).ToArray()))
            .ToArray();
        var pending = project.Attachments
            .Where(attachment => attachment.MessageId is null)
            .OrderBy(attachment => attachment.CreatedAt)
            .Select(MapAttachment)
            .ToArray();
        return new(
            project.Id,
            project.SelectedProvider,
            project.SelectedModel,
            project.Conversation.RollingSummary,
            messages,
            pending);
    }

    private static AttachmentInfo MapAttachment(Attachment attachment) =>
        new(
            attachment.Id,
            attachment.OriginalFileName,
            attachment.MimeType,
            attachment.SizeBytes,
            attachment.Sha256);
}

public sealed class ModelCacheRepository(
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : IModelCacheRepository
{
    public async Task<IReadOnlyList<AiModelInfo>> GetAsync(
        AiProviderKind provider,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entries = await db.ModelCacheEntries
            .AsNoTracking()
            .Where(entry => entry.Provider == provider)
            .OrderBy(entry => entry.DisplayName)
            .ToArrayAsync(cancellationToken);
        return entries.Select(entry =>
        {
            var capabilities = JsonSerializer.Deserialize<ModelCapabilities>(entry.CapabilitiesJson)
                ?? new(false, false, false);
            return new AiModelInfo(
                entry.Provider,
                entry.ModelId,
                entry.DisplayName,
                capabilities.Images,
                capabilities.Documents,
                capabilities.Tools,
                entry.RefreshedAt);
        }).ToArray();
    }

    public async Task ReplaceAsync(
        AiProviderKind provider,
        IReadOnlyList<AiModelInfo> models,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.ModelCacheEntries
            .Where(entry => entry.Provider == provider)
            .ExecuteDeleteAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        await db.ModelCacheEntries.AddRangeAsync(models.Select(model => new ModelCacheEntry
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ModelId = model.Id,
            DisplayName = model.DisplayName,
            CapabilitiesJson = JsonSerializer.Serialize(
                new ModelCapabilities(model.SupportsImages, model.SupportsDocuments, model.SupportsTools)),
            RefreshedAt = now,
        }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record ModelCapabilities(bool Images, bool Documents, bool Tools);
}
