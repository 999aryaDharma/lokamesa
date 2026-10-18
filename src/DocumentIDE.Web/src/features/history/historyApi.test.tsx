/**
 * HIST-04 — screen 10 against the *real* payload shape.
 *
 * These tests do not inject a hand-written source: they render the shipped
 * `HistoryScreen` with `createOperationHistorySource()`, so the fetch parsing,
 * the outcome chips, the storage meter and the undo review are proven end to end
 * against `GET /api/history/operations`, `GET /api/history/storage` and the
 * existing reviewed undo route.
 */
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import HistoryScreen from './HistoryScreen';
import { createOperationHistorySource } from './historyApi';

const GIB = 1024 ** 3;

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

const appliedEntry = {
  operationId: 'op-applied',
  planId: 'op-applied',
  operationType: 'Search & Replace',
  status: 'Applied',
  recordedUtc: '2026-10-18T12:04:18Z',
  startedUtc: '2026-10-18T12:04:17Z',
  completedUtc: '2026-10-18T12:04:18Z',
  affectedFileCount: 2,
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

const undoneEntry = {
  operationId: 'op-undone',
  planId: 'op-undone',
  operationType: 'Search & Replace',
  status: 'Undone',
  recordedUtc: '2026-10-18T10:00:00Z',
  startedUtc: '2026-10-18T10:00:00Z',
  completedUtc: '2026-10-18T10:00:01Z',
  affectedFileCount: 1,
  snapshotId: 'snap-2',
  files: [{ documentId: 'd1', outcome: 'Success', reasonCode: null, detail: null }],
  undo: {
    status: 'Completed',
    files: [{ documentId: 'd1', outcome: 'Restored', reasonCode: null, detail: null }],
  },
};

function availablePage(entries: unknown[]) {
  return {
    status: 'Available',
    entries,
    skip: 0,
    limit: 50,
    totalCount: entries.length,
    hasMore: false,
    nextSkip: null,
  };
}

const storage = {
  status: 'Available',
  usedBytes: 2 * GIB,
  capacityBytes: 5 * GIB,
};

interface MockOptions {
  page?: unknown;
  storage?: unknown;
  undo?: unknown;
}

function installHistoryFetch(options: MockOptions = {}) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : String(input);
    if (url.includes('/api/history/operations')) {
      return jsonResponse(options.page ?? availablePage([appliedEntry, undoneEntry]));
    }
    if (url.includes('/api/history/storage')) {
      return jsonResponse(options.storage ?? storage);
    }
    if (url.endsWith('/undo')) {
      return jsonResponse(
        options.undo ?? {
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
        },
      );
    }
    return jsonResponse({}, false, 404);
  });
}

