using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-15 — Git worktree discovery and "open existing worktree" context
/// (spec 13 §16).
///
/// Worktrees are a filesystem/worktree-context feature, not branch management:
/// this contract exposes read-only Git facts and the active filesystem context.
/// It never creates branches, switches branches, or mutates Git history.
/// Implementations invoke Git exclusively through <see cref="IGitProcessRunner"/>.
/// </summary>
public interface IGitWorktreeService
{
    /// <summary>
    /// Lists every worktree registered for the repository containing
    /// <paramref name="workingDirectory"/> using `git worktree list --porcelain`.
    /// A non-repository directory yields a typed <see cref="GitWorktreeListStatus.NotARepository"/>
    /// state; missing Git yields <see cref="GitWorktreeListStatus.GitUnavailable"/>.
    /// </summary>
    Task<GitWorktreeListResult> ListWorktreesAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a Git-registered worktree as the active Document IDE
    /// filesystem/worktree context (spec 13 §16.2).
    ///
    /// Only locations Git itself registers can be opened, and a worktree that
    /// is not attached to the workspace requires the explicit FS-01
    /// folder-source attachment flow — nothing is attached, detached, or
    /// reorganized by this call. No branch is switched or created.
    /// </summary>
    Task<GitWorktreeOpenResult> OpenWorktreeAsync(
        GitWorktreeOpenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The worktree made active by the last successful open, or <c>null</c>
    /// when no worktree context was opened in this session.
    /// </summary>
    GitWorktreeInfo? ActiveWorktree { get; }
}
