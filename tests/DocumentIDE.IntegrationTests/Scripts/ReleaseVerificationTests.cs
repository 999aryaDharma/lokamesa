using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace DocumentIDE.IntegrationTests.Scripts;

/// <summary>
/// REL-02 — Release Verification Pipeline.
/// Red stage: these tests must FAIL before scripts/verify-release.ps1 exists.
/// Acceptance: any release-gate failure blocks packaging/release (nonzero exit,
/// no artifact/packaging marker), checksums are generated, and the verification
/// report names the exact commit.
/// </summary>
public sealed class ReleaseVerificationTests
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

    private static (int ExitCode, string Output) RunVerifyRelease(string arguments, int timeoutMs = 120000)
    {
        string repoRoot = GetRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "verify-release.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -NonInteractive -File \"{scriptPath}\" {arguments}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(timeoutMs))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException("verify-release.ps1 self-test timed out.");
        }
        return (proc.ExitCode, stdout + stderr);
    }

    [Fact]
    public void VerifyRelease_Script_Exists()
    {
        string scriptPath = Path.Combine(GetRepoRoot(), "scripts", "verify-release.ps1");
        Assert.True(File.Exists(scriptPath), $"Release verification script should exist at {scriptPath}");
    }

    [Fact]
    public void VerifyRelease_Script_IsFailClosed()
    {
        string scriptPath = Path.Combine(GetRepoRoot(), "scripts", "verify-release.ps1");
        Assert.True(File.Exists(scriptPath), $"Release verification script should exist at {scriptPath}");

        string content = File.ReadAllText(scriptPath);
        // Fail-closed orchestration: any gate failure exits nonzero before packaging.
        Assert.Contains("$ErrorActionPreference = \"Stop\"", content);
        Assert.Contains("exit 1", content);
        // No 'green by skipping': skips must be explicit and environment-gated.
        Assert.Contains("EnvironmentGated", content);
        // Artifact checksums attached to the release.
        Assert.Contains("Get-FileHash", content);
        // Verification report names the exact commit.
        Assert.Contains("rev-parse HEAD", content);
        // Word/COM capable-runner detection.
        Assert.Contains("WINWORD", content);
        // Packaging is invoked only after gates pass, then verified post-package.
        Assert.Contains("build-release.ps1", content);
        Assert.Contains("PackagingVerification", content);
    }

    [Fact]
    public void VerifyRelease_FailingGate_BlocksRelease()
    {
        string repoRoot = GetRepoRoot();
        Assert.True(File.Exists(Path.Combine(repoRoot, "scripts", "verify-release.ps1")),
            "Release verification script should exist.");

        // Self-test runs synthetic gates only (no toolchain): one gate is forced
        // to fail, so the run must exit nonzero and must not package anything.
        var (exitCode, output) = RunVerifyRelease(
            "-SelfTest -FailGate SyntheticBackend -OutputDir artifacts-test/selftest-fail");
        try
        {
            Assert.True(exitCode != 0,
                $"A failing release gate must return nonzero. Output:\n{output}");
            Assert.Contains("BLOCKED", output);
            Assert.DoesNotContain("[PACKAGED]", output);
        }
        finally
        {
            string outDir = Path.Combine(repoRoot, "artifacts-test", "selftest-fail");
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    [Fact]
    public void VerifyRelease_PassingSelfTest_ProducesCommitPinnedReport()
    {
        string repoRoot = GetRepoRoot();
        Assert.True(File.Exists(Path.Combine(repoRoot, "scripts", "verify-release.ps1")),
            "Release verification script should exist.");

        string outDir = Path.Combine(repoRoot, "artifacts-test", "selftest-pass");
        var (exitCode, output) = RunVerifyRelease(
            "-SelfTest -OutputDir artifacts-test/selftest-pass");
        try
        {
            Assert.Equal(0, exitCode);
            string reportPath = Path.Combine(outDir, "verification-report.json");
            Assert.True(File.Exists(reportPath), $"Self-test should write {reportPath}.\n{output}");
            string report = File.ReadAllText(reportPath);
            Assert.Contains("\"commit\"", report);
            Assert.Contains("\"verdict\"", report);
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }
}
