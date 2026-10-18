using System;
using System.IO;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-01 test fixture: installs a `pre-commit` hook that writes a `started`
/// marker, sleeps, then writes a `finished` marker.
///
/// This makes process-tree termination observable: the hook runs inside the
/// Git process tree (git.exe → sh → sleep), so the `finished` marker can only
/// appear if a descendant survived cancellation/timeout.
/// </summary>
internal static class HangingCommitHook
{
    public const string StartedMarkerName = "scm01-hook-started.txt";
    public const string FinishedMarkerName = "scm01-hook-finished.txt";

    public static string StartedMarker(TestGitRepository repository) =>
        Path.Combine(repository.RootPath, StartedMarkerName);

    public static string FinishedMarker(TestGitRepository repository) =>
        Path.Combine(repository.RootPath, FinishedMarkerName);

    public static void Write(TestGitRepository repository, int sleepSeconds)
    {
        string hooksDirectory = Path.Combine(repository.RootPath, ".git", "hooks");
        Directory.CreateDirectory(hooksDirectory);
        string hookPath = Path.Combine(hooksDirectory, "pre-commit");
        File.WriteAllText(
            hookPath,
            $"#!/bin/sh\ntouch {StartedMarkerName}\nsleep {sleepSeconds}\ntouch {FinishedMarkerName}\n");
    }
}
