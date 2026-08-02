using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Infrastructure;

public sealed class DpapiSecretStore(RuntimePaths paths, ILogger<DpapiSecretStore> logger) : ISecretStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("PolygonAiBuilder.Secrets.v1");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string FilePath => paths.SecretsPath;

    public async Task<SecretBundle> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return SecretBundle.Empty;
        }

        try
        {
            await using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var document = await JsonSerializer.DeserializeAsync<EncryptedSecretsDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (document is null || document.Version != 1)
            {
                throw new SecretStoreException("Định dạng tệp thông tin bí mật không được hỗ trợ.");
            }

            return new(
                Unprotect(document.OpenAiApiKeyEncrypted),
                Unprotect(document.GeminiApiKeyEncrypted),
                Unprotect(document.PolygonApiKeyEncrypted),
                Unprotect(document.PolygonApiSecretEncrypted));
        }
        catch (SecretStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException(
                "Không thể đọc thông tin bí mật đã mã hóa. Tệp có thể bị hỏng hoặc thuộc tài khoản Windows khác.",
                exception);
        }
    }

    public async Task SaveAsync(SecretBundle secrets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        paths.EnsureDirectories();

        var document = new EncryptedSecretsDocument
        {
            Version = 1,
            OpenAiApiKeyEncrypted = Protect(secrets.OpenAiApiKey),
            GeminiApiKeyEncrypted = Protect(secrets.GeminiApiKey),
            PolygonApiKeyEncrypted = Protect(secrets.PolygonApiKey),
            PolygonApiSecretEncrypted = Protect(secrets.PolygonApiSecret),
        };

        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
            RestrictAccessOnWindows(FilePath);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException("Không thể lưu thông tin bí mật đã mã hóa.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException cleanupException)
                {
                    logger.LogWarning(cleanupException, "Unable to remove an unused secret-store temporary file.");
                }
            }
        }
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required to store credentials.");
        }

        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToBase64String(
                ProtectedData.Protect(plaintext, OptionalEntropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required to read credentials.");
        }

        var ciphertext = Convert.FromBase64String(value);
        var plaintext = ProtectedData.Unprotect(ciphertext, OptionalEntropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void RestrictAccessOnWindows(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictWindowsAccess(filePath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictWindowsAccess(string filePath)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot determine the current Windows user.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(filePath).SetAccessControl(security);
    }

    private sealed class EncryptedSecretsDocument
    {
        public int Version { get; set; }
        public string OpenAiApiKeyEncrypted { get; set; } = string.Empty;
        public string GeminiApiKeyEncrypted { get; set; } = string.Empty;
        public string PolygonApiKeyEncrypted { get; set; } = string.Empty;
        public string PolygonApiSecretEncrypted { get; set; } = string.Empty;
    }
}

public sealed class SecretStoreException : Exception
{
    public SecretStoreException(string message)
        : base(message)
    {
    }

    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
