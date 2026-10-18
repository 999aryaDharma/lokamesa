using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.History;

/// <summary>
/// SCM-09 — optional Document IDE work session (spec 13 §13/§24).
///
/// A work session groups a user's activity across multiple files and
/// operations. <b>It is not a Git commit.</b> The session is Document IDE
/// History metadata: it stays usable with no Git repository at all, and the
/// repository/worktree roots are recorded only as context when Git discovery
/// happened to succeed.
/// </summary>
public class WorkSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Workspace the session belongs to when the caller has one; optional.</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>Repository root discovered when the session started; null when Git is absent.</summary>
    public string? RepositoryRoot { get; set; }

    /// <summary>Worktree root discovered when the session started; null when Git is absent.</summary>
    public string? WorktreeRoot { get; set; }

    public string StartedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public string? EndedAt { get; set; }

    public string? Label { get; set; }

    /// <summary>Documents this session changed (Document IDE History association).</summary>
    public List<WorkSessionDocument> ChangedDocuments { get; } = new();

    /// <summary>
    /// Commit ids correlated after a successful commit (spec 13 §13). Derived
    /// correlation metadata only: it never alters Git history, is never
    /// required for a commit to succeed, and may be deleted or rebuilt without
    /// making the repository unusable (spec 13 §24).
    /// </summary>
    public List<WorkSessionCommit> CommitCorrelations { get; } = new();
}

/// <summary>
/// SCM-09 — one document changed by a work session (step 2 "record
/// changed-document association"). One association per document per session.
/// </summary>
public class WorkSessionDocument
{
    public string SessionId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Path the operation reported; null when the caller has none.</summary>
    public string? Path { get; set; }

    public string RecordedAt { get; set; } = DateTime.UtcNow.ToString("O");
}

/// <summary>
/// SCM-09 — one Git commit correlated with a work session (step 3 "attach
/// successful commit ids"). The subject/short id are convenience display
/// metadata copied from the commit response so the correlation is still
/// readable when Git is later unavailable.
/// </summary>
public class WorkSessionCommit
{
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Full commit object id exactly as Git reported it; never invented here.</summary>
    public string CommitId { get; set; } = string.Empty;

    public string? ShortId { get; set; }

    public string? Summary { get; set; }

    public string RecordedAt { get; set; } = DateTime.UtcNow.ToString("O");
}

/// <summary>
/// SCM-09 — persistence boundary for work sessions and their correlations
/// (spec 13 §24). Implementations store disposable derived metadata and never
/// write into the Git repository.
/// </summary>
public interface IWorkSessionRepository
{
    Task CreateAsync(WorkSession session, CancellationToken cancellationToken = default);

    Task<WorkSession?> GetByIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or refreshes the changed-document association for one session;
    /// <c>false</c> when the session does not exist.
    /// </summary>
    Task<bool> RecordChangedDocumentAsync(WorkSessionDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or refreshes one commit correlation for a session; <c>false</c>
    /// when the session does not exist.
    /// </summary>
    Task<bool> AttachCommitAsync(WorkSessionCommit commit, CancellationToken cancellationToken = default);

    /// <summary>Drops one session and its correlations; Git is never touched.</summary>
    Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// SCM-09 — typed outcome of one correlation request (spec 13 §20/§24).
///
/// <see cref="SessionRequired"/>, <see cref="DocumentRequired"/> and
/// <see cref="CommitRequired"/> are input rejections: nothing ran.
/// <see cref="StoreUnavailable"/> is the correlation-is-optional outcome — the
/// Git commit it failed to describe stays perfectly valid.
/// </summary>
public enum WorkSessionOutcome
{
    /// <summary>The request completed; <see cref="WorkSessionResult.Session"/> carries the session.</summary>
    Succeeded = 0,

    /// <summary>A session id is required; the request was rejected before anything ran.</summary>
    SessionRequired = 1,

    /// <summary>A document id is required; the request was rejected before anything ran.</summary>
    DocumentRequired = 2,

    /// <summary>A commit id is required; the request was rejected before anything ran.</summary>
    CommitRequired = 3,

    /// <summary>There is no such work session in the correlation store.</summary>
    SessionNotFound = 4,

    /// <summary>The correlation store could not be used; the Git commit is unaffected (spec 13 §24).</summary>
    StoreUnavailable = 5,
}

/// <summary>
/// SCM-09 — typed result of one work-session/correlation request.
/// </summary>
public sealed record WorkSessionResult(
    WorkSessionOutcome Outcome,
    WorkSession? Session,
    string? Message,
    string? ErrorCode = null)
{
    /// <summary>True only when the request completed with a session.</summary>
    public bool IsSuccess => Outcome == WorkSessionOutcome.Succeeded;
}

/// <summary>
/// SCM-09 — Document IDE history ↔ Git correlation service (spec 13 §13/§24).
///
/// The service records Document IDE History (sessions, changed documents) and
/// appends Git commit ids as derived correlation metadata. It contains no Git
/// code path at all: it can never create, amend or push a commit, and a
/// correlation failure never invalidates an existing commit.
/// </summary>
public interface IWorkSessionService
{
    Task<WorkSessionResult> StartAsync(
        string? workspaceId,
        string? repositoryRoot,
        string? worktreeRoot,
        string? label,
        CancellationToken cancellationToken = default);

    Task<WorkSessionResult> RecordChangedDocumentAsync(
        string? sessionId,
        string? documentId,
        string? path,
        CancellationToken cancellationToken = default);

    Task<WorkSessionResult> AttachCommitAsync(
        string? sessionId,
        string? commitId,
        string? shortId,
        string? summary,
        CancellationToken cancellationToken = default);

    Task<WorkSessionResult> GetAsync(string? sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops one session and its correlations. Disposable derived metadata
    /// (spec 13 §24): this never touches the Git repository.
    /// </summary>
    Task<WorkSessionResult> DeleteAsync(string? sessionId, CancellationToken cancellationToken = default);
}
