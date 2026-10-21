using System;
using System.IO;
using Xunit;

namespace DocumentIDE.IntegrationTests.Release;

/// <summary>
/// DEV-UPD — the runbook is the operator contract for the migration, the task
/// lifecycle, the log/state paths and the data-safety guarantee. This test keeps
/// those anchors from silently disappearing.
/// </summary>
public sealed class DevAutoUpdateRunbookTests
{
    [Fact]
    public void Runbook_documents_migration_control_and_data_safety()
    {
        string runbook = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "docs", "runbooks", "dev-auto-update.md"));

        Assert.Contains("per-user", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install-dev-autoupdate.ps1", runbook);
        Assert.Contains("last-built.sha", runbook);
        Assert.Contains("autoupdate.log", runbook);
        Assert.Contains("user documents", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", runbook, StringComparison.OrdinalIgnoreCase);
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
