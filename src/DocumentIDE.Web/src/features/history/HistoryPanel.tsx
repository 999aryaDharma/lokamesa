import PushButton from '../shell/PushButton';

export type BatchFileOutcomeView = 'Success' | 'Skipped' | 'Failure';
export type UndoFileOutcomeView = 'Restored' | 'AlreadyRestored' | 'Conflict' | 'Failure' | 'Skipped';
export type OperationStatusView = 'Completed' | 'Partial' | 'Blocked' | 'Canceled';

export interface OperationFileOutcomeView {
  documentId: string;
  outcome: BatchFileOutcomeView;
  reasonCode: string | null;
  detail: string | null;
}

export interface OperationUndoOutcomeView {
  documentId: string;
  outcome: UndoFileOutcomeView;
  reasonCode: string | null;
  detail: string | null;
}

export interface OperationUndoView {
  status: OperationStatusView;
  files: OperationUndoOutcomeView[];
}

export interface OperationHistoryEntryView {
  operationId: string;
  operationType: string;
  recordedUtc: string;
  status: OperationStatusView;
  snapshotId: string | null;
  files: OperationFileOutcomeView[];
  undo: OperationUndoView | null;
}

export interface HistoryStorageUsageView {
  usedBytes: number;
  capacityBytes: number;
}

export interface HistoryPanelProps {
  entries: OperationHistoryEntryView[];
  storageUsage: HistoryStorageUsageView;
  documentName: (documentId: string) => string;
  onInspect: (operationId: string) => void;
  onUndo: (operationId: string) => void;
  onOpenDocument: (documentId: string) => void;
}

const statusLabels: Record<OperationStatusView, string> = {
  Completed: 'Completed',
  Partial: 'Partial',
  Blocked: 'Blocked',
  Canceled: 'Cancelled',
};

const countOutcomes = (entry: OperationHistoryEntryView) => ({
  succeeded: entry.files.filter(f => f.outcome === 'Success').length,
  skipped: entry.files.filter(f => f.outcome === 'Skipped').length,
  failed: entry.files.filter(f => f.outcome === 'Failure').length,
});

const fileOutcomeLabels: Record<BatchFileOutcomeView, string> = {
  Success: 'Succeeded',
  Skipped: 'Skipped',
  Failure: 'Failed',
};

const undoOutcomeLabels: Record<UndoFileOutcomeView, string> = {
  Restored: 'Restored',
  AlreadyRestored: 'Already restored',
  Conflict: 'Conflict',
  Failure: 'Failed',
  Skipped: 'Skipped',
};

const undoCounts = (undo: OperationUndoView) => ({
  restored: undo.files.filter(f => f.outcome === 'Restored').length,
  alreadyRestored: undo.files.filter(f => f.outcome === 'AlreadyRestored').length,
  conflicts: undo.files.filter(f => f.outcome === 'Conflict').length,
  failures: undo.files.filter(f => f.outcome === 'Failure').length,
  skipped: undo.files.filter(f => f.outcome === 'Skipped').length,
});

const fileCountLabel = (count: number) => (count === 1 ? '1 file' : `${count} files`);

/** Spec 00 §101.9 / spec 02: 5 GiB budget with a warning at 80%. */
const HISTORY_WARNING_RATIO = 0.8;

const formatGiB = (bytes: number) => `${(bytes / (1024 ** 3)).toFixed(1)} GiB`;

/** A completed operation with skipped/failed files must never read as a complete success. */
const statusText = (status: OperationStatusView, failed: number, skipped: number) => {
  if (status === 'Completed' && (failed > 0 || skipped > 0)) {
    return failed > 0 ? 'Completed with failures' : 'Completed with skipped files';
  }
  return statusLabels[status];
};

/**
 * UI-01 — outcome chips. Tones are derived from the *rendered wording*, so a
 * "Completed with failures" operation can never be painted as a clean success.
 * These are presentational `<span>`s: nothing here adds, hides or reinterprets
 * an outcome, and the text stays the single source of truth for the tests.
 */
const statusChipClass = (label: string) =>
  label === statusLabels.Completed
    ? 'bg-mac-success/15 text-mac-success'
    : label === statusLabels.Blocked
      ? 'bg-mac-danger/15 text-mac-danger'
      : label === statusLabels.Canceled
        ? 'bg-mac-chip text-mac-text-2'
        : 'bg-mac-warning/15 text-mac-warning';

