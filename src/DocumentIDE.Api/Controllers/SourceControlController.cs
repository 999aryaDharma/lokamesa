using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.History;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>Typed Git failure surface (spec 13 §20); never raw secrets.</summary>
public sealed record SourceControlFailureResponse(
    string Status,
    string ErrorKind,
    string? ErrorMessage,
    int? ExitCode);

/// <summary>
/// Read-only repository/worktree context for the Source Control pane
/// (spec 13 §5/§7). The branch value is context metadata only: there is no
/// branch-management surface in this feature.
/// </summary>
public sealed record SourceControlContextResponse(
    string Status,
    bool HasRepository,
    string? RepositoryRoot,
    string? WorktreeRoot,
    string? GitDirReference,
    string? HeadCommit,
    string? BranchName,
    bool IsDetached,
    string? UpstreamReference,
    SourceControlFailureResponse? Failure);

/// <summary>
/// One change entry as reported by Git (spec 13 §6/§8). <c>Path</c> is
/// worktree-relative exactly as Git reported it; <c>AbsolutePath</c> is the
/// contained local path (null when it cannot be safely resolved) and
/// <c>DocumentId</c> links to an already-registered document so the existing
/// opening/inspection flows (Word, Markdown, Quick Look) can be reused.
/// </summary>
public sealed record SourceControlChangeResponse(
    string Path,
    string? OriginalPath,
    string State,
    string IndexChange,
    string WorkingTreeChange,
    string ConflictKind,
    string StatusCharacters,
    string? AbsolutePath,
    string? DocumentId);

/// <summary>
/// Changes grouped by Git state (spec 13 §8): staged, unstaged ("CHANGES"),
/// untracked and conflicts are deliberately separate. A file with both an
/// index and a working-tree change appears in both staged and unstaged
/// groups, exactly like Git reports it.
/// </summary>
public sealed record SourceControlSectionsResponse(
    IReadOnlyList<SourceControlChangeResponse> Staged,
    IReadOnlyList<SourceControlChangeResponse> Unstaged,
    IReadOnlyList<SourceControlChangeResponse> Untracked,
    IReadOnlyList<SourceControlChangeResponse> Conflicts);

