using System.Threading.Channels;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Application;

public sealed class ModelCatalogService(
    IEnumerable<IAiProvider> providers,
    IModelCacheRepository cacheRepository) : IModelCatalogService
{
    public async Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(
        AiProviderKind provider,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        if (!refresh)
        {
            var cached = await cacheRepository.GetAsync(provider, cancellationToken);
            if (cached.Count > 0)
            {
                return cached;
            }
        }

        var models = await Resolve(provider).ListModelsAsync(cancellationToken);
        await cacheRepository.ReplaceAsync(provider, models, cancellationToken);
        return models;
    }

    public Task<ConnectionTestResult> TestConnectionAsync(
        AiProviderKind provider,
        CancellationToken cancellationToken = default) =>
        Resolve(provider).TestConnectionAsync(cancellationToken);

    private IAiProvider Resolve(AiProviderKind provider) =>
        providers.SingleOrDefault(candidate => candidate.Kind == provider)
        ?? throw new InvalidOperationException($"AI provider {provider} is not registered.");
}

public sealed class AiWorkspaceService(
    IConversationRepository conversationRepository,
    IAttachmentStore attachmentStore,
    IEnumerable<IAiProvider> providers) : IAiWorkspaceService
{
    private const string SystemInstruction = """
        You are a careful assistant helping a student create a new competitive-programming problem for Codeforces Polygon.
        Follow the user's requested difficulty and do not make the problem harder on your own.
        Prefer a familiar, approachable story when it fits. Do not advance workflow steps or synchronize anything externally.
        A statement has exactly five fields: title, legend, input, output, and note. Samples are test configuration, never statement fields.
        GNU C++17 is fixed. Do not propose validators, brute-force solutions, wrong solutions, or local execution of uploaded files.
        Reply in the language used by the user unless asked otherwise.
        """;

    public Task<AiWorkspaceSnapshot?> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        conversationRepository.GetAsync(projectId, cancellationToken);

    public Task SetSelectionAsync(
        Guid projectId,
        AiProviderKind provider,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return conversationRepository.SetSelectionAsync(projectId, provider, model.Trim(), cancellationToken);
    }

    public IAsyncEnumerable<AiChatProgress> SendAsync(
        Guid projectId,
        string content,
        AiProviderKind provider,
        string model,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content) && attachmentIds.Count == 0)
        {
            throw new ArgumentException("Tin nhắn hoặc tệp đính kèm không được để trống.", nameof(content));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var channel = Channel.CreateUnbounded<AiChatProgress>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        _ = ProduceAsync(
            channel.Writer,
            projectId,
            content,
            provider,
            model,
            attachmentIds,
            cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task ProduceAsync(
        ChannelWriter<AiChatProgress> writer,
        Guid projectId,
        string content,
        AiProviderKind provider,
        string model,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        AiTurnStart? start = null;
        string? responseId = null;
        try
        {
            var providerAdapter = providers.SingleOrDefault(candidate => candidate.Kind == provider)
                ?? throw new InvalidOperationException($"AI provider {provider} is not registered.");
            start = await conversationRepository.StartTurnAsync(
                projectId,
                content.Trim(),
                provider,
                model.Trim(),
                attachmentIds,
                cancellationToken);
            await writer.WriteAsync(new(
                start.UserMessageId,
                start.AssistantMessageId,
                string.Empty,
                MessageStatus.Streaming), cancellationToken);
            var turns = new List<AiChatTurn>();
            foreach (var message in start.Workspace.Messages
                         .Where(message => message.Id != start.AssistantMessageId)
                         .Where(message => message.Role is MessageRole.User or MessageRole.Assistant)
                         .Where(message => message.Status is MessageStatus.Completed or MessageStatus.Cancelled
                             || message.Id == start.UserMessageId))
            {
                var attachments = message.Id == start.UserMessageId
                    ? await attachmentStore.LoadContentsAsync(
                        message.Attachments.Select(attachment => attachment.Id).ToArray(),
                        cancellationToken)
                    : [];
                turns.Add(new(message.Role, message.ContentMarkdown, attachments));
            }

            if (!string.IsNullOrWhiteSpace(start.Workspace.RollingSummary))
            {
                turns.Insert(0, new(
                    MessageRole.User,
                    $"Conversation summary from earlier turns:\n{start.Workspace.RollingSummary}",
                    []));
            }

            await foreach (var streamEvent in providerAdapter.StreamChatAsync(
                               new AiChatRequest(model.Trim(), SystemInstruction, turns),
                               cancellationToken))
            {
                responseId = streamEvent.ProviderResponseId ?? responseId;
                if (streamEvent.Kind == AiStreamEventKind.TextDelta && streamEvent.Text.Length > 0)
                {
                    await conversationRepository.AppendAssistantAsync(
                        start.AssistantMessageId,
                        streamEvent.Text,
                        responseId,
                        cancellationToken);
                    await writer.WriteAsync(new(
                        start.UserMessageId,
                        start.AssistantMessageId,
                        streamEvent.Text,
                        MessageStatus.Streaming), cancellationToken);
                }
            }

            await conversationRepository.FinishAssistantAsync(
                start.AssistantMessageId,
                MessageStatus.Completed,
                responseId,
                null,
                null,
                cancellationToken);
            await writer.WriteAsync(new(
                start.UserMessageId,
                start.AssistantMessageId,
                string.Empty,
                MessageStatus.Completed), cancellationToken);
            writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (start is not null)
            {
                await conversationRepository.FinishAssistantAsync(
                    start.AssistantMessageId,
                    MessageStatus.Cancelled,
                    responseId,
                    "cancelled",
                    "Người dùng đã dừng phản hồi.",
                    CancellationToken.None);
            }

            writer.TryComplete();
        }
        catch (Exception exception)
        {
            var code = exception is ExternalServiceException external ? external.Code : "ai_provider_error";
            var safeMessage = exception is IntegrationConfigurationException or ExternalServiceException
                ? exception.Message
                : "Provider AI gặp lỗi khi tạo phản hồi.";
            if (start is not null)
            {
                await conversationRepository.FinishAssistantAsync(
                    start.AssistantMessageId,
                    MessageStatus.Failed,
                    responseId,
                    code,
                    safeMessage,
                    CancellationToken.None);
                writer.TryWrite(new(
                    start.UserMessageId,
                    start.AssistantMessageId,
                    string.Empty,
                    MessageStatus.Failed,
                    safeMessage));
                writer.TryComplete();
            }
            else
            {
                writer.TryComplete(exception);
            }
        }
    }
}
