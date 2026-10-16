using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-16 — safe Git worktree creation and removal (spec 13 §16.3–§16.5).
///
/// Worktrees are created only from refs that already exist: this contract can
/// never create a branch as a side effect, and it has no branch-management
/// surface. Removal is explicit, never forced, and refuses dirty, locked, main,
/// or missing worktrees. Implementations invoke Git exclusively through
/// <see cref="IGitProcessRunner"/> and resolve the true worktree root before
/// any mutation so the SCM-01 per-worktree mutation queue keys correctly.
/// </summary>
public interface IGitWorktreeMutationService
{
    /// <summary>
    /// Creates a worktree at <see cref="GitWorktreeCreateRequest.TargetPath"/>
    /// from the existing ref <see cref="GitWorktreeCreateRequest.Ref"/>
    /// (existing branch, tag/ref, or explicit commit). No branch is created:
    /// a local branch is checked out as-is and every other ref resolves to a
    /// detached worktree at its commit.
    /// </summary>
    Task<GitWorktreeCreateResult> CreateWorktreeAsync(
        GitWorktreeCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the registered worktree at
    /// <see cref="GitWorktreeRemoveRequest.WorktreePath"/> through the normal
    /// Git removal (never <c>--force</c>). The worktree must be registered,
    /// clean, unlocked, and not the main working tree.
    /// </summary>
    Task<GitWorktreeRemoveResult> RemoveWorktreeAsync(
        GitWorktreeRemoveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the safe worktree metadata refresh (<c>git worktree prune</c>) and
    /// returns the re-read registry. Valid worktrees are never deleted; only
    /// stale metadata of missing, unlocked worktrees is pruned by Git itself.
    /// </summary>
    Task<GitWorktreeRefreshResult> RefreshWorktreesAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
