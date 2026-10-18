import { useCallback, useEffect, useMemo, useState } from 'react';
import InspectorCard, { InspectorField } from '../shell/InspectorCard';
import ChangeActions from './ChangeActions';
import CommitBox from './CommitBox';
import ConflictPanel from './ConflictPanel';
import DiscardRestoreDialog from './DiscardRestoreDialog';
import GitHistoryPanel from './GitHistoryPanel';
import MarkdownGitDiff, { type GitDiffStatusView } from './MarkdownGitDiff';
import PullDialog from './PullDialog';
import PushButton from './PushButton';
import RemoteStatus from './RemoteStatus';
import SourceControlPanel from './SourceControlPanel';
import { buildUnifiedDiff, type UnifiedDiffView } from './markdownDiff';
import { fileKind } from './sourceControlFileKind';
import {
  fetchConflictState,
  fetchFileVersion,
  fetchMarkdownSource,
  fetchRemoteState,
  fetchSourceControlStatus,
  fetchWorktrees,
  type WorktreeApiEntry,
} from './sourceControlApi';
import type {
  SourceControlChangeEntry,
  SourceControlConflictModel,
  SourceControlRemoteModel,
  SourceControlStatusModel,
} from './sourceControlModels';

/**
 * UI-03 screen 05 — Source control, composed from the shipped SCM components.
 *
 * Real data only:
 *   - the change groups and the repository inspector come from `GET status`;
 *   - the branch/ahead/behind header comes from `GET remote`;
 *   - the worktree count comes from `GET worktrees`;
 *   - the unmerged state comes from `GET conflicts`;
 *   - the commit bar posts the real `commit` / `commit-all` routes.
 *
 * Adaptation, recorded in the handoff: a `.docx` row never renders a text
 * diff. It shows the document-aware change summary and routes to screen 06.
 * A `.md` row renders the exact HEAD ↔ working-tree line diff composed from
 * `GET file-version` and `GET /api/documents/{id}/markdown` (the SCM-04 HTTP
 * diff route is absent in this build, so the screen uses the real read-only
 * sources it does have rather than inventing a diff).
 */

export interface SourceControlScreenProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string | null;
  inspectorOpen: boolean;
  onOpenWorktrees: () => void;
  onOpenDocxDiff: (entry: SourceControlChangeEntry) => void;
  onOpenInWord: (documentId: string) => void;
  onOpenMarkdown: (documentId: string) => void;
  /** Opens the existing FS-01 folder-source attachment flow (never auto-attach). */
  onAttachFolder: () => void;
}

interface MarkdownDiffState {
  loading: boolean;
  status: GitDiffStatusView | null;
  diff: UnifiedDiffView | null;
  error: string | null;
}

function emptyDiffState(): MarkdownDiffState {
  return { loading: false, status: null, diff: null, error: null };
}

function branchLabel(status: SourceControlStatusModel | null): string {
  if (!status || !status.hasRepository) return 'No repository';
  if (status.branchName) return status.branchName;
  return status.isDetached ? 'Detached HEAD' : 'Unknown branch';
}

function aheadBehindLabel(remote: SourceControlRemoteModel | null): string {
  if (!remote) return '';
  if (!remote.hasUpstream) return 'No upstream';
  if (remote.ahead === null || remote.behind === null) return 'Tracking info unavailable';
  return `${remote.ahead} ahead · ${remote.behind} behind`;
}

const secondaryButtonClassName =
  'px-2.5 py-1 text-xs font-medium text-mac-text bg-mac-content border border-mac-hairline-strong ' +
  'rounded-lg hover:bg-mac-chip focus:outline-none focus:ring-2 focus:ring-mac-accent/40 ' +
  'disabled:opacity-50 transition-colors';