/// <summary>
/// Changes-pane payload (spec 13 §7): grouped change sections plus read-only
/// branch/worktree context and ahead/behind counters. When no upstream is
/// configured the response reports that explicitly (null counters and
/// <c>HasUpstream=false</c>) instead of pretending 0/0 (spec 13 §14.2).
/// Remote actions (fetch/pull/push) are not part of this surface.
/// </summary>
public sealed record SourceControlStatusResponse(
    string Status,
    bool HasRepository,
    string? WorktreeRoot,
    string? BranchName,
    bool IsDetached,
    string? HeadCommit,
    string? UpstreamReference,
    bool HasUpstream,
    int? Ahead,
    int? Behind,
    SourceControlSectionsResponse Sections,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-06 — request body for the single-file stage/unstage actions.
/// <c>Path</c> is the workspace folder or active-document path used to
/// discover the repository; <c>ChangePath</c> is the worktree-relative path
/// exactly as Git reported it in the status pane (treated as untrusted input
/// and validated by the stage service before Git runs).
/// </summary>
public sealed record SourceControlStageRequest(string? Path, string? ChangePath);

/// <summary>
/// SCM-06 — request body for the whole-worktree stage-all/unstage-all
/// actions. <c>Path</c> is the workspace folder or active-document path used
/// to discover the repository; the mutation is scoped to exactly that
/// resolved worktree.
/// </summary>
public sealed record SourceControlStageAllRequest(string? Path);

/// <summary>
/// SCM-06 — typed stage/unstage outcome (spec 13 §10/§20). On success
/// <c>Status</c> carries the status re-read from Git after the mutation, so
/// the pane renders the new state from the mutation response and never keeps
/// a proprietary staging area; failures stay typed and inspectable.
/// </summary>
public sealed record SourceControlStageResponse(
    string Outcome,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    SourceControlStatusResponse? Status,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-07 — request body for the Commit / Commit All actions. <c>Path</c> is
/// the workspace folder or active-document path used to discover the
/// repository; <c>Message</c> is the required commit message, passed to Git as
/// a structured argument and never concatenated into a shell string
/// (spec 13 §11.1/§18).
/// </summary>
public sealed record SourceControlCommitRequest(string? Path, string? Message);

/// <summary>
/// SCM-07 — identity and subject of the commit that was created
/// (spec 13 §11.1 step 6). <c>BranchName</c> is <c>null</c> on a detached HEAD.
/// </summary>
public sealed record SourceControlCommitInfoResponse(
    string CommitId,
    string ShortId,
    string Summary,
    string? BranchName);

/// <summary>
/// SCM-07 — typed Commit / Commit All outcome (spec 13 §11/§20).
///
/// On success <c>Commit</c> carries the new commit identifier and <c>Status</c>
/// the status re-read from Git after the commit (the real Git index is the only
/// staging authority). <c>Flow</c> reports which flow ran; for Commit All
/// <c>StagedPaths</c> lists the paths that flow staged. Failures stay typed and
/// inspectable; the commit path never pushes anywhere (spec 13 §14.4/§23).
/// </summary>
public sealed record SourceControlCommitResponse(
    string Outcome,
    string Flow,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    SourceControlCommitInfoResponse? Commit,
    IReadOnlyList<string>? StagedPaths,
    SourceControlStatusResponse? Status,
    SourceControlFailureResponse? Failure);

// -------------------------------------------------------------------------
// SCM-08 — Git commit/file history and document-version payloads (spec 13
// §9.4/§12/§20/§25)
// -------------------------------------------------------------------------

/// <summary>SCM-08 — one commit as Git reports it (spec 13 §12).</summary>
public sealed record SourceControlHistoryCommitResponse(
    string CommitId,
    string ShortId,
    string AuthorName,
    string AuthorEmail,
    string CommitterTime,
    string Summary,
    int FileCount);

/// <summary>
/// SCM-08 — one page of commits plus the window it came from, so the pane can
/// lazily append the next page (spec 13 §25).
/// </summary>
public sealed record SourceControlHistoryPageResponse(
    IReadOnlyList<SourceControlHistoryCommitResponse> Commits,
    int Skip,
    int Limit,
    bool HasMore,
    int? NextSkip);

/// <summary>
/// SCM-08 — typed history payload (spec 13 §12/§20). <c>Source</c> is always
/// <c>"Git"</c>: it marks the data as Git-derived so the pane keeps Git History
/// visibly separate from Document IDE History (spec 13 §4.3/§12).
/// </summary>
public sealed record SourceControlHistoryResponse(
    string Status,
    string Source,
    string? ErrorCode,
    string? Message,
    bool IsAvailable,
    SourceControlHistoryPageResponse? Page,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-08 — one document version read back from a commit (spec 13 §12).
/// <c>Content</c> is <c>null</c> for a binary blob or for a blob larger than the
/// served bound (which is never read); <c>SizeBytes</c> is always the true size.
/// </summary>
public sealed record SourceControlFileVersionInfoResponse(
    string CommitId,
    string Path,
    string? Content,
    bool IsBinary,
    bool IsTruncated,
    long SizeBytes);

/// <summary>SCM-08 — typed document-version payload (spec 13 §12/§20).</summary>
public sealed record SourceControlFileVersionResponse(
    string Status,
    string Source,
    string? ErrorCode,
    string? Message,
    bool IsAvailable,
    SourceControlFileVersionInfoResponse? Version,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-09 — request body that starts a Document IDE work session (spec 13
/// §13/§24). <c>Path</c> is the workspace folder or active-document path used
/// only for a read-only Git discovery so the session can record repository
/// context; the session itself is Document IDE History metadata and stays valid
/// with no Git repository at all.
/// </summary>
public sealed record SourceControlWorkSessionRequest(string? Path, string? WorkspaceId, string? Label);

/// <summary>
/// SCM-09 — request body that records one changed document against a session.
/// <c>DocumentId</c> is required (a blank id is a typed 400); <c>Path</c> is the
/// path the operation reported and is optional display metadata.
/// </summary>
public sealed record SourceControlWorkSessionDocumentRequest(string? DocumentId, string? Path);

/// <summary>
/// SCM-09 — request body that correlates one already-created Git commit with a
/// session. <c>CommitId</c> is required (a blank id is a typed 400); the short
/// id and summary are convenience display metadata copied from the commit
/// response, never invented by this surface and never written into Git.
/// </summary>
public sealed record SourceControlWorkSessionCommitRequest(string? CommitId, string? ShortId, string? Summary);

/// <summary>SCM-09 — one changed-document association in a session payload.</summary>
public sealed record SourceControlWorkSessionDocumentResponse(string DocumentId, string? Path);

/// <summary>
/// SCM-09 — one correlated commit in a session payload. It is derived metadata:
/// the id is exactly what Git reported at commit time and is never required for
/// the commit to remain valid (spec 13 §24).
/// </summary>
public sealed record SourceControlWorkSessionCommitResponse(string CommitId, string? ShortId, string? Summary);

/// <summary>
/// SCM-09 — the Document IDE History side of the correlation payload: the
/// session plus the documents it changed. It is kept deliberately separate from
/// any Git history so the pane renders a correlation, not a unified timeline.
/// </summary>
public sealed record SourceControlWorkSessionResponse(
    string Id,
    string? WorkspaceId,
    string? RepositoryRoot,
    string? WorktreeRoot,
    string StartedAt,
    string? EndedAt,
    string? Label,
    IReadOnlyList<SourceControlWorkSessionDocumentResponse> ChangedDocuments,
    IReadOnlyList<SourceControlWorkSessionCommitResponse> CommitCorrelations);

/// <summary>
/// SCM-09 — typed correlation outcome (spec 13 §20/§24). Input rejections
/// (<c>SessionRequired</c>/<c>DocumentRequired</c>/<c>CommitRequired</c>) are
/// typed 400s where nothing ran; every execution outcome — including
/// <c>SessionNotFound</c> and <c>StoreUnavailable</c> — stays HTTP 200 with this
/// body so the pane renders it and never mistakes a correlation problem for a
/// broken commit.
/// </summary>
public sealed record SourceControlCorrelationResponse(
    string Outcome,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    SourceControlWorkSessionResponse? Session);

/// <summary>
/// SCM-11 — upstream/tracking state of one worktree (spec 13 §14.1/§14.2):
/// the configured upstream reference and remote name plus ahead/behind counts.
///
/// When the current branch has <b>no configured upstream</b> the response
/// reports the explicit typed status <c>NoUpstream</c> with <c>Ahead</c>/
/// <c>Behind</c> <c>null</c> — never a fake <c>0/0</c> (spec 13 §14.2). Remote
/// actions stay out of this surface.
/// </summary>
public sealed record SourceControlRemoteResponse(
    string Status,
    bool HasRepository,
    string? WorktreeRoot,
    string? BranchName,
    bool IsDetached,
    string? HeadCommit,
    string? RemoteName,
    string? UpstreamReference,
    bool HasUpstream,
    int? Ahead,
    int? Behind,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-11 — request body for the explicit Fetch action. <c>Path</c> is the
/// workspace folder or active-document path used to discover the repository;
/// the fetch runs against exactly that resolved worktree root (spec 13 §19).
/// </summary>
public sealed record SourceControlFetchRequest(string? Path);

/// <summary>
/// SCM-11 — typed Fetch outcome (spec 13 §14.1/§14.2/§20). On success
/// <c>Remote</c> carries the tracking state re-read from Git after the fetch
/// (so the pane renders the updated ahead/behind); a branch with no configured
/// upstream is the first-class <c>NoUpstream</c> outcome and remote failures
/// stay typed and inspectable. A fetch never changes document contents or the
/// index and never pulls/pushes/merges.
/// </summary>
public sealed record SourceControlFetchResponse(
    string Outcome,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    SourceControlRemoteResponse? Remote,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-03/SCM-06/SCM-07/SCM-09/SCM-11 - privileged Source Control API consumed by the
/// changes pane (spec 13 §18): read-only repository/status queries, explicit
/// file-level stage/unstage actions, the explicit Commit / Commit All actions,
/// the SCM-09 Document IDE History correlation surface and read-only
/// remote-state plus the explicit Fetch action. Every route
/// inherits the localhost session-token guard from <c>PrivilegedRequestGuard</c>;
/// there is no unauthenticated path and there is still no branch-management
/// surface.
/// </summary>
[ApiController]
[Route("api/source-control")]
public sealed class SourceControlController : ControllerBase
{
    private static readonly SourceControlSectionsResponse EmptySections = new(
        Array.Empty<SourceControlChangeResponse>(),
        Array.Empty<SourceControlChangeResponse>(),
        Array.Empty<SourceControlChangeResponse>(),
        Array.Empty<SourceControlChangeResponse>());

    private readonly GitRepositoryService _repositories;
    private readonly GitStageService _stages;
    private readonly GitCommitService _commits;
    private readonly GitRemoteService _remotes;
    private readonly GitPushService _pushes;
    private readonly GitPullService _pulls;
    private readonly GitHistoryService _history;
    private readonly GitRestoreService _restores;
    private readonly IDocumentRepository _documents;
    private readonly IWorkSessionService _workSessions;

    public SourceControlController(
        GitRepositoryService repositories,
        GitStageService stages,
        GitCommitService commits,
        GitRemoteService remotes,
        GitPushService pushes,
        GitHistoryService history,
        GitRestoreService restores,
        IDocumentRepository documents,
        IWorkSessionService workSessions,
        GitPullService pulls)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
        _remotes = remotes ?? throw new ArgumentNullException(nameof(remotes));
        _pushes = pushes ?? throw new ArgumentNullException(nameof(pushes));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _restores = restores ?? throw new ArgumentNullException(nameof(restores));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _workSessions = workSessions ?? throw new ArgumentNullException(nameof(workSessions));
        _pulls = pulls ?? throw new ArgumentNullException(nameof(pulls));
    }

    /// <summary>
    /// Read-only repository/worktree context for a workspace or active-document
    /// path (spec 13 §5). A path outside Git stays a valid source and yields a
    /// typed <c>NoRepository</c> state rather than an error status.
    /// </summary>
    [HttpGet("context")]
    public async Task<IActionResult> GetContext(
        [FromQuery] string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new
            {
                errorCode = "PathRequired",
                error = "The 'path' query parameter (workspace folder or active document) is required.",
            });
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(path, cancellationToken);
        return Ok(ToContextResponse(context));
    }

    /// <summary>
    /// Current Git state grouped by staged/unstaged/untracked/conflict
    /// sections (spec 13 §7/§8). Every call re-derives status from Git; no
    /// cached snapshot is served, so a refresh always shows the current state.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(
        [FromQuery] string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathRequiredBadRequest("query parameter");
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(path, cancellationToken);
        if (!context.HasRepository)
        {
            return Ok(FailureStatus(
                context.Status.ToString(),
                null,
                context.BranchName,
                context.IsDetached,
                context.HeadCommit,
                context.UpstreamReference,
                context.Failure));
        }

        GitStatusResult status = await _repositories.GetStatusAsync(context.WorktreeRoot!, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            return Ok(FailureStatus(
                status.Status.ToString(),
                context.WorktreeRoot,
                context.BranchName,
                context.IsDetached,
                context.HeadCommit,
                context.UpstreamReference,
                status.Failure));
        }

        return Ok(await MapSnapshotAsync(status.Snapshot, cancellationToken));
    }

    // ---------------------------------------------------------------------
    // SCM-06 — stage/unstage actions (spec 13 §9.4/§10/§19)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Stages one worktree-relative file (<c>git add</c>, spec 13 §9.4).
    /// Success returns the status re-read from Git; a non-contained path is a
    /// typed 400 and Git never runs.
    /// </summary>
    [HttpPost("stage")]
    public Task<IActionResult> Stage(
        [FromBody] SourceControlStageRequest? request,
        CancellationToken cancellationToken) =>
        RunFileActionAsync(request, stage: true, cancellationToken);

    /// <summary>
    /// Unstages one worktree-relative file (<c>git restore --staged</c>,
    /// spec 13 §9.4). Working-tree edits are never touched.
    /// </summary>
    [HttpPost("unstage")]
    public Task<IActionResult> Unstage(
        [FromBody] SourceControlStageRequest? request,
        CancellationToken cancellationToken) =>
        RunFileActionAsync(request, stage: false, cancellationToken);

    /// <summary>
    /// Stages every eligible change in the resolved worktree
    /// (<c>git add --all</c>, spec 13 §9.4/§11.2): modified, deleted and
    /// untracked-not-ignored files only.
    /// </summary>
    [HttpPost("stage-all")]
    public Task<IActionResult> StageAll(
        [FromBody] SourceControlStageAllRequest? request,
        CancellationToken cancellationToken) =>
        RunAllActionAsync(request, stage: true, cancellationToken);

    /// <summary>
    /// Unstages every staged path in the resolved worktree
    /// (<c>git restore --staged</c>, spec 13 §9.4); working-tree content is
    /// never touched.
    /// </summary>
    [HttpPost("unstage-all")]
    public Task<IActionResult> UnstageAll(
        [FromBody] SourceControlStageAllRequest? request,
        CancellationToken cancellationToken) =>
        RunAllActionAsync(request, stage: false, cancellationToken);

    // ---------------------------------------------------------------------
    // SCM-07 — commit actions (spec 13 §11/§19/§20)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Commits the current staged state (<c>git commit -m &lt;message&gt;</c>,
    /// spec 13 §11.1). Unstaged-only changes are never silently included and
    /// there is no automatic push. A blank message is a typed 400 and Git never
    /// runs; other failures stay HTTP 200 with the typed outcome.
    /// </summary>
    [HttpPost("commit")]
    public Task<IActionResult> Commit(
        [FromBody] SourceControlCommitRequest? request,
        CancellationToken cancellationToken) =>
        RunCommitActionAsync(request, GitCommitFlow.Commit, cancellationToken);

    /// <summary>
    /// Stages every eligible change and commits it (<c>git add --all</c> then
    /// <c>git commit -m &lt;message&gt;</c>, spec 13 §11.2) — an explicitly
    /// distinct flow from <see cref="Commit"/>. The response reports the flow it
    /// ran and the paths it staged.
    /// </summary>
    [HttpPost("commit-all")]
    public Task<IActionResult> CommitAll(
        [FromBody] SourceControlCommitRequest? request,
        CancellationToken cancellationToken) =>
        RunCommitActionAsync(request, GitCommitFlow.CommitAll, cancellationToken);

    private async Task<IActionResult> RunCommitActionAsync(
        SourceControlCommitRequest? request,
        GitCommitFlow flow,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitCommitResult result = flow == GitCommitFlow.CommitAll
            ? await _commits.CommitAllAsync(request.Path!, request.Message, cancellationToken)
            : await _commits.CommitAsync(request.Path!, request.Message, cancellationToken);
        return await ToCommitResponseAsync(result, cancellationToken);
    }

    /// <summary>
    /// Maps a typed commit result onto the privileged API contract
    /// (spec 13 §11/§20): a rejected message is a typed 400 (nothing ran),
    /// every other outcome stays HTTP 200 so the pane can render it, and a
    /// success carries the new commit identity plus the refreshed status.
    /// </summary>
    private async Task<IActionResult> ToCommitResponseAsync(
        GitCommitResult result,
        CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case GitCommitOutcome.MessageRequired:
            case GitCommitOutcome.MessageInvalid:
                return BadRequest(new
                {
                    errorCode = result.ErrorCode ?? "MessageRequired",
                    error = result.Message,
                });

            case GitCommitOutcome.Succeeded:
                GitStatusResult refreshed = result.Status!;
                SourceControlStatusResponse? status = refreshed.IsAvailable && refreshed.Snapshot is not null
                    ? await MapSnapshotAsync(refreshed.Snapshot, cancellationToken)
                    : null;

                return Ok(new SourceControlCommitResponse(
                    result.Outcome.ToString(),
                    result.Flow.ToString(),
                    true,
                    null,
                    null,
                    ToCommitInfo(result.Commit),
                    result.StagedPaths,
                    status,
                    // A status read-back that failed is surfaced typed rather
                    // than pretending a clean state.
                    ToFailure(refreshed.IsAvailable ? null : refreshed.Failure)));

            default:
                return Ok(new SourceControlCommitResponse(
                    result.Outcome.ToString(),
                    result.Flow.ToString(),
                    false,
                    result.ErrorCode,
                    result.Message,
                    null,
                    result.StagedPaths,
                    null,
                    ToFailure(result.Failure)));
        }
    }

    private static SourceControlCommitInfoResponse? ToCommitInfo(GitCommitInfo? info) =>
        info is null
            ? null
            : new SourceControlCommitInfoResponse(
                info.CommitId,
                info.ShortId,
                info.Summary,
                info.BranchName);

    // ---------------------------------------------------------------------
    // SCM-11 — remote state & fetch (spec 13 §14.1/§14.2/§19/§20)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Reads the worktree's upstream/tracking state (spec 13 §14.1/§14.2): the
    /// configured upstream reference and remote name plus ahead/behind counts.
    /// A branch with no configured upstream is the explicit typed
    /// <c>NoUpstream</c> status with null counters, never a fake <c>0/0</c>; a
    /// path outside Git stays a valid source and yields the typed
    /// <c>NoRepository</c> status with HTTP 200. A missing path is a typed 400
    /// and nothing runs.
    /// </summary>
    [HttpGet("remote")]
    public async Task<IActionResult> GetRemote(
        [FromQuery] string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathRequiredBadRequest("query parameter");
        }

        GitRemoteStateResult result = await _remotes.GetRemoteStateAsync(path, cancellationToken);
        return Ok(ToRemoteResponse(result));
    }

    /// <summary>
    /// Runs one explicit Fetch (<c>git fetch</c>, spec 13 §14.1) against the
    /// resolved worktree root and returns the tracking state re-read from Git.
    /// Fetch updates only remote-tracking state — document contents and the
    /// index are never touched (spec 13 §14.1). A branch with no configured
    /// upstream is the typed <c>NoUpstream</c> outcome and runs no fetch; remote
    /// failures (unreachable host, authentication required, rejected, permission
    /// denied, Git missing) stay HTTP 200 with the typed outcome so the pane can
    /// render them. A missing request path is a typed 400 and Git never runs.
    /// </summary>
    [HttpPost("fetch")]
    public async Task<IActionResult> Fetch(
        [FromBody] SourceControlFetchRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitFetchResult result = await _remotes.FetchAsync(request.Path!, cancellationToken);
        return Ok(ToFetchResponse(result));
    }

    /// <summary>
    /// Maps a typed remote-state result onto the API contract (spec 13
    /// §14.1/§14.2): the state is present for both resolved states (upstream
    /// present or absent) and <c>NoUpstream</c> stays a non-error status with
    /// null counters; failures keep their typed status and failure payload.
    /// </summary>
    private static SourceControlRemoteResponse ToRemoteResponse(GitRemoteStateResult result)
    {
        if (result.State is not GitRemoteState state)
        {
            return new SourceControlRemoteResponse(
                result.Outcome.ToString(),
                false,
                null,
                null,
                false,
                null,
                null,
                null,
                false,
                null,
                null,
                ToFailure(result.Failure));
        }

        return new SourceControlRemoteResponse(
            result.Outcome.ToString(),
            true,
            state.WorktreeRoot,
            state.BranchName,
            state.IsDetached,
            state.HeadCommit,
            state.RemoteName,
            state.UpstreamReference,
            state.HasUpstream,
            state.Ahead,
            state.Behind,
            null);
    }

    /// <summary>
    /// Maps the tracking state carried by a fetch result; the status is
    /// <c>Available</c> with an upstream and the explicit <c>NoUpstream</c>
    /// without one (spec 13 §14.2).
    /// </summary>
    private static SourceControlRemoteResponse? ToRemoteResponse(GitRemoteState? state) =>
        state is null
            ? null
            : new SourceControlRemoteResponse(
                state.HasUpstream ? "Available" : "NoUpstream",
                true,
                state.WorktreeRoot,
                state.BranchName,
                state.IsDetached,
                state.HeadCommit,
                state.RemoteName,
                state.UpstreamReference,
                state.HasUpstream,
                state.Ahead,
                state.Behind,
                null);

    /// <summary>
    /// Maps a typed fetch result onto the API contract (spec 13 §14.1/§20):
    /// success carries the refreshed tracking state; every failure keeps its
    /// typed outcome, code and originating failure and stays HTTP 200.
    /// </summary>
    private static SourceControlFetchResponse ToFetchResponse(GitFetchResult result) =>
        new(
            result.Outcome.ToString(),
            result.IsSuccess,
            result.ErrorCode,
            result.Message,
            ToRemoteResponse(result.Remote),
            ToFailure(result.Failure));

    private async Task<IActionResult> RunFileActionAsync(
        SourceControlStageRequest? request,
        bool stage,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitStageResult result = stage
            ? await _stages.StageAsync(request.Path!, request.ChangePath, cancellationToken)
            : await _stages.UnstageAsync(request.Path!, request.ChangePath, cancellationToken);
        return await ToStageResponseAsync(result, cancellationToken);
    }

    private async Task<IActionResult> RunAllActionAsync(
        SourceControlStageAllRequest? request,
        bool stage,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitStageResult result = stage
            ? await _stages.StageAllAsync(request.Path!, cancellationToken)
            : await _stages.UnstageAllAsync(request.Path!, cancellationToken);
        return await ToStageResponseAsync(result, cancellationToken);
    }

    /// <summary>
    /// Maps a typed stage/unstage result onto the privileged API contract:
    /// rejected paths are typed 400s (nothing ran); non-repository and Git
    /// failures stay HTTP 200 with the typed outcome/failure so the pane can
    /// render them; success carries the refreshed status payload.
    /// </summary>
    private async Task<IActionResult> ToStageResponseAsync(
        GitStageResult result,
        CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case GitStageOutcome.RejectedPath:
                return BadRequest(new
                {
                    errorCode = result.ErrorCode ?? "PathRejected",
                    error = result.Message,
                });

            case GitStageOutcome.Succeeded:
                GitStatusResult refreshed = result.Status!;
                if (refreshed.IsAvailable && refreshed.Snapshot is not null)
                {
                    return Ok(new SourceControlStageResponse(
                        result.Outcome.ToString(),
                        true,
                        null,
                        null,
                        await MapSnapshotAsync(refreshed.Snapshot, cancellationToken),
                        null));
                }

                // The index changed but the follow-up status read failed; the
                // typed failure is surfaced instead of pretending a clean state.
                return Ok(new SourceControlStageResponse(
                    result.Outcome.ToString(),
                    true,
                    null,
                    result.Message,
                    null,
                    ToFailure(refreshed.Failure)));

            default:
                return Ok(new SourceControlStageResponse(
                    result.Outcome.ToString(),
                    false,
                    null,
                    result.Message,
                    null,
                    ToFailure(result.Failure)));
        }
    }

    private static IActionResult PathRequiredBadRequest(string source) =>
        new BadRequestObjectResult(new
        {
            errorCode = "PathRequired",
            error = $"The 'path' {source} (workspace folder or active document) is required.",
        });

    /// <summary>Builds the empty-sections typed-failure status payload.</summary>
    private static SourceControlStatusResponse FailureStatus(
        string status,
        string? worktreeRoot,
        string? branchName,
        bool isDetached,
        string? headCommit,
        string? upstreamReference,
        GitProcessResult? failure) =>
        new(
            status,
            false,
            worktreeRoot,
            branchName,
            isDetached,
            headCommit,
            upstreamReference,
            upstreamReference is not null,
            null,
            null,
            EmptySections,
            ToFailure(failure));

    /// <summary>
    /// Builds the full status payload from one Git status snapshot, including
    /// the grouped change sections (spec 13 §7/§8).
    /// </summary>
    private async Task<SourceControlStatusResponse> MapSnapshotAsync(
        GitStatusSnapshot snapshot,
        CancellationToken cancellationToken) =>
        new(
            "Available",
            true,
            snapshot.WorktreeRoot,
            snapshot.BranchName,
            snapshot.IsDetached,
            snapshot.HeadCommit,
            snapshot.UpstreamReference,
            snapshot.HasUpstream,
            snapshot.Ahead,
            snapshot.Behind,
            new SourceControlSectionsResponse(
                await MapEntriesAsync(snapshot, entry => entry.HasStagedChange, cancellationToken),
                await MapEntriesAsync(snapshot, entry => entry.HasWorkingTreeChange, cancellationToken),
                await MapEntriesAsync(snapshot, entry => entry.IsUntracked, cancellationToken),
                await MapEntriesAsync(snapshot, entry => entry.IsConflicted, cancellationToken)),
            null);

    private static SourceControlContextResponse ToContextResponse(GitRepositoryContext context) =>
        new(
            context.Status.ToString(),
            context.HasRepository,
            context.RepositoryRoot,
            context.WorktreeRoot,
            context.GitDirReference,
            context.HeadCommit,
            context.BranchName,
            context.IsDetached,
            context.UpstreamReference,
            ToFailure(context.Failure));

    private async Task<IReadOnlyList<SourceControlChangeResponse>> MapEntriesAsync(
        GitStatusSnapshot snapshot,
        Func<GitStatusEntry, bool> inGroup,
        CancellationToken cancellationToken)
    {
        var mapped = new List<SourceControlChangeResponse>();
        foreach (GitStatusEntry entry in snapshot.Entries.Where(inGroup))
        {
            string? absolutePath = TryResolveWithinWorktree(snapshot.WorktreeRoot, entry.Path);
            string? documentId = null;
            if (absolutePath is not null)
            {
                var document = await _documents.GetByPathAsync(absolutePath);
                documentId = document?.Id;
            }

            mapped.Add(new SourceControlChangeResponse(
                entry.Path,
                entry.OriginalPath,
                entry.State.ToString(),
                entry.IndexChange.ToString(),
                entry.WorkingTreeChange.ToString(),
                entry.ConflictKind.ToString(),
                entry.StatusCharacters,
                absolutePath,
                documentId));
        }

        return mapped;
    }

    /// <summary>
    /// Combines a Git-reported worktree-relative path with the worktree root.
    /// Git output is treated as untrusted input (spec 13 §23): a path that
    /// resolves outside the worktree yields <c>null</c> instead of exposing an
    /// escaped local path.
    /// </summary>
    private static string? TryResolveWithinWorktree(string worktreeRoot, string relativePath)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreeRoot));
            string combined = Path.GetFullPath(
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!string.Equals(combined, root, StringComparison.OrdinalIgnoreCase)
                && !combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return combined;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static SourceControlFailureResponse? ToFailure(GitProcessResult? failure) =>
        failure is null
            ? null
            : new SourceControlFailureResponse(
                failure.Status.ToString(),
                failure.ErrorKind.ToString(),
                failure.ErrorMessage,
                failure.ExitCode);

    // =====================================================================
    // SCM-09 — Document IDE History ↔ Git correlation (spec 13 §13/§24)
    //
    // Bounded region: this surface owns no Git mutation at all. Starting a
    // session performs only a read-only Git discovery to record repository
    // context; a session, its changed documents and its commit correlations are
    // derived Document IDE History metadata that stay usable without Git, and
    // no route here can create, amend or push a commit.
    // =====================================================================

    /// <summary>
    /// Starts a Document IDE work session (spec 13 §13). The Git context is
    /// discovered read-only; when the path is outside Git the session still
    /// starts with no repository context and Git is never initialised.
    /// </summary>
    [HttpPost("work-sessions")]
    public async Task<IActionResult> StartWorkSession(
        [FromBody] SourceControlWorkSessionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        // Read-only context discovery: the correlation surface never initialises
        // Git and never mutates the repository.
        GitRepositoryContext context = await _repositories.DiscoverAsync(request.Path!, cancellationToken);

        return ToCorrelationResponse(await _workSessions.StartAsync(
            request.WorkspaceId,
            context.HasRepository ? context.RepositoryRoot : null,
            context.HasRepository ? context.WorktreeRoot : null,
            request.Label,
            cancellationToken));
    }

    /// <summary>Reads one work session and its correlation metadata.</summary>
    [HttpGet("work-sessions/{sessionId}")]
    public async Task<IActionResult> GetWorkSession(
        string? sessionId,
        CancellationToken cancellationToken) =>
        ToCorrelationResponse(await _workSessions.GetAsync(sessionId, cancellationToken));

    /// <summary>
    /// Records one document as changed by a session (Document IDE History
    /// association, spec 13 §13). A blank document id is a typed 400 and nothing
    /// is written.
    /// </summary>
    [HttpPost("work-sessions/{sessionId}/documents")]
    public async Task<IActionResult> RecordWorkSessionDocument(
        string? sessionId,
        [FromBody] SourceControlWorkSessionDocumentRequest? request,
        CancellationToken cancellationToken) =>
        ToCorrelationResponse(await _workSessions.RecordChangedDocumentAsync(
            sessionId,
            request?.DocumentId,
            request?.Path,
            cancellationToken));

    /// <summary>
    /// Correlates an already-created Git commit id with a session (spec 13 §13).
    /// The commit was created by the explicit SCM-07 flow; this route only
    /// records derived correlation metadata and never touches Git. A blank
    /// commit id is a typed 400.
    /// </summary>
    [HttpPost("work-sessions/{sessionId}/commit-correlations")]
    public async Task<IActionResult> CorrelateWorkSessionCommit(
        string? sessionId,
        [FromBody] SourceControlWorkSessionCommitRequest? request,
        CancellationToken cancellationToken) =>
        ToCorrelationResponse(await _workSessions.AttachCommitAsync(
            sessionId,
            request?.CommitId,
            request?.ShortId,
            request?.Summary,
            cancellationToken));

    /// <summary>
    /// Drops one session and its correlation metadata (spec 13 §24). The Git
    /// commit it described is untouched and stays valid.
    /// </summary>
    [HttpDelete("work-sessions/{sessionId}")]
    public async Task<IActionResult> DeleteWorkSession(
        string? sessionId,
        CancellationToken cancellationToken) =>
        ToCorrelationResponse(await _workSessions.DeleteAsync(sessionId, cancellationToken));

    /// <summary>
    /// Maps a typed work-session/correlation result onto the privileged API
    /// contract (spec 13 §20/§24): input rejections are typed 400s (nothing
    /// ran), every execution outcome stays HTTP 200 with a typed body so the
    /// pane renders it, and a correlation failure is never conflated with a
    /// broken Git commit.
    /// </summary>
    private static IActionResult ToCorrelationResponse(WorkSessionResult result) =>
        result.Outcome is WorkSessionOutcome.SessionRequired
            or WorkSessionOutcome.DocumentRequired
            or WorkSessionOutcome.CommitRequired
            ? new BadRequestObjectResult(new
            {
                errorCode = result.ErrorCode ?? result.Outcome.ToString(),
                error = result.Message,
            })
            : new OkObjectResult(new SourceControlCorrelationResponse(
                result.Outcome.ToString(),
                result.IsSuccess,
                result.ErrorCode,
                result.Message,
                ToWorkSessionResponse(result.Session)));

    private static SourceControlWorkSessionResponse? ToWorkSessionResponse(WorkSession? session) =>
        session is null
            ? null
            : new SourceControlWorkSessionResponse(
                session.Id,
                session.WorkspaceId,
                session.RepositoryRoot,
                session.WorktreeRoot,
                session.StartedAt,
                session.EndedAt,
                session.Label,
                session.ChangedDocuments
                    .Select(document => new SourceControlWorkSessionDocumentResponse(
                        document.DocumentId,
                        document.Path))
                    .ToList(),
                session.CommitCorrelations
                    .Select(correlation => new SourceControlWorkSessionCommitResponse(
                        correlation.CommitId,
                        correlation.ShortId,
                        correlation.Summary))
                    .ToList());

    // ---------------------------------------------------------------------
    // SCM-13 — explicit push (spec 13 §14.4/§18/§19/§20)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Runs one explicit Push (<c>git push</c>, spec 13 §14.4) against the
    /// resolved worktree and returns the tracking state re-read from Git. The
    /// push is scoped to the current branch's configured upstream: a branch with
    /// no configured upstream is the typed <c>NoUpstream</c> outcome and runs no
    /// Git at all — never a push to a guessed default remote (spec 13
    /// §14.2/§14.4). Push is always user initiated and carries no
    /// force/refspec/scope-broadening argument (spec 13 §14.4/§23). Every remote
    /// failure (rejection, authentication, permission, unreachable host, Git
    /// missing) stays HTTP 200 with a typed outcome so the pane can render it,
    /// and a rejected push never rewrites local history. A missing request path
    /// is a typed 400 and Git never runs.
    /// </summary>
    [HttpPost("push")]
    public async Task<IActionResult> Push(
        [FromBody] SourceControlPushRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitPushResult result = await _pushes.PushAsync(request.Path!, cancellationToken);
        return Ok(ToPushResponse(result));
    }

    /// <summary>
    /// Maps a typed push result onto the API contract (spec 13 §14.4/§20):
    /// success carries the tracking state re-read from Git after the push; a
    /// branch with no configured upstream carries that explicit state; every
    /// failure keeps its typed outcome, code and originating failure and stays
    /// HTTP 200. A push never changes document contents or the index.
    /// </summary>
    private static SourceControlPushResponse ToPushResponse(GitPushResult result) =>
        new(
            result.Outcome.ToString(),
            result.IsSuccess,
            result.ErrorCode,
            result.Message,
            ToRemoteResponse(result.Remote),
            ToFailure(result.Failure));

    // ---------------------------------------------------------------------
    // SCM-12 — conservative fast-forward pull
    // (spec 13 §14.3/§18/§19/§20/§21/§23)
    //
    // Bounded append-only region: Pull is a POST that never merges and never
    // rebases. The preflight (dirty/conflicted) and divergence outcomes are
    // typed blocking states returned with HTTP 200 so the pane can render them;
    // a missing request path is a typed 400 and Git never runs.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Runs one conservative Pull (<c>git pull --ff-only --no-rebase</c>, spec 13
    /// §14.3) against the resolved worktree and returns the typed outcome plus
    /// the refreshed status. A dirty or conflicted worktree is refused typed
    /// before Git runs; diverged history is the typed blocking <c>Diverged</c>
    /// outcome with no merge commit and no rebase; a branch with no configured
    /// upstream runs no Git at all. Every execution outcome stays HTTP 200 with a
    /// typed body; a missing request path is a typed 400 and Git never runs.
    /// </summary>
    [HttpPost("pull")]
    public async Task<IActionResult> Pull(
        [FromBody] SourceControlPullRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitPullResult result = await _pulls.PullAsync(request.Path!, cancellationToken);
        return Ok(await ToPullResponseAsync(result, cancellationToken));
    }

    /// <summary>
    /// Maps a typed pull result onto the API contract (spec 13 §14.3/§20):
    /// success carries the previous/new HEAD, the changed paths and the status
    /// re-read from Git; blocking states keep their typed outcome and message so
    /// the UI can show divergence as a blocking state rather than an error
    /// string. Nothing here ever exposes a credential.
    /// </summary>
    private async Task<SourceControlPullResponse> ToPullResponseAsync(
        GitPullResult result,
        CancellationToken cancellationToken)
    {
        SourceControlStatusResponse? status =
            result.Refresh?.Status is { IsAvailable: true, Snapshot: { } snapshot }
                ? await MapSnapshotAsync(snapshot, cancellationToken)
                : null;

        return new SourceControlPullResponse(
            result.Outcome.ToString(),
            result.IsSuccess,
            result.IsBlocking,
            result.ErrorCode,
            result.Message,
            result.OperationId,
            result.PreviousHead,
            result.Head,
            result.Refresh?.ChangedPaths ?? Array.Empty<string>(),
            result.Refresh?.ReindexedDocuments ?? 0,
            result.Refresh?.StatusRefreshed ?? false,
            result.Refresh?.HistoryRecorded ?? false,
            ToRemoteResponse(result.Remote),
            status,
            ToFailure(result.Failure));
    }

    // ---------------------------------------------------------------------
    // SCM-08 - Git history, file history and document-version inspection
    // (spec 13 §9.4/§12/§18/§20/§25). Read-only: nothing here mutates the
    // repository, and there is no branch-management, push, pull or remote
    // surface.
    // ---------------------------------------------------------------------

    /// <summary>Marks the payload as Git-derived, not Document IDE history (spec 13 §4.3).</summary>
    private const string GitHistorySource = "Git";

    /// <summary>
    /// One page of the repository's commit history, newest first
    /// (<c>git log</c>, spec 13 §9.4/§12). A path outside Git stays a valid
    /// source and yields a typed <c>NoRepository</c> state (HTTP 200).
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? path,
        [FromQuery] int? skip,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathRequiredBadRequest("query parameter");
        }

        GitHistoryResult result = await _history.GetHistoryAsync(
            path,
            new GitHistoryQuery(skip ?? 0, limit ?? GitHistoryQuery.DefaultLimit),
            cancellationToken);
        return ToHistoryResponse(result);
    }

    /// <summary>
    /// One page of the commits that touched one selected document
    /// (<c>git log -- &lt;path&gt;</c>, spec 13 §9.4/§12). The document path is
    /// untrusted input and must be the worktree-relative path Git reported.
    /// </summary>
    [HttpGet("file-history")]
    public async Task<IActionResult> GetFileHistory(
        [FromQuery] string? path,
        [FromQuery] string? file,
        [FromQuery] int? skip,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathRequiredBadRequest("query parameter");
        }

        GitHistoryResult result = await _history.GetFileHistoryAsync(
            path,
            file,
            new GitHistoryQuery(skip ?? 0, limit ?? GitHistoryQuery.DefaultLimit),
            cancellationToken);
        return ToHistoryResponse(result);
    }

    /// <summary>
    /// The selected document as it was committed in one selected commit
    /// (<c>git show &lt;commit&gt;:&lt;path&gt;</c>, spec 13 §12). Strictly
    /// read-only: the working tree, the index and HEAD are untouched.
    /// </summary>
    [HttpGet("file-version")]
    public async Task<IActionResult> GetFileVersion(
        [FromQuery] string? path,
        [FromQuery] string? file,
        [FromQuery] string? commit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathRequiredBadRequest("query parameter");
        }

        GitFileVersionResult result = await _history.GetFileVersionAsync(path, file, commit, cancellationToken);
        return ToFileVersionResponse(result);
    }

    /// <summary>
    /// Maps a typed history result onto the privileged API contract
    /// (spec 13 §12/§20/§25): a rejected request is a typed HTTP 400 with
    /// nothing run; every Git-reported state stays HTTP 200 so the pane renders
    /// it instead of an error page.
    /// </summary>
    private IActionResult ToHistoryResponse(GitHistoryResult result)
    {
        if (result.Status is GitHistoryStatus.InvalidRequest or GitHistoryStatus.PathRejected)
        {
            return BadRequest(new
            {
                errorCode = result.ErrorCode ?? result.Status.ToString(),
                error = result.Message,
            });
        }

        GitHistoryPage? page = result.Page;
        return Ok(new SourceControlHistoryResponse(
            result.Status.ToString(),
            GitHistorySource,
            result.ErrorCode,
            result.Message,
            result.IsAvailable,
            page is null
                ? null
                : new SourceControlHistoryPageResponse(
                    page.Commits.Select(ToHistoryCommit).ToArray(),
                    page.Skip,
                    page.Limit,
                    page.HasMore,
                    page.NextSkip),
            ToFailure(result.Failure)));
    }

    /// <summary>
    /// Maps a typed document-version result onto the privileged API contract
    /// with the same rejection/typed-state split as the history routes
    /// (spec 13 §12/§20).
    /// </summary>
    private IActionResult ToFileVersionResponse(GitFileVersionResult result)
    {
        if (result.Status is GitHistoryStatus.InvalidRequest or GitHistoryStatus.PathRejected)
        {
            return BadRequest(new
            {
                errorCode = result.ErrorCode ?? result.Status.ToString(),
                error = result.Message,
            });
        }

        GitFileVersion? version = result.Version;
        return Ok(new SourceControlFileVersionResponse(
            result.Status.ToString(),
            GitHistorySource,
            result.ErrorCode,
            result.Message,
            result.IsAvailable,
            version is null
                ? null
                : new SourceControlFileVersionInfoResponse(
                    version.CommitId,
                    version.Path,
                    version.Content,
                    version.IsBinary,
                    version.IsTruncated,
                    version.SizeBytes),
            ToFailure(result.Failure)));
    }

    private static SourceControlHistoryCommitResponse ToHistoryCommit(GitHistoryEntry entry) =>
        new(
            entry.CommitId,
            entry.ShortId,
            entry.AuthorName,
            entry.AuthorEmail,
            entry.CommitterTime,
            entry.Summary,
            entry.FileCount);

    // ---------------------------------------------------------------------
    // SCM-10 - safe Git discard & restore (spec 13 §15/§19/§20/§21/§23).
    //
    // Bounded append-only region: a destructive discard/restore is a POST and
    // is never reachable through the status/refresh GET; the apply routes
    // require the review token returned by the review route. Nothing here
    // creates a commit, pushes, pulls, resets or touches any other file.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Reviews one destructive operation before it can run (spec 13
    /// §15.1/§15.2): the affected path, current diff, staged state, target Git
    /// version, recovery availability and the confirmation token. Strictly
    /// read-only; a rejected path/commit is a typed 400 with nothing run.
    /// </summary>
    [HttpPost("discard/review")]
    public async Task<IActionResult> ReviewDiscard(
        [FromBody] SourceControlRestoreReviewRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitRestoreReviewResult result = await _restores.ReviewAsync(
            request.Path!,
            request.ChangePath,
            request.Commit,
            cancellationToken);

        if (result.Status == GitRestoreReviewStatus.RejectedPath)
        {
            return BadRequest(new
            {
                errorCode = result.ErrorCode ?? "PathRejected",
                error = result.Message,
            });
        }

        return Ok(ToRestoreReviewResponse(result));
    }

    /// <summary>
    /// Discards one file's working-tree changes after an explicit review
    /// (spec 13 §15.1). The reviewed token is required: without it the request
    /// is a typed 400 and Git never runs.
    /// </summary>
    [HttpPost("discard")]
    public Task<IActionResult> Discard(
        [FromBody] SourceControlRestoreRequest? request,
        CancellationToken cancellationToken) =>
        RunRestoreActionAsync(request, GitRestoreMode.DiscardWorktree, cancellationToken);

    /// <summary>
    /// Restores one file's working-tree content from a selected commit after an
    /// explicit review (spec 13 §15.2). The reviewed token is required; the
    /// index, HEAD and every other file stay untouched.
    /// </summary>
    [HttpPost("restore")]
    public Task<IActionResult> Restore(
        [FromBody] SourceControlRestoreRequest? request,
        CancellationToken cancellationToken) =>
        RunRestoreActionAsync(request, GitRestoreMode.RestoreVersion, cancellationToken);

    private async Task<IActionResult> RunRestoreActionAsync(
        SourceControlRestoreRequest? request,
        GitRestoreMode mode,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitRestoreResult result = mode == GitRestoreMode.DiscardWorktree
            ? await _restores.DiscardAsync(
                request.Path!,
                request.ChangePath,
                request.ReviewToken,
                cancellationToken)
            : await _restores.RestoreFromCommitAsync(
                request.Path!,
                request.ChangePath,
                request.Commit,
                request.ReviewToken,
                cancellationToken);

        switch (result.Outcome)
        {
            // Input rejections: nothing ran, so the client can review or fix
            // the request rather than reading a misleading result.
            case GitRestoreOutcome.ReviewRequired:
            case GitRestoreOutcome.ReviewStale:
            case GitRestoreOutcome.RejectedPath:
                return BadRequest(new
                {
                    errorCode = result.ErrorCode ?? result.Outcome.ToString(),
                    error = result.Message,
                });

            default:
                return Ok(await ToRestoreResponseAsync(result, cancellationToken));
        }
    }

    /// <summary>
    /// Maps a typed review result onto the privileged API contract
    /// (spec 13 §15.1/§20).
    /// </summary>
    private static SourceControlRestoreReviewResponse ToRestoreReviewResponse(GitRestoreReviewResult result)
    {
        GitRestoreReview? review = result.Review;
        return new SourceControlRestoreReviewResponse(
            result.Status.ToString(),
            result.IsAvailable,
            result.ErrorCode,
            result.Message,
            review is null
                ? null
                : new SourceControlRestoreReviewInfoResponse(
                    review.Mode.ToString(),
                    review.Path,
                    review.SourceCommit,
                    review.TargetSource,
                    review.Diff,
                    review.IsDiffTruncated,
                    review.IndexChange?.ToString(),
                    review.WorkingTreeChange?.ToString(),
                    review.StatusCharacters,
                    review.IsStaged,
                    review.IsConflicted,
                    review.IsUntracked,
                    review.HasWorkingTreeChange,
                    review.RecoveryAvailable,
                    review.RecoveryDetail,
                    review.CurrentSizeBytes,
                    review.IsApplicable,
                    review.BlockedReason,
                    review.ReviewToken),
            ToFailure(result.Failure));
    }

    /// <summary>
    /// Maps a typed discard/restore result onto the API contract
    /// (spec 13 §15/§20): success carries the refreshed status Git re-read after
    /// the mutation; every deliberate refusal or Git failure stays HTTP 200 with
    /// a typed body so the pane can render it.
    /// </summary>
    private async Task<SourceControlRestoreResponse> ToRestoreResponseAsync(
        GitRestoreResult result,
        CancellationToken cancellationToken)
    {
        GitStatusResult? refreshed = result.Status;
        SourceControlStatusResponse? status = refreshed is { IsAvailable: true, Snapshot: not null }
            ? await MapSnapshotAsync(refreshed.Snapshot, cancellationToken)
            : null;

        return new SourceControlRestoreResponse(
            result.Outcome.ToString(),
            result.Mode.ToString(),
            result.IsSuccess,
            result.ErrorCode,
            result.Message,
            result.OperationId,
            result.SnapshotId,
            result.RecoveryRecorded,
            result.HistoryRecorded,
            result.Reindexed,
            status,
            ToFailure(result.Failure ?? (refreshed is { IsAvailable: false } ? refreshed.Failure : null)));
    }

    // =====================================================================
    // SCM-18 — worktree API surface (spec 13 §16/§18/§20)
    //
    // Bounded append-only region: a thin, typed HTTP surface over the
    // already-tested SCM-15 discovery/open and SCM-16 create/remove/prune
    // services. This region adds no Git invocation, creates or switches no
    // branch, and never forces a removal. HTTP 400 is reserved for input
    // rejected before any Git ran; every inspectable Git outcome — including
    // RequiresAttachmentFlow, which is a signal and not an error — stays
    // HTTP 200 with its typed body so screen 07 can render it.
    //
    // The services are injected per action ([FromServices]) so the region
    // stays append-only and the controller's existing constructor is
    // untouched.
    // =====================================================================

    /// <summary>
    /// Lists every worktree Git registers for the repository containing
    /// <paramref name="path"/> (spec 13 §16.1). A path outside Git is the
    /// typed <c>NotARepository</c> state (HTTP 200), never an error page; a
    /// missing path is a typed 400 and Git never runs.
    /// </summary>
    [HttpGet("worktrees")]
    public async Task<IActionResult> ListWorktrees(
        [FromQuery] string? path,
        [FromServices] IGitWorktreeService worktrees,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathRequiredBadRequest("query parameter");
        }

        GitWorktreeListResult result = await worktrees.ListWorktreesAsync(path, cancellationToken);
        return Ok(ToWorktreeListResponse(result));
    }

    /// <summary>
    /// Makes a Git-registered worktree the active filesystem context, or
    /// reports that it is not attached to the workspace (spec 13 §16.2).
    /// <c>RequiresAttachmentFlow</c> is the typed signal to route the user
    /// through the existing FS-01 attachment flow: this route never attaches,
    /// detaches, or reorganizes a folder source, and no branch is switched.
    /// Malformed input (blank id/path, non-canonical path) is a typed 400 and
    /// nothing runs; every Git-derived outcome stays HTTP 200.
    /// </summary>
    [HttpPost("worktrees/open")]
    public async Task<IActionResult> OpenWorktree(
        [FromBody] SourceControlWorktreeOpenRequest? request,
        [FromServices] IGitWorktreeService worktrees,
        CancellationToken cancellationToken)
    {
        GitWorktreeOpenResult result = await worktrees.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(
                request?.WorkspaceId ?? string.Empty,
                request?.WorkingDirectory ?? string.Empty,
                request?.WorktreePath ?? string.Empty),
            cancellationToken);

        if (result.Status == GitWorktreeOpenStatus.InvalidRequest)
        {
            return WorktreeInputRejected(result.Status.ToString(), result.ErrorMessage);
        }

        // Opening and the attachment signal are not errors; every other
        // non-opened status (not registered, unavailable, list failure) keeps
        // its typed status as the machine-readable code.
        string? errorCode = result.Status is GitWorktreeOpenStatus.Opened
            or GitWorktreeOpenStatus.RequiresAttachmentFlow
            ? null
            : result.Status.ToString();

        return Ok(new SourceControlWorktreeOpenResponse(
            result.Status.ToString(),
            result.IsOpened,
            result.IsAttachedToWorkspace,
            errorCode,
            result.ErrorMessage,
            ToWorktreeInfo(result.Worktree),
            ToWorktreeInfos(result.ListResult),
            ToWorktreeFailure(null, result.ListResult)));
    }

    /// <summary>
    /// Creates a worktree from an existing branch, tag, or commit
    /// (spec 13 §16.3). No branch is ever created as a side effect. A missing
    /// or unsafe target path or a malformed ref token is a typed 400 and Git
    /// never runs; a non-existent ref, occupied target, or Git failure stays
    /// HTTP 200 with the typed outcome so the UI can render it.
    /// </summary>
    [HttpPost("worktrees")]
    public async Task<IActionResult> CreateWorktree(
        [FromBody] SourceControlWorktreeCreateRequest? request,
        [FromServices] IGitWorktreeMutationService worktrees,
        CancellationToken cancellationToken)
    {
        GitWorktreeCreateResult result = await worktrees.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(
                request?.WorkingDirectory ?? string.Empty,
                request?.TargetPath ?? string.Empty,
                request?.Ref ?? string.Empty),
            cancellationToken);

        if (result.Status is GitWorktreeCreateStatus.InvalidRequest
            or GitWorktreeCreateStatus.InvalidTargetPath)
        {
            return WorktreeInputRejected(result.Status.ToString(), result.ErrorMessage);
        }

        return Ok(new SourceControlWorktreeCreateResponse(
            result.Status.ToString(),
            result.IsSuccess,
            result.IsSuccess ? null : result.Status.ToString(),
            result.ErrorMessage,
            ToWorktreeInfo(result.Worktree),
            ToWorktreeInfos(result.ListResult),
            ToWorktreeFailure(result.IsSuccess ? null : result.ProcessResult, result.ListResult)));
    }

    /// <summary>
    /// Removes one registered worktree (spec 13 §16.4). Removal is explicit
    /// and never forced: a dirty, locked, main, missing, or unregistered
    /// worktree is refused with a typed HTTP 200 body, and a dirty refusal
    /// carries the change list. A missing/unsafe path is a typed 400 and Git
    /// never runs.
    /// </summary>
    [HttpDelete("worktrees")]
    public async Task<IActionResult> RemoveWorktree(
        [FromBody] SourceControlWorktreeRemoveRequest? request,
        [FromServices] IGitWorktreeMutationService worktrees,
        CancellationToken cancellationToken)
    {
        GitWorktreeRemoveResult result = await worktrees.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(
                request?.WorkingDirectory ?? string.Empty,
                request?.WorktreePath ?? string.Empty),
            cancellationToken);

        if (result.Status is GitWorktreeRemoveStatus.InvalidRequest
            or GitWorktreeRemoveStatus.InvalidTargetPath)
        {
            return WorktreeInputRejected(result.Status.ToString(), result.ErrorMessage);
        }

        return Ok(new SourceControlWorktreeRemoveResponse(
            result.Status.ToString(),
            result.IsSuccess,
            result.IsSuccess ? null : result.Status.ToString(),
            result.ErrorMessage,
            ToWorktreeInfo(result.Worktree),
            result.Changes is null
                ? Array.Empty<SourceControlWorktreeChangeResponse>()
                : result.Changes.Select(ToWorktreeChange).ToArray(),
            ToWorktreeInfos(result.ListResult),
            ToWorktreeFailure(result.IsSuccess ? null : result.ProcessResult, result.ListResult)));
    }

    /// <summary>
    /// Runs the safe worktree metadata refresh (spec 13 §16.5) and returns the
    /// re-read registry. Only stale metadata of missing, unlocked worktrees is
    /// pruned by Git; a valid worktree is never deleted. A missing path is a
    /// typed 400 and Git never runs.
    /// </summary>
    [HttpPost("worktrees/prune")]
    public async Task<IActionResult> RefreshWorktrees(
        [FromBody] SourceControlWorktreeRefreshRequest? request,
        [FromServices] IGitWorktreeMutationService worktrees,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return PathRequiredBadRequest("request field");
        }

        GitWorktreeRefreshResult result =
            await worktrees.RefreshWorktreesAsync(request.Path!, cancellationToken);

        return Ok(new SourceControlWorktreeRefreshResponse(
            result.Status.ToString(),
            result.IsSuccess,
            result.IsSuccess ? null : result.Status.ToString(),
            result.ErrorMessage,
            ToWorktreeInfos(result.ListResult),
            ToWorktreeFailure(result.IsSuccess ? null : result.ProcessResult, result.ListResult)));
    }

    /// <summary>
    /// Maps a typed registry listing onto the API contract (spec 13 §16.1/§20):
    /// the status and failure stay inspectable and are never flattened.
    /// </summary>
    private static SourceControlWorktreeListResponse ToWorktreeListResponse(GitWorktreeListResult result) =>
        new(
            result.Status.ToString(),
            result.IsSuccess,
            result.Worktrees.Select(worktree => ToWorktreeInfo(worktree)!).ToArray(),
            result.IsSuccess ? null : result.Status.ToString(),
            result.ErrorMessage,
            result.IsSuccess ? null : ToFailure(result.ProcessResult));

    /// <summary>
    /// Maps the optional re-read registry of a mutation onto the typed payload;
    /// <c>null</c> means the registry could not be re-read, never an empty
    /// registry.
    /// </summary>
    private static IReadOnlyList<SourceControlWorktreeInfoResponse>? ToWorktreeInfos(
        GitWorktreeListResult? list) =>
        list is null
            ? null
            : list.Worktrees.Select(worktree => ToWorktreeInfo(worktree)!).ToArray();

    /// <summary>
    /// Surfaces the originating Git failure of either the mutation or the
    /// post-mutation registry re-read, so a failed re-read is never hidden by a
    /// successful mutation.
    /// </summary>
    private static SourceControlFailureResponse? ToWorktreeFailure(
        GitProcessResult? failure,
        GitWorktreeListResult? list) =>
        ToFailure(failure ?? (list is { IsSuccess: false } ? list.ProcessResult : null));

    private static SourceControlWorktreeInfoResponse? ToWorktreeInfo(GitWorktreeInfo? worktree) =>
        worktree is null
            ? null
            : new SourceControlWorktreeInfoResponse(
                worktree.Path,
                worktree.HeadCommit,
                worktree.BranchRef,
                worktree.BranchName,
                worktree.IsDetached,
                worktree.IsBare,
                worktree.IsLocked,
                worktree.LockReason,
                worktree.IsPrunable,
                worktree.PruneReason,
                worktree.IsMainWorktree);

    private static SourceControlWorktreeChangeResponse ToWorktreeChange(GitStatusEntry entry) =>
        new(
            entry.Path,
            entry.OriginalPath,
            entry.State.ToString(),
            entry.IndexChange.ToString(),
            entry.WorkingTreeChange.ToString(),
            entry.ConflictKind.ToString(),
            entry.StatusCharacters);

    private static IActionResult WorktreeInputRejected(string errorCode, string? message) =>
        new BadRequestObjectResult(new
        {
            errorCode,
            error = message,
        });
}