function renderScreen() {
  render(
    <HistoryScreen
      path="C:\\Docs"
      inspectorOpen={false}
      onAttachFolder={() => {}}
      onOpenDocument={() => {}}
      documentName={(id) => ({ d1: 'one.md', d2: 'two.md' })[id] ?? id}
      source={createOperationHistorySource()}
    />,
  );
  fireEvent.click(screen.getByRole('radio', { name: 'Operations' }));
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('HistoryScreen — HIST-04 real operation listing', () => {
  it('renders real rows, persisted outcome chips and the real storage meter', async () => {
    const fetchMock = installHistoryFetch();
    renderScreen();

    const list = await screen.findByRole('list', { name: 'Operation history' });
    expect(list).toBeInTheDocument();
    expect(screen.getAllByTestId(/^history-entry-op-/)).toHaveLength(2);

    const applied = screen.getByTestId('history-entry-op-applied');
    expect(within(applied).getByTestId('history-entry-status')).toHaveTextContent(
      'Applied with failures',
    );
    expect(within(applied).getByTestId('history-entry-file-count')).toHaveTextContent('2 files');
    expect(within(applied).getByTestId('history-entry-counts')).toHaveTextContent('1 succeeded');
    expect(within(applied).getByTestId('history-entry-counts')).toHaveTextContent('1 failed');

    const undone = screen.getByTestId('history-entry-op-undone');
    expect(within(undone).getByTestId('history-entry-status')).toHaveTextContent('Undone');
    const undoOutcomes = within(undone).getByRole('list', { name: 'Undo outcomes for op-undone' });
    expect(within(undoOutcomes).getByText('Restored')).toBeInTheDocument();

    expect(screen.getByTestId('history-storage-usage')).toHaveTextContent(
      'History storage: 2.0 GiB of 5.0 GiB (40%)',
    );

    // The screen read the real bounded routes, not a per-plan history route.
    const urls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(urls.some((url) => url.includes('/api/history/operations?skip=0&limit=50'))).toBe(true);
    expect(urls.some((url) => url.endsWith('/api/history/storage'))).toBe(true);
  });

  it('undo from a listed operation opens the conflict review when it conflicts', async () => {
    installHistoryFetch();
    renderScreen();

    const row = await screen.findByTestId('history-entry-op-applied');
    fireEvent.click(within(row).getByRole('button', { name: 'Undo' }));

    const dialog = await screen.findByRole('dialog', { name: 'Undo review' });
    expect(within(dialog).getByTestId('undo-conflict-warning')).toHaveTextContent(
      '1 file changed after the operation and was not overwritten',
    );
    expect(within(dialog).getByText('Restored')).toBeInTheDocument();
    expect(within(dialog).getByText(/ExternallyModified/)).toBeInTheDocument();
  });

  it('undo from a listed operation completes without a conflict when nothing changed', async () => {
    installHistoryFetch({
      undo: {
        operationId: 'op-undone',
        snapshotId: 'snap-2',
        status: 'Completed',
        files: [{ documentId: 'd1', outcome: 'Restored', reasonCode: null, detail: null }],
      },
    });
    renderScreen();

    const row = await screen.findByTestId('history-entry-op-undone');
    fireEvent.click(within(row).getByRole('button', { name: 'Undo' }));

    const dialog = await screen.findByRole('dialog', { name: 'Undo review' });
    expect(within(dialog).getByTestId('undo-summary')).toHaveTextContent('1 restored');
    expect(within(dialog).queryByTestId('undo-conflict-warning')).not.toBeInTheDocument();
    expect(within(dialog).getByText('Restored')).toBeInTheDocument();
  });

  it('an unavailable read renders the typed unavailable state, never an empty list', async () => {
    installHistoryFetch({
      page: {
        status: 'Unavailable',
        errorCode: 'HistoryUnavailable',
        error: 'The Document IDE operation history could not be read.',
        entries: [],
        skip: 0,
        limit: 50,
        totalCount: 0,
        hasMore: false,
        nextSkip: null,
      },
    });
    renderScreen();

    const unavailable = await screen.findByTestId('history-operations-unavailable');
    expect(unavailable).toHaveTextContent(/could not be loaded/i);
    expect(screen.getByTestId('history-operations-unavailable-reason')).toHaveTextContent(
      'The Document IDE operation history could not be read.',
    );
    expect(screen.queryByRole('list', { name: 'Operation history' })).not.toBeInTheDocument();
    expect(screen.queryByText('No operations recorded yet.')).not.toBeInTheDocument();
    expect(screen.queryByTestId('history-storage-usage')).not.toBeInTheDocument();
  });

  it('an empty available history renders the real empty state, not unavailable', async () => {
    installHistoryFetch({ page: availablePage([]) });
    renderScreen();

    await waitFor(() => expect(screen.getByText('No operations recorded yet.')).toBeInTheDocument());
    expect(screen.queryByTestId('history-operations-unavailable')).not.toBeInTheDocument();
    expect(screen.getByTestId('history-storage-usage')).toBeInTheDocument();
  });

  it('an unavailable storage read is a genuine failure, not a fabricated meter', async () => {
    installHistoryFetch({
      storage: {
        status: 'Unavailable',
        errorCode: 'HistoryStorageUnavailable',
        error: 'The Document IDE history storage usage could not be read.',
        usedBytes: null,
        capacityBytes: null,
      },
    });
    renderScreen();

    const unavailable = await screen.findByTestId('history-operations-unavailable');
    expect(unavailable).toHaveTextContent(/could not be loaded/i);
    expect(screen.queryByTestId('history-storage-usage')).not.toBeInTheDocument();
  });
});
