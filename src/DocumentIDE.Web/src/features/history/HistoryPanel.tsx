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
 * HIST-02 review surface inside a history entry: conflicted files are surfaced with their
 * reason and are never described as restored or overwritten. Retrying undo stays a plain
 * typed action — this component cannot express a force/overwrite request.
 */
function UndoReview({ entry, documentName, onOpenDocument }: Pick<HistoryPanelProps, 'documentName' | 'onOpenDocument'> & { entry: OperationHistoryEntryView }) {
  const undo = entry.undo;
  if (!undo) return null;
  const { restored, alreadyRestored, conflicts, failures, skipped } = undoCounts(undo);
  return <div data-testid="history-entry-undo">
    <p data-testid="history-entry-undo-summary">
      Undo ({statusLabels[undo.status]}): {restored} restored · {conflicts} conflict{conflicts === 1 ? '' : 's'} · {failures} failed · {skipped} skipped
      {alreadyRestored > 0 ? ` · ${alreadyRestored} already restored` : ''}
    </p>
    {conflicts > 0 && <p data-testid="history-entry-undo-conflict-warning">
      {conflicts} {conflicts === 1 ? 'file' : 'files'} changed after the operation and {conflicts === 1 ? 'was' : 'were'} not overwritten.
    </p>}
    <ul aria-label={`Undo outcomes for ${entry.operationId}`}>
      {undo.files.map(file => <li key={file.documentId}>
        {file.outcome === 'Conflict' || file.outcome === 'Failure'
          ? <button onClick={() => onOpenDocument(file.documentId)}>Open {documentName(file.documentId)}</button>
          : <span>{documentName(file.documentId)}</span>}{' '}
        <span>{undoOutcomeLabels[file.outcome]}</span>
        {file.reasonCode ? <span>{` (${file.reasonCode})`}</span> : null}
        {file.detail ? <p>{file.detail}</p> : null}
      </li>)}
    </ul>
  </div>;
}

/**
 * BAT-11 — operation history timeline: per-operation status, per-file outcome counts,
 * inspect/undo actions, storage usage and failure navigation.
 */
export function HistoryPanel({ entries, storageUsage, documentName, onInspect, onUndo, onOpenDocument }: HistoryPanelProps) {
  const usageRatio = storageUsage.capacityBytes > 0 ? storageUsage.usedBytes / storageUsage.capacityBytes : 0;
  return <section aria-label="History">
    <h2>History</h2>
    <p data-testid="history-storage-usage">
      History storage: {formatGiB(storageUsage.usedBytes)} of {formatGiB(storageUsage.capacityBytes)} ({Math.round(usageRatio * 100)}%)
    </p>
    {storageUsage.capacityBytes > 0 && <progress aria-label="History storage usage" value={storageUsage.usedBytes} max={storageUsage.capacityBytes} />}
    {usageRatio >= HISTORY_WARNING_RATIO && <p role="alert" data-testid="history-storage-warning">
      History storage is at or above the {Math.round(HISTORY_WARNING_RATIO * 100)}% warning threshold. Expired snapshots are cleaned up automatically; unexpired snapshots are never evicted silently.
    </p>}
    {entries.length === 0
      ? <p>No operations recorded yet.</p>
      : <ol aria-label="Operation history">
        {entries.map(entry => {
          const { succeeded, skipped, failed } = countOutcomes(entry);
          // Nothing can be restored without a snapshot or without a successfully changed file;
          // blocked/no-op operations are never presented as undoable.
          const canUndo = entry.snapshotId !== null && succeeded > 0;
          return <li key={entry.operationId} data-testid={`history-entry-${entry.operationId}`}>
            <time dateTime={entry.recordedUtc}>{new Date(entry.recordedUtc).toLocaleString()}</time>
            <h3>{entry.operationType}</h3>
            <p data-testid="history-entry-file-count">{fileCountLabel(entry.files.length)}</p>
            <p data-testid="history-entry-status">{statusText(entry.status, failed, skipped)}</p>
            <p data-testid="history-entry-counts">{succeeded} succeeded · {skipped} skipped · {failed} failed</p>
            <ul aria-label={`File outcomes for ${entry.operationId}`}>
              {entry.files.map(file => <li key={file.documentId}>
                {file.outcome === 'Failure'
                  ? <button onClick={() => onOpenDocument(file.documentId)}>Open {documentName(file.documentId)}</button>
                  : <span>{documentName(file.documentId)}</span>}{' '}
                <span>{fileOutcomeLabels[file.outcome]}</span>
                {file.reasonCode ? <span>{` (${file.reasonCode})`}</span> : null}
                {file.detail ? <p>{file.detail}</p> : null}
              </li>)}
            </ul>
            <UndoReview entry={entry} documentName={documentName} onOpenDocument={onOpenDocument} />
            <div>
              <button onClick={() => onInspect(entry.operationId)}>Inspect</button>
              {canUndo
                ? <button onClick={() => onUndo(entry.operationId)}>Undo</button>
                : <p data-testid="history-entry-undo-unavailable">{entry.snapshotId === null
                  ? 'No recovery snapshot for this operation; undo is unavailable.'
                  : 'No files were changed; nothing to undo.'}</p>}
            </div>
          </li>;
        })}
      </ol>}
  </section>;
}

export default HistoryPanel;
