/**
 * UI-03 screen 12 — dialogs & overlays reachability.
 *
 * The design shows six reviewed-plan overlays. This build mounts exactly the
 * ones with a real data source and a real trigger:
 *
 *   - Merge order            → PdfExportPanel (PdfActions)
 *   - PDF progress           → PdfExportPanel (PdfActions)
 *   - PDF collision          → PdfExportPanel (PdfActions)
 *   - Undo review            → HistoryScreen (proved in HistoryScreen.test.tsx)
 *
 * QuickLook (needs the WORD-04 page pipeline), ContextPackDialog (no data
 * source) and MissingResourceCard (no rendered host) stay unmounted. The
 * negative assertions below exist so nobody "fixes" this with a stub later.
 */
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { useCanvasStore } from './features/canvas/canvasStore';
import { useGraphStore } from './features/graph/graphStore';
import { useSearchStore } from './features/search/searchStore';
import { useViewStore } from './features/workspace/viewStore';

const reportDocument = {
  id: 'docx-1',
  filename: 'Report.docx',
  extension: '.docx',
  currentPath: 'C:/Docs/Report.docx',
  documentType: 'docx',
  availabilityStatus: 'available',
};

const annexDocument = {
  id: 'docx-2',
  filename: 'Annex.docx',
  extension: '.docx',
  currentPath: 'C:/Docs/Annex.docx',
  documentType: 'docx',
  availabilityStatus: 'available',
};

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

interface FetchOptions {
  singleExport?: () => Response;
}

function installFetch({ singleExport }: FetchOptions = {}) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : String(input);
    if (url.endsWith('/api/pdf/export-batch')) {
      return jsonResponse({
        totalCount: 2,
        succeededCount: 2,
        skippedCount: 0,
        failedCount: 0,
        cancelledCount: 0,
        isSuccess: true,
        summary: '2 PDFs generated / 0 skipped / 0 failed',
        files: [
          {
            sourceDocxPath: 'C:/Docs/Report.docx',
            status: 'Succeeded',
            outputPath: 'C:/Docs/Report.pdf',
            errorMessage: null,
          },
          {
            sourceDocxPath: 'C:/Docs/Annex.docx',
            status: 'Succeeded',
            outputPath: 'C:/Docs/Annex.pdf',
            errorMessage: null,
          },
        ],
      });
    }
    if (url.endsWith('/api/pdf/export')) {
      return (
        singleExport?.() ??
        jsonResponse({
          status: 'Succeeded',
          outputPath: 'C:/Docs/Report.pdf',
          sourceFingerprintBefore: 'A',
          sourceFingerprintAfter: 'A',
          errorMessage: null,
          isSuccess: true,
        })
      );
    }
    if (url.includes('/api/word/open')) {
      return jsonResponse({ success: true, status: 'Success', errorMessage: '' });
    }
    if (url.includes('/markdown')) {
      return jsonResponse({ documentId: 'docx-1', source: '' });
    }
    if (url.includes('/folder-sources')) {
      return jsonResponse([]);
    }
    if (url.includes('/graph')) {
      return jsonResponse({ nodes: [], edges: [] });
    }
    if (url.includes('/api/shares')) {
      return jsonResponse([]);
    }
    if (url.includes('/documents')) {
      return jsonResponse([reportDocument, annexDocument]);
    }
    return jsonResponse({}, false, 404);
  });
}

beforeEach(() => {
  localStorage.clear();
  useCanvasStore.getState().reset();
  useSearchStore.getState().reset();
  useViewStore.getState().reset();
  useGraphStore.getState().reset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('UI-03 screen 12 — only overlays with a real source and trigger', () => {
  it('mounts the merge-order overlay from the real PDF panel', async () => {
    installFetch();
    render(<App />);
    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.id === 'docx-2')).toBe(true),
    );

    expect(screen.queryAllByRole('dialog')).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'PDF' }));
    act(() => {
      useCanvasStore.getState().setSelection(['docx-1', 'docx-2']);
    });
    fireEvent.click(screen.getByRole('button', { name: 'Merge to PDF' }));

    expect(screen.getByRole('dialog', { name: 'Merge document order' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Merge document order' })).not.toBeInTheDocument();
  });

  it('mounts the PDF collision overlay when the engine reports a real collision', async () => {
    installFetch({
      singleExport: () =>
        jsonResponse({
          status: 'OutputCollision',
          outputPath: 'C:/Docs/Report.pdf',
          sourceFingerprintBefore: 'A',
          sourceFingerprintAfter: 'A',
          errorMessage: null,
          isSuccess: false,
        }),
    });
    render(<App />);
    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.id === 'docx-1')).toBe(true),
    );

    fireEvent.click(screen.getByRole('button', { name: 'PDF' }));
    act(() => {
      useCanvasStore.getState().setSelection(['docx-1']);
    });
    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));

    const collision = await screen.findByRole('group', { name: 'Output file already exists' });
    expect(collision).toHaveTextContent('Report.docx');
    expect(collision).toHaveTextContent('never overwritten silently');
    expect(screen.getByRole('radio', { name: 'Overwrite' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Rename' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Skip' })).toBeInTheDocument();
  });

  it('mounts the PDF progress overlay from the real batch export response', async () => {
    installFetch();
    render(<App />);
    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.id === 'docx-2')).toBe(true),
    );

    fireEvent.click(screen.getByRole('button', { name: 'PDF' }));
    act(() => {
      useCanvasStore.getState().setSelection(['docx-1', 'docx-2']);
    });
    fireEvent.click(screen.getByRole('button', { name: 'Export as separate PDFs' }));

    expect(
      await screen.findByRole('region', { name: 'Batch PDF export progress' }),
    ).toHaveTextContent('2 PDFs generated / 0 skipped / 0 failed');
  });

  it('leaves QuickLook, the context pack and the missing-resource overlay unmounted', async () => {
    installFetch();
    render(<App />);
    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.id === 'docx-1')).toBe(true),
    );

    // Open the surfaces that could plausibly host a stubbed overlay.
    fireEvent.click(screen.getByRole('button', { name: 'PDF' }));
    act(() => {
      useCanvasStore.getState().setSelection(['docx-1']);
    });

    // QuickLook needs the WORD-04 Word → PDF → page-image pipeline; absent.
    expect(screen.queryByTestId('quick-look-dialog')).not.toBeInTheDocument();
    // Context pack has no data source; absent.
    expect(
      screen.queryByRole('dialog', { name: 'Context Pack Export & Token Estimate' }),
    ).not.toBeInTheDocument();
    expect(screen.queryByTestId('budget-warning-banner')).not.toBeInTheDocument();
    // MissingResourceCard has no rendered host in this build; absent.
    expect(screen.queryByRole('group', { name: /Missing (file|folder):/ })).not.toBeInTheDocument();
    // Undo review is mounted by screen 10 only when a real undo result exists.
    expect(screen.queryByRole('dialog', { name: 'Undo review' })).not.toBeInTheDocument();
  });
});
