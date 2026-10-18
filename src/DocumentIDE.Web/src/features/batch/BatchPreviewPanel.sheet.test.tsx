import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import BatchPreviewPanel from './BatchPreviewPanel';

/**
 * UI-03 screen 04 — the batch surface adopts the design's *centred* sheet
 * chrome, but stays non-modal (`scrim={false}`): a scrim would change shipped
 * batch behaviour. These assertions pin both halves of that decision.
 */

const completed = {
  planId: 'p1',
  jobId: 'j1',
  status: 'Completed',
  progress: 100,
  errorCode: null,
  scope: {
    workspaceId: 'w1',
    type: 1,
    documentIds: [],
    folderPath: 'root',
    includeSubfolders: true,
    excludedBranches: [],
    collectionId: null,
    includedSubfolders: [],
    subfolderCount: 0,
    failures: [],
  },
  filesScanned: 1,
  filesAffected: 1,
  occurrenceCount: 1,
  breakdown: { body: 1, table: 0, header: 0, footer: 0 },
  files: [
    {
      documentId: 'd1',
      fileName: 'one.docx',
      selected: true,
      lockState: 'Unknown',
      failureCode: null,
      occurrences: [{ id: 'o1', origin: 'Body', context: 'context', selected: true }],
    },
  ],
};

const applied = {
  planId: 'p1',
  snapshotId: 's1',
  status: 'Completed',
  files: [
    { documentId: 'd1', filePath: 'one.docx', outcome: 'Success', reasonCode: null, detail: null },
  ],
};

beforeEach(() => {
  Object.assign(window, {
    __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
    __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
  });
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      const data = url.endsWith('/workspaces')
        ? [{ id: 'w1', name: 'Office' }]
        : url.endsWith('/scope-options')
          ? { documents: [], collections: [] }
          : url.endsWith('/preview')
            ? { planId: 'p1', jobId: 'j1' }
            : url.endsWith('/apply')
              ? applied
              : completed;
      return { ok: true, status: 200, json: async () => data } as Response;
    }),
  );
});

async function openReview() {
  render(<BatchPreviewPanel />);
  await screen.findByRole('option', { name: 'Office' });
  fireEvent.change(screen.getByLabelText('Workspace'), { target: { value: 'w1' } });
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '1' } });
  fireEvent.change(screen.getByLabelText('Root folder'), { target: { value: 'root' } });
  fireEvent.change(screen.getByLabelText('Exact find'), { target: { value: 'needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await screen.findByText('Completed: 100%');
}

it('renders a centred sheet over the canvas without becoming modal', async () => {
  await openReview();

  const layer = screen.getByTestId('batch-sheet-layer');
  expect(layer).toHaveClass('items-center');
  expect(layer).toHaveClass('justify-center');
  // Non-modal: no dimming layer, no scrim, and outside clicks still reach the app.
  expect(layer).toHaveClass('pointer-events-none');
  expect(layer).not.toHaveClass('bg-black/20');
  expect(screen.queryByTestId('sheet-scrim')).not.toBeInTheDocument();

  const dialog = screen.getByRole('dialog', { name: 'Batch operations' });
  expect(dialog).not.toHaveAttribute('aria-modal');
  expect(dialog).toHaveClass('pointer-events-auto');

  // The shipped `region` shape (used by BatchApp.test.tsx) is preserved.
  expect(screen.getByRole('region', { name: 'Batch operations' })).toBeInTheDocument();
});

it('keeps the secondary and primary footer actions working in the sheet', async () => {
  await openReview();

  const save = screen.getByRole('button', { name: 'Save selection' });
  const apply = screen.getByRole('button', { name: 'Apply' });
  expect(save.closest('footer')).not.toBeNull();
  expect(apply.closest('footer')).not.toBeNull();

  fireEvent.click(apply);
  expect(
    await screen.findByText('Apply Completed: 1 applied / 0 skipped / 0 failed'),
  ).toBeInTheDocument();
});
