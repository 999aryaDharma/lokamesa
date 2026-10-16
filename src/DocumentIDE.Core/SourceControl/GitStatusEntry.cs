using System.Collections.Generic;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-02 — per-side change kind from the porcelain v2 <c>XY</c> status pair
/// (spec 13 §6). <c>X</c> describes the index/staged side, <c>Y</c> the
/// working-tree side; a side that matches HEAD is <see cref="Unmodified"/>.
/// </summary>
public enum GitChangeKind
{
    Unmodified = 0,
    Modified = 1,
    Added = 2,
    Deleted = 3,
    Renamed = 4,
    Copied = 5,
    TypeChanged = 6,

    /// <summary>Unmerged/conflict participant (only reported for <c>u</c> entries).</summary>
    Unmerged = 7,
}

/// <summary>
/// SCM-02 — overall presentation state of a status entry (spec 13 §6/§8):
/// exactly the state vocabulary the Source Control surface groups by.
/// </summary>
public enum GitFileState
{
    Unmodified = 0,
    Modified = 1,
    Added = 2,
    Deleted = 3,
    Renamed = 4,
    Copied = 5,
    TypeChanged = 6,
    Untracked = 7,
    Ignored = 8,
    Conflicted = 9,
}

/// <summary>
/// SCM-02 — unmerged (conflict) classification of a porcelain v2 <c>u</c>
/// entry (spec 13 §21). <see cref="None"/> means Git reported an unmerged
/// entry whose combination this parser does not classify; the entry still
/// stays conflicted and inspectable via <see cref="GitStatusEntry.StatusCharacters"/>.
/// </summary>
public enum GitConflictKind
{
    None = 0,

    /// <summary><c>UU</c> — unmerged, both modified.</summary>
    BothModified = 1,

    /// <summary><c>AA</c> — unmerged, both added.</summary>
    BothAdded = 2,

    /// <summary><c>DD</c> — unmerged, both deleted.</summary>
    BothDeleted = 3,

    /// <summary><c>AU</c> — unmerged, added by us.</summary>
    AddedByUs = 4,

    /// <summary><c>UA</c> — unmerged, added by them.</summary>
    AddedByThem = 5,

    /// <summary><c>DU</c> — unmerged, deleted by us.</summary>
    DeletedByUs = 6,

    /// <summary><c>UD</c> — unmerged, deleted by them.</summary>
    DeletedByThem = 7,
}

/// <summary>SCM-02 — porcelain v2 entry record type.</summary>
public enum GitStatusEntryType
{
    /// <summary><c>1</c> — ordinary changed entry.</summary>
    Ordinary = 0,

    /// <summary><c>2</c> — renamed or copied entry (carries <see cref="GitStatusEntry.OriginalPath"/>).</summary>
    RenamedOrCopied = 1,

    /// <summary><c>u</c> — unmerged (conflict) entry.</summary>
    Unmerged = 2,

    /// <summary><c>?</c> — untracked entry.</summary>
    Untracked = 3,

    /// <summary><c>!</c> — ignored entry.</summary>
    Ignored = 4,
}

/// <summary>
/// SCM-02 — one Git status entry parsed from
/// <c>git status --porcelain=v2 -z --branch</c> (spec 13 §6/§8).
///
/// The entry deliberately keeps the index side (<see cref="IndexChange"/>) and
/// the working-tree side (<see cref="WorkingTreeChange"/>) separate, so staged
/// and unstaged changes for the same file remain distinguishable. Paths are
/// repository-relative exactly as Git reports them (forward slashes), never
/// absolute local paths.
/// </summary>
public sealed record GitStatusEntry
{
    /// <summary>Repository-relative path exactly as Git reported it.</summary>
    public required string Path { get; init; }

    /// <summary>Previous path for rename/copy entries; otherwise <c>null</c>.</summary>
    public string? OriginalPath { get; init; }

    /// <summary>The porcelain v2 record type this entry was parsed from.</summary>
    public required GitStatusEntryType EntryType { get; init; }

