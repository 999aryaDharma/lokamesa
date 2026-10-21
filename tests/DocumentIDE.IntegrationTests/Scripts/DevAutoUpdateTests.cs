using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace DocumentIDE.IntegrationTests.Scripts;

/// <summary>
/// DEV-UPD — the local watcher owns the risky part of the feature (stopping the
/// app, swapping the install directory, rolling back). Its script contract and
/// its self-test are the regression gate; the self-test exercises the dead-PID
/// rejection, build de-duplication, sibling-safe swap, injected failure and
/// rollback without touching the real install.
/// </summary>
public sealed class DevAutoUpdateTests
{
    [Fact]
    public void Watcher_script_has_the_required_contract()
    {
        string script = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "scripts", "dev-autoupdate.ps1"));

        foreach (string token in new[]
                 {
                     "origin/master",
                     "last-built.sha",
                     "runtime.json",
                     "prepare-restart",
                     "/quit",
                     "update-manifest.json",
                     "updater.lock",
                 })
        {
            Assert.Contains(token, script);
        }
    }

    [Fact]
    public void Watcher_self_test_swaps_and_rolls_back()
    {
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File scripts/dev-autoupdate.ps1 -SelfTest")
        {
            WorkingDirectory = GetRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        bool exited = process.WaitForExit(120_000);
        string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();

        Assert.True(exited, "self-test timed out: " + output);
        Assert.True(process.ExitCode == 0, output);
    }

    [Fact]
    public void Task_installer_registers_a_per_user_logon_task()
    {
        string script = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "scripts", "install-dev-autoupdate.ps1"));

        Assert.Contains("Register-ScheduledTask", script);
        Assert.Contains("AtLogOn", script);
        Assert.Contains("DocumentIDE Dev Auto-Update", script);
        Assert.Contains("-Uninstall", script);
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