/// <summary>
/// SCM-13 — request body for the explicit Push action. <c>Path</c> is the
/// workspace folder or active-document path used to discover the repository;
/// the push runs against exactly that resolved true worktree root
/// (spec 13 §19).
/// </summary>
public sealed record SourceControlPushRequest(string? Path);

/// <summary>
/// SCM-13 — typed Push outcome (spec 13 §14.4/§20). On success <c>Remote</c>
/// carries the tracking state re-read from Git after the push; a branch with no
/// configured upstream is the first-class <c>NoUpstream</c> outcome and ran no
/// Git; remote failures (authentication, rejection, permission, unreachable
/// host, Git missing) stay typed and inspectable. A rejected push leaves local
/// commits unchanged, and no failure payload ever carries credential material.
/// </summary>
public sealed record SourceControlPushResponse(
    string Outcome,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    SourceControlRemoteResponse? Remote,
    SourceControlFailureResponse? Failure);

// -------------------------------------------------------------------------
// SCM-10 — safe discard/restore payloads (spec 13 §15/§20)
// -------------------------------------------------------------------------

/// <summary>
/// SCM-10 — request body for the review step. <c>Path</c> is the workspace
/// folder or active-document path used to discover the repository;
/// <c>ChangePath</c> is the worktree-relative path exactly as Git reported it
/// (treated as untrusted input); <c>Commit</c> is the version to restore from
/// and is <c>null</c> when reviewing a working-tree discard.
/// </summary>
public sealed record SourceControlRestoreReviewRequest(string? Path, string? ChangePath, string? Commit);

