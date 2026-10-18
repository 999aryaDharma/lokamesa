import SheetShell from '../shell/SheetShell';
import PushButton from '../shell/PushButton';

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
 *
 * UI-01 — restyled with `mac-*` tokens and the shared sheet panel chrome.
 * `scrim={false}`: this dialog is rendered inline today (no scrim, no
 * `aria-modal`), and turning it into a modal overlay would be a behaviour
 * change; the scrim follows existing modality. Every testid, label and
 * callback is unchanged.
 */
export function UndoConflictDialog({ isOpen, result, documentName, onClose }: UndoConflictDialogProps) {
  if (!isOpen) return null;

  const count = (outcome: UndoOutcome) => result.files.filter(f => f.outcome === outcome).length;
  const restored = count('Restored');
  const alreadyRestored = count('AlreadyRestored');
  const conflicts = count('Conflict');
  const failures = count('Failure');
  const skipped = count('Skipped');

  return <SheetShell
    title="Undo review"
    scrim={false}
    widthClass="max-w-[520px]"
    footer={<div className="flex w-full justify-end">
      <PushButton onClick={onClose}>Close</PushButton>
    </div>}
  >
    <p data-testid="undo-summary" className="text-[11px] text-mac-text-2">
      {restored} restored · {conflicts} conflict{conflicts === 1 ? '' : 's'} · {failures} failed · {skipped} skipped
      {alreadyRestored > 0 ? ` · ${alreadyRestored} already restored` : ''}
    </p>
    {conflicts > 0 && <p data-testid="undo-conflict-warning" className="mt-2 rounded bg-mac-danger/10 px-2 py-1 text-[11px] text-mac-danger">
      {conflicts} {conflicts === 1 ? 'file' : 'files'} changed after the operation and {conflicts === 1 ? 'was' : 'were'} not overwritten.
    </p>}
    <ul aria-label="Undo file results" className="mt-2 space-y-1">
      {result.files.map(file => <li key={file.documentId} className="rounded-md border border-mac-hairline bg-mac-content px-2 py-1 text-[11px] text-mac-text-2">
        <strong className="text-mac-text">{documentName(file.documentId)}</strong>{' '}
        <span className="rounded bg-mac-chip px-1.5 py-0.5 text-[10px] font-medium text-mac-text-2">{outcomeLabels[file.outcome]}</span>
        {file.reasonCode ? <span className="text-mac-text-3">{` (${file.reasonCode})`}</span> : null}
        {file.detail ? <p className="text-[11px] text-mac-text-2">{file.detail}</p> : null}
      </li>)}
    </ul>
  </SheetShell>;
}
