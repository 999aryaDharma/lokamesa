import { render, screen, within, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { HistoryPanel, type HistoryPanelProps, type OperationHistoryEntryView } from './HistoryPanel';

const names: Record<string, string> = { d1: 'one.md', d2: 'two.md', d3: 'three.md', d4: 'four.md' };

const partialEntry: OperationHistoryEntryView = {
  operationId: 'op-partial',
  operationType: 'Search & Replace',
  recordedUtc: '2026-10-06T14:30:00Z',
  status: 'Partial',
  snapshotId: 'snap-1',
  undo: null,
  files: [
    { documentId: 'd1', outcome: 'Success', reasonCode: null, detail: null },
    { documentId: 'd2', outcome: 'Failure', reasonCode: 'MutationFailed', detail: 'Atomic replace failed; original content preserved.' },
    { documentId: 'd3', outcome: 'Skipped', reasonCode: 'Stale', detail: 'File changed after preview; re-preview required.' },
  ],
};

function renderPanel(overrides: Partial<HistoryPanelProps> = {}) {
  const onInspect = vi.fn();
  const onUndo = vi.fn();
  const onOpenDocument = vi.fn();
  render(<HistoryPanel
    entries={[partialEntry]}
    storageUsage={{ usedBytes: 1024 ** 3, capacityBytes: 5 * 1024 ** 3 }}
    documentName={id => names[id] ?? id}
    onInspect={onInspect}
    onUndo={onUndo}
    onOpenDocument={onOpenDocument}
    {...overrides}
  />);
  return { onInspect, onUndo, onOpenDocument };
}

it('Partial result is visible: explicit partial wording and per-outcome counts, never a success claim', () => {
  renderPanel();
  const entry = screen.getByTestId('history-entry-op-partial');
  const status = within(entry).getByTestId('history-entry-status');
  expect(status).toHaveTextContent('Partial');
  expect(status).not.toHaveTextContent(/complete success|all files|done/i);
  const counts = within(entry).getByTestId('history-entry-counts');
  expect(counts).toHaveTextContent('1 succeeded');
  expect(counts).toHaveTextContent('1 skipped');
  expect(counts).toHaveTextContent('1 failed');
});

const renameEntry: OperationHistoryEntryView = {
  operationId: 'op-rename',
  operationType: 'Batch Rename',
  recordedUtc: '2026-10-05T09:15:00Z',
  status: 'Completed',
  snapshotId: null,
  undo: null,
  files: [{ documentId: 'd4', outcome: 'Success', reasonCode: null, detail: null }],
};

it('renders the operation timeline with type, time and file count in the given order', () => {
  renderPanel({ entries: [partialEntry, renameEntry] });
  const rows = screen.getAllByTestId(/^history-entry-op-/);
  expect(rows).toHaveLength(2);
  expect(rows.map(row => within(row).getByRole('heading').textContent)).toEqual(['Search & Replace', 'Batch Rename']);
  expect(rows[0].querySelector('time')?.getAttribute('dateTime')).toBe('2026-10-06T14:30:00Z');
  expect(within(rows[0]).getByTestId('history-entry-file-count')).toHaveTextContent('3 files');
  expect(within(rows[1]).getByTestId('history-entry-file-count')).toHaveTextContent('1 file');
});

it('renders an empty state when no operations were recorded', () => {
  renderPanel({ entries: [] });
  expect(screen.getByText('No operations recorded yet.')).toBeInTheDocument();
  expect(screen.queryByRole('list', { name: 'Operation history' })).not.toBeInTheDocument();
});

it('never claims complete success when a Completed operation still has failures', () => {
  renderPanel({ entries: [{ ...partialEntry, operationId: 'op-mixed', status: 'Completed' }] });
  const entry = screen.getByTestId('history-entry-op-mixed');
  const status = within(entry).getByTestId('history-entry-status');
  expect(status).toHaveTextContent('Completed with failures');
  expect(status).not.toHaveTextContent(/complete success|all files|done/i);
  expect(within(entry).getByTestId('history-entry-counts')).toHaveTextContent('1 failed');
});

it('renders a Completed operation with only skipped files as completed with skips', () => {
  renderPanel({ entries: [{ ...partialEntry, operationId: 'op-skips', status: 'Completed', files: [partialEntry.files[2]] }] });
  const entry = screen.getByTestId('history-entry-op-skips');
  const status = within(entry).getByTestId('history-entry-status');
  expect(status).toHaveTextContent('Completed with skipped files');
  expect(status).not.toHaveTextContent(/complete success|all files|done/i);
});

it('renders a Blocked operation as blocked, not done', () => {
  const blocked: OperationHistoryEntryView = {
    ...partialEntry,
    operationId: 'op-blocked',
    status: 'Blocked',
    snapshotId: null,
    files: [
      { documentId: 'd2', outcome: 'Skipped', reasonCode: 'RecoveryBlocked', detail: 'Recovery snapshot could not be created; no file was changed.' },
      { documentId: 'd3', outcome: 'Skipped', reasonCode: 'BatchBlocked', detail: 'Batch blocked before the first file was touched.' },
    ],
  };
  renderPanel({ entries: [blocked] });
  const entry = screen.getByTestId('history-entry-op-blocked');
  const status = within(entry).getByTestId('history-entry-status');
  expect(status).toHaveTextContent('Blocked');
  expect(status).not.toHaveTextContent(/complete success|all files|done/i);
});

it('exposes inspect and undo actions through typed callbacks', () => {
  const { onInspect, onUndo } = renderPanel();
  const entry = screen.getByTestId('history-entry-op-partial');
  fireEvent.click(within(entry).getByRole('button', { name: 'Inspect' }));
  expect(onInspect).toHaveBeenCalledWith('op-partial');
  fireEvent.click(within(entry).getByRole('button', { name: 'Undo' }));
  expect(onUndo).toHaveBeenCalledWith('op-partial');
});

it('does not present an operation as undoable without a recovery snapshot or without changed files', () => {
  const skippedOnly: OperationHistoryEntryView = { ...partialEntry, operationId: 'op-nothing', snapshotId: 'snap-9', files: [partialEntry.files[2]] };
  renderPanel({ entries: [renameEntry, skippedOnly] });
  const rename = screen.getByTestId('history-entry-op-rename');
  expect(within(rename).queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument();
  expect(within(rename).getByTestId('history-entry-undo-unavailable')).toHaveTextContent('No recovery snapshot');
  const nothing = screen.getByTestId('history-entry-op-nothing');
  expect(within(nothing).queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument();
  expect(within(nothing).getByTestId('history-entry-undo-unavailable')).toHaveTextContent('nothing to undo');
});

const conflictEntry: OperationHistoryEntryView = {
  operationId: 'op-undo-conflict',
  operationType: 'Search & Replace',
  recordedUtc: '2026-10-06T10:00:00Z',
  status: 'Completed',
  snapshotId: 'snap-2',
  files: [
    { documentId: 'd1', outcome: 'Success', reasonCode: null, detail: null },
    { documentId: 'd2', outcome: 'Success', reasonCode: null, detail: null },
  ],
  undo: {
    status: 'Partial',
    files: [
      { documentId: 'd1', outcome: 'Restored', reasonCode: null, detail: null },
      { documentId: 'd2', outcome: 'Conflict', reasonCode: 'ExternallyModified', detail: 'File changed since the operation; newer content was not overwritten.' },
    ],
  },
};

it('Undo action follows conflict policy: conflicts are surfaced and never overwritten', () => {
  const { onUndo, onOpenDocument } = renderPanel({ entries: [conflictEntry] });
  const entry = screen.getByTestId('history-entry-op-undo-conflict');

  // HIST-02 policy: conflicted files are surfaced with their reason, restored files only as restored.
  const outcomes = within(entry).getByRole('list', { name: 'Undo outcomes for op-undo-conflict' });
  expect(within(outcomes).getByText('Restored')).toBeInTheDocument();
  const conflictRow = within(outcomes).getByRole('button', { name: 'Open two.md' }).closest('li')!;
  expect(conflictRow).toHaveTextContent('Conflict');
  expect(conflictRow).toHaveTextContent('ExternallyModified');
  expect(conflictRow).toHaveTextContent('newer content was not overwritten');
  expect(within(entry).getByTestId('history-entry-undo-conflict-warning'))
    .toHaveTextContent('1 file changed after the operation and was not overwritten');

  // The newer content can be inspected, but no force/overwrite path is presented.
  fireEvent.click(within(outcomes).getByRole('button', { name: 'Open two.md' }));
  expect(onOpenDocument).toHaveBeenCalledWith('d2');
  expect(within(entry).queryByRole('button', { name: /force|overwrite/i })).not.toBeInTheDocument();

  // Re-undo stays a plain typed action: the operation id only, no force flag.
  fireEvent.click(within(entry).getByRole('button', { name: 'Undo' }));
  expect(onUndo).toHaveBeenCalledTimes(1);
  expect(onUndo.mock.calls[0]).toEqual(['op-undo-conflict']);
});

const GIB = 1024 ** 3;

it('History usage warning appears at threshold', () => {
  renderPanel({ storageUsage: { usedBytes: 4 * GIB, capacityBytes: 5 * GIB } });
  const usage = screen.getByTestId('history-storage-usage');
  expect(usage).toHaveTextContent('4.0 GiB of 5.0 GiB');
  expect(usage).toHaveTextContent('80%');
  const warning = screen.getByTestId('history-storage-warning');
  expect(warning).toHaveTextContent('80% warning threshold');
  // Honest copy: retention is stated, automatic cleanup is not (no production caller wires it) and no silent eviction.
  expect(warning).toHaveTextContent('History is retained for 30 days');
  expect(warning).toHaveTextContent('expired snapshots are not cleaned up automatically yet');
  expect(warning).toHaveTextContent('unexpired snapshots are never evicted silently');
  expect(warning).not.toHaveTextContent(/are cleaned up automatically/i);
});

it('does not warn below the storage threshold', () => {
  renderPanel({ storageUsage: { usedBytes: 3.9 * GIB, capacityBytes: 5 * GIB } });
  expect(screen.getByTestId('history-storage-usage')).toHaveTextContent('78%');
  expect(screen.queryByTestId('history-storage-warning')).not.toBeInTheDocument();
});

it('renders a zero-capacity storage figure without dividing by zero', () => {
  renderPanel({ storageUsage: { usedBytes: 0, capacityBytes: 0 } });
  expect(screen.getByTestId('history-storage-usage')).toHaveTextContent('0.0 GiB of 0.0 GiB (0%)');
  expect(screen.queryByTestId('history-storage-warning')).not.toBeInTheDocument();
});

it('links failed files back to their documents so a partial result stays inspectable', () => {
  const { onOpenDocument } = renderPanel();
  const entry = screen.getByTestId('history-entry-op-partial');
  const files = within(entry).getByRole('list', { name: 'File outcomes for op-partial' });
  expect(within(files).getAllByRole('listitem')).toHaveLength(3);
  const failedRow = within(files).getByRole('button', { name: 'Open two.md' }).closest('li')!;
  expect(failedRow).toHaveTextContent('Failed');
  expect(failedRow).toHaveTextContent('MutationFailed');
  expect(failedRow).toHaveTextContent('original content preserved');
  fireEvent.click(within(files).getByRole('button', { name: 'Open two.md' }));
  expect(onOpenDocument).toHaveBeenCalledWith('d2');
});

it('keeps a changed-after-preview file inspectable with its stale reason and no failure link', () => {
  renderPanel();
  const entry = screen.getByTestId('history-entry-op-partial');
  const files = within(entry).getByRole('list', { name: 'File outcomes for op-partial' });
  const skippedRow = within(files).getByText('three.md').closest('li')!;
  expect(skippedRow).toHaveTextContent('Skipped');
  expect(skippedRow).toHaveTextContent('Stale');
  expect(skippedRow).toHaveTextContent('File changed after preview; re-preview required.');
  expect(within(files).queryByRole('button', { name: 'Open three.md' })).not.toBeInTheDocument();
});
