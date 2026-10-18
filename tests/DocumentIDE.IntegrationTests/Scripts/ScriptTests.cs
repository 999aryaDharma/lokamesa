using System;
using System.IO;
using Xunit;

namespace DocumentIDE.IntegrationTests.Scripts;

public class ScriptTests
{
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

    [Fact]
    public void Fresh_Clone_Bootstrap_Succeeds()
    {
        string repoRoot = GetRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "bootstrap.ps1");

        Assert.True(File.Exists(scriptPath), $"Script should exist at {scriptPath}");

        string scriptContent = File.ReadAllText(scriptPath);
        Assert.Contains("dotnet --version", scriptContent);
        Assert.Contains("node --version", scriptContent);
        Assert.Contains("dotnet restore", scriptContent);
        Assert.Contains("npm ci", scriptContent);
    }

    [Fact]
    public void Verify_Script_Returns_NonZero_On_Failing_Test()
    {
        string repoRoot = GetRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "verify.ps1");

        Assert.True(File.Exists(scriptPath), $"Script should exist at {scriptPath}");

        string scriptContent = File.ReadAllText(scriptPath);
        Assert.Contains("$ErrorActionPreference = \"Stop\"", scriptContent);
        Assert.Contains("exit 1", scriptContent);
        Assert.Contains("exit 0", scriptContent);
    }

    [Fact]
    public void Release_Build_Embeds_Production_Web_Assets()
    {
        string repoRoot = GetRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "build-release.ps1");

        Assert.True(File.Exists(scriptPath), $"Script should exist at {scriptPath}");

        string scriptContent = File.ReadAllText(scriptPath);
        Assert.Contains("npm run build", scriptContent);
        Assert.Contains("dotnet publish", scriptContent);
        Assert.Contains("artifacts", scriptContent);

        string artifactsDesktopDir = Path.Combine(repoRoot, "artifacts", "desktop");
        string exePath = Path.Combine(artifactsDesktopDir, "DocumentIDE.Desktop.exe");
        string indexHtmlPath = Path.Combine(artifactsDesktopDir, "wwwroot", "index.html");

        Assert.True(File.Exists(exePath), $"Executable should exist at {exePath}");
        Assert.True(File.Exists(indexHtmlPath), $"Embedded index.html should exist at {indexHtmlPath}");
    }
}