const fileOutcomeChipClass = (outcome: BatchFileOutcomeView) =>
  outcome === 'Success'
    ? 'bg-mac-success/15 text-mac-success'
    : outcome === 'Failure'
      ? 'bg-mac-danger/15 text-mac-danger'
      : 'bg-mac-chip text-mac-text-2';

const undoOutcomeChipClass = (outcome: UndoFileOutcomeView) =>
  outcome === 'Restored' || outcome === 'AlreadyRestored'
    ? 'bg-mac-success/15 text-mac-success'
    : outcome === 'Conflict' || outcome === 'Failure'
      ? 'bg-mac-danger/15 text-mac-danger'
      : 'bg-mac-chip text-mac-text-2';

const chipBase = 'rounded px-1.5 py-0.5 text-[10px] font-medium whitespace-nowrap';

/**
 * HIST-02 review surface inside a history entry: conflicted files are surfaced with their
 * reason and are never described as restored or overwritten. Retrying undo stays a plain
 * typed action — this component cannot express a force/overwrite request.
 */
function UndoReview({ entry, documentName, onOpenDocument }: Pick<HistoryPanelProps, 'documentName' | 'onOpenDocument'> & { entry: OperationHistoryEntryView }) {
  const undo = entry.undo;
  if (!undo) return null;
  const { restored, alreadyRestored, conflicts, failures, skipped } = undoCounts(undo);
  return <div data-testid="history-entry-undo" className="mt-2 rounded-md border border-mac-hairline bg-mac-toolbar/60 px-2 py-1.5">
    <p data-testid="history-entry-undo-summary" className="text-[11px] text-mac-text-2">
      Undo ({statusLabels[undo.status]}): {restored} restored · {conflicts} conflict{conflicts === 1 ? '' : 's'} · {failures} failed · {skipped} skipped
      {alreadyRestored > 0 ? ` · ${alreadyRestored} already restored` : ''}
    </p>
    {conflicts > 0 && <p data-testid="history-entry-undo-conflict-warning" className="mt-1 rounded bg-mac-danger/10 px-2 py-1 text-[11px] text-mac-danger">
      {conflicts} {conflicts === 1 ? 'file' : 'files'} changed after the operation and {conflicts === 1 ? 'was' : 'were'} not overwritten.
    </p>}
    <ul aria-label={`Undo outcomes for ${entry.operationId}`} className="mt-1 space-y-0.5">
      {undo.files.map(file => <li key={file.documentId} className="text-[11px] text-mac-text-2">
        {file.outcome === 'Conflict' || file.outcome === 'Failure'
          ? <button onClick={() => onOpenDocument(file.documentId)} className="text-mac-accent hover:underline">Open {documentName(file.documentId)}</button>
          : <span className="text-mac-text">{documentName(file.documentId)}</span>}{' '}
        <span className={`${chipBase} ${undoOutcomeChipClass(file.outcome)}`}>{undoOutcomeLabels[file.outcome]}</span>
        {file.reasonCode ? <span className="text-mac-text-3">{` (${file.reasonCode})`}</span> : null}
        {file.detail ? <p className="text-[11px] text-mac-text-2">{file.detail}</p> : null}
      </li>)}
    </ul>
  </div>;
}

/**
 * BAT-11 — operation history timeline: per-operation status, per-file outcome counts,
 * inspect/undo actions, storage usage and failure navigation.
 *
 * UI-01 — restyled with `mac-*` tokens (screen 10: operation list + outcome chips +
 * detail + storage meter). Every count, label and testid is unchanged, and nothing
 * is invented: the chips render exactly the wording already derived from the entry.
 */
