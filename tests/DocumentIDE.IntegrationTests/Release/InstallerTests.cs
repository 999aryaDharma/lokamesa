using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        Assert.Contains("Scope=\"perUser\"", wxsContent);
        Assert.Contains("LocalAppDataFolder", wxsContent);
        Assert.DoesNotContain("ProgramFiles64Folder", wxsContent);
        Assert.Contains("Version=\"1.0.2.0\"", wxsContent);

        // Check Start Menu shortcut
        Assert.Contains("Shortcut", wxsContent);

        // Check WebView2 Evergreen prerequisite check
        Assert.Contains("F3017226-FE2A-4295-8BDF-00C3A9A7E4C5", wxsContent);
    }

    [Fact]
    public void App_and_installer_versions_agree()
    {
        string repoRoot = GetRepoRoot();
        string wxsContent = File.ReadAllText(Path.Combine(repoRoot, "installer", "DocumentIDE.wxs"));

        XDocument csproj = XDocument.Load(
            Path.Combine(repoRoot, "src", "DocumentIDE.Desktop", "DocumentIDE.Desktop.csproj"));
        string? version = csproj
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Version")
            ?.Value;

        Assert.Equal("1.0.2.0", version);
        Assert.Contains($"Version=\"{version}\"", wxsContent);
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

        Assert.Contains("publish-payload.ps1", scriptContent);
        Assert.Contains("DocumentIDE.wxs", scriptContent);
        Assert.Contains("wix build", scriptContent);

        string installerMsi = Path.Combine(repoRoot, "artifacts", "installer", "DocumentIDE-Setup.msi");
        Assert.True(File.Exists(installerMsi), $"Installer MSI artifact should exist at {installerMsi}");

        // Existence alone is not evidence: an earlier manifest produced a 0.31 MB
        // MSI that carried only the two apphosts, so the installed product could
        // not start. Assert the File table actually carries the self-contained
        // published payload (runtime assemblies, SQLite, WebView2 loader, wwwroot).
        AssertMsiContainsPublishedPayload(installerMsi);
    }

    /// <summary>
    /// Reads the MSI's File table through the Windows Installer COM API and
    /// asserts the self-contained desktop payload is really packaged.
    /// A size/existence check on the .msi file itself would not catch a manifest
    /// that references only two files.
    /// </summary>
    private static void AssertMsiContainsPublishedPayload(string msiPath)
    {
        Type? installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
        Assert.True(installerType is not null, "Windows Installer COM must be available to inspect the MSI.");

        object installer = Activator.CreateInstance(installerType!)!;
        object database = installerType!.InvokeMember(
            "OpenDatabase", BindingFlags.InvokeMethod, null, installer, new object?[] { msiPath, 0 })!;
        object view = database.GetType().InvokeMember(
            "OpenView", BindingFlags.InvokeMethod, null, database,
            new object?[] { "SELECT FileName, FileSize FROM File" })!;
        view.GetType().InvokeMember("Execute", BindingFlags.InvokeMethod, null, view, null);

        var names = new List<string>();
        long totalBytes = 0;
        while (true)
        {
            object? record = view.GetType().InvokeMember("Fetch", BindingFlags.InvokeMethod, null, view, null);
            if (record is null)
            {
                break;
            }

            names.Add((string)record.GetType().InvokeMember(
                "StringData", BindingFlags.GetProperty, null, record, new object?[] { 1 })!);
            totalBytes += (int)record.GetType().InvokeMember(
                "IntegerData", BindingFlags.GetProperty, null, record, new object?[] { 2 })!;
        }

        foreach (string required in new[]
                 {
                     "DocumentIDE.Api.dll",
                     "DocumentIDE.Core.dll",
                     "DocumentIDE.Infrastructure.dll",
                     "DocumentIDE.Desktop.exe",
                     "DocumentIDE.ShareHost.exe",
                     "index.html",
                 })
        {
            Assert.Contains(names, name => name.EndsWith(required, StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(
            names.Count > 100,
            $"The MSI should carry the whole published payload; it lists only {names.Count} file(s).");
        Assert.True(
            totalBytes > 100L * 1024 * 1024,
            $"The MSI payload should exceed 100 MB; it totals {totalBytes} bytes.");
    }
}
