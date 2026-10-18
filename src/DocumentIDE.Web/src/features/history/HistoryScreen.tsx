import { useCallback, useEffect, useState } from 'react';
import GitHistoryScreen from '../source-control/GitHistoryScreen';
import InspectorCard, { InspectorField } from '../shell/InspectorCard';
import SegmentedControl from '../shell/SegmentedControl';
import HistoryPanel, {
  type HistoryStorageUsageView,
  type OperationHistoryEntryView,
} from './HistoryPanel';
import { UndoConflictDialog, type UndoResultView } from './UndoConflictDialog';

/**
 * UI-03 screen 10 — History & undo.
 *
 * One History surface with two authorities, kept visually distinct (spec 13
 * §4.3/§12: Git history never replaces Document IDE History):
 *
 *   - **Operations** — Document IDE operations, the shipped `HistoryPanel`
 *     (per-operation outcome chips + per-file outcomes + the real
 *     storage-capacity meter) and the shipped `UndoConflictDialog` for the
 *     conflict-aware undo review (HIST-02). Rows only ever come from an
 *     injected `OperationHistorySource`; there is no local sample data.
 *   - **Git** — part A's `GitHistoryScreen` unchanged (`GET
 *     /api/source-control/history`).
 *
 * HONESTY GAP (recorded in `docs/sprints/handoffs/UI-03-HANDOFF.md`): this
 * build's privileged API exposes per-plan apply/undo/history routes
 * (`POST /api/batch-operations/{planId}/apply|undo`,
 * `GET /api/batch-operations/{planId}/history`) but **no route that lists
 * operations or the history storage usage**. The shell therefore passes no
 * `source`, and the Operations tab renders a typed unavailable state: no rows,
 * no meter, and no "no operations recorded" claim that would imply an empty but
 * working list. When a real read-only listing route lands, wiring it is a
 * single `source` prop at the shell; nothing in this component changes.
 *
 * The Git tab is the default, preserving part A's shipped History behaviour
 * for the sidebar row while the Operations listing is unavailable.
 */

export type HistoryTab = 'operations' | 'git';

/** The real payload the Operations tab renders; never synthesized here. */
export interface OperationHistoryPayload {
  entries: OperationHistoryEntryView[];
  storageUsage: HistoryStorageUsageView;
}

/**
 * The only way the Operations tab obtains data. `load` must resolve real
 * records; `undo` must hit the real conflict-aware undo path.
 */
export interface OperationHistorySource {
  load: () => Promise<OperationHistoryPayload>;
  undo: (operationId: string) => Promise<UndoResultView>;
}

/**
 * Named plainly so the product never implies the list is empty rather than
 * absent. The exact routes that DO exist are listed; the missing one is named.
 */
export const HISTORY_OPERATIONS_UNAVAILABLE_REASON =
  'The privileged API exposes per-plan apply/undo/history routes ' +
  '(POST /api/batch-operations/{planId}/apply, POST .../{planId}/undo and ' +
  'GET .../{planId}/history), but no route lists Document IDE operations or ' +
  'history storage usage.';

export interface HistoryScreenProps {
  /** Repository path for the Git surface; `null` renders its real no-path state. */
  path: string | null;
  inspectorOpen: boolean;
  onAttachFolder: () => void;
  onOpenDocument?: (documentId: string) => void;
  /** Resolves a document id to the name the rest of the app already shows. */
  documentName?: (documentId: string) => string;
  /** Absent when the shipped API has no operations listing route. */
  source?: OperationHistorySource;
  /** Overrides the typed reason (e.g. a transport error detail). */
  unavailableReason?: string;
  initialTab?: HistoryTab;
}

type OperationsState =
  | { status: 'loading' }
  | { status: 'ready'; payload: OperationHistoryPayload }
  | { status: 'unavailable'; reason: string };

const TAB_OPTIONS = [
  { value: 'operations' as const, label: 'Operations', testId: 'history-tab-operations' },
  { value: 'git' as const, label: 'Git', testId: 'history-tab-git' },
];

