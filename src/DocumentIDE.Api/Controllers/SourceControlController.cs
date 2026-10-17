using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Repositories;
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

    public SourceControlController(
        GitRepositoryService repositories,
        GitStageService stages,
        GitCommitService commits,
        IDocumentRepository documents)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
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
}
