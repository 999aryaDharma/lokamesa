import { useCallback, useEffect, useMemo, useState } from 'react';
import InspectorCard, { InspectorField } from '../shell/InspectorCard';
import WorktreeCreateDialog, { type WorktreeCreateRequest } from './WorktreeCreateDialog';
import WorktreeList, { type WorktreeListEntry } from './WorktreeList';
import {
  createWorktree,
  fetchRemoteState,
  fetchWorktrees,
  openWorktree,
  pruneWorktrees,
  removeWorktree,
  typedFailureMessage,
} from './sourceControlApi';
import type { SourceControlRemoteModel } from './sourceControlModels';

/**
 * UI-03 screen 07 — Worktrees.
 *
 * Composed from the shipped SCM-15 `WorktreeList` and SCM-16
 * `WorktreeCreateDialog`, driven by the SCM-18 routes. Opening a registered
 * worktree that is not attached to the workspace returns the typed
 * `RequiresAttachmentFlow` signal; this screen surfaces it as an explicit
 * action into the existing FS-01 folder-source flow and never attaches
 * automatically. Prune is the safe metadata refresh (it never deletes a valid
 * worktree). Canvas files are never moved by any action here.
 */

export interface WorktreesScreenProps {
  path: string | null;
  workspaceId: string;
  inspectorOpen: boolean;
  onRequestAttachment: (path: string) => void;
}

function basename(path: string): string {
  const parts = path.replace(/\\/g, '/').split('/').filter(Boolean);
  return parts.length > 0 ? parts[parts.length - 1] : path;
}

interface AttachmentRequired {
  path: string;
  message: string;
}

const buttonClassName =
  'rounded-lg border border-mac-hairline-strong bg-mac-content px-2.5 py-1 text-xs font-medium ' +
  'text-mac-text hover:bg-mac-chip focus:outline-none focus:ring-2 focus:ring-mac-accent/40 ' +
  'disabled:opacity-50 transition-colors';

const primaryButtonClassName =
  'rounded-lg bg-mac-accent px-2.5 py-1 text-xs font-medium text-white hover:bg-mac-accent/90 ' +
  'focus:outline-none focus:ring-2 focus:ring-mac-accent/40 disabled:opacity-50 transition-colors';

