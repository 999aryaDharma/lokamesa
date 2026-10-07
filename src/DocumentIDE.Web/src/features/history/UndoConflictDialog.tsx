export type UndoOutcome = 'Restored' | 'AlreadyRestored' | 'Conflict' | 'Failure' | 'Skipped';
export type UndoStatus = 'Completed' | 'Partial' | 'Blocked' | 'Canceled';

export interface UndoFileResultView {
  documentId: string;
  outcome: UndoOutcome;
  reasonCode: string | null;
  detail: string | null;
}

export interface UndoResultView {
  operationId: string;
  snapshotId?: string | null;
  status: UndoStatus;
  files: UndoFileResultView[];
}

export interface UndoConflictDialogProps {
  isOpen: boolean;
  result: UndoResultView;
  documentName: (documentId: string) => string;
  onClose: () => void;
}

const outcomeLabels: Record<UndoOutcome, string> = {
  Restored: 'Restored',
  AlreadyRestored: 'Already restored',
  Conflict: 'Conflict',
  Failure: 'Failed',
  Skipped: 'Skipped',
};

/**
 * HIST-02 — per-file review of an undo result.
 * Conflicted files are shown as a warning and are never overwritten by an undo:
 * the newer content stays until the user decides how to handle it.
 */
export function UndoConflictDialog({ isOpen, result, documentName, onClose }: UndoConflictDialogProps) {
  if (!isOpen) return null;

  const count = (outcome: UndoOutcome) => result.files.filter(f => f.outcome === outcome).length;
  const restored = count('Restored');
  const alreadyRestored = count('AlreadyRestored');
  const conflicts = count('Conflict');
  const failures = count('Failure');
  const skipped = count('Skipped');

  return <div role="dialog" aria-label="Undo review">
    <h2>Undo review</h2>
    <p data-testid="undo-summary">
      {restored} restored · {conflicts} conflict{conflicts === 1 ? '' : 's'} · {failures} failed · {skipped} skipped
      {alreadyRestored > 0 ? ` · ${alreadyRestored} already restored` : ''}
    </p>
    {conflicts > 0 && <p data-testid="undo-conflict-warning">
      {conflicts} {conflicts === 1 ? 'file' : 'files'} changed after the operation and {conflicts === 1 ? 'was' : 'were'} not overwritten.
    </p>}
    <ul aria-label="Undo file results">
      {result.files.map(file => <li key={file.documentId}>
        <strong>{documentName(file.documentId)}</strong>{' '}
        <span>{outcomeLabels[file.outcome]}</span>
        {file.reasonCode ? <span>{` (${file.reasonCode})`}</span> : null}
        {file.detail ? <p>{file.detail}</p> : null}
      </li>)}
    </ul>
    <button onClick={onClose}>Close</button>
  </div>;
}