    /// <summary>The index/staged side of the change (<c>X</c>); <see cref="GitChangeKind.Unmodified"/> when clean.</summary>
    public GitChangeKind IndexChange { get; init; } = GitChangeKind.Unmodified;

    /// <summary>The working-tree side of the change (<c>Y</c>); <see cref="GitChangeKind.Unmodified"/> when clean.</summary>
    public GitChangeKind WorkingTreeChange { get; init; } = GitChangeKind.Unmodified;

    /// <summary>Conflict classification for unmerged entries; <see cref="GitConflictKind.None"/> otherwise.</summary>
    public GitConflictKind ConflictKind { get; init; } = GitConflictKind.None;

    /// <summary>
    /// Two-character <c>XY</c> status for inspectability. For ordinary/renamed
    /// and unmerged records this is exactly what Git reported; for untracked
    /// and ignored records (which carry no <c>XY</c>) it follows the
    /// <c>git status --short</c> convention <c>??</c>/<c>!!</c>.
    /// </summary>
    public string StatusCharacters { get; init; } = "..";

    public bool IsUntracked => EntryType == GitStatusEntryType.Untracked;

    public bool IsIgnored => EntryType == GitStatusEntryType.Ignored;

    public bool IsUnmerged => EntryType == GitStatusEntryType.Unmerged;

    public bool IsConflicted => IsUnmerged;

    /// <summary>True when the entry has a normal index/staged change (spec 13 §8 STAGED CHANGES).</summary>
    public bool HasStagedChange =>
        !IsUntracked && !IsIgnored && !IsUnmerged && IndexChange != GitChangeKind.Unmodified;

    /// <summary>True when the entry has a normal working-tree change (spec 13 §8 CHANGES).</summary>
    public bool HasWorkingTreeChange =>
        !IsUntracked && !IsIgnored && !IsUnmerged && WorkingTreeChange != GitChangeKind.Unmodified;

    /// <summary>Overall presentation state used for grouping in the Source Control surface.</summary>
    public GitFileState State =>
        IsIgnored ? GitFileState.Ignored
        : IsUntracked ? GitFileState.Untracked
        : IsUnmerged ? GitFileState.Conflicted
        : IndexChange != GitChangeKind.Unmodified ? ToFileState(IndexChange)
        : ToFileState(WorkingTreeChange);

    private static GitFileState ToFileState(GitChangeKind kind) => kind switch
    {
        GitChangeKind.Modified => GitFileState.Modified,
        GitChangeKind.Added => GitFileState.Added,
        GitChangeKind.Deleted => GitFileState.Deleted,
        GitChangeKind.Renamed => GitFileState.Renamed,
        GitChangeKind.Copied => GitFileState.Copied,
        GitChangeKind.TypeChanged => GitFileState.TypeChanged,
        GitChangeKind.Unmerged => GitFileState.Conflicted,
        _ => GitFileState.Unmodified,
    };
}

/// <summary>
/// SCM-02 — one machine-readable status snapshot of a worktree, derived from
/// Git (spec 13 §6). This is computed data: Git remains authoritative and the
/// snapshot is never persisted as a parallel database (spec 13 §4.2/§24).
/// </summary>
public sealed record GitStatusSnapshot(
    string WorktreeRoot,
    string? BranchName,
    bool IsDetached,
    string? HeadCommit,
    string? UpstreamReference,
    int? Ahead,
    int? Behind,
    IReadOnlyList<GitStatusEntry> Entries)
{
    /// <summary>True when Git reported a configured upstream branch.</summary>
    public bool HasUpstream => !string.IsNullOrEmpty(UpstreamReference);
}

/// <summary>
/// SCM-02 — typed outcome of one status refresh. Failures are inspectable
/// statuses with the originating <see cref="GitProcessResult"/>, never silent
/// fallbacks (spec 13 §20).
/// </summary>
public sealed record GitStatusResult(
    GitRepositoryStatus Status,
    GitStatusSnapshot? Snapshot,
    GitProcessResult? Failure = null)
{
    /// <summary>True only when a parseable status snapshot is present.</summary>
    public bool IsAvailable => Status == GitRepositoryStatus.Available && Snapshot is not null;
}
