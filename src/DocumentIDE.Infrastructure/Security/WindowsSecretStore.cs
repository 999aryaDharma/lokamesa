using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.Security;

namespace DocumentIDE.Infrastructure.Security;

public class WindowsSecretStore : ISecretStore
{
    private readonly string _secretsDirectory;

    public WindowsSecretStore(string? secretsDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(secretsDirectory))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localAppData, "DocumentIDE");
            _secretsDirectory = Path.Combine(appDir, "secrets");
        }
        else
        {
            _secretsDirectory = secretsDirectory;
        }
    }

    public async Task SetSecretAsync(string key, SecretValue secret)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (secret == null) throw new ArgumentNullException(nameof(secret));

        if (!Directory.Exists(_secretsDirectory))
        {
            Directory.CreateDirectory(_secretsDirectory);
        }

        var keyPath = GetSecretFilePath(key);
        var plainBytes = Encoding.UTF8.GetBytes(secret.Value);

        byte[] encryptedBytes;
        if (OperatingSystem.IsWindows())
        {
            encryptedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        else
        {
            encryptedBytes = plainBytes;
        }

        await File.WriteAllBytesAsync(keyPath, encryptedBytes);
    }

    public async Task<SecretValue?> GetSecretAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var keyPath = GetSecretFilePath(key);
        if (!File.Exists(keyPath))
        {
            return null;
        }

        var encryptedBytes = await File.ReadAllBytesAsync(keyPath);
        byte[] plainBytes;
        if (OperatingSystem.IsWindows())
        {
            plainBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        else
        {
            plainBytes = encryptedBytes;
        }

        var plainText = Encoding.UTF8.GetString(plainBytes);
        return new SecretValue(plainText);
    }

    public Task DeleteSecretAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var keyPath = GetSecretFilePath(key);
        if (File.Exists(keyPath))
        {
            File.Delete(keyPath);
        }

        return Task.CompletedTask;
    }

    private string GetSecretFilePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_secretsDirectory, $"{safeKey}.bin");
    }
}
