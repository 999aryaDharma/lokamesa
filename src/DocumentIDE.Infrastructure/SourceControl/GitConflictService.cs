using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-14 — Git conflict state &amp; resolution shell application service
/// (spec 13 §20/§21/§23).
///
/// The service <b>observes and routes</b>; it never mutates conflicted content:
/// <list type="bullet">
/// <item><see cref="GetStateAsync"/> reads the real unmerged state through the
/// SCM-02 status snapshot. Conflict kinds come from that snapshot's porcelain
/// v2 classification — there is no second status parser,</item>
/// <item><see cref="CheckActionAsync"/> returns a typed blocking decision for
/// commit/commit-all, path-scoped discard/restore, and pull. A state that cannot
/// be read safely is also refused rather than assumed clean,</item>
/// <item>a DOCX conflict is reported like any other conflict and is
/// <b>never</b> merged, parsed or written by this service — a human resolves it
/// through Word and the existing guarded host,</item>
/// <item>the only opening action is an already-registered document id; the
/// service never exposes a worktree path or creates a new opener.</item>
/// </list>
///
/// Git remains the single authority for unmerged state (spec 13 §4.2/§24);
/// nothing here is persisted. Any Git diagnostic carried on a typed result is
/// passed through the SCM-13 credential redactor (spec 13 §23).
/// </summary>
public sealed class GitConflictService
{
    /// <summary>Bound on paths echoed in one typed message.</summary>
    private const int MaxListedPaths = 20;

    private readonly GitRepositoryService _repositories;
    private readonly IDocumentRepository _documents;

    public GitConflictService(GitRepositoryService repositories, IDocumentRepository documents)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    /// <summary>
    /// Reads the worktree's real unmerged state (spec 13 §21). Paths and
    /// conflict kinds are exactly what SCM-02's snapshot reported; registered
    /// documents additionally receive the existing manual-open action.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="cancellationToken">Cancels the read-only Git invocations (typed, never thrown).</param>
    public async Task<GitConflictStateResult> GetStateAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        // A vanished worktree is a typed state, not an exception and not a Git
        // invocation against an arbitrary path (spec 13 §20).
        if (!PathExists(repositoryPath))
        {
            return new GitConflictStateResult(
                GitConflictStatus.WorktreeMissing,
                null,
                null,
                "The worktree path no longer exists; no conflict state was read.");
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            GitConflictStatus discovery = context.Status switch
            {
                GitRepositoryStatus.NoRepository => GitConflictStatus.NoRepository,
                GitRepositoryStatus.GitUnavailable => GitConflictStatus.GitUnavailable,
                _ => GitConflictStatus.Failed,
            };

            return new GitConflictStateResult(
                discovery,
                null,
                context.Failure is null ? null : GitCredentialRedactor.Sanitize(context.Failure),
                GitCredentialRedactor.Redact(
                    context.Failure?.ErrorMessage ?? "The repository could not be discovered; no conflict state was read."));
        }