/// <summary>
/// SCM-10 — the reviewed confirmation payload (spec 13 §15.1/§15.2). It shows
/// what will be lost — the current diff, staged state, target version and
/// recovery availability — and carries the token the apply route requires.
/// </summary>
public sealed record SourceControlRestoreReviewInfoResponse(
    string Mode,
    string Path,
    string? SourceCommit,
    string TargetSource,
    string Diff,
    bool IsDiffTruncated,
    string? IndexChange,
    string? WorkingTreeChange,
    string StatusCharacters,
    bool IsStaged,
    bool IsConflicted,
    bool IsUntracked,
    bool HasWorkingTreeChange,
    bool RecoveryAvailable,
    string RecoveryDetail,
    long? CurrentSizeBytes,
    bool IsApplicable,
    string? BlockedReason,
    string ReviewToken);

/// <summary>SCM-10 — typed review response (spec 13 §15.1/§20).</summary>
public sealed record SourceControlRestoreReviewResponse(
    string Status,
    bool IsAvailable,
    string? ErrorCode,
    string? Message,
    SourceControlRestoreReviewInfoResponse? Review,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-10 — request body for the destructive apply step. <c>ReviewToken</c> is
/// required and must be the token the review returned; a missing or stale token
/// is a typed 400 with nothing run (spec 13 §15/§23).
/// </summary>
public sealed record SourceControlRestoreRequest(
    string? Path,
    string? ChangePath,
    string? Commit,
    string? ReviewToken);

/// <summary>
/// SCM-10 — typed discard/restore outcome (spec 13 §15/§20). On success
/// <c>Status</c> carries the status re-read from Git after the mutation,
/// <c>SnapshotId</c> the HIST-01 recovery snapshot created before it, and the
/// history/reindex flags report the Document IDE History record and reindex.
/// </summary>
public sealed record SourceControlRestoreResponse(
    string Outcome,
    string Mode,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    string? OperationId,
    string? SnapshotId,
    bool RecoveryRecorded,
    bool HistoryRecorded,
    bool Reindexed,
    SourceControlStatusResponse? Status,
    SourceControlFailureResponse? Failure);

// -------------------------------------------------------------------------
// SCM-12 — safe fast-forward pull payloads (spec 13 §14.3/§20)
// -------------------------------------------------------------------------

/// <summary>
/// SCM-12 — request body for the conservative Pull action. <c>Path</c> is the
/// workspace folder or active-document path used to discover the repository;
/// the pull runs against exactly that resolved true worktree root (spec 13 §19).
/// </summary>
public sealed record SourceControlPullRequest(string? Path);

/// <summary>
/// SCM-12 — typed Pull outcome (spec 13 §14.3/§20). On success <c>Head</c> is the
/// fast-forwarded commit, <c>ChangedPaths</c> the files the pull brought in, and
/// <c>Status</c> the status re-read from Git. <c>IsBlocking</c> is true for
/// diverged history and for a dirty/conflicted worktree (nothing ran), so the UI
/// renders a blocking state instead of a raw error; no outcome ever carries
/// credential material or performs a merge/rebase.
/// </summary>
public sealed record SourceControlPullResponse(
    string Outcome,
    bool IsSuccess,
    bool IsBlocking,
    string? ErrorCode,
    string? Message,
    string? OperationId,
    string? PreviousHead,
    string? Head,
    IReadOnlyList<string> ChangedPaths,
    int ReindexedDocuments,
    bool StatusRefreshed,
    bool HistoryRecorded,
    SourceControlRemoteResponse? Remote,
    SourceControlStatusResponse? Status,
    SourceControlFailureResponse? Failure);

// -------------------------------------------------------------------------
// SCM-18 — worktree API payloads (spec 13 §16/§18/§20)
//
// Every payload mirrors the typed service outcome instead of the raw Git
// output: status/isSuccess/errorCode/message plus the read-only registry data.
// `Path` values are Git's canonical registered locations, never arbitrary
// caller paths, and no payload carries secret or credential material.
// -------------------------------------------------------------------------

/// <summary>
/// SCM-18 — one registered worktree as Git reports it (spec 13 §16.1):
/// canonical path, HEAD commit, branch or detached state, and locked/prunable
/// metadata. Branch values are context metadata only — there is no
/// branch-management surface.
/// </summary>
public sealed record SourceControlWorktreeInfoResponse(
    string Path,
    string HeadCommit,
    string? BranchRef,
    string? BranchName,
    bool IsDetached,
    bool IsBare,
    bool IsLocked,
    string? LockReason,
    bool IsPrunable,
    string? PruneReason,
    bool IsMainWorktree);

/// <summary>
/// SCM-18 — typed registry listing (spec 13 §16.1/§20). A path outside Git is
/// the typed <c>NotARepository</c> status with an empty list (HTTP 200), never
/// a partial or invented registry.
/// </summary>
public sealed record SourceControlWorktreeListResponse(
    string Status,
    bool IsSuccess,
    IReadOnlyList<SourceControlWorktreeInfoResponse> Worktrees,
    string? ErrorCode,
    string? Message,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-18 — request body for the open-existing-worktree action
/// (spec 13 §16.2). <c>WorkspaceId</c> selects the workspace whose folder
/// sources are consulted; <c>WorkingDirectory</c> locates the repository and
/// <c>WorktreePath</c> must be a location Git registered.
/// </summary>
public sealed record SourceControlWorktreeOpenRequest(
    string? WorkspaceId,
    string? WorkingDirectory,
    string? WorktreePath);

/// <summary>
/// SCM-18 — typed open outcome (spec 13 §16.2/§20). <c>RequiresAttachmentFlow</c>
/// is a signal, not an error: nothing was attached and the caller must route
/// the user through the existing FS-01 folder-source flow. <c>Worktrees</c>
/// carries the registry read during the action when it is available.
/// </summary>
public sealed record SourceControlWorktreeOpenResponse(
    string Status,
    bool IsSuccess,
    bool IsAttachedToWorkspace,
    string? ErrorCode,
    string? Message,
    SourceControlWorktreeInfoResponse? Worktree,
    IReadOnlyList<SourceControlWorktreeInfoResponse>? Worktrees,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-18 — request body for the create action (spec 13 §16.3).
/// <c>Ref</c> must already exist (local branch, tag, or explicit commit):
/// creation never invents a branch.
/// </summary>
public sealed record SourceControlWorktreeCreateRequest(
    string? WorkingDirectory,
    string? TargetPath,
    string? Ref);

/// <summary>
/// SCM-18 — typed create outcome (spec 13 §16.3/§20). Success carries the
/// created worktree plus the re-read registry; refused refs and Git failures
/// stay typed and inspectable.
/// </summary>
public sealed record SourceControlWorktreeCreateResponse(
    string Status,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    SourceControlWorktreeInfoResponse? Worktree,
    IReadOnlyList<SourceControlWorktreeInfoResponse>? Worktrees,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-18 — request body for the remove action (spec 13 §16.4).
/// <c>WorktreePath</c> must be exactly Git's registered canonical location.
/// </summary>
public sealed record SourceControlWorktreeRemoveRequest(
    string? WorkingDirectory,
    string? WorktreePath);

/// <summary>
/// SCM-18 — one change entry of a removal-blocking dirty worktree
/// (spec 13 §16.4: "show the current change list"). Paths are
/// worktree-relative exactly as Git reported them.
/// </summary>
public sealed record SourceControlWorktreeChangeResponse(
    string Path,
    string? OriginalPath,
    string State,
    string IndexChange,
    string WorkingTreeChange,
    string ConflictKind,
    string StatusCharacters);

/// <summary>
/// SCM-18 — typed removal outcome (spec 13 §16.4/§20). Removal is never
/// forced: a dirty worktree is refused with <c>Changes</c>, and main, locked,
/// missing, or unregistered worktrees keep their typed blocking status.
/// </summary>
public sealed record SourceControlWorktreeRemoveResponse(
    string Status,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    SourceControlWorktreeInfoResponse? Worktree,
    IReadOnlyList<SourceControlWorktreeChangeResponse> Changes,
    IReadOnlyList<SourceControlWorktreeInfoResponse>? Worktrees,
    SourceControlFailureResponse? Failure);

/// <summary>
/// SCM-18 — request body for the safe worktree refresh/prune action
/// (spec 13 §16.5). <c>Path</c> is the workspace folder or active-document
/// path used to locate the repository.
/// </summary>
public sealed record SourceControlWorktreeRefreshRequest(string? Path);

/// <summary>
/// SCM-18 — typed refresh outcome (spec 13 §16.5/§20). Success carries the
/// re-read registry; pruning never deletes a valid worktree.
/// </summary>
public sealed record SourceControlWorktreeRefreshResponse(
    string Status,
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<SourceControlWorktreeInfoResponse>? Worktrees,
    SourceControlFailureResponse? Failure);
