using System;
using System.IO;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-04/SCM-05 — shared worktree path safety for read-only document diffs
/// (spec 13 §23).
///
/// Requested paths are treated as untrusted input: they must be
/// worktree-relative document paths (no rooted/drive-qualified form, no
/// <c>.</c>/<c>..</c>/empty segments, no <c>:</c>), and containment is
/// re-checked on the combined <see cref="Path.GetFullPath(string)"/> result so
/// no code path can read outside the worktree — including worktrees rooted at
/// a drive root (e.g. <c>D:\</c>), where naive prefix comparison would
/// false-reject every document.
/// </summary>
internal static class GitDocumentPath
{
    /// <summary>Maximum accepted relative path length (mirrors SCM-04).</summary>
    public const int MaxRelativePathLength = 4096;

    /// <summary>Canonicalizes an existing worktree root without following a trailing separator.</summary>
    public static bool TryCanonicalizeRoot(
        string worktreeRoot,
        out string canonicalRoot,
        out string? error)
    {
        canonicalRoot = string.Empty;
        error = null;

        try
        {
            canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreeRoot));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Validates a path as a worktree-relative document path: no rooted/
    /// drive-qualified form, no <c>.</c>/<c>..</c> segments, no empty
    /// segments, no <c>:</c> (which would corrupt the <c>rev:path</c> object
    /// syntax).
    /// </summary>
    public static bool TryNormalizeRelativePath(string path, out string normalized)
    {
        normalized = string.Empty;

        string candidate = path.Replace('\\', '/').Trim();
        if (candidate.Length == 0 || candidate.Length > MaxRelativePathLength)
        {
            return false;
        }

        if (candidate.StartsWith('/')
            || Path.IsPathRooted(candidate)
            || candidate.Contains(':'))
        {
            return false;
        }

        foreach (string segment in candidate.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return false;
            }

            // `.git` is repository metadata, never a document path, and it may
            // carry remote URLs with embedded credentials. Containment alone
            // accepts it (it resolves inside the worktree), so the segment is
            // rejected here for every document surface at once.
            if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// Combines the validated relative path with the worktree root and
    /// re-checks containment on the combined absolute path (spec 13 §23).
    /// </summary>
    public static bool TryResolveContainedPath(string canonicalRoot, string gitPath, out string fullPath)
    {
        fullPath = string.Empty;

        try
        {
            string native = gitPath.Replace('/', Path.DirectorySeparatorChar);
            string combined = Path.GetFullPath(Path.Combine(canonicalRoot, native));
            string relative = Path.GetRelativePath(canonicalRoot, combined);
            if (relative == ".."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                return false;
            }

            fullPath = combined;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return false;
        }
    }
}
