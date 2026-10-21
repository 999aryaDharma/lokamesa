using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace DocumentIDE.Core.Diagnostics;

/// <summary>
/// Identity of the running build. The payload publisher writes
/// <c>build-info.json</c> next to the executable so the dev auto-updater can
/// confirm which commit is actually running; a hand-built or older payload
/// falls back to assembly metadata with an unknown SHA.
/// </summary>
public sealed record BuildInfo(string Version, string Sha)
{
    public static BuildInfo Load(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        try
        {
            string path = Path.Combine(baseDirectory, "build-info.json");
            if (File.Exists(path))
            {
                var payload = JsonSerializer.Deserialize<BuildInfoPayload>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload is not null && !string.IsNullOrWhiteSpace(payload.Version))
                {
                    return new BuildInfo(
                        payload.Version,
                        string.IsNullOrWhiteSpace(payload.Sha) ? "unknown" : payload.Sha);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or unreadable identity file is not fatal: fall back to
            // the assembly version so the app still reports something usable.
        }

        return new BuildInfo(
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0",
            "unknown");
    }

    private sealed record BuildInfoPayload(string Version, string Sha);
}