        string worktreeRoot = context.WorktreeRoot!;
        GitStatusResult status = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            GitConflictStatus mapped = MapStatusFailure(status, worktreeRoot);
            return new GitConflictStateResult(
                mapped,
                null,
                status.Failure is null ? null : GitCredentialRedactor.Sanitize(status.Failure),
                GitCredentialRedactor.Redact(StatusFailureMessage(mapped, status.Failure)));
        }

        GitStatusSnapshot snapshot = status.Snapshot;
        var conflicts = new List<GitConflictEntry>();
        foreach (GitStatusEntry entry in snapshot.Entries.Where(entry => entry.IsConflicted))
        {
            string? documentId = null;
            GitConflictOpenAction openAction = GitConflictOpenAction.None;

            // The worktree-relative path is untrusted input: containment is
            // re-checked before it is used for a read-only document lookup, and
            // the resolved path never leaves this method (spec 13 §23).
            if (GitDocumentPath.TryNormalizeRelativePath(entry.Path, out string gitPath)
                && GitDocumentPath.TryResolveContainedPath(worktreeRoot, gitPath, out string absolutePath))
            {
                var document = await _documents.GetByPathAsync(absolutePath);
                if (document is not null)
                {
                    documentId = document.Id;
                    openAction = IsDocxPath(entry.Path)
                        ? GitConflictOpenAction.OpenInWord
                        : GitConflictOpenAction.OpenDocument;
                }
            }

            conflicts.Add(new GitConflictEntry(
                entry.Path,
                entry.ConflictKind,
                entry.StatusCharacters,
                documentId,
                openAction));
        }

        var state = new GitConflictState(
            snapshot.WorktreeRoot,
            snapshot.BranchName,
            snapshot.IsDetached,
            snapshot.HeadCommit,
            conflicts);

        return conflicts.Count == 0
            ? new GitConflictStateResult(
                GitConflictStatus.NoConflicts,
                state,
                null,
                "The repository has no unresolved conflicts.")
            : new GitConflictStateResult(
                GitConflictStatus.Available,
                state,
                null,
                "The repository has unresolved conflicts. Commit, discard/restore on a conflicted path, " +
                "and pull are blocked until they are resolved by hand.");
    }

    /// <summary>
    /// Evaluates one unsafe action against the current unmerged state
    /// (spec 13 §21). The answer is a typed decision, never a thrown exception:
    /// a conflicted repository blocks commit/commit-all/pull, a path-scoped
    /// discard/restore blocks only the conflicted path it targets, and an
    /// unreadable/vanished repository is refused because safety cannot be
    /// established.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="action">The unsafe action to evaluate.</param>
    /// <param name="changePath">The worktree-relative target for a path-scoped discard/restore; ignored for other actions.</param>
    /// <param name="cancellationToken">Cancels the read-only Git invocations (typed, never thrown).</param>
    public async Task<GitConflictActionDecision> CheckActionAsync(
        string repositoryPath,
        GitConflictAction action,
        string? changePath = null,
        CancellationToken cancellationToken = default)
    {
        GitConflictStateResult state = await GetStateAsync(repositoryPath, cancellationToken);

        return state.Status switch
        {
            GitConflictStatus.Available => EvaluateAction(state.State!, action, changePath),
            GitConflictStatus.NoConflicts => Allowed(action),
            _ => BlockedByStatus(action, state),
        };
    }

    /// <summary>
    /// Pure evaluation of one action against an already-read conflict state.
    /// <see cref="GitConflictAction.Discard"/> and
    /// <see cref="GitConflictAction.Restore"/> are evaluated per path when a
    /// target is supplied; commit/commit-all/pull are whole-worktree actions.
    /// </summary>
    public static GitConflictActionDecision EvaluateAction(
        GitConflictState state,
        GitConflictAction action,
        string? changePath = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        IReadOnlyList<GitConflictEntry> conflicts = state.Conflicts;
        if (conflicts.Count == 0)
        {
            return Allowed(action);
        }

        bool pathScoped = action is GitConflictAction.Discard or GitConflictAction.Restore;
        if (pathScoped && !string.IsNullOrWhiteSpace(changePath))
        {
            // A request path is untrusted: reject an unusable one rather than
            // comparing a path that cannot be a worktree-relative file path.
            if (!GitDocumentPath.TryNormalizeRelativePath(changePath, out string target))
            {
                return new GitConflictActionDecision(
                    action,
                    true,
                    "PathRejected",
                    $"The {ActionLabel(action)} action was rejected: '{changePath}' is not a " +
                    "worktree-relative file path.",
                    Array.Empty<string>());
            }

            GitConflictEntry? match = conflicts.FirstOrDefault(conflict =>
                string.Equals(conflict.Path, target, StringComparison.Ordinal));
            if (match is null)
            {
                // Restoring a different, non-conflicted file does not overwrite
                // conflict state, so it stays available (spec 13 §21).
                return Allowed(action);
            }

            return new GitConflictActionDecision(
                action,
                true,
                "ConflictedPath",
                $"The {ActionLabel(action)} action is blocked for '{match.Path}': it is an unmerged " +
                "conflict participant. Resolve the conflict first.",
                new[] { match.Path });
        }

        IReadOnlyList<string> conflictedPaths = conflicts
            .Select(conflict => conflict.Path)
            .ToArray();

        string scope = pathScoped ? "is blocked while the repository has unresolved conflicts" : "is blocked";
        return new GitConflictActionDecision(
            action,
            true,
            "ConflictedWorktree",
            $"The {ActionLabel(action)} action {scope}; nothing ran and no file was changed. " +
            "Resolve the conflicts first. " + DescribePaths(conflictedPaths),
            conflictedPaths);
    }

    /// <summary>
    /// The unmerged entries of an SCM-02 snapshot. Shared so callers never
    /// re-implement conflict detection from raw Git output (spec 13 §6).
    /// </summary>
    public static IReadOnlyList<GitStatusEntry> FindConflicts(GitStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Entries.Where(entry => entry.IsConflicted).ToArray();
    }

    private static GitConflictActionDecision Allowed(GitConflictAction action) =>
        new(
            action,
            false,
            "Allowed",
            $"The {ActionLabel(action)} action is allowed; the repository has no unresolved conflicts.",
            Array.Empty<string>());

    private static GitConflictActionDecision BlockedByStatus(
        GitConflictAction action,
        GitConflictStateResult state) =>
        new(
            action,
            true,
            state.Status.ToString(),
            GitCredentialRedactor.Redact(
                $"The {ActionLabel(action)} action is blocked because the conflict state could not be " +
                $"read safely ({state.Status}). Nothing ran. " +
                (state.Message ?? string.Empty)).Trim(),
            Array.Empty<string>());

    private static string ActionLabel(GitConflictAction action) => action switch
    {
        GitConflictAction.Commit => "Commit",
        GitConflictAction.CommitAll => "Commit All",
        GitConflictAction.Discard => "Discard",
        GitConflictAction.Restore => "Restore",
        GitConflictAction.Pull => "Pull",
        _ => action.ToString(),
    };

    private static string DescribePaths(IReadOnlyList<string> paths)
    {
        IEnumerable<string> listed = paths.Count <= MaxListedPaths
            ? paths
            : paths.Take(MaxListedPaths);

        string suffix = paths.Count > MaxListedPaths
            ? $" (and {paths.Count - MaxListedPaths} more)"
            : string.Empty;

        return $"Affected paths: {string.Join(", ", listed)}{suffix}.";
    }

    private static bool IsDocxPath(string path) =>
        path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    private static bool PathExists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static GitConflictStatus MapStatusFailure(GitStatusResult status, string worktreeRoot)
    {
        if (!PathExists(worktreeRoot))
        {
            return GitConflictStatus.WorktreeMissing;
        }

        if (status.Failure is { } failure)
        {
            if (failure.Status == GitProcessStatus.Unavailable
                || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled)
            {
                return GitConflictStatus.GitUnavailable;
            }

            if (failure.ErrorKind == GitProcessErrorKind.GitLockContention)
            {
                return GitConflictStatus.IndexLocked;
            }
        }

        return status.Status switch
        {
            GitRepositoryStatus.NoRepository => GitConflictStatus.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitConflictStatus.GitUnavailable,
            _ => GitConflictStatus.Unreadable,
        };
    }

    private static string StatusFailureMessage(GitConflictStatus status, GitProcessResult? failure) =>
        status switch
        {
            GitConflictStatus.WorktreeMissing =>
                "The worktree is no longer available; no conflict state was read.",
            GitConflictStatus.GitUnavailable =>
                "Git is not available; no conflict state was read.",
            GitConflictStatus.IndexLocked =>
                "The Git index is locked by another operation; no conflict state was read safely.",
            GitConflictStatus.Unreadable =>
                failure?.ErrorMessage ?? "Git status could not be read or parsed; no conflict state was read safely.",
            _ => failure?.ErrorMessage ?? "The conflict state could not be read.",
        };
}
