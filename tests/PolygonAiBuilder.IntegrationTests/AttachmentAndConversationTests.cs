using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class AttachmentAndConversationTests
{
    [Fact]
    public async Task TextAttachment_IsNormalizedHashedAndPersistedWithConversation()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();
        await provider.MigratePolygonAiBuilderDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync("attachment-conversation");
        var store = scope.ServiceProvider.GetRequiredService<IAttachmentStore>();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("line 1\r\nline 2"));
        var attachment = await store.SaveAsync(
            project.Id,
            "idea.md",
            "text/markdown",
            content,
            content.Length);

        Assert.Equal(64, attachment.Sha256.Length);
        var loadedContent = Assert.Single(await store.LoadContentsAsync([attachment.Id]));
        Assert.Equal("line 1\nline 2", loadedContent.ExtractedText);

        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var start = await conversations.StartTurnAsync(
            project.Id,
            "review this",
            AiProviderKind.OpenAI,
            "gpt-test",
            [attachment.Id],
            CancellationToken.None);
        await conversations.AppendAssistantAsync(start.AssistantMessageId, "partial", "resp-test", CancellationToken.None);
        await conversations.FinishAssistantAsync(
            start.AssistantMessageId,
            MessageStatus.Cancelled,
            "resp-test",
            "cancelled",
            "stopped",
            CancellationToken.None);

        var workspace = await conversations.GetAsync(project.Id, CancellationToken.None);
        Assert.NotNull(workspace);
        Assert.Empty(workspace.PendingAttachments);
        Assert.Equal(AiProviderKind.OpenAI, workspace.SelectedProvider);
        Assert.Equal("gpt-test", workspace.SelectedModel);
        var user = Assert.Single(workspace.Messages, message => message.Role == MessageRole.User);
        Assert.Single(user.Attachments);
        var assistant = Assert.Single(workspace.Messages, message => message.Role == MessageRole.Assistant);
        Assert.Equal("partial", assistant.ContentMarkdown);
        Assert.Equal(MessageStatus.Cancelled, assistant.Status);
    }

    [Fact]
    public async Task ZipTraversal_IsRejectedAndNeverExtracted()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();
        await provider.MigratePolygonAiBuilderDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var project = await scope.ServiceProvider.GetRequiredService<IProjectService>().CreateAsync("unsafe-zip");
        var store = scope.ServiceProvider.GetRequiredService<IAttachmentStore>();
        await using var archiveBytes = new MemoryStream();
        using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(Encoding.UTF8.GetBytes("unsafe"));
        }

        archiveBytes.Position = 0;
        var exception = await Assert.ThrowsAsync<AttachmentValidationException>(() => store.SaveAsync(
            project.Id,
            "unsafe.zip",
            "application/zip",
            archiveBytes,
            archiveBytes.Length));
        Assert.Contains("không an toàn", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "escape.txt")));
    }
}
