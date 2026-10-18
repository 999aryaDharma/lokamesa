using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace DocumentIDE.IntegrationTests.Scripts;

/// <summary>
/// REL-03 — V1 Readiness Gate.
/// Red stage: the readiness report does not exist before this sprint's deliverable.
/// The gate must be honest and evidence-based:
/// - exactly one machine-readable verdict line,
/// - a GO verdict requires zero FAIL/UNVERIFIED mapping rows,
/// - a BLOCKED verdict requires at least one itemized FAIL/UNVERIFIED row,
/// - every spec-12 completion gate must be mapped in the report,
/// - the placeholder/skipped-test/offline scans and residual risks must be recorded,
/// - no release-blocking suite may contain a skipped test (safety/security gate).
/// </summary>
public sealed class V1ReadinessGateTests
{
    /// <summary>
    /// Every completion gate from docs/specs/12-implementation-plan.md
    /// ("Completion Gates" + "Updated Completion Gates"). Each string must appear
    /// verbatim in the readiness report so a dropped gate fails this test.
    /// </summary>
    private static readonly string[] Spec12CompletionGates =
    {
        "Filesystem remains source of truth.",
        "Named workspaces persist.",
        "Canvas placement preserves spatial memory.",
        "Search is incremental/realtime.",
        "DOCX opens in Word.",
        "Markdown edits internally.",
        "Search & Replace is deterministic, recursive, previewable, undoable.",
        "30-day recovery works.",
        "PDF single/batch/merge works.",
        "Graph distinguishes explicit vs inferred.",
        "Public share cannot mutate local files.",
        "Security suite passes.",
        "Golden DOCX regression suite passes.",
        "foreign browser origin cannot invoke privileged local API",
        "ShareHost is separate and has no privileged mutation/Word dependencies",
        "Live Share never auto-expands membership",
        "Word COM maximum concurrency is 1",
        "SQLite WAL concurrency test passes",
        "destructive batch fails closed without recovery capacity",
        "hostile DOCX resource tests pass",
        "share CSP/token/rate-limit tests pass",
        "canvas/graph/PDF performance regressions pass",
    };

    /// <summary>Release-blocking test suites (REL-02 named projections) that must not be skipped.</summary>
    private static readonly string[] ReleaseBlockingSuites =
    {
        "DocumentIDE.SecurityTests",
        "DocumentIDE.SafetyTests",
        "DocumentIDE.GoldenTests",
        "DocumentIDE.ArchitectureTests",
        "DocumentIDE.PerformanceTests",
        "DocumentIDE.SoakTests",
    };

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

    private static string GetReportPath() =>
        Path.Combine(GetRepoRoot(), "docs", "quality", "v1-readiness-report.md");

    private static string ReadReport()
    {
        string path = GetReportPath();
        Assert.True(File.Exists(path), $"V1 readiness report should exist at {path}");
        return File.ReadAllText(path);
    }

    private static string ParseVerdict(string content)
    {
        var matches = Regex.Matches(content, @"^\*\*Verdict:\*\*\s*(GO|BLOCKED)\s*$", RegexOptions.Multiline);
        Assert.True(matches.Count == 1,
            $"Report must contain exactly one '**Verdict:** GO|BLOCKED' line; found {matches.Count}.");
        return matches[0].Groups[1].Value;
    }

    [Fact]
    public void ReadinessReport_Exists()
    {
        Assert.True(File.Exists(GetReportPath()),
            $"REL-03 deliverable missing: {GetReportPath()}");
    }

    [Fact]
    public void ReadinessReport_HasExactlyOneVerdictLine()
    {
        ParseVerdict(ReadReport());
    }

    [Fact]
    public void ReadinessReport_VerdictIsConsistentWithMappedEvidence()
    {
        string content = ReadReport();
        string verdict = ParseVerdict(content);
        bool hasFailingRow = Regex.IsMatch(content, @"\|\s*(FAIL|UNVERIFIED)\s*\|");

        if (verdict == "GO")
        {
            Assert.False(hasFailingRow,
                "GO verdict is only valid when no mapped requirement is FAIL or UNVERIFIED.");
        }
        else
        {
            Assert.True(hasFailingRow,
                "BLOCKED verdict must itemize at least one FAIL/UNVERIFIED requirement.");
        }
    }

    [Fact]
    public void ReadinessReport_MapsEverySpec12CompletionGate()
    {
        string content = ReadReport();
        foreach (string gate in Spec12CompletionGates)
        {
            Assert.Contains(gate, content);
        }
    }

    [Fact]
    public void ReadinessReport_IncludesStepEvidence_ScansOfflineRisks()
    {
        string content = ReadReport().ToLowerInvariant();
        Assert.Contains("placeholder scan", content);
        Assert.Contains("skipped-test scan", content);
        Assert.Contains("offline", content);
        Assert.Contains("residual risks", content);
    }

    [Fact]
    public void NoSkippedTests_InReleaseBlockingSuites()
    {
        string repoRoot = GetRepoRoot();
        foreach (string suite in ReleaseBlockingSuites)
        {
            string suiteDir = Path.Combine(repoRoot, "tests", suite);
            Assert.True(Directory.Exists(suiteDir), $"Expected release-blocking suite directory {suiteDir}");
            foreach (string file in Directory.EnumerateFiles(suiteDir, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                Assert.False(Regex.IsMatch(source, @"\[(Fact|Theory)\s*\(\s*Skip\s*="),
                    $"Release-blocking suite '{suite}' must not skip tests: {file}");
            }
        }
    }
}
