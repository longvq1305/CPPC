using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class AttachmentStore(
    RuntimePaths paths,
    IDbContextFactory<BuilderDbContext> contextFactory,
    TimeProvider timeProvider) : IAttachmentStore
{
    public const long MaximumFileBytes = 20L * 1024 * 1024;
    public const long MaximumMessageBytes = 50L * 1024 * 1024;
    private const long MaximumArchiveBytes = 50L * 1024 * 1024;
    private const int MaximumArchiveEntries = 100;
    private const long MaximumExtractedTextBytes = 5L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".pdf", ".txt", ".md",
        ".cpp", ".c", ".h", ".hpp", ".cs", ".json", ".zip",
    };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".cpp", ".c", ".h", ".hpp", ".cs", ".json",
    };

    public async Task<AttachmentInfo> SaveAsync(
        Guid projectId,
        string originalFileName,
        string mimeType,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (declaredLength is < 0 or > MaximumFileBytes)
        {
            throw new AttachmentValidationException("Mỗi tệp đính kèm không được vượt quá 20 MB.");
        }

        var safeName = Path.GetFileName(originalFileName.Trim());
        var extension = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(safeName) || !AllowedExtensions.Contains(extension))
        {
            throw new AttachmentValidationException("Định dạng tệp không được hỗ trợ.");
        }

        await using (var db = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (!await db.ProblemProjects.AnyAsync(project => project.Id == projectId, cancellationToken))
            {
                throw new KeyNotFoundException("Không tìm thấy dự án.");
            }
        }

        var attachmentId = Guid.NewGuid();
        var directory = GetAttachmentDirectory(projectId);
        Directory.CreateDirectory(directory);
        var storedName = $"{attachmentId:N}{extension.ToLowerInvariant()}";
        var localPath = Path.Combine(directory, storedName);
        string? extractedPath = null;
        long actualLength = 0;
        string hash;
        try
        {
            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var destination = new FileStream(
                             localPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    actualLength += read;
                    if (actualLength > MaximumFileBytes)
                    {
                        throw new AttachmentValidationException("Mỗi tệp đính kèm không được vượt quá 20 MB.");
                    }

                    incrementalHash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            hash = Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                extractedPath = await InspectArchiveAsync(localPath, directory, attachmentId, cancellationToken);
            }
            else if (TextExtensions.Contains(extension))
            {
                extractedPath = await NormalizeTextAsync(localPath, directory, attachmentId, cancellationToken);
            }
        }
        catch
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }

            if (extractedPath is not null && File.Exists(extractedPath))
            {
                File.Delete(extractedPath);
            }

            throw;
        }

        var attachment = new Attachment
        {
            Id = attachmentId,
            ProblemProjectId = projectId,
            OriginalFileName = safeName,
            StoredFileName = storedName,
            MimeType = NormalizeMimeType(extension, mimeType),
            SizeBytes = actualLength,
            Sha256 = hash,
            LocalPath = localPath,
            ExtractedTextPath = extractedPath,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        await using (var db = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await db.Attachments.AddAsync(attachment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new(attachment.Id, safeName, attachment.MimeType, actualLength, hash);
    }

    public async Task<IReadOnlyList<AiAttachmentContent>> LoadContentsAsync(
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default)
    {
        if (attachmentIds.Count == 0)
        {
            return [];
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = attachmentIds.Distinct().ToArray();
        var attachments = await db.Attachments
            .AsNoTracking()
            .Where(attachment => ids.Contains(attachment.Id))
            .ToArrayAsync(cancellationToken);
        if (attachments.Length != ids.Length)
        {
            throw new AttachmentValidationException("Không thể đọc một hoặc nhiều tệp đính kèm.");
        }

        var result = new List<AiAttachmentContent>(attachments.Length);
        foreach (var attachment in attachments.OrderBy(item => Array.IndexOf(ids, item.Id)))
        {
            EnsureControlledPath(attachment.LocalPath);
            var data = await File.ReadAllBytesAsync(attachment.LocalPath, cancellationToken);
            string? extractedText = null;
            if (attachment.ExtractedTextPath is not null)
            {
                EnsureControlledPath(attachment.ExtractedTextPath);
                extractedText = await File.ReadAllTextAsync(attachment.ExtractedTextPath, cancellationToken);
            }

            result.Add(new(
                attachment.Id,
                attachment.OriginalFileName,
                attachment.MimeType,
                data,
                extractedText));
        }

        return result;
    }

    public async Task<bool> RemovePendingAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attachment = await db.Attachments
            .SingleOrDefaultAsync(item => item.Id == attachmentId, cancellationToken);
        if (attachment is null || attachment.MessageId is not null)
        {
            return false;
        }

        EnsureControlledPath(attachment.LocalPath);
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
        if (File.Exists(attachment.LocalPath))
        {
            File.Delete(attachment.LocalPath);
        }

        if (attachment.ExtractedTextPath is not null)
        {
            EnsureControlledPath(attachment.ExtractedTextPath);
            if (File.Exists(attachment.ExtractedTextPath))
            {
                File.Delete(attachment.ExtractedTextPath);
            }
        }

        return true;
    }

    private async Task<string?> NormalizeTextAsync(
        string localPath,
        string directory,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var path = Path.Combine(directory, $"{attachmentId:N}.text.txt");
        await File.WriteAllTextAsync(path, normalized, new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private async Task<string?> InspectArchiveAsync(
        string archivePath,
        string directory,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new AttachmentValidationException("ZIP có quá nhiều tệp; giới hạn là 100 entry.");
        }

        var inspectionRoot = Path.GetFullPath(Path.Combine(directory, $"{attachmentId:N}-inspection"));
        long totalLength = 0;
        long extractedTextLength = 0;
        var text = new StringBuilder();
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = Path.GetFullPath(Path.Combine(inspectionRoot, entry.FullName));
            if (!resolved.StartsWith(inspectionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(resolved, inspectionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new AttachmentValidationException("ZIP chứa đường dẫn không an toàn.");
            }

            totalLength += entry.Length;
            if (totalLength > MaximumArchiveBytes)
            {
                throw new AttachmentValidationException("Tổng dung lượng giải nén của ZIP không được vượt quá 50 MB.");
            }

            if (entry.Length == 0 || !TextExtensions.Contains(Path.GetExtension(entry.Name)))
            {
                continue;
            }

            if (extractedTextLength + entry.Length > MaximumExtractedTextBytes)
            {
                throw new AttachmentValidationException("Nội dung text trong ZIP vượt quá giới hạn 5 MB.");
            }

            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, StrictUtf8, true, leaveOpen: false);
            string entryText;
            try
            {
                entryText = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (DecoderFallbackException)
            {
                continue;
            }

            extractedTextLength += StrictUtf8.GetByteCount(entryText);
            text.AppendLine($"--- {entry.FullName} ---");
            text.AppendLine(entryText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        }

        if (text.Length == 0)
        {
            return null;
        }

        var path = Path.Combine(directory, $"{attachmentId:N}.text.txt");
        await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private string GetAttachmentDirectory(Guid projectId) =>
        Path.Combine(paths.ProjectsPath, projectId.ToString("N"), "attachments");

    private void EnsureControlledPath(string candidate)
    {
        var root = Path.GetFullPath(paths.ProjectsPath) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(candidate);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new AttachmentValidationException("Đường dẫn tệp đính kèm nằm ngoài thư mục dự án.");
        }
    }

    private static string NormalizeMimeType(string extension, string supplied) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        ".json" => "application/json",
        ".zip" => "application/zip",
        _ => string.IsNullOrWhiteSpace(supplied) ? "text/plain" : supplied.Trim().ToLowerInvariant(),
    };
}
