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
/// SCM-03/SCM-06/SCM-07 — privileged Source Control API consumed by the changes
/// pane (spec 13 §18): read-only repository/status queries, explicit file-level
/// stage/unstage actions, and the explicit Commit / Commit All actions. Every
/// route inherits the localhost session-token guard from
/// <c>PrivilegedRequestGuard</c>; there is no unauthenticated path and there is
/// still no branch-management surface.
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
    private readonly IDocumentRepository _documents;
    private readonly IWorkSessionService _workSessions;

    public SourceControlController(
        GitRepositoryService repositories,
        GitStageService stages,
        GitCommitService commits,
        IDocumentRepository documents,
        IWorkSessionService workSessions)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _workSessions = workSessions ?? throw new ArgumentNullException(nameof(workSessions));
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
}
