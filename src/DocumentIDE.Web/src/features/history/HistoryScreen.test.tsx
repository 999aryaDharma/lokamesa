import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import HistoryScreen, {
  HISTORY_OPERATIONS_UNAVAILABLE_REASON,
  type OperationHistorySource,
} from './HistoryScreen';
import type { OperationHistoryEntryView } from './HistoryPanel';
import type { UndoResultView } from './UndoConflictDialog';

const GIB = 1024 ** 3;

const appliedEntry: OperationHistoryEntryView = {
  operationId: 'op-applied',
  operationType: 'Search & Replace',
  recordedUtc: '2026-10-18T12:04:18Z',
  status: 'Completed',
  snapshotId: 'snap-1',
  files: [
    { documentId: 'd1', outcome: 'Success', reasonCode: null, detail: null },
    {
      documentId: 'd2',
      outcome: 'Failure',
      reasonCode: 'MutationFailed',
      detail: 'Atomic replace failed; original content preserved.',
    },
  ],
  undo: null,
};

const undoneEntry: OperationHistoryEntryView = {
  operationId: 'op-undone',
  operationType: 'Batch apply',
  recordedUtc: '2026-10-18T10:00:00Z',
  status: 'Completed',
  snapshotId: 'snap-2',
  files: [{ documentId: 'd1', outcome: 'Success', reasonCode: null, detail: null }],
  undo: {
    status: 'Completed',
    files: [{ documentId: 'd1', outcome: 'Restored', reasonCode: null, detail: null }],
  },
};

const payload = {
  entries: [appliedEntry, undoneEntry],
  storageUsage: { usedBytes: 2 * GIB, capacityBytes: 5 * GIB },
};

const undoResult: UndoResultView = {
  operationId: 'op-applied',
  snapshotId: 'snap-1',
  status: 'Partial',
  files: [
    { documentId: 'd1', outcome: 'Restored', reasonCode: null, detail: null },
    {
      documentId: 'd2',
      outcome: 'Conflict',
      reasonCode: 'ExternallyModified',
      detail: 'File changed since the operation; newer content was not overwritten.',
    },
  ],
};

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

function createSource(overrides: Partial<OperationHistorySource> = {}) {
  const load = vi.fn(async () => payload);
  const undo = vi.fn(async () => undoResult);
  const source: OperationHistorySource = { load, undo, ...overrides };
  return { source, load, undo };
}

