import { useState, type FormEvent } from 'react';

/**
 * SCM-16 — worktree create & remove surface (spec 13 §16.3–§16.5).
 *
 * The create form only accepts an EXISTING ref: there are no branch-creation
 * controls anywhere in this dialog, and the copy states that tags/commits
 * become detached worktrees. Removal is explicit and reviewed — a two-step
 * confirmation is required, and dirty, locked, missing, or main worktrees are
 * shown as blocked; no force option exists in this UI.
 */

export interface WorktreeCreateRequest {
  targetPath: string;
  /** Existing branch, tag, remote-tracking ref, or commit id. */
  ref: string;
}

/** Presentation mirror of the backend removal-preflight outcome. */
export interface RemovableWorktree {
  path: string;
  branchName: string | null;
  /** Git reported uncommitted changes; removal is blocked. */
  isDirty?: boolean;
  /** Cleanliness could not be established; removal is blocked (fail closed). */
  isCleanlinessUnknown?: boolean;
  isLocked?: boolean;
  /** Registered but the directory is missing; use Refresh Worktrees instead. */
  isPrunable?: boolean;
  isMainWorktree?: boolean;
}

/**
 * The single reason a worktree cannot be removed, or null when removal is
 * allowed. Mirrors the backend preflight order (main, locked, missing, dirty).
 */
export function removalBlockedReason(worktree: RemovableWorktree): string | null {
  if (worktree.isMainWorktree) {
    return 'The main worktree cannot be removed.';
  }
  if (worktree.isLocked) {
    return 'Locked worktrees must be unlocked in Git first.';
  }
  if (worktree.isPrunable) {
    return 'The worktree directory is missing; use Refresh worktrees for stale metadata.';
  }
  if (worktree.isDirty) {
    return 'Contains uncommitted changes; removal is blocked.';
  }
  if (worktree.isCleanlinessUnknown) {
    return 'Dirty state could not be determined; removal is blocked.';
  }
  return null;
}

interface WorktreeCreateDialogProps {
  /** False when Git is not installed/discoverable (spec 13 §16 edge cases). */
  gitAvailable: boolean;
  /** False when the active workspace folder is not inside a Git repository. */
  repositoryDetected: boolean;
  onCreateWorktree?: (request: WorktreeCreateRequest) => void;
  /** Worktrees offered for removal, with their preflight state. */
  removableWorktrees?: RemovableWorktree[];
  onRemoveWorktree?: (path: string) => void;
  /** Safe metadata refresh (prune of stale entries Git itself reports). */
  onRefreshWorktrees?: () => void;
  /** Any worktree operation in flight; all actions are disabled while true. */
  busy?: boolean;
  errorMessage?: string | null;
}

function branchLabel(worktree: RemovableWorktree): string {
  return worktree.branchName ?? 'Detached';
}