export function HistoryPanel({ entries, storageUsage, documentName, onInspect, onUndo, onOpenDocument }: HistoryPanelProps) {
  const usageRatio = storageUsage.capacityBytes > 0 ? storageUsage.usedBytes / storageUsage.capacityBytes : 0;
  return <section aria-label="History" className="space-y-3">
    <h2 className="text-[13px] font-semibold text-mac-text">History</h2>
    <div className="rounded-lg border border-mac-hairline bg-mac-toolbar px-3 py-2">
      <p data-testid="history-storage-usage" className="text-[11px] text-mac-text-2">
        History storage: {formatGiB(storageUsage.usedBytes)} of {formatGiB(storageUsage.capacityBytes)} ({Math.round(usageRatio * 100)}%)
      </p>
      {storageUsage.capacityBytes > 0 && <progress aria-label="History storage usage" value={storageUsage.usedBytes} max={storageUsage.capacityBytes} className="mt-1 h-1.5 w-full accent-mac-accent" />}
      {/* Retention is spec 00 §25 / spec 07 §23. The copy must not claim an automatic cleanup
          sweep: HistoryStorage.CleanupExpiredAsync has no production caller yet. Wording pinned by test. */}
      {usageRatio >= HISTORY_WARNING_RATIO && <p role="alert" data-testid="history-storage-warning" className="mt-2 text-[11px] text-mac-warning">
        History storage is at or above the {Math.round(HISTORY_WARNING_RATIO * 100)}% warning threshold. History is retained for 30 days; expired snapshots are not cleaned up automatically yet, and unexpired snapshots are never evicted silently.
      </p>}
    </div>
    {entries.length === 0
      ? <p className="text-xs text-mac-text-3">No operations recorded yet.</p>
      : <ol aria-label="Operation history" className="space-y-2">
        {entries.map(entry => {
          const { succeeded, skipped, failed } = countOutcomes(entry);
          // Nothing can be restored without a snapshot or without a successfully changed file;
          // blocked/no-op operations are never presented as undoable.
          const canUndo = entry.snapshotId !== null && succeeded > 0;
          const label = statusText(entry.status, failed, skipped);
          return <li key={entry.operationId} data-testid={`history-entry-${entry.operationId}`} className="rounded-lg border border-mac-hairline bg-mac-content p-3">
            <div className="flex items-start justify-between gap-2">
              <div className="flex items-center gap-2">
                <h3 className="text-xs font-semibold text-mac-text">{entry.operationType}</h3>
                <span data-testid="history-entry-status" className={`${chipBase} ${statusChipClass(label)}`}>{label}</span>
              </div>
              <time dateTime={entry.recordedUtc} className="whitespace-nowrap text-[11px] text-mac-text-3">{new Date(entry.recordedUtc).toLocaleString()}</time>
            </div>
            <p data-testid="history-entry-file-count" className="mt-1 text-[11px] text-mac-text-3">{fileCountLabel(entry.files.length)}</p>
            <p data-testid="history-entry-counts" className="mt-1 flex flex-wrap items-center gap-1 text-[11px] text-mac-text-2">
              <span className="rounded bg-mac-success/15 px-1.5 py-0.5 font-medium text-mac-success">{succeeded} succeeded</span>
              <span className="text-mac-text-3">·</span>
              <span className="rounded bg-mac-chip px-1.5 py-0.5 font-medium text-mac-text-2">{skipped} skipped</span>
              <span className="text-mac-text-3">·</span>
              <span className="rounded bg-mac-danger/15 px-1.5 py-0.5 font-medium text-mac-danger">{failed} failed</span>
            </p>
            <ul aria-label={`File outcomes for ${entry.operationId}`} className="mt-2 space-y-0.5">
              {entry.files.map(file => <li key={file.documentId} className="text-[11px] text-mac-text-2">
                {file.outcome === 'Failure'
                  ? <button onClick={() => onOpenDocument(file.documentId)} className="text-mac-accent hover:underline">Open {documentName(file.documentId)}</button>
                  : <span className="text-mac-text">{documentName(file.documentId)}</span>}{' '}
                <span className={`${chipBase} ${fileOutcomeChipClass(file.outcome)}`}>{fileOutcomeLabels[file.outcome]}</span>
                {file.reasonCode ? <span className="text-mac-text-3">{` (${file.reasonCode})`}</span> : null}
                {file.detail ? <p className="text-[11px] text-mac-text-2">{file.detail}</p> : null}
              </li>)}
            </ul>
            <UndoReview entry={entry} documentName={documentName} onOpenDocument={onOpenDocument} />
            <div className="mt-2 flex items-center gap-2">
              <PushButton onClick={() => onInspect(entry.operationId)} size="sm">Inspect</PushButton>
              {canUndo
                ? <PushButton onClick={() => onUndo(entry.operationId)} size="sm" variant="primary">Undo</PushButton>
                : <p data-testid="history-entry-undo-unavailable" className="text-[11px] text-mac-text-3">{entry.snapshotId === null
                  ? 'No recovery snapshot for this operation; undo is unavailable.'
                  : 'No files were changed; nothing to undo.'}</p>}
            </div>
          </li>;
        })}
      </ol>}
  </section>;
}

export default HistoryPanel;