function describeError(error: unknown): string {
  return error instanceof Error && error.message
    ? error.message
    : 'The Document IDE operations request failed.';
}

export default function HistoryScreen({
  path,
  inspectorOpen,
  onAttachFolder,
  onOpenDocument,
  documentName,
  source,
  unavailableReason,
  initialTab = 'git',
}: HistoryScreenProps) {  const [tab, setTab] = useState<HistoryTab>(initialTab);
  const [operations, setOperations] = useState<OperationsState>(() =>
    source
      ? { status: 'loading' }
      : { status: 'unavailable', reason: unavailableReason ?? HISTORY_OPERATIONS_UNAVAILABLE_REASON },
  );
  const [reloadToken, setReloadToken] = useState(0);
  const [selectedOperationId, setSelectedOperationId] = useState<string | null>(null);
  const [undoResult, setUndoResult] = useState<UndoResultView | null>(null);
  const [undoError, setUndoError] = useState<string | null>(null);
  const [undoPending, setUndoPending] = useState<string | null>(null);

  /** Leaving Operations dismisses its transient undo review (no hidden modal). */
  const handleTabChange = useCallback((next: HistoryTab) => {
    setTab(next);
    if (next !== 'operations') {
      setUndoResult(null);
      setUndoError(null);
      setUndoPending(null);
      setSelectedOperationId(null);
    }
  }, []);

  // Load only when the Operations tab is actually shown, and re-read after a
  // successful undo so the list reflects the persisted undo record.
  useEffect(() => {
    if (tab !== 'operations') return;
    if (!source) {
      setOperations({
        status: 'unavailable',
        reason: unavailableReason ?? HISTORY_OPERATIONS_UNAVAILABLE_REASON,
      });
      return;
    }

    let cancelled = false;
    setOperations({ status: 'loading' });
    source
      .load()
      .then((payload) => {
        if (!cancelled) setOperations({ status: 'ready', payload });
      })
      .catch((error: unknown) => {
        // A transport failure is a typed unavailable state too: the screen
        // never falls back to an empty list or sample rows.
        if (!cancelled) {
          setOperations({ status: 'unavailable', reason: describeError(error) });
        }
      });

    return () => {
      cancelled = true;
    };
  }, [tab, source, unavailableReason, reloadToken]);

  const handleUndo = useCallback(
    async (operationId: string) => {
      if (!source || undoPending !== null) return;
      setUndoError(null);
      setUndoPending(operationId);
      try {
        const result = await source.undo(operationId);
        setUndoResult(result);
        setReloadToken((token) => token + 1);
      } catch (error: unknown) {
        setUndoError(describeError(error));
      } finally {
        setUndoPending(null);
      }
    },
    [source, undoPending],
  );

  const entries = operations.status === 'ready' ? operations.payload.entries : [];
  const selectedOperation =
    entries.find((entry) => entry.operationId === selectedOperationId) ?? null;
  const resolveDocumentName = documentName ?? ((documentId: string) => documentId);

  return (
    <section
      aria-label="History and undo"
      data-testid="history-screen"
      className="flex h-full min-h-0 w-full flex-col bg-mac-content"
    >
      <header className="flex h-11 shrink-0 items-center gap-3 border-b border-mac-hairline px-3">
        <h1 className="text-sm font-semibold text-mac-text">History</h1>
        <SegmentedControl
          ariaLabel="History source"
          value={tab}
          options={TAB_OPTIONS}
          onChange={handleTabChange}
          size="sm"
        />
        <span className="truncate text-[11px] text-mac-text-2">
          {tab === 'operations'
            ? 'Document IDE operations · every destructive operation keeps a recovery snapshot'
            : 'Git commit history · read-only'}
        </span>
      </header>

      {tab === 'git' ? (
        <div className="flex min-h-0 flex-1">
          <GitHistoryScreen
            path={path}
            inspectorOpen={inspectorOpen}
            onAttachFolder={onAttachFolder}
          />
        </div>
      ) : (
        <div className="flex min-h-0 flex-1">
          <div
            data-testid="history-operations-surface"
            className="min-w-0 flex-1 overflow-y-auto px-3 py-3"
          >
            {operations.status === 'loading' && (
              <p data-testid="history-operations-loading" className="text-xs text-mac-text-2">
                Loading Document IDE operations…
              </p>
            )}

            {operations.status === 'unavailable' && (
              <div
                data-testid="history-operations-unavailable"
                role="status"
                className="max-w-xl rounded-lg border border-mac-hairline bg-mac-toolbar p-4"
              >
                <h2 className="text-[13px] font-semibold text-mac-text">
                  Document IDE operations are not available in this build
                </h2>
                <p
                  data-testid="history-operations-unavailable-reason"
                  className="mt-2 text-xs text-mac-text-2"
                >
                  {operations.reason}
                </p>
                <p className="mt-2 text-[11px] text-mac-text-3">
                  No operations are listed and no storage figure is shown, because no route
                  supplies them. Nothing on this screen is a placeholder list.
                </p>
              </div>
            )}

            {operations.status === 'ready' && (
              <>
                <HistoryPanel
                  entries={operations.payload.entries}
                  storageUsage={operations.payload.storageUsage}
                  documentName={resolveDocumentName}
                  onInspect={setSelectedOperationId}
                  onUndo={(operationId) => void handleUndo(operationId)}
                  onOpenDocument={(documentId) => onOpenDocument?.(documentId)}
                />
                {undoPending && (
                  <p role="status" className="mt-2 text-[11px] text-mac-text-2">
                    Undoing operation…
                  </p>
                )}
                {undoError && (
                  <p
                    role="alert"
                    data-testid="history-undo-error"
                    className="mt-2 text-[11px] text-mac-danger"
                  >
                    {undoError}
                  </p>
                )}
                <UndoConflictDialog
                  isOpen={undoResult !== null}
                  result={
                    undoResult ?? {
                      operationId: '',
                      status: 'Completed',
                      files: [],
                    }
                  }
                  documentName={resolveDocumentName}
                  onClose={() => setUndoResult(null)}
                />
              </>
            )}
          </div>

          {inspectorOpen && (
            <aside
              aria-label="History inspector"
              className="flex h-full w-[280px] shrink-0 flex-col gap-4 overflow-y-auto border-l border-mac-hairline bg-mac-inspector px-3 py-3"
            >
              <header className="flex items-center justify-between">
                <h2 className="text-[11px] font-semibold text-mac-text">Operation</h2>
              </header>

              {selectedOperation ? (
                <InspectorCard
                  title="Selected operation"
                  testId="history-operation-detail"
                >
                  <InspectorField label="Type" value={selectedOperation.operationType} />
                  <InspectorField label="Status" value={selectedOperation.status} />
                  <InspectorField
                    label="Recorded"
                    value={new Date(selectedOperation.recordedUtc).toLocaleString()}
                  />
                  <InspectorField
                    label="Files"
                    value={
                      selectedOperation.files.length === 1
                        ? '1 file'
                        : `${selectedOperation.files.length} files`
                    }
                  />
                  <InspectorField
                    label="Snapshot"
                    value={
                      selectedOperation.snapshotId ? (
                        <span className="font-mono">{selectedOperation.snapshotId}</span>
                      ) : (
                        'None'
                      )
                    }
                  />
                  <InspectorField
                    label="Undo"
                    value={selectedOperation.undo ? selectedOperation.undo.status : 'Not undone'}
                  />
                </InspectorCard>
              ) : (
                <p className="text-[11px] text-mac-text-3">
                  Select an operation to inspect its type, recorded time, file count and recovery
                  snapshot.
                </p>
              )}

              <InspectorCard title="Undo policy" testId="history-undo-policy">
                <p className="text-[11px] text-mac-text-3">
                  Undo restores the snapshot taken before the operation. A file that changed after
                  the operation is reported as a conflict and is never overwritten; a partial undo
                  must be reviewed — it is never silent.
                </p>
              </InspectorCard>
            </aside>
          )}
        </div>
      )}
    </section>
  );
}
