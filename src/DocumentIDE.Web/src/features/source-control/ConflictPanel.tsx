import type {
  GitConflictKind,
  GitConflictStatus,
  SourceControlConflictEntry,
  SourceControlConflictModel,
} from './sourceControlModels';

/**
 * SCM-14 — Git conflict state & resolution shell (spec 13 §20/§21).
 *
 * The panel is a self-contained, props-driven view: it performs no fetch and
 * runs no Git. It makes the real unmerged state visible, names the actions the
 * backend refuses, and offers only the existing document-open seam for
 * registered documents. DOCX conflicts are never merged automatically — the
 * user resolves them in Word and saves through the existing guarded host.
 */

export interface ConflictPanelProps {
  /** Latest conflict summary, or null before the first load completes. */
  state: SourceControlConflictModel | null;
  isLoading?: boolean;
  /** Transport-level failure (e.g. the local API is unreachable). */
  errorMessage?: string | null;
  /**
   * Opens an already-registered document through the existing guarded open
   * flow (the Word route for DOCX, the document-open seam otherwise). The
   * panel passes the document id only; it never builds a filesystem path or an
   * alternative opener.
   */
  onOpenDocument?: (documentId: string) => void;
  /**
   * UI-03: optional DOCX-specific routing so a `.docx` conflict can use the
   * existing Word path while other documents keep the document-open seam.
   * When absent, `onOpenDocument` is used unchanged.
   */
  onOpenDocxDocument?: (documentId: string) => void;
}

const kindLabels: Record<GitConflictKind, string> = {
  None: 'Unclassified conflict',
  BothModified: 'Both modified',
  BothAdded: 'Both added',
  BothDeleted: 'Both deleted',
  AddedByUs: 'Added by us',
  AddedByThem: 'Added by them',
  DeletedByUs: 'Deleted by us',
  DeletedByThem: 'Deleted by them',
};

const secondaryButtonClassName =
  'px-2.5 py-1 text-xs font-medium text-mac-text bg-mac-content border border-mac-hairline rounded-lg ' +
  'hover:bg-mac-canvas focus:outline-none focus:ring-2 focus:ring-mac-accent/40 transition-colors';

function typedStatusMessage(status: Exclude<GitConflictStatus, 'Available'>): string {
  switch (status) {
    case 'NoConflicts':
      return 'No unresolved conflicts. The repository is not in an unmerged state.';
    case 'NoRepository':
      return 'This folder is not inside a Git repository.';
    case 'GitUnavailable':
      return 'Git is not available on this machine. Install Git to inspect conflict state.';
    case 'WorktreeMissing':
      return 'The worktree no longer exists; no conflict state was read.';
    case 'IndexLocked':
      return 'The Git index is locked by another operation; conflict state cannot be read safely.';
    case 'Unreadable':
      return 'The conflict state could not be read from Git; no conclusion was drawn.';
    case 'Failed':
    default:
      return 'The conflict state could not be determined.';
  }
}

function TypedState({ state }: { state: SourceControlConflictModel }) {
  const status = state.status as Exclude<GitConflictStatus, 'Available'>;
  const isClean = status === 'NoConflicts';

  return (
    <div
      data-testid={`conflict-status-${status}`}
      role={isClean ? undefined : 'alert'}
      className={`p-3 text-sm rounded-lg border ${
        isClean
          ? 'text-mac-text-2 bg-mac-canvas border-mac-hairline'
          : 'text-mac-danger bg-mac-danger/10 border-mac-danger/40'
      }`}
    >
      <p className="font-medium">{typedStatusMessage(status)}</p>
      {state.message && state.message !== typedStatusMessage(status) && (
        <p className="mt-1 text-xs text-mac-text-3">{state.message}</p>
      )}
      {state.failure && (
        <p className="mt-1 text-xs">
          {state.failure.errorKind}
          {state.failure.errorMessage ? `: ${state.failure.errorMessage}` : ''}
        </p>
      )}
    </div>
  );
}

