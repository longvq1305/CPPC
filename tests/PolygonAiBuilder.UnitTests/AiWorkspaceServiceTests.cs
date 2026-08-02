using System.Runtime.CompilerServices;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class AiWorkspaceServiceTests
{
    [Fact]
    public async Task Cancellation_WaitsUntilFinalStatusIsPersisted()
    {
        var repository = new CancellationConversationRepository();
        var service = new AiWorkspaceService(repository, new EmptyAttachmentStore(), [new WaitingProvider()]);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = service.SendAsync(
                repository.ProjectId,
                "hello",
                AiProviderKind.Gemini,
                "gemini-test",
                [],
                cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enumerator.MoveNextAsync().AsTask());

        Assert.Equal(MessageStatus.Cancelled, repository.FinishedStatus);
    }

    [Fact]
    public async Task HistoricalAttachmentOnlyTurn_ReloadsAttachmentAndSkipsEmptyCancelledReply()
    {
        var attachmentId = Guid.NewGuid();
        var repository = new HistoryConversationRepository(attachmentId);
        var attachmentStore = new TrackingAttachmentStore(attachmentId);
        var provider = new CapturingProvider();
        var service = new AiWorkspaceService(repository, attachmentStore, [provider]);

        await foreach (var _ in service.SendAsync(
                           repository.ProjectId,
                           "continue",
                           AiProviderKind.Gemini,
                           "gemini-test",
                           [],
                           CancellationToken.None))
        {
        }

        Assert.NotNull(provider.LastRequest);
        Assert.Collection(
            provider.LastRequest.Turns,
            previous =>
            {
                Assert.Equal(MessageRole.User, previous.Role);
                Assert.Empty(previous.Content);
                Assert.Equal(attachmentId, Assert.Single(previous.Attachments).Id);
            },
            current =>
            {
                Assert.Equal(MessageRole.User, current.Role);
                Assert.Equal("continue", current.Content);
                Assert.Empty(current.Attachments);
            });
        Assert.Contains(attachmentId, attachmentStore.LoadedIds);
    }

    private sealed class CancellationConversationRepository : IConversationRepository
    {
        public Guid ProjectId { get; } = Guid.NewGuid();
        public MessageStatus? FinishedStatus { get; private set; }

        public Task<AiWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<AiWorkspaceSnapshot?>(null);

        public Task<AiTurnStart> StartTurnAsync(
            Guid projectId,
            string content,
            AiProviderKind provider,
            string model,
            IReadOnlyCollection<Guid> attachmentIds,
            CancellationToken cancellationToken)
        {
            var userId = Guid.NewGuid();
            var assistantId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var messages = new ConversationMessageInfo[]
            {
                new(userId, MessageRole.User, content, provider.ToString(), model, MessageStatus.Completed, now, null, null, []),
                new(assistantId, MessageRole.Assistant, "", provider.ToString(), model, MessageStatus.Streaming, now.AddMilliseconds(1), null, null, []),
            };
            return Task.FromResult(new AiTurnStart(
                userId,
                assistantId,
                new(projectId, provider, model, "", messages, [])));
        }

        public Task AppendAssistantAsync(Guid messageId, string delta, string? providerResponseId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FinishAssistantAsync(
            Guid messageId,
            MessageStatus status,
            string? providerResponseId,
            string? errorCode,
            string? errorDetails,
            CancellationToken cancellationToken)
        {
            FinishedStatus = status;
            return Task.CompletedTask;
        }

        public Task SetSelectionAsync(Guid projectId, AiProviderKind provider, string model, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class EmptyAttachmentStore : IAttachmentStore
    {
        public Task<AttachmentInfo> SaveAsync(Guid projectId, string originalFileName, string mimeType, Stream content, long declaredLength, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AiAttachmentContent>> LoadContentsAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiAttachmentContent>>([]);

        public Task<bool> RemovePendingAsync(Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class HistoryConversationRepository(Guid attachmentId) : IConversationRepository
    {
        public Guid ProjectId { get; } = Guid.NewGuid();

        public Task<AiWorkspaceSnapshot?> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<AiWorkspaceSnapshot?>(null);

        public Task<AiTurnStart> StartTurnAsync(
            Guid projectId,
            string content,
            AiProviderKind provider,
            string model,
            IReadOnlyCollection<Guid> attachmentIds,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var previousUserId = Guid.NewGuid();
            var previousAssistantId = Guid.NewGuid();
            var currentUserId = Guid.NewGuid();
            var currentAssistantId = Guid.NewGuid();
            var attachment = new AttachmentInfo(attachmentId, "idea.png", "image/png", 3, "sha256");
            ConversationMessageInfo[] messages =
            [
                new(previousUserId, MessageRole.User, "", provider.ToString(), model, MessageStatus.Completed, now, null, null, [attachment]),
                new(previousAssistantId, MessageRole.Assistant, "", provider.ToString(), model, MessageStatus.Cancelled, now.AddMilliseconds(1), "cancelled", "stopped", []),
                new(currentUserId, MessageRole.User, content, provider.ToString(), model, MessageStatus.Completed, now.AddMilliseconds(2), null, null, []),
                new(currentAssistantId, MessageRole.Assistant, "", provider.ToString(), model, MessageStatus.Streaming, now.AddMilliseconds(3), null, null, []),
            ];
            return Task.FromResult(new AiTurnStart(
                currentUserId,
                currentAssistantId,
                new(projectId, provider, model, "", messages, [])));
        }

        public Task AppendAssistantAsync(Guid messageId, string delta, string? providerResponseId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FinishAssistantAsync(
            Guid messageId,
            MessageStatus status,
            string? providerResponseId,
            string? errorCode,
            string? errorDetails,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetSelectionAsync(Guid projectId, AiProviderKind provider, string model, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TrackingAttachmentStore(Guid attachmentId) : IAttachmentStore
    {
        public List<Guid> LoadedIds { get; } = [];

        public Task<AttachmentInfo> SaveAsync(Guid projectId, string originalFileName, string mimeType, Stream content, long declaredLength, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AiAttachmentContent>> LoadContentsAsync(
            IReadOnlyCollection<Guid> attachmentIds,
            CancellationToken cancellationToken = default)
        {
            LoadedIds.AddRange(attachmentIds);
            IReadOnlyList<AiAttachmentContent> result =
            [
                new(attachmentId, "idea.png", "image/png", [1, 2, 3], null),
            ];
            return Task.FromResult(result);
        }

        public Task<bool> RemovePendingAsync(Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class CapturingProvider : IAiProvider
    {
        public AiProviderKind Kind => AiProviderKind.Gemini;
        public AiChatRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModelInfo>>([]);

        public async IAsyncEnumerable<AiStreamEvent> StreamChatAsync(
            AiChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            await Task.CompletedTask;
            yield return new(AiStreamEventKind.Completed, ProviderResponseId: "done");
        }

        public Task<T> GenerateStructuredAsync<T>(AiStructuredRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class WaitingProvider : IAiProvider
    {
        public AiProviderKind Kind => AiProviderKind.Gemini;

        public Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModelInfo>>([]);

        public async IAsyncEnumerable<AiStreamEvent> StreamChatAsync(
            AiChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public Task<T> GenerateStructuredAsync<T>(AiStructuredRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