function errorMessageOf(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

export default function SourceControlScreen({
  path,
  inspectorOpen,
  onOpenWorktrees,
  onOpenDocxDiff,
  onOpenInWord,
  onOpenMarkdown,
  onAttachFolder,
}: SourceControlScreenProps) {
  const [status, setStatus] = useState<SourceControlStatusModel | null>(null);
  const [statusLoading, setStatusLoading] = useState(false);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [remote, setRemote] = useState<SourceControlRemoteModel | null>(null);
  const [remoteError, setRemoteError] = useState<string | null>(null);
  const [worktrees, setWorktrees] = useState<WorktreeApiEntry[]>([]);
  const [worktreesError, setWorktreesError] = useState<string | null>(null);
  const [conflicts, setConflicts] = useState<SourceControlConflictModel | null>(null);
  const [conflictsError, setConflictsError] = useState<string | null>(null);
  const [selectedEntry, setSelectedEntry] = useState<SourceControlChangeEntry | null>(null);
  const [markdownDiff, setMarkdownDiff] = useState<MarkdownDiffState>(emptyDiffState);
  const [pullOpen, setPullOpen] = useState(false);
  const [discardEntry, setDiscardEntry] = useState<SourceControlChangeEntry | null>(null);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [refreshTick, setRefreshTick] = useState(0);

  const loadStatus = useCallback(async () => {
    if (!path) {
      setStatus(null);
      setStatusError(null);
      return;
    }
    setStatusLoading(true);
    setStatusError(null);
    try {
      setStatus(await fetchSourceControlStatus(path));
    } catch (error) {
      setStatus(null);
      setStatusError(errorMessageOf(error, 'The source control status could not be read.'));
    } finally {
      setStatusLoading(false);
    }
  }, [path]);

  const loadRemote = useCallback(async () => {
    if (!path) {
      setRemote(null);
      setRemoteError(null);
      return;
    }
    try {
      setRemote(await fetchRemoteState(path));
      setRemoteError(null);
    } catch (error) {
      setRemote(null);
      setRemoteError(errorMessageOf(error, 'The remote state could not be read.'));
    }
  }, [path]);

  const loadWorktrees = useCallback(async () => {
    if (!path) {
      setWorktrees([]);
      setWorktreesError(null);
      return;
    }
    try {
      const result = await fetchWorktrees(path);
      setWorktrees(result.worktrees);
      setWorktreesError(result.isSuccess ? null : result.message ?? 'The worktree list could not be read.');
    } catch (error) {
      setWorktrees([]);
      setWorktreesError(errorMessageOf(error, 'The worktree list could not be read.'));
    }
  }, [path]);

  const conflictCount = status?.sections.conflicts.length ?? 0;

  const loadConflicts = useCallback(async () => {
    if (!path || conflictCount === 0) {
      setConflicts(null);
      setConflictsError(null);
      return;
    }
    try {
      setConflicts(await fetchConflictState(path));
      setConflictsError(null);
    } catch (error) {
      setConflicts(null);
      setConflictsError(errorMessageOf(error, 'The conflict state could not be read.'));
    }
  }, [conflictCount, path]);

  useEffect(() => {
    void loadStatus();
    void loadRemote();
    void loadWorktrees();
  }, [loadStatus, loadRemote, loadWorktrees, refreshTick]);

  useEffect(() => {
    void loadConflicts();
  }, [loadConflicts, refreshTick]);

  // A refresh that no longer contains the selected row must not leave a stale
  // diff on screen.
  useEffect(() => {
    if (!status || !selectedEntry) return;
    const stillPresent = [
      ...status.sections.staged,
      ...status.sections.unstaged,
      ...status.sections.untracked,
      ...status.sections.conflicts,
    ].some((entry) => entry.path === selectedEntry.path);
    if (!stillPresent) {
      setSelectedEntry(null);
    }
  }, [selectedEntry, status]);

  useEffect(() => {
    if (!path || !selectedEntry || fileKind(selectedEntry.path) !== 'md') {
      setMarkdownDiff(emptyDiffState());
      return;
    }

    let active = true;
    setMarkdownDiff({ loading: true, status: null, diff: null, error: null });

    void (async () => {
      try {
        const headFile = selectedEntry.originalPath ?? selectedEntry.path;
        const head = await fetchFileVersion(path, headFile, 'HEAD');
        if (!active) return;

        let headText: string | null = null;
        if (head.isAvailable && head.version) {
          if (head.version.isBinary || head.version.content === null) {
            throw new Error('The HEAD version is not a readable Markdown text.');
          }
          headText = head.version.content;
        } else if (head.status !== 'NotFound') {
          throw new Error(head.message ?? `The HEAD version is unavailable (${head.status}).`);
        }

        let workingText: string | null = null;
        if (selectedEntry.state === 'Deleted') {
          workingText = null;
        } else if (selectedEntry.documentId) {
          workingText = await fetchMarkdownSource(selectedEntry.documentId);
        } else {
          throw new Error(
            'This path is not a registered Markdown document, so its working-tree text cannot be read safely.',
          );
        }
        if (!active) return;

        if (headText === null && workingText === null) {
          throw new Error('No readable version of this file exists on either side of the diff.');
        }

        setMarkdownDiff({
          loading: false,
          status: 'available',
          diff: buildUnifiedDiff(headText, workingText, selectedEntry.path),
          error: null,
        });
      } catch (error) {
        if (!active) return;
        setMarkdownDiff({
          loading: false,
          status: 'failed',
          diff: null,
          error: errorMessageOf(error, 'The Markdown diff could not be produced.'),
        });
      }
    })();

    return () => {
      active = false;
    };
  }, [path, selectedEntry]);

  const refreshAll = useCallback(() => {
    setRefreshTick((tick) => tick + 1);
  }, []);

  const handleStatusRefreshed = useCallback((next: SourceControlStatusModel) => {
    setStatus(next);
    setStatusError(null);
  }, []);

  const branch = branchLabel(status);
  const aheadBehind = aheadBehindLabel(remote);
  const selectedKind = selectedEntry ? fileKind(selectedEntry.path) : null;
  const canDiscard =
    selectedEntry !== null &&
    selectedKind === 'md' &&
    selectedEntry.state !== 'Untracked' &&
    selectedEntry.conflictKind === 'None' &&
    selectedEntry.workingTreeChange !== 'Unmodified';

  const inspector = useMemo(() => {
    if (!inspectorOpen) return null;
    return (
      <aside
        aria-label="Source control inspector"
        className="flex h-full w-[280px] shrink-0 flex-col gap-4 overflow-y-auto border-l border-mac-hairline bg-mac-inspector px-3 py-3"
      >
        <header className="flex items-center justify-between gap-2">
          <h2 className="text-[11px] font-semibold text-mac-text">Repository</h2>
        </header>

        <InspectorCard title="Repository" testId="inspector-repository">
          <div className="truncate font-mono text-[11px] text-mac-text-2" title={path ?? undefined}>
            {path ?? 'No folder source'}
          </div>
          <InspectorField label="Branch" value={branch} />
          <InspectorField
            label="Upstream"
            value={remote?.upstreamReference ?? (remote?.hasUpstream === false ? 'No upstream' : '—')}
          />
          <InspectorField
            label="Ahead"
            value={remote?.ahead ?? '—'}
            tone={remote?.ahead ? 'success' : 'default'}
          />
          <InspectorField
            label="Behind"
            value={remote?.behind ?? '—'}
            tone={remote?.behind ? 'warning' : 'default'}
          />
          <InspectorField
            label="Worktrees"
            value={
              <span data-testid="inspector-worktree-count">
                {worktreesError ? '—' : worktrees.length}
              </span>
            }
          />
          <InspectorField
            label="Staged"
            value={
              <span data-testid="inspector-staged-count">
                {status ? status.sections.staged.length : '—'}
              </span>
            }
          />
          <InspectorField
            label="Changes"
            value={
              <span data-testid="inspector-change-count">
                {status ? status.sections.unstaged.length : '—'}
              </span>
            }
          />
          <InspectorField label="Conflicts" value={status ? conflictCount : '—'} />
        </InspectorCard>

        <button
          type="button"
          onClick={onOpenWorktrees}
          className="w-full rounded-md border border-mac-hairline-strong bg-mac-content px-2 py-1.5 text-xs font-medium text-mac-text hover:bg-mac-chip"
        >
          Open worktrees
        </button>

        <InspectorCard title="Remote" testId="inspector-remote">
          {path ? (
            <RemoteStatus path={path} onRemoteChanged={setRemote} />
          ) : (
            <p className="text-xs text-mac-text-3">No folder source attached.</p>
          )}
          {remoteError && (
            <p role="alert" className="text-xs text-mac-danger">
              {remoteError}
            </p>
          )}
          {path && (
            <PushButton
              path={path}
              hasUpstream={remote?.hasUpstream ?? false}
              onPushed={setRemote}
            />
          )}
          {path && (
            <button
              type="button"
              onClick={() => setPullOpen(true)}
              className={secondaryButtonClassName}
            >
              Pull…
            </button>
          )}
        </InspectorCard>

        <InspectorCard title="Git history" testId="inspector-history">
          <button
            type="button"
            aria-expanded={historyOpen}
            onClick={() => setHistoryOpen((open) => !open)}
            className={secondaryButtonClassName}
          >
            {historyOpen ? 'Hide git history' : 'Show git history'}
          </button>
          {historyOpen && path && (
            <div className="max-h-72 overflow-y-auto rounded-lg border border-mac-hairline bg-mac-content">
              <GitHistoryPanel path={path} selectedPath={selectedEntry?.path ?? null} />
            </div>
          )}
        </InspectorCard>

        {worktreesError && (
          <p role="alert" className="text-xs text-mac-danger">
            {worktreesError}
          </p>
        )}
      </aside>
    );
  }, [
    branch,
    conflictCount,
    historyOpen,
    inspectorOpen,
    onOpenWorktrees,
    path,
    remote,
    remoteError,
    selectedEntry?.path,
    status,
    worktrees.length,
    worktreesError,
  ]);

  if (!path) {
    return (
      <section
        aria-label="Source control"
        data-testid="source-control-screen"
        className="flex h-full min-h-0 w-full flex-col items-center justify-center gap-3 bg-mac-content p-6 text-center"
      >
        <h1 className="text-sm font-semibold text-mac-text">Source control</h1>
        <p data-testid="source-control-no-path" className="max-w-md text-xs text-mac-text-2">
          Attach a folder source first. Source control reads the Git repository that contains an
          attached workspace folder; it never scans an arbitrary path.
        </p>
        <button
          type="button"
          onClick={onAttachFolder}
          className="rounded-lg bg-mac-accent px-3 py-1.5 text-xs font-medium text-white hover:bg-mac-accent/90"
        >
          Attach folder source
        </button>
      </section>
    );
  }

  return (
    <section
      aria-label="Source control"
      data-testid="source-control-screen"
      className="flex h-full min-h-0 w-full bg-mac-content"
    >
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-11 shrink-0 items-center justify-between gap-3 border-b border-mac-hairline px-3">
          <div className="flex min-w-0 items-baseline gap-2">
            <h1 className="text-sm font-semibold text-mac-text">Source control</h1>
            <span
              data-testid="scm-context"
              className="truncate text-[11px] text-mac-text-2"
            >
              {branch}
              {aheadBehind ? ` · ${aheadBehind}` : ''}
            </span>
          </div>
          <button
            type="button"
            aria-label="Refresh source control"
            onClick={refreshAll}
            className={secondaryButtonClassName}
          >
            ↻
          </button>
        </header>

        <div className="flex min-h-0 flex-1">
          <div className="w-[360px] shrink-0 overflow-y-auto border-r border-mac-hairline bg-mac-canvas p-2">
            {statusError && (
              <div
                role="alert"
                className="mb-2 rounded-lg border border-mac-danger/40 bg-mac-danger/10 p-2 text-xs text-mac-danger"
              >
                {statusError}
              </div>
            )}
            <SourceControlPanel
              status={status}
              isLoading={statusLoading}
              errorMessage={null}
              onSelectionChange={setSelectedEntry}
              onOpenDocument={(documentId) => {
                const selected = selectedEntry;
                if (selected && fileKind(selected.path) === 'docx') {
                  onOpenInWord(documentId);
                } else {
                  onOpenMarkdown(documentId);
                }
              }}
              renderChangeActions={(entry) => (
                <ChangeActions
                  path={path}
                  change={entry}
                  onStatusRefreshed={(next) => {
                    handleStatusRefreshed(next);
                    refreshAll();
                  }}
                />
              )}
            />
          </div>

          <div className="min-w-0 flex-1 overflow-y-auto p-3">
            {!selectedEntry && (
              <div data-testid="scm-no-selection" className="text-xs text-mac-text-3">
                Select a changed file to review its diff.
              </div>
            )}

            {selectedEntry && selectedKind === 'md' && (
              <div className="space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <h2 className="min-w-0 truncate text-sm font-medium text-mac-text">
                    {selectedEntry.path}
                  </h2>
                  {canDiscard && (
                    <button
                      type="button"
                      onClick={() => setDiscardEntry(selectedEntry)}
                      className={secondaryButtonClassName}
                    >
                      Discard changes…
                    </button>
                  )}
                </div>
                {markdownDiff.loading && (
                  <p className="text-xs text-mac-text-3">Loading working-tree diff…</p>
                )}
                {markdownDiff.status && (
                  <MarkdownGitDiff
                    status={markdownDiff.status}
                    diff={markdownDiff.diff}
                    failureMessage={markdownDiff.error}
                  />
                )}
              </div>
            )}

            {selectedEntry && selectedKind === 'docx' && (
              <div
                data-testid="docx-change-summary"
                className="max-w-xl space-y-3 rounded-xl border border-mac-hairline bg-mac-content p-4 shadow-sm"
              >
                <div className="flex items-center gap-2">
                  <span className="rounded border border-mac-hairline bg-mac-canvas px-1.5 py-0.5 font-mono text-xs text-mac-text-2">
                    {selectedEntry.statusCharacters}
                  </span>
                  <span className="truncate font-mono text-xs text-mac-text">
                    {selectedEntry.path}
                  </span>
                </div>
                <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
                  <dt className="text-mac-text-2">State</dt>
                  <dd className="text-mac-text">{selectedEntry.state}</dd>
                  <dt className="text-mac-text-2">Format</dt>
                  <dd className="text-mac-text">DOCX · OOXML</dd>
                  <dt className="text-mac-text-2">Index</dt>
                  <dd className="text-mac-text">{selectedEntry.indexChange}</dd>
                  <dt className="text-mac-text-2">Working tree</dt>
                  <dd className="text-mac-text">{selectedEntry.workingTreeChange}</dd>
                </dl>
                <p className="text-xs text-mac-text-2">
                  A DOCX change is reviewed as a document-aware report. This surface never renders a
                  unified text diff for a binary document.
                </p>
                <button
                  type="button"
                  onClick={() => onOpenDocxDiff(selectedEntry)}
                  className="rounded-lg bg-mac-accent px-3 py-1.5 text-xs font-medium text-white hover:bg-mac-accent/90"
                >
                  Open document diff
                </button>
              </div>
            )}

            {selectedEntry && selectedKind === 'other' && (
              <div className="max-w-xl rounded-xl border border-mac-hairline bg-mac-canvas p-4 text-xs text-mac-text-2">
                No preview is available for this file type. The change remains inspectable in the
                grouped list; Document IDE does not render a text diff for it.
              </div>
            )}

            {conflictCount > 0 && (
              <div className="mt-4">
                <ConflictPanel
                  state={conflicts}
                  isLoading={conflicts === null && conflictsError === null}
                  errorMessage={conflictsError}
                  onOpenDocument={onOpenMarkdown}
                  onOpenDocxDocument={onOpenInWord}
                />
              </div>
            )}

          </div>
        </div>

        {(!status || status.hasRepository) && (
          <footer className="shrink-0 border-t border-mac-hairline bg-mac-toolbar px-3 py-2">
            <CommitBox
              path={path}
              commitLabel="Commit staged"
              commitAllLabel="Commit all"
              confirmCommitAll
              gitUnavailable={status?.status === 'GitUnavailable'}
              onStatusRefreshed={(next) => {
                handleStatusRefreshed(next);
                refreshAll();
              }}
              onCommitSucceeded={() => refreshAll()}
            />
          </footer>
        )}
      </div>

      {inspector}

      {pullOpen && (
        <PullDialog
          path={path}
          open
          hasUpstream={remote?.hasUpstream ?? false}
          onPulled={() => refreshAll()}
          onClose={() => setPullOpen(false)}
        />
      )}

      {discardEntry && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/20 p-4">
          <div className="max-h-full w-full max-w-2xl overflow-y-auto rounded-xl bg-mac-content shadow-xl">
            <DiscardRestoreDialog
              path={path}
              changePath={discardEntry.path}
              onCompleted={() => {
                setDiscardEntry(null);
                refreshAll();
              }}
              onCancel={() => setDiscardEntry(null)}
            />
            <div className="flex justify-end border-t border-mac-hairline p-2">
              <button
                type="button"
                onClick={() => setDiscardEntry(null)}
                className={secondaryButtonClassName}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

    </section>
  );
}
