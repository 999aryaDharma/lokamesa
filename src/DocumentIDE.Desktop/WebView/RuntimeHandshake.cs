using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace DocumentIDE.Desktop.WebView;

/// <summary>
/// One-run handshake consumed by the local auto-updater:
/// <c>{ "pid", "port", "version", "sha", "controlToken" }</c> at
/// <see cref="DefaultPath"/>.
/// </summary>
public sealed record RuntimeHandshakePayload(
    int Pid,
    int Port,
    string Version,
    string Sha,
    string ControlToken);

/// <summary>
/// Owns <c>%LOCALAPPDATA%\DocumentIDE\runtime.json</c> for the lifetime of the
/// process. The file is removed on clean exit; a crash leaves a stale file that
/// the watcher rejects by PID and cleans up.
/// </summary>
public sealed class RuntimeHandshake : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly string _path;

    private RuntimeHandshake(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DocumentIDE",
        "runtime.json");

    public static RuntimeHandshake Write(string path, RuntimeHandshakePayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(payload);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
        RestrictToCurrentUser(path);
        return new RuntimeHandshake(path);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // A locked file must not crash shutdown; the watcher rejects it by PID.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: the next run overwrites it.
        }
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user is null)
            {
                return;
            }

            var security = new FileSecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or System.Security.SecurityException
            or PrivilegeNotHeldException
            or InvalidOperationException)
        {
            // Best effort: the file already lives under the per-user LOCALAPPDATA.
        }
    }
}
