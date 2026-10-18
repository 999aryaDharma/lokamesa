import { render, screen, within, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { UndoConflictDialog, type UndoResultView } from './UndoConflictDialog';

const result: UndoResultView = {
  operationId: 'op-1',
  snapshotId: 'snap-1',
  status: 'Partial',
  files: [
    { documentId: 'd1', outcome: 'Restored', reasonCode: null, detail: null },
    { documentId: 'd2', outcome: 'Conflict', reasonCode: 'ExternallyModified', detail: 'File changed since the operation; newer content was not overwritten.' },
    { documentId: 'd3', outcome: 'Failure', reasonCode: 'SnapshotMissing', detail: 'Snapshot copy no longer exists; cannot restore.' },
    { documentId: 'd4', outcome: 'Skipped', reasonCode: 'Cancelled', detail: 'Undo cancelled before this file was processed.' },
  ],
};

const names: Record<string, string> = { d1: 'one.md', d2: 'two.md', d3: 'three.md', d4: 'four.md' };

function renderDialog(overrides: Partial<Parameters<typeof UndoConflictDialog>[0]> = {}) {
  const onClose = vi.fn();
  render(<UndoConflictDialog isOpen result={result} documentName={id => names[id] ?? id} onClose={onClose} {...overrides} />);
  return onClose;
}

it('renders one row per file with its outcome', () => {
  renderDialog();
  const list = screen.getByRole('list', { name: 'Undo file results' });
  const rows = within(list).getAllByRole('listitem');
  expect(rows).toHaveLength(4);
  expect(rows.map(row => row.textContent)).toEqual([
    expect.stringContaining('one.md'),
    expect.stringContaining('two.md'),
    expect.stringContaining('three.md'),
    expect.stringContaining('four.md'),
  ]);
  expect(within(list).getByText('Restored')).toBeInTheDocument();
  expect(within(list).getByText('Conflict')).toBeInTheDocument();
  expect(within(list).getByText('Failed')).toBeInTheDocument();
  expect(within(list).getByText('Skipped')).toBeInTheDocument();
});

it('warns that conflicted files were not overwritten', () => {
  renderDialog();
  const warning = screen.getByTestId('undo-conflict-warning');
  expect(warning).toHaveTextContent('1 file changed after the operation and was not overwritten');
  const row = screen.getByText('two.md').closest('li')!;
  expect(row).toHaveTextContent('ExternallyModified');
});

it('shows failure reasons and details per file', () => {
  renderDialog();
  const row = screen.getByText('three.md').closest('li')!;
  expect(row).toHaveTextContent('SnapshotMissing');
  expect(row).toHaveTextContent('Snapshot copy no longer exists; cannot restore.');
});

it('summarizes restored, conflicted, failed and skipped counts', () => {
  renderDialog();
  expect(screen.getByTestId('undo-summary')).toHaveTextContent('1 restored');
  expect(screen.getByTestId('undo-summary')).toHaveTextContent('1 conflict');
  expect(screen.getByTestId('undo-summary')).toHaveTextContent('1 failed');
  expect(screen.getByTestId('undo-summary')).toHaveTextContent('1 skipped');
});

it('calls onClose from the close button', () => {
  const onClose = renderDialog();
  fireEvent.click(screen.getByRole('button', { name: 'Close' }));
  expect(onClose).toHaveBeenCalledTimes(1);
});

it('renders nothing when not open', () => {
  renderDialog({ isOpen: false });
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
});

it('does not show the conflict warning when no file conflicted', () => {
  renderDialog({
    result: { ...result, status: 'Completed', files: [result.files[0]] },
  });
  expect(screen.queryByTestId('undo-conflict-warning')).not.toBeInTheDocument();
});