export default function WorktreesScreen({
  path,
  workspaceId,
  inspectorOpen,
  onRequestAttachment,
}: WorktreesScreenProps) {
  const [worktrees, setWorktrees] = useState<WorktreeListEntry[]>([]);
  const [gitAvailable, setGitAvailable] = useState(true);
  const [repositoryDetected, setRepositoryDetected] = useState(true);
  const [loading, setLoading] = useState(false);
  const [listError, setListError] = useState<string | null>(null);
  const [remote, setRemote] = useState<SourceControlRemoteModel | null>(null);
  const [busyPath, setBusyPath] = useState<string | null>(null);
  const [attachmentRequired, setAttachmentRequired] = useState<AttachmentRequired | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [createBusy, setCreateBusy] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [removeTarget, setRemoveTarget] = useState<string | null>(null);
  const [removeBusy, setRemoveBusy] = useState(false);
  const [removeError, setRemoveError] = useState<string | null>(null);
  const [pruneBusy, setPruneBusy] = useState(false);

  const applyList = useCallback((entries: WorktreeListEntry[]) => {
    setWorktrees(entries);
  }, []);

  const loadWorktrees = useCallback(async () => {
    if (!path) {
      setWorktrees([]);
      setListError(null);
      return;
    }
    setLoading(true);
    setListError(null);
    try {
      const result = await fetchWorktrees(path);
      setGitAvailable(result.status !== 'GitUnavailable');
      setRepositoryDetected(result.status !== 'NotARepository');
      applyList(
        result.worktrees.map((entry) => ({
          path: entry.path,
          headCommit: entry.headCommit,
          branchName: entry.branchName,
          isDetached: entry.isDetached,
          isBare: entry.isBare,
          isLocked: entry.isLocked,
          isPrunable: entry.isPrunable,
          isMainWorktree: entry.isMainWorktree,
        })),
      );
      if (!result.isSuccess) {
        setListError(result.message ?? `Git reported ${result.status}.`);
      }
    } catch (error) {
      setWorktrees([]);
      setListError(error instanceof Error ? error.message : 'The worktree list could not be read.');
    } finally {
      setLoading(false);
    }
  }, [applyList, path]);

  const loadRemote = useCallback(async () => {
    if (!path) {
      setRemote(null);
      return;
    }
    try {
      setRemote(await fetchRemoteState(path));
    } catch {
      setRemote(null);
    }
  }, [path]);

  useEffect(() => {
    void loadWorktrees();
    void loadRemote();
  }, [loadRemote, loadWorktrees]);

  const mainWorktree = useMemo(
    () => worktrees.find((entry) => entry.isMainWorktree) ?? worktrees[0] ?? null,
    [worktrees],
  );

  async function handleOpen(worktreePath: string) {
    if (!path) return;
    setBusyPath(worktreePath);
    setAttachmentRequired(null);
    setListError(null);
    try {
      const result = await openWorktree(workspaceId, path, worktreePath);
      if (result.worktrees) {
        applyList(
          result.worktrees.map((entry) => ({
            path: entry.path,
            headCommit: entry.headCommit,
            branchName: entry.branchName,
            isDetached: entry.isDetached,
            isBare: entry.isBare,
            isLocked: entry.isLocked,
            isPrunable: entry.isPrunable,
            isMainWorktree: entry.isMainWorktree,
          })),
        );
      }
      if (result.status === 'RequiresAttachmentFlow') {
        // Typed signal only: nothing was attached, so the FS-01 flow is the
        // only path forward.
        setAttachmentRequired({
          path: worktreePath,
          message:
            result.message ??
            'This worktree is registered in Git but is not attached to this workspace.',
        });
        return;
      }
      if (!result.isSuccess) {
        setListError(typedFailureMessage('The worktree could not be opened', result));
      }
    } catch (error) {
      setListError(error instanceof Error ? error.message : 'The worktree could not be opened.');
    } finally {
      setBusyPath(null);
    }
  }

  async function handleCreate(request: WorktreeCreateRequest) {
    if (!path) return;
    setCreateBusy(true);
    setCreateError(null);
    try {
      const result = await createWorktree(path, request.targetPath, request.ref);
      if (!result.isSuccess) {
        setCreateError(typedFailureMessage('The worktree could not be created', result));
        return;
      }
      if (result.worktrees) {
        applyList(
          result.worktrees.map((entry) => ({
            path: entry.path,
            headCommit: entry.headCommit,
            branchName: entry.branchName,
            isDetached: entry.isDetached,
            isBare: entry.isBare,
            isLocked: entry.isLocked,
            isPrunable: entry.isPrunable,
            isMainWorktree: entry.isMainWorktree,
          })),
        );
      } else {
        await loadWorktrees();
      }
      setCreateOpen(false);
    } catch (error) {
      setCreateError(
        error instanceof Error ? error.message : 'The worktree could not be created.',
      );
    } finally {
      setCreateBusy(false);
    }
  }

  async function handleRemoveConfirmed() {
    if (!path || !removeTarget) return;
    setRemoveBusy(true);
    setRemoveError(null);
    try {
      const result = await removeWorktree(path, removeTarget);
      if (!result.isSuccess) {
        setRemoveError(typedFailureMessage('The worktree could not be removed', result));
        return;
      }
      if (result.worktrees) {
        applyList(
          result.worktrees.map((entry) => ({
            path: entry.path,
            headCommit: entry.headCommit,
            branchName: entry.branchName,
            isDetached: entry.isDetached,
            isBare: entry.isBare,
            isLocked: entry.isLocked,
            isPrunable: entry.isPrunable,
            isMainWorktree: entry.isMainWorktree,
          })),
        );
      } else {
        await loadWorktrees();
      }
      setRemoveTarget(null);
    } catch (error) {
      setRemoveError(error instanceof Error ? error.message : 'The worktree could not be removed.');
    } finally {
      setRemoveBusy(false);
    }
  }

  async function handlePrune() {
    if (!path) return;
    setPruneBusy(true);
    setListError(null);
    try {
      const result = await pruneWorktrees(path);
      if (!result.isSuccess) {
        setListError(typedFailureMessage('The worktree metadata refresh failed', result));
        return;
      }
      if (result.worktrees) {
        applyList(
          result.worktrees.map((entry) => ({
            path: entry.path,
            headCommit: entry.headCommit,
            branchName: entry.branchName,
            isDetached: entry.isDetached,
            isBare: entry.isBare,
            isLocked: entry.isLocked,
            isPrunable: entry.isPrunable,
            isMainWorktree: entry.isMainWorktree,
          })),
        );
      } else {
        await loadWorktrees();
      }
    } catch (error) {
      setListError(
        error instanceof Error ? error.message : 'The worktree metadata refresh failed.',
      );
    } finally {
      setPruneBusy(false);
    }
  }

  if (!path) {
    return (
      <section
        aria-label="Worktrees"
        data-testid="worktrees-screen"
        className="flex h-full min-h-0 w-full flex-col items-center justify-center gap-3 bg-mac-content p-6 text-center"
      >
        <h1 className="text-sm font-semibold text-mac-text">Worktrees</h1>
        <p data-testid="worktrees-no-path" className="max-w-md text-xs text-mac-text-2">
          Attach a folder source first. Worktrees are read from the Git repository that contains an
          attached workspace folder.
        </p>
        <button type="button" onClick={() => onRequestAttachment('')} className={primaryButtonClassName}>
          Attach folder source
        </button>
      </section>
    );
  }

  return (
    <section
      aria-label="Worktrees"
      data-testid="worktrees-screen"
      className="flex h-full min-h-0 w-full bg-mac-content"
    >
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-11 shrink-0 items-center justify-between gap-3 border-b border-mac-hairline px-3">
          <div className="flex min-w-0 items-baseline gap-2">
            <h1 className="text-sm font-semibold text-mac-text">Worktrees</h1>
            <span data-testid="worktrees-context" className="truncate text-[11px] text-mac-text-2">
              {path} · {worktrees.length} {worktrees.length === 1 ? 'worktree' : 'worktrees'}
              {mainWorktree?.branchName ? ` · branch ${mainWorktree.branchName}` : ''}
            </span>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={pruneBusy}
              onClick={() => void handlePrune()}
              className={buttonClassName}
            >
              {pruneBusy ? 'Pruning…' : 'Prune'}
            </button>
            <button
              type="button"
              aria-label="New worktree"
              onClick={() => {
                setCreateError(null);
                setCreateOpen(true);
              }}
              className={primaryButtonClassName}
            >
              + New worktree
            </button>
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto p-3">
          {attachmentRequired && (
            <div
              data-testid="worktree-attachment-required"
              role="status"
              className="mb-3 max-w-3xl rounded-lg border border-mac-warning/40 bg-mac-warning/10 p-3 text-xs text-mac-text"
            >
              <p className="font-medium">Attachment required</p>
              <p className="text-mac-text-2">{attachmentRequired.message}</p>
              <p className="mt-1 font-mono text-[11px] text-mac-text-3">{attachmentRequired.path}</p>
              <p className="mt-1 text-mac-text-2">
                Nothing was attached. Continue through the folder-source attachment flow to use
                this worktree.
              </p>
              <button
                type="button"
                onClick={() => onRequestAttachment(attachmentRequired.path)}
                className={`${primaryButtonClassName} mt-2`}
              >
                Attach folder source
              </button>
            </div>
          )}

          <div className="mb-2 grid grid-cols-[minmax(0,1fr)_140px_120px_180px] gap-2 px-3 text-[10px] font-semibold uppercase tracking-[0.06em] text-mac-text-3">
            <span>Path</span>
            <span>Branch</span>
            <span>State</span>
            <span>Actions</span>
          </div>

          <WorktreeList
            gitAvailable={gitAvailable}
            repositoryDetected={repositoryDetected}
            worktrees={worktrees}
            onOpenWorktree={(worktreePath) => void handleOpen(worktreePath)}
            onRemoveWorktree={(worktreePath) => {
              setRemoveError(null);
              setRemoveTarget(worktreePath);
            }}
            busyPath={busyPath}
            errorMessage={listError}
            isLoading={loading}
          />

          <p className="mt-3 max-w-3xl text-[11px] text-mac-text-3">
            Removing a worktree with uncommitted changes is blocked — commit or discard first.
            Worktree operations never move files on the canvas.
          </p>
        </div>
      </div>

      {inspectorOpen && (
        <aside
          aria-label="Worktrees inspector"
          className="flex h-full w-[280px] shrink-0 flex-col gap-4 overflow-y-auto border-l border-mac-hairline bg-mac-inspector px-3 py-3"
        >
          <header className="flex items-center justify-between">
            <h2 className="text-[11px] font-semibold text-mac-text">Repository</h2>
          </header>

          <InspectorCard title="Repository" testId="worktrees-inspector-repository">
            <InspectorField label="Repository" value={basename(path)} />
            <InspectorField label="Path" value={<span className="font-mono">{path}</span>} />
            <InspectorField label="Branch" value={mainWorktree?.branchName ?? '—'} />
            <InspectorField
              label="Upstream"
              value={remote?.upstreamReference ?? (remote?.hasUpstream === false ? 'No upstream' : '—')}
            />
            <InspectorField label="Ahead" value={remote?.ahead ?? '—'} tone={remote?.ahead ? 'success' : 'default'} />
            <InspectorField label="Behind" value={remote?.behind ?? '—'} tone={remote?.behind ? 'warning' : 'default'} />
            <InspectorField label="Worktrees" value={worktrees.length} />
            <InspectorField label="Remote" value={remote?.remoteName ?? '—'} />
          </InspectorCard>

          <InspectorCard title="Worktree rules" testId="worktree-rules">
            <InspectorField label="Main worktree" value="Not removable" />
            <InspectorField label="Clean required" value="To remove" />
            <InspectorField label="Checkout" value="Existing ref" />
            <InspectorField label="Canvas files" value="Never moved" />
            <p className="pt-1 text-[11px] text-mac-text-3">
              Worktrees are Git checkouts. The canvas never rearranges documents when a worktree
              changes.
            </p>
          </InspectorCard>
        </aside>
      )}

      {createOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/20 p-4">
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Create worktree"
            className="max-h-full w-full max-w-2xl overflow-y-auto rounded-xl bg-mac-content shadow-xl"
          >
            <WorktreeCreateDialog
              gitAvailable={gitAvailable}
              repositoryDetected={repositoryDetected}
              onCreateWorktree={(request) => void handleCreate(request)}
              busy={createBusy}
              errorMessage={createError}
            />
            <div className="flex justify-end gap-2 border-t border-mac-hairline p-2">
              <button
                type="button"
                disabled={createBusy}
                onClick={() => setCreateOpen(false)}
                className={buttonClassName}
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {removeTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/20 p-4">
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Remove worktree"
            className="w-full max-w-lg space-y-3 rounded-xl border border-mac-hairline bg-mac-content p-4 shadow-xl"
          >
            <h2 className="text-sm font-semibold text-mac-text">Remove worktree</h2>
            <p className="font-mono text-xs text-mac-text">{removeTarget}</p>
            <p className="text-xs text-mac-text-2">
              This deletes the worktree directory and its Git registration. Dirty worktrees are
              refused and nothing is forced. This action cannot be undone.
            </p>
            {removeError && (
              <div role="alert" className="rounded-lg border border-mac-danger/40 bg-mac-danger/10 p-2 text-xs text-mac-danger">
                {removeError}
              </div>
            )}
            <div className="flex justify-end gap-2">
              <button
                type="button"
                disabled={removeBusy}
                onClick={() => setRemoveTarget(null)}
                className={buttonClassName}
              >
                Cancel
              </button>
              <button
                type="button"
                disabled={removeBusy}
                onClick={() => void handleRemoveConfirmed()}
                className="rounded-lg bg-mac-danger px-2.5 py-1 text-xs font-medium text-white hover:bg-mac-danger/90 disabled:opacity-50"
              >
                {removeBusy ? 'Removing…' : 'Confirm remove'}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