function ConflictRow({
  entry,
  onOpenDocument,
  onOpenDocxDocument,
}: {
  entry: SourceControlConflictEntry;
  onOpenDocument?: (documentId: string) => void;
  onOpenDocxDocument?: (documentId: string) => void;
}) {
  const canOpen = entry.openAction !== 'None' && entry.documentId !== null;

  return (
    <li
      data-testid={`conflict-entry-${entry.path}`}
      className="space-y-1 px-2 py-2 border border-mac-hairline rounded-lg bg-mac-content"
    >
      <div className="flex items-center gap-2 flex-wrap">
        <span className="shrink-0 text-xs font-mono text-mac-text-3 bg-mac-chip border border-mac-hairline rounded px-1.5 py-0.5">
          {entry.statusCharacters}
        </span>
        <span className="text-sm font-mono text-mac-text truncate">{entry.path}</span>
        <span className="text-xs text-mac-text-2">{kindLabels[entry.conflictKind]}</span>
        {entry.isDocx && (
          <span className="text-xs text-mac-text-3">
            DOCX — resolved in Word, never auto-merged by Document IDE.
          </span>
        )}
        <span className="ml-auto shrink-0">
          {canOpen ? (
            <button
              type="button"
              data-testid={`conflict-open-${entry.path}`}
              onClick={() => {
                if (entry.isDocx && onOpenDocxDocument) {
                  onOpenDocxDocument(entry.documentId!);
                  return;
                }
                onOpenDocument?.(entry.documentId!);
              }}
              className={secondaryButtonClassName}
              aria-label={`Open for manual resolution: ${entry.path}`}
            >
              Open for manual resolution
            </button>
          ) : (
            <span className="text-xs text-mac-text-3">
              Not a registered document — resolve it outside Document IDE.
            </span>
          )}
        </span>
      </div>
    </li>
  );
}

export default function ConflictPanel({
  state,
  isLoading = false,
  errorMessage = null,
  onOpenDocument,
  onOpenDocxDocument,
}: ConflictPanelProps) {
  if (state === null) {
    return (
      <div className="w-full max-w-xl space-y-3">
        {errorMessage && (
          <div
            role="alert"
            className="p-3 text-sm text-mac-danger bg-mac-danger/10 border border-mac-danger/40 rounded-lg"
          >
            {errorMessage}
          </div>
        )}
        <div
          data-testid={isLoading ? 'conflict-loading' : 'conflict-no-state'}
          className="p-4 text-sm text-mac-text-2 bg-mac-canvas border border-mac-hairline rounded-xl"
        >
          {isLoading
            ? 'Loading conflict state...'
            : 'No conflict state available yet.'}
        </div>
      </div>
    );
  }

  if (state.status !== 'Available') {
    return (
      <div className="w-full max-w-xl space-y-3">
        {errorMessage && (
          <div
            role="alert"
            className="p-3 text-sm text-mac-danger bg-mac-danger/10 border border-mac-danger/40 rounded-lg"
          >
            {errorMessage}
          </div>
        )}
        <TypedState state={state} />
      </div>
    );
  }

  return (
    <div
      data-testid="conflict-panel"
      className="w-full max-w-xl p-4 bg-mac-content rounded-xl border border-mac-hairline shadow-sm space-y-3"
    >
      <div className="space-y-1">
        <h3 data-testid="conflict-count" className="text-sm font-semibold text-mac-text">
          Conflicts ({state.conflicts.length})
        </h3>
        <p className="text-xs text-mac-text-2">
          Document IDE never merges conflicted content automatically. DOCX conflicts are resolved in
          Word and saved through the existing guarded host; then stage the resolved file. While these
          conflicts exist, Commit, Commit All, Discard/Restore on a conflicted path, and Pull are
          refused.
        </p>
      </div>

      {errorMessage && (
        <div
          role="alert"
          className="p-3 text-sm text-mac-danger bg-mac-danger/10 border border-mac-danger/40 rounded-lg"
        >
          {errorMessage}
        </div>
      )}

      {state.blockedActions.length > 0 && (
        <div
          data-testid="conflict-blocked-actions"
          className="p-2 text-xs text-mac-text-2 bg-mac-canvas border border-mac-hairline rounded-lg space-y-1"
        >
          <p className="font-medium text-mac-text">Blocked until resolved</p>
          <ul className="space-y-1">
            {state.blockedActions.map((blocked) => (
              <li key={blocked.action} data-testid={`conflict-blocked-${blocked.action}`}>
                <span className="font-medium">{blocked.action}</span>
                <span className="text-mac-text-3"> ({blocked.errorCode})</span>
                {': '}
                {blocked.message}
              </li>
            ))}
          </ul>
        </div>
      )}

      {state.conflicts.length === 0 ? (
        <div data-testid="conflict-no-conflicts" className="text-sm text-mac-text-3">
          No unresolved conflicts.
        </div>
      ) : (
        <ul className="space-y-1">
          {state.conflicts.map((entry) => (
            <ConflictRow
              key={entry.path}
              entry={entry}
              onOpenDocument={onOpenDocument}
              onOpenDocxDocument={onOpenDocxDocument}
            />
          ))}
        </ul>
      )}
    </div>
  );
}
