using System;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-04/SCM-05 — shared Git stderr signatures for read-only version lookups.
///
/// `git show &lt;rev&gt;:&lt;path&gt;` reports an absent version and an unmerged
/// path with stable, localized-out English messages (the runner pins
/// <c>LC_ALL=C</c>); both services must distinguish "this side of the diff
/// simply does not exist" from a real failure and from the §21 conflict state
/// in exactly the same way.
/// </summary>
internal static class GitErrorSignatures
{
    public static bool IsHeadPathMissing(GitProcessResult result) =>
        result.StandardError.Contains("does not exist in '", StringComparison.OrdinalIgnoreCase)
        || result.StandardError.Contains("exists on disk, but not in '", StringComparison.OrdinalIgnoreCase);

    public static bool IsIndexPathMissing(GitProcessResult result) =>
        result.StandardError.Contains("does not exist (neither on disk nor in the index)", StringComparison.OrdinalIgnoreCase)
        || result.StandardError.Contains("exists on disk, but not in the index", StringComparison.OrdinalIgnoreCase);

    public static bool IsUnmergedIndexPath(GitProcessResult result) =>
        result.StandardError.Contains("is in the index, but not at stage 0", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SCM-08 — an unborn HEAD (a worktree with no committed history yet) is a
    /// normal repository state, so callers translate it into "no history yet"
    /// instead of a failure.
    ///
    /// The probe is <c>rev-parse --verify --quiet HEAD</c>: exit 1 with empty
    /// stderr means the branch has no commit yet, exactly the probe SCM-05 uses.
    /// An exit code plus empty stderr is stronger than scraping the
    /// <c>does not have any commits yet</c> wording, which a Git message change
    /// could silently turn into a typed failure.
    /// </summary>
    public static bool IsUnbornHeadProbe(GitProcessResult probe) =>
        probe.Status == GitProcessStatus.Failed
        && probe.ExitCode == 1
        && string.IsNullOrWhiteSpace(probe.StandardError);
}
