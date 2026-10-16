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
/// SCM-03 — privileged Source Control API consumed by the changes pane
/// (spec 13 §18). Read-only in this sprint: it exposes exactly the current Git
/// state and contains no stage/commit/remote/branch-management actions.
/// Every route inherits the localhost session-token guard from
/// <c>PrivilegedRequestGuard</c>; there is no unauthenticated path.
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
    private readonly IDocumentRepository _documents;

    public SourceControlController(GitRepositoryService repositories, IDocumentRepository documents)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
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
            return BadRequest(new
            {
                errorCode = "PathRequired",
                error = "The 'path' query parameter (workspace folder or active document) is required.",
            });
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(path, cancellationToken);
        if (!context.HasRepository)
        {
            return Ok(new SourceControlStatusResponse(
                context.Status.ToString(),
                false,
                null,
                context.BranchName,
                context.IsDetached,
                context.HeadCommit,
                context.UpstreamReference,
                context.UpstreamReference is not null,
                null,
                null,
                EmptySections,
                ToFailure(context.Failure)));
        }

        GitStatusResult status = await _repositories.GetStatusAsync(context.WorktreeRoot!, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            return Ok(new SourceControlStatusResponse(
                status.Status.ToString(),
                false,
                context.WorktreeRoot,
                context.BranchName,
                context.IsDetached,
                context.HeadCommit,
                context.UpstreamReference,
                context.UpstreamReference is not null,
                null,
                null,
                EmptySections,
                ToFailure(status.Failure)));
        }

        GitStatusSnapshot snapshot = status.Snapshot;
        SourceControlSectionsResponse sections = new(
            await MapEntriesAsync(snapshot, entry => entry.HasStagedChange, cancellationToken),
            await MapEntriesAsync(snapshot, entry => entry.HasWorkingTreeChange, cancellationToken),
            await MapEntriesAsync(snapshot, entry => entry.IsUntracked, cancellationToken),
            await MapEntriesAsync(snapshot, entry => entry.IsConflicted, cancellationToken));

        return Ok(new SourceControlStatusResponse(
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
            sections,
            null));
    }

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
