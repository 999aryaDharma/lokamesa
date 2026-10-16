/**
 * SCM-15 — read-only mirror of the Core `GitWorktreeInfo` contract.
 *
 * This is presentation metadata only: Document IDE never creates, switches,
 * renames or deletes branches from this view. `path` is the canonical path
 * reported by `git worktree list --porcelain`.
 */
export interface WorktreeListEntry {
  path: string;
  headCommit: string;
  branchName: string | null;
  isDetached: boolean;
  isBare: boolean;
  isLocked: boolean;
  isPrunable: boolean;
  isMainWorktree: boolean;
}

interface WorktreeListProps {
  /** False when Git is not installed/discoverable (spec 13 §16 edge cases). */
  gitAvailable: boolean;
  /** False when the active workspace folder is not inside a Git repository. */
  repositoryDetected: boolean;
  worktrees: WorktreeListEntry[];
  /** Canonical path of the worktree currently acting as Document IDE context. */
  activeWorktreePath?: string | null;
  onOpenWorktree?: (path: string) => void;
  /** Path currently being opened; all open actions are disabled while set. */
  busyPath?: string | null;
  errorMessage?: string | null;
  isLoading?: boolean;
}

function branchLabel(entry: WorktreeListEntry): string {
  if (entry.branchName) {
    return entry.branchName;
  }
  if (entry.isDetached) {
    return 'Detached';
  }
  if (entry.isBare) {
    return 'Bare';
  }
  return 'Unknown';
}

export default function WorktreeList({
  gitAvailable,
  repositoryDetected,
  worktrees,
  activeWorktreePath,
  onOpenWorktree,
  busyPath,
  errorMessage,
  isLoading = false,
}: WorktreeListProps) {
  if (!gitAvailable) {
    return (
      <div
        data-testid="worktree-list-git-unavailable"
        className="w-full max-w-xl p-4 text-sm text-slate-600 bg-slate-50 border border-slate-200 rounded-xl"
      >
        Git is not available on this machine. Install Git to see worktrees.
      </div>
    );
  }

  if (!repositoryDetected) {
    return (
      <div
        data-testid="worktree-list-no-repository"
        className="w-full max-w-xl p-4 text-sm text-slate-600 bg-slate-50 border border-slate-200 rounded-xl"
      >
        This folder is not inside a Git repository.
      </div>
    );
  }

  const busy = busyPath != null;

  return (
    <div className="w-full max-w-xl p-4 bg-white rounded-xl border border-slate-200 shadow-sm space-y-3">
      <h3 className="text-sm font-semibold text-slate-700">Git Worktrees</h3>

      {errorMessage && (
        <div
          role="alert"
          className="p-3 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg"
        >
          {errorMessage}
        </div>
      )}

      {isLoading ? (
        <div data-testid="worktree-list-loading" className="text-sm text-slate-500">
          Loading worktrees...
        </div>
      ) : worktrees.length === 0 ? (
        <div data-testid="worktree-list-empty" className="text-sm text-slate-500">
          Git reported no worktrees for this repository.
        </div>
      ) : (
        <ul className="space-y-2">
          {worktrees.map((entry) => {
            const isActive = activeWorktreePath === entry.path;
            const isBusy = busyPath === entry.path;

            return (
              <li
                key={entry.path}
                className="p-3 bg-slate-50 border border-slate-200 rounded-lg flex items-center justify-between gap-2"
              >
                <div className="flex flex-col min-w-0">
                  <span className="text-sm font-mono text-slate-800 truncate">
                    {entry.path}
                  </span>
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="text-xs font-medium text-slate-600">
                      {branchLabel(entry)}
                    </span>
                    {entry.isMainWorktree && (
                      <span className="text-xs px-2 py-0.5 rounded bg-blue-50 text-blue-700 font-medium border border-blue-200">
                        Main worktree
                      </span>
                    )}
                    {entry.isLocked && (
                      <span className="text-xs px-2 py-0.5 rounded bg-amber-50 text-amber-700 font-medium border border-amber-200">
                        Locked
                      </span>
                    )}
                    {entry.isPrunable && (
                      <span className="text-xs px-2 py-0.5 rounded bg-amber-50 text-amber-700 font-medium border border-amber-200">
                        Prunable
                      </span>
                    )}
                  </div>
                </div>

                {isActive ? (
                  <span
                    data-testid={`worktree-active-${entry.path}`}
                    className="shrink-0 text-xs px-2 py-0.5 rounded bg-emerald-50 text-emerald-700 font-medium border border-emerald-200"
                  >
                    Active
                  </span>
                ) : (
                  <button
                    type="button"
                    aria-label={`Open worktree ${entry.path}`}
                    disabled={busy}
                    onClick={() => onOpenWorktree?.(entry.path)}
                    className="shrink-0 px-3 py-1.5 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 transition-colors"
                  >
                    {isBusy ? 'Opening...' : 'Open'}
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
