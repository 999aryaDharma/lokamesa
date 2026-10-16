using System;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-01 — conservative mapping from Git process output to typed error kinds
/// (spec 13 §20).
///
/// The runner's environment policy pins <c>LC_ALL=C</c>, so these signatures are
/// deterministic rather than locale-dependent. Matching is first-match-wins on
/// unambiguous markers; anything else stays <see cref="GitProcessErrorKind.Unknown"/>
/// instead of guessing (no silent fallback).
/// </summary>
public static class GitFailureClassifier
{
    /// <summary>
    /// Classifies a failed Git invocation. Returns
    /// <see cref="GitProcessErrorKind.None"/> for a zero exit code.
    /// </summary>
    public static GitProcessErrorKind Classify(int exitCode, string? standardOutput, string? standardError)
    {
        if (exitCode == 0)
        {
            return GitProcessErrorKind.None;
        }

        string stderr = standardError ?? string.Empty;
        string stdout = standardOutput ?? string.Empty;

        // Order matters: more specific signatures first.
        if (Contains(stderr, "untracked working tree files would be overwritten"))
        {
            return GitProcessErrorKind.GitUntrackedConflict;
        }

        if (Contains(stderr, "CONFLICT")
            || Contains(stderr, "automatic merge failed")
            || Contains(stderr, "unmerged paths"))
        {
            return GitProcessErrorKind.GitConflict;
        }

        if (Contains(stderr, "contains modified or untracked files")
            || Contains(stderr, "would be overwritten"))
        {
            return GitProcessErrorKind.GitDirtyWorktree;
        }

        if (Contains(stderr, "not a git repository"))
        {
            return GitProcessErrorKind.GitRepositoryNotFound;
        }

        if (Contains(stderr, "is not a working tree"))
        {
            return GitProcessErrorKind.GitWorktreeInvalid;
        }

        if (Contains(stderr, "index.lock")
            || Contains(stderr, "another git process seems to be running")
            || Contains(stderr, "could not lock config file"))
        {
            return GitProcessErrorKind.GitLockContention;
        }

        if (Contains(stderr, "authentication failed")
            || Contains(stderr, "could not read username")
            || Contains(stderr, "terminal prompts disabled")
            || Contains(stderr, "permission denied (publickey"))
        {
            return GitProcessErrorKind.GitAuthRequired;
        }

        if (Contains(stderr, "permission denied")
            || Contains(stderr, "access is denied"))
        {
            return GitProcessErrorKind.GitPermissionDenied;
        }

        if (Contains(stderr, "hook declined")
            || Contains(stderr, "hook exited with code")
            || Contains(stderr, "hook failed"))
        {
            return GitProcessErrorKind.GitCommitHookFailed;
        }

        if (Contains(stderr, "remote rejected")
            || Contains(stderr, "failed to push some refs")
            || Contains(stderr, "non-fast-forward")
            || Contains(stderr, "! [rejected]")
            || Contains(stdout, "! [rejected]"))
        {
            return GitProcessErrorKind.GitPushRejected;
        }

        if (Contains(stderr, "not possible to fast-forward")
            || Contains(stderr, "diverging branches")
            || Contains(stderr, "need to specify how to reconcile divergent branches"))
        {
            return GitProcessErrorKind.GitPullNotFastForward;
        }

        return GitProcessErrorKind.Unknown;
    }

    private static bool Contains(string haystack, string marker) =>
        haystack.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
