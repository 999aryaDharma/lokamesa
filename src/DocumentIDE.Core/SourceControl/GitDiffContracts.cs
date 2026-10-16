using System.Collections.Generic;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-04 — the pair of file versions a Markdown Git diff compares
/// (spec 13 §9.1/§9.4).
///
/// Git distinguishes three states — HEAD, index (staging area) and working
/// tree — and the diff surface must be able to show each adjacent/complete
/// pair as appropriate to whether the user inspects staged or unstaged
/// changes.
/// </summary>
public enum GitDiffTarget
{
    /// <summary>
    /// HEAD ↔ index: what a commit of the currently staged state would record
    /// (the staged diff; <c>git diff --cached</c>).
    /// </summary>
    Staged = 0,

    /// <summary>
    /// HEAD ↔ working tree: the complete change, staged plus unstaged
    /// (the working-tree diff; <c>git diff HEAD</c>).
    /// </summary>
    WorkingTree = 1,

    /// <summary>
    /// index ↔ working tree: changes that are not staged yet
    /// (<c>git diff</c>).
    /// </summary>
    Unstaged = 2,
}

/// <summary>
/// SCM-04 — typed outcome of one file-diff request. Failures are inspectable
/// statuses with the originating <see cref="GitProcessResult"/>; nothing is
/// silently guessed (spec 13 §20).
/// </summary>
public enum GitDiffStatus
{
    /// <summary>A diff was materialized (hunks may still be empty when the versions are equal).</summary>
    Available = 0,

    /// <summary>The worktree/path is not inside a usable Git repository any more.</summary>
    NoRepository = 1,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 2,

    /// <summary>The requested path is not a safe worktree-relative document path; it was rejected before any access.</summary>
    PathRejected = 3,

    /// <summary>The diff could not be produced for another reason; inspect <see cref="GitDiffResult.Failure"/>.</summary>
    Failed = 4,
}

/// <summary>SCM-04 — role of one rendered diff line.</summary>
public enum GitDiffLineKind
{
    /// <summary>Unchanged line shown for context.</summary>
    Context = 0,

    /// <summary>Line present in the newer/right version only.</summary>
    Added = 1,

    /// <summary>Line present in the older/left version only.</summary>
    Removed = 2,
}

/// <summary>
/// SCM-04 — one rendered line of a textual diff. Exactly one of
/// <see cref="OldLineNumber"/>/<see cref="NewLineNumber"/> is set for
/// added/removed lines; both are set for context lines.
/// </summary>
/// <param name="Kind">Line role.</param>
/// <param name="Content">The line's text without its line terminator (never HTML/script interpreted by consumers).</param>
/// <param name="OldLineNumber">1-based line number in the left/older version, when the line belongs to it.</param>
/// <param name="NewLineNumber">1-based line number in the right/newer version, when the line belongs to it.</param>
/// <param name="NoNewlineAtEndOfFile">True when this line is the final line of its version and that version has no trailing newline.</param>
public sealed record GitDiffLine(
    GitDiffLineKind Kind,
    string Content,
    int? OldLineNumber,
    int? NewLineNumber,
    bool NoNewlineAtEndOfFile = false);

/// <summary>
/// SCM-04 — one unified-diff hunk over a contiguous region of both versions.
/// Line numbering follows the unified-diff convention: <see cref="OldStart"/>
/// and <see cref="NewStart"/> are the 1-based first line numbers; when the
/// corresponding count is 0 (pure addition/removal on that side) they carry
/// the number of the line before the hunk, which is 0 at the start of a file.
/// </summary>
public sealed record GitDiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<GitDiffLine> Lines)
{
    /// <summary>The unified header, e.g. <c>@@ -1,5 +1,6 @@</c> (a count of 1 is elided, as in Git).</summary>
    public string Header => $"@@ -{FormatRange(OldStart, OldCount)} +{FormatRange(NewStart, NewCount)} @@";

    private static string FormatRange(int start, int count) =>
        count == 1 ? start.ToString() : $"{start},{count}";
}

/// <summary>
/// SCM-04 — materialized, read-only textual diff of one file between two
/// versions (spec 13 §9.1).
///
/// The content is assembled from Git (<c>git show</c> for HEAD/index) and from
/// the working-tree file on disk; no repository file is ever written. Sides
/// that do not exist in a version are explicit (<see cref="LeftExists"/>/
/// <see cref="RightExists"/>) instead of being guessed.
/// </summary>
public sealed record GitDiff(
    GitDiffTarget Target,
    string Path,
    string WorktreeRoot,
    string LeftLabel,
    string RightLabel,
    bool LeftExists,
    bool RightExists,
    bool IsBinary,
    IReadOnlyList<GitDiffHunk> Hunks,
    string UnifiedText)
{
    /// <summary>True when the materialized versions have at least one textual difference.</summary>
    public bool HasChanges => Hunks.Count > 0;
}

/// <summary>
/// SCM-04 — typed outcome of one file-diff request, mirroring the SCM-02
/// result shape: either an available <see cref="GitDiff"/> or a typed failure
/// with the originating <see cref="GitProcessResult"/>.
/// </summary>
public sealed record GitDiffResult(
    GitDiffStatus Status,
    GitDiff? Diff,
    GitProcessResult? Failure = null)
{
    /// <summary>True only when a diff payload is present.</summary>
    public bool IsAvailable => Status == GitDiffStatus.Available && Diff is not null;
}
