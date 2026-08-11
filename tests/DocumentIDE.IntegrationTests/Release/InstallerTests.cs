using System;
using System.IO;
using System.Xml.Linq;
using DocumentIDE.Core.Security;
using Xunit;

namespace DocumentIDE.IntegrationTests.Release;

public class InstallerTests
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
    public void Installer_Manifest_DocumentIDE_Wxs_Exists_And_Valid()
    {
        string repoRoot = GetRepoRoot();
        string wxsPath = Path.Combine(repoRoot, "installer", "DocumentIDE.wxs");

        Assert.True(File.Exists(wxsPath), $"WiX manifest must exist at {wxsPath}");

        string wxsContent = File.ReadAllText(wxsPath);
        XDocument doc = XDocument.Parse(wxsContent);

        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        Assert.NotNull(doc.Root);

        // Check Product / Package configuration
        Assert.Contains("Document IDE", wxsContent);
        Assert.Contains("DocumentIDE.Desktop.exe", wxsContent);
        Assert.Contains("DocumentIDE.ShareHost.exe", wxsContent);
        Assert.Contains("wwwroot", wxsContent);
        Assert.Contains("ProgramFiles64Folder", wxsContent);

        // Check Start Menu shortcut
        Assert.Contains("Shortcut", wxsContent);

        // Check WebView2 Evergreen prerequisite check
        Assert.Contains("F3017226-FE2A-4295-8BDF-00C3A9A7E4C5", wxsContent);
    }

    [Fact]
    public void Uninstall_Protection_Preserves_UserProfile_Documents()
    {
        string repoRoot = GetRepoRoot();
        string wxsPath = Path.Combine(repoRoot, "installer", "DocumentIDE.wxs");

        Assert.True(File.Exists(wxsPath), $"WiX manifest must exist at {wxsPath}");
        string wxsContent = File.ReadAllText(wxsPath);

        // Verify no RemoveFile or RemoveFolder element targets user profile or document extensions
        Assert.DoesNotContain("USERPROFILE", wxsContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonalFolder", wxsContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DesktopFolder", wxsContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*.docx", wxsContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*.md", wxsContent, StringComparison.OrdinalIgnoreCase);

        // Verify policy assertion
        bool isProtected = PrerequisiteValidator.VerifyUninstallProtectionPolicy(wxsPath);
        Assert.True(isProtected, "Uninstall protection policy must verify installer does not target user document directories.");
    }

    [Fact]
    public void App_Start_WebView2_Evergreen_Prerequisite_Check()
    {
        var result = PrerequisiteValidator.ValidateWebView2Runtime();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosticSummary));
    }

    [Fact]
    public void Build_Release_Script_Packages_Wxs_Installer_And_ShareHost()
    {
        string repoRoot = GetRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "build-release.ps1");

        Assert.True(File.Exists(scriptPath), $"Script must exist at {scriptPath}");
        string scriptContent = File.ReadAllText(scriptPath);

        Assert.Contains("DocumentIDE.ShareHost.csproj", scriptContent);
        Assert.Contains("DocumentIDE.wxs", scriptContent);
        Assert.Contains("wix build", scriptContent);

        string installerMsi = Path.Combine(repoRoot, "artifacts", "installer", "DocumentIDE-Setup.msi");
        Assert.True(File.Exists(installerMsi), $"Installer MSI artifact should exist at {installerMsi}");
    }
}