function renderScreen(overrides: Partial<React.ComponentProps<typeof HistoryScreen>> = {}) {
  const { source, load, undo } = createSource();
  render(
    <HistoryScreen
      path="C:\\Docs"
      inspectorOpen={false}
      onAttachFolder={() => {}}
      onOpenDocument={() => {}}
      source={source}
      {...overrides}
    />,
  );
  return { load, undo };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('HistoryScreen (UI-03 screen 10)', () => {
  it('composes one History surface with an Operations | Git segmented control', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({ status: 'Unavailable' }));
    renderScreen();

    const control = screen.getByRole('radiogroup', { name: 'History source' });
    const operations = within(control).getByRole('radio', { name: 'Operations' });
    const git = within(control).getByRole('radio', { name: 'Git' });

    // Part A shipped Git on this row; the composed screen keeps it as the
    // default surface so the existing History navigation does not regress.
    expect(git).toHaveAttribute('aria-checked', 'true');
    expect(operations).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByTestId('git-history-screen')).toBeInTheDocument();
    expect(screen.queryByTestId('history-operations-surface')).not.toBeInTheDocument();

    fireEvent.click(operations);

    expect(operations).toHaveAttribute('aria-checked', 'true');
    expect(git).toHaveAttribute('aria-checked', 'false');
    expect(await screen.findByTestId('history-operations-surface')).toBeInTheDocument();
    expect(screen.queryByTestId('git-history-screen')).not.toBeInTheDocument();

    fireEvent.click(git);
    expect(screen.getByTestId('git-history-screen')).toBeInTheDocument();
    expect(screen.queryByTestId('history-operations-surface')).not.toBeInTheDocument();
  });

  it('renders operations with the real outcome chips and storage meter from the history payload', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({}));
    const { load } = renderScreen();

    fireEvent.click(screen.getByRole('radio', { name: 'Operations' }));

    const list = await screen.findByRole('list', { name: 'Operation history' });
    expect(list).toBeInTheDocument();
    expect(screen.getAllByTestId(/^history-entry-op-/)).toHaveLength(2);
    expect(load).toHaveBeenCalledTimes(1);

    const applied = screen.getByTestId('history-entry-op-applied');
    expect(within(applied).getByRole('heading')).toHaveTextContent('Search & Replace');
    // A Completed operation that still has a failed file is never painted as success.
    expect(within(applied).getByTestId('history-entry-status')).toHaveTextContent(
      'Completed with failures',
    );
    expect(within(applied).getByTestId('history-entry-counts')).toHaveTextContent('1 succeeded');
    expect(within(applied).getByTestId('history-entry-counts')).toHaveTextContent('1 failed');
    const fileOutcomes = within(applied).getByRole('list', { name: 'File outcomes for op-applied' });
    expect(within(fileOutcomes).getByText('Succeeded')).toBeInTheDocument();
    expect(within(fileOutcomes).getByText('Failed')).toBeInTheDocument();

    // Undone operations come from the same payload; the undo outcome is real.
    const undone = screen.getByTestId('history-entry-op-undone');
    const undoOutcomes = within(undone).getByRole('list', { name: 'Undo outcomes for op-undone' });
    expect(within(undoOutcomes).getByText('Restored')).toBeInTheDocument();

    // The storage meter is the shipped HistoryPanel meter, fed by real numbers.
    expect(screen.getByTestId('history-storage-usage')).toHaveTextContent(
      'History storage: 2.0 GiB of 5.0 GiB (40%)',
    );
  });

  it('renders a typed unavailable state instead of an empty fake list when the read fails', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({}));
    renderScreen({ source: undefined });

    fireEvent.click(screen.getByRole('radio', { name: 'Operations' }));

    const unavailable = await screen.findByTestId('history-operations-unavailable');
    expect(unavailable).toHaveTextContent(/could not be loaded/i);
    expect(screen.getByTestId('history-operations-unavailable-reason')).toHaveTextContent(
      HISTORY_OPERATIONS_UNAVAILABLE_REASON,
    );
    // The failure is named, not hidden: no rows and no route claim.
    expect(screen.getByTestId('history-operations-unavailable-reason')).toHaveTextContent(
      /did not return the Document IDE operation history/i,
    );
    // Absent is better than fake: no rows, no meter, no empty-list claim.
    expect(screen.queryByRole('list', { name: 'Operation history' })).not.toBeInTheDocument();
    expect(screen.queryAllByTestId(/^history-entry-/)).toHaveLength(0);
    expect(screen.queryByTestId('history-storage-usage')).not.toBeInTheDocument();
    expect(screen.queryByText('No operations recorded yet.')).not.toBeInTheDocument();
  });

  it('turns a rejected load into the same typed unavailable state', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({}));
    renderScreen({
      source: {
        load: async () => {
          throw new Error('HistoryUnavailable: the history read failed (503).');
        },
        undo: async () => undoResult,
      },
    });

    fireEvent.click(screen.getByRole('radio', { name: 'Operations' }));

    const unavailable = await screen.findByTestId('history-operations-unavailable');
    expect(screen.getByTestId('history-operations-unavailable-reason')).toHaveTextContent(
      'HistoryUnavailable: the history read failed (503).',
    );
    expect(unavailable).not.toHaveTextContent(/No operations recorded yet/);
    expect(screen.queryByRole('list', { name: 'Operation history' })).not.toBeInTheDocument();
  });

  it('opens the undo conflict review from a real undo result and keeps it actionable', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({}));
    const { undo, load } = renderScreen();

    fireEvent.click(screen.getByRole('radio', { name: 'Operations' }));
    const row = await screen.findByTestId('history-entry-op-applied');

    fireEvent.click(within(row).getByRole('button', { name: 'Undo' }));

    const dialog = await screen.findByRole('dialog', { name: 'Undo review' });
    expect(undo).toHaveBeenCalledWith('op-applied');
    expect(within(dialog).getByTestId('undo-conflict-warning')).toHaveTextContent(
      '1 file changed after the operation and was not overwritten',
    );
    const outcomes = within(dialog).getByRole('list', { name: 'Undo file results' });
    expect(within(outcomes).getByText('Restored')).toBeInTheDocument();
    expect(within(outcomes).getByText('Conflict')).toBeInTheDocument();
    expect(within(outcomes).getByText(/ExternallyModified/)).toBeInTheDocument();

    // Closing is a real action; the persisted history is re-read after an undo.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog', { name: 'Undo review' })).not.toBeInTheDocument();
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
  });

  it('surfaces an undo transport failure as a typed error and opens no fake review', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({}));
    const undo = vi.fn(async () => {
      throw new Error('Undo request failed (503).');
    });
    renderScreen({
      source: { load: async () => payload, undo },
    });

    fireEvent.click(screen.getByRole('radio', { name: 'Operations' }));
    const row = await screen.findByTestId('history-entry-op-applied');
    fireEvent.click(within(row).getByRole('button', { name: 'Undo' }));

    expect(await screen.findByTestId('history-undo-error')).toHaveTextContent(
      'Undo request failed (503).',
    );
    expect(screen.queryByRole('dialog', { name: 'Undo review' })).not.toBeInTheDocument();
  });

  it('shows the selected operation detail in the inspector without inventing fields', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({}));
    renderScreen({ inspectorOpen: true });

    fireEvent.click(screen.getByRole('radio', { name: 'Operations' }));
    const appliedRow = await screen.findByTestId('history-entry-op-applied');
    fireEvent.click(within(appliedRow).getByRole('button', { name: 'Inspect' }));

    const detail = screen.getByTestId('history-operation-detail');
    expect(within(detail).getByText('Search & Replace')).toBeInTheDocument();
    expect(within(detail).getByText('snap-1')).toBeInTheDocument();
    // The design's meter belongs to the real HistoryPanel; it is not duplicated.
    expect(screen.getAllByTestId('history-storage-usage')).toHaveLength(1);
  });
});
