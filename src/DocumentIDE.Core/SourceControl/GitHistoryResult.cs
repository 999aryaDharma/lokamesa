using System.Collections.Generic;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-08 — typed status of one read-only Git history request
/// (spec 13 §12/§20).
///
/// Git history is derived from the repository on every read; nothing is cached
/// and no Document IDE history row is consulted. A failure is an inspectable
/// status with the originating <see cref="GitProcessResult"/>, never a silent
/// empty list: an empty page means "Git reported no commits", not "the read
/// failed".
/// </summary>
public enum GitHistoryStatus
{
    /// <summary>Git answered; <see cref="GitHistoryResult.Page"/> (or <see cref="GitFileVersionResult.Version"/>) carries the data — possibly empty.</summary>
    Available = 0,

    /// <summary>The requested path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 1,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 2,

    /// <summary>The request itself is unusable (bad pagination window, missing/invalid commit id); nothing ran.</summary>
    InvalidRequest = 3,

    /// <summary>The requested document path is not a contained worktree-relative path; nothing ran.</summary>
    PathRejected = 4,

    /// <summary>The file does not exist at the requested commit (a valid, non-error answer for version inspection).</summary>
    NotFound = 5,

    /// <summary>Git ran and failed for a reason this service does not classify further; inspect <see cref="GitHistoryResult.Failure"/>.</summary>
    Failed = 6,
}

/// <summary>
/// SCM-08 — pagination window for a history read (spec 13 §25: "large history
/// views should be paginated/lazy-loaded").
/// </summary>
/// <param name="Skip">Number of matching commits to skip (the running total the client already has).</param>
/// <param name="Limit">Maximum number of commits to return in this page.</param>
public sealed record GitHistoryQuery(int Skip = 0, int Limit = GitHistoryQuery.DefaultLimit)
{
    /// <summary>Default page size used when the client does not ask for one.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Largest page this surface will serve; larger requests are rejected typed.</summary>
    public const int MaxLimit = 200;

    /// <summary>Default page-1 window.</summary>
    public static GitHistoryQuery Default { get; } = new();
}

/// <summary>
/// SCM-08 — one commit as Git reports it (spec 13 §12: commit_id, short_id,
/// author, committer_time, message, file_count).
/// </summary>
/// <param name="CommitId">Full commit object id.</param>
/// <param name="ShortId">Git's abbreviated commit id.</param>
/// <param name="AuthorName">Author name Git recorded.</param>
/// <param name="AuthorEmail">Author email Git recorded.</param>
/// <param name="CommitterTime">Committer time as Git's strict ISO-8601 (<c>%cI</c>) string; never localized.</param>
/// <param name="Summary">Commit subject (message's first line).</param>
/// <param name="FileCount">
/// Number of files Git reports for this commit. For a file-history read the
/// count is pathspec-limited, i.e. the files of that commit that match the
/// requested document — Git's own semantics for <c>log -- &lt;path&gt;</c>.
/// </param>
public sealed record GitHistoryEntry(
    string CommitId,
    string ShortId,
    string AuthorName,
    string AuthorEmail,
    string CommitterTime,
    string Summary,
    int FileCount);

/// <summary>
/// SCM-08 — one page of commits plus the window it came from, so a caller can
/// lazily append the next page without re-reading what it already has.
/// </summary>
/// <param name="Commits">Commits in Git's order (newest first).</param>
/// <param name="Skip">The window's skip, echoed for the client.</param>
/// <param name="Limit">The window's limit, echoed for the client.</param>
/// <param name="HasMore">True when Git reported more matching commits beyond this page.</param>
/// <param name="NextSkip">Skip to pass for the next page; <c>null</c> when the history is exhausted.</param>
public sealed record GitHistoryPage(
    IReadOnlyList<GitHistoryEntry> Commits,
    int Skip,
    int Limit,
    bool HasMore,
    int? NextSkip);

/// <summary>
/// SCM-08 — typed result of one repository- or file-history read
/// (spec 13 §12/§20).
/// </summary>
/// <param name="Status">Typed status vocabulary.</param>
/// <param name="Page">The commit page when the read succeeded; <c>null</c> otherwise.</param>
/// <param name="Failure">Originating typed Git failure when Git ran and failed.</param>
/// <param name="Message">Safe, human-readable context (never raw secrets).</param>
/// <param name="ErrorCode">Machine-readable rejection code for input rejections.</param>
public sealed record GitHistoryResult(
    GitHistoryStatus Status,
    GitHistoryPage? Page,
    GitProcessResult? Failure,
    string? Message = null,
    string? ErrorCode = null)
{
    /// <summary>True only when Git answered the read.</summary>
    public bool IsAvailable => Status == GitHistoryStatus.Available;
}

/// <summary>
/// SCM-08 — one document version read back from a selected commit
/// (<c>git show &lt;commit&gt;:&lt;path&gt;</c>, spec 13 §12).
///
/// The read is a projection of the committed blob, never of the working tree:
/// inspecting a version cannot change what is on disk.
/// </summary>
/// <param name="CommitId">The commit the version was read from.</param>
/// <param name="Path">Worktree-relative path as Git recorded it.</param>
/// <param name="Content">
/// Text content, or <c>null</c> for a binary blob (a byte blob is never
/// presented as text) — also truncated to the service's bound when
/// <see cref="IsTruncated"/> is true.
/// </param>
/// <param name="IsBinary">True when the blob is not text and <see cref="Content"/> is <c>null</c>.</param>
/// <param name="IsTruncated">True when the blob was larger than the served bound and <see cref="Content"/> was clipped.</param>
/// <param name="SizeBytes">The blob's true size, whether or not the content was clipped.</param>
public sealed record GitFileVersion(
    string CommitId,
    string Path,
    string? Content,
    bool IsBinary,
    bool IsTruncated,
    int SizeBytes);

/// <summary>
/// SCM-08 — typed result of one document-version inspection
/// (spec 13 §12/§20).
/// </summary>
/// <param name="Status">Typed status vocabulary.</param>
/// <param name="Version">The version when the read succeeded; <c>null</c> otherwise.</param>
/// <param name="Failure">Originating typed Git failure when Git ran and failed.</param>
/// <param name="Message">Safe, human-readable context (never raw secrets).</param>
/// <param name="ErrorCode">Machine-readable rejection code for input rejections.</param>
public sealed record GitFileVersionResult(
    GitHistoryStatus Status,
    GitFileVersion? Version,
    GitProcessResult? Failure,
    string? Message = null,
    string? ErrorCode = null)
{
    /// <summary>True only when Git answered the read.</summary>
    public bool IsAvailable => Status == GitHistoryStatus.Available;
}
