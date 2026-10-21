using System;
using System.IO;
using Xunit;

namespace DocumentIDE.IntegrationTests.Scripts;

/// <summary>
/// DEV-UPD — one publish contract shared by the release build and the local
/// auto-updater: both must produce the same payload layout and identity file.
/// </summary>
public sealed class PublishPayloadTests
{
    [Fact]
    public void Publish_payload_script_defines_the_shared_payload_contract()
    {
        string script = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "scripts", "publish-payload.ps1"));

        Assert.Contains("DocumentIDE.Desktop.csproj", script);
        Assert.Contains("DocumentIDE.ShareHost.csproj", script);
        Assert.Contains("build-info.json", script);
        Assert.Contains("wwwroot", script);
    }

    [Fact]
    public void Publish_payload_script_accepts_the_release_parameters()
    {
        string script = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "scripts", "publish-payload.ps1"));

        Assert.Contains("[string]$RepoRoot", script);
        Assert.Contains("[string]$OutputDir", script);
        Assert.Contains("[string]$Version", script);
        Assert.Contains("[string]$Sha", script);
    }

    [Fact]
    public void Release_build_delegates_to_the_shared_publisher()
    {
        string script = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "scripts", "build-release.ps1"));

        Assert.Contains("publish-payload.ps1", script);
        Assert.Contains("DocumentIDE.Desktop.csproj", script);
        Assert.Contains("-Version $version", script);
    }

    private static string GetRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "DocumentIDE.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Could not find repository root containing DocumentIDE.sln");
    }
}