export default function WorktreeCreateDialog({
  gitAvailable,
  repositoryDetected,
  onCreateWorktree,
  removableWorktrees,
  onRemoveWorktree,
  onRefreshWorktrees,
  busy = false,
  errorMessage,
}: WorktreeCreateDialogProps) {
  const [targetPath, setTargetPath] = useState('');
  const [ref, setRef] = useState('');
  const [confirmingPath, setConfirmingPath] = useState<string | null>(null);

  if (!gitAvailable) {
    return (
      <div
        data-testid="worktree-dialog-git-unavailable"
        className="w-full max-w-xl p-4 text-sm text-slate-600 bg-slate-50 border border-slate-200 rounded-xl"
      >
        Git is not available on this machine. Install Git to create or remove worktrees.
      </div>
    );
  }

  if (!repositoryDetected) {
    return (
      <div
        data-testid="worktree-dialog-no-repository"
        className="w-full max-w-xl p-4 text-sm text-slate-600 bg-slate-50 border border-slate-200 rounded-xl"
      >
        This folder is not inside a Git repository.
      </div>
    );
  }

  const canCreate = !busy && targetPath.trim().length > 0 && ref.trim().length > 0;

  function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canCreate) {
      return;
    }
    onCreateWorktree?.({ targetPath: targetPath.trim(), ref: ref.trim() });
  }

  function confirmRemoval(path: string) {
    setConfirmingPath(null);
    onRemoveWorktree?.(path);
  }

  return (
    <div className="w-full max-w-xl p-4 bg-white rounded-xl border border-slate-200 shadow-sm space-y-4">
      <section aria-labelledby="worktree-create-heading" className="space-y-3">
        <h3 id="worktree-create-heading" className="text-sm font-semibold text-slate-700">
          Create Worktree
        </h3>
        <p className="text-xs text-slate-500">
          Created from an existing branch, tag, or commit. No branch is ever created: existing
          branches are checked out, tags and commits become detached worktrees.
        </p>

        {errorMessage && (
          <div
            role="alert"
            className="p-3 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg"
          >
            {errorMessage}
          </div>
        )}

        <form className="space-y-3" onSubmit={handleCreate}>
          <div className="space-y-1">
            <label htmlFor="worktree-target-path" className="block text-xs font-medium text-slate-600">
              Target path
            </label>
            <input
              id="worktree-target-path"
              type="text"
              value={targetPath}
              disabled={busy}
              placeholder="C:\projects\app-wt-feature"
              onChange={(event) => setTargetPath(event.target.value)}
              className="w-full px-3 py-1.5 text-sm font-mono text-slate-800 border border-slate-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
            />
          </div>

          <div className="space-y-1">
            <label htmlFor="worktree-ref" className="block text-xs font-medium text-slate-600">
              Existing ref (branch, tag, or commit)
            </label>
            <input
              id="worktree-ref"
              type="text"
              value={ref}
              disabled={busy}
              placeholder="feature-x"
              onChange={(event) => setRef(event.target.value)}
              className="w-full px-3 py-1.5 text-sm font-mono text-slate-800 border border-slate-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
            />
          </div>

          <button
            type="submit"
            disabled={!canCreate}
            className="px-3 py-1.5 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 transition-colors"
          >
            {busy ? 'Working...' : 'Create worktree'}
          </button>
        </form>
      </section>

      {removableWorktrees && removableWorktrees.length > 0 && (
        <section aria-labelledby="worktree-remove-heading" className="space-y-3 border-t border-slate-200 pt-3">
          <h3 id="worktree-remove-heading" className="text-sm font-semibold text-slate-700">
            Remove Worktree
          </h3>
          <p className="text-xs text-slate-500">
            Removal deletes the worktree directory and its Git registration. Dirty worktrees
            cannot be removed; there is no force option.
          </p>

          <ul className="space-y-2">
            {removableWorktrees.map((worktree) => {
              const blockedReason = removalBlockedReason(worktree);
              const isConfirming = confirmingPath === worktree.path;

              return (
                <li
                  key={worktree.path}
                  className="p-3 bg-slate-50 border border-slate-200 rounded-lg space-y-2"
                >
                  <div className="flex flex-col min-w-0">
                    <span className="text-sm font-mono text-slate-800 truncate">{worktree.path}</span>
                    <span className="text-xs font-medium text-slate-600">{branchLabel(worktree)}</span>
                  </div>

                  {blockedReason ? (
                    <span
                      data-testid={`worktree-remove-blocked-${worktree.path}`}
                      className="inline-block text-xs px-2 py-0.5 rounded bg-amber-50 text-amber-700 font-medium border border-amber-200"
                    >
                      {blockedReason}
                    </span>
                  ) : isConfirming ? (
                    <div
                      data-testid={`worktree-remove-confirm-${worktree.path}`}
                      className="space-y-2"
                    >
                      <p className="text-xs text-red-700">
                        Remove worktree at {worktree.path}? This deletes the worktree directory and
                        its Git registration. This action cannot be undone.
                      </p>
                      <div className="flex gap-2">
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => confirmRemoval(worktree.path)}
                          className="px-3 py-1.5 text-sm font-medium text-white bg-red-600 rounded-lg hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50 transition-colors"
                        >
                          Confirm remove
                        </button>
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => setConfirmingPath(null)}
                          className="px-3 py-1.5 text-sm font-medium text-slate-700 bg-white border border-slate-300 rounded-lg hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 transition-colors"
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  ) : (
                    <button
                      type="button"
                      aria-label={`Remove worktree ${worktree.path}`}
                      disabled={busy}
                      onClick={() => setConfirmingPath(worktree.path)}
                      className="px-3 py-1.5 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-lg hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-red-400 disabled:opacity-50 transition-colors"
                    >
                      Remove
                    </button>
                  )}
                </li>
              );
            })}
          </ul>

          {onRefreshWorktrees && (
            <div className="space-y-1">
              <button
                type="button"
                disabled={busy}
                onClick={() => onRefreshWorktrees()}
                className="px-3 py-1.5 text-sm font-medium text-slate-700 bg-white border border-slate-300 rounded-lg hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 transition-colors"
              >
                Refresh worktrees
              </button>
              <p className="text-xs text-slate-500">
                Clears stale Git metadata for worktrees whose directories are gone. Valid and
                locked worktrees are never touched.
              </p>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
