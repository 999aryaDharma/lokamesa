/**
 * App shell reachability tests (gaps G1, G3, G4, G5, G6, G7, G8).
 *
 * These render the shipped `App` shell and assert that the previously
 * unreachable surfaces are actually mounted and driven by the app:
 *   - G6: workspace bootstrap populates the canvas store from the API;
 *   - G3: the search panel mounts from the tool bar;
 *   - G5: the owner share panel mounts and reaches the share API;
 *   - G1/G7: a graph node double-click opens the Markdown editor;
 *   - G8: the PDF export panel mounts and reaches the privileged PDF route;
 *   - G4: the deliberate QuickLook-equivalent product path for a DOCX is
 *     open-in-Word, proven by a real POST /api/word/open from the canvas.
 *
 * No test-only props are used — `App` owns the wiring, so a pass here is
 * evidence of product reachability.
 */
import { render, screen, fireEvent, waitFor, act, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { useCanvasStore } from './features/canvas/canvasStore';
import { useGraphStore } from './features/graph/graphStore';
import { useSearchStore } from './features/search/searchStore';
import { useViewStore } from './features/workspace/viewStore';

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

interface GraphFixture {
  nodes: Array<{ documentId: string; displayName?: string }>;
  edges: Array<Record<string, unknown>>;
}

const markdownDocument = {
  id: 'doc-1',
  filename: 'notes.md',
  currentPath: 'C:/Docs/notes.md',
  availabilityStatus: 'available',
};

const docxDocument = {
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

/** Minimal real-shaped SCM payload used by the UI-03 reachability tests. */
function scmStatusPayload() {
  return {
    status: 'Available',
    hasRepository: true,
    worktreeRoot: 'C:/Docs',
    branchName: 'main',
    isDetached: false,
    headCommit: 'abcdef0123456789abcdef0123456789abcdef01',
    upstreamReference: 'origin/main',
    hasUpstream: true,
    ahead: 0,
    behind: 0,
    sections: {
      staged: [
        {
          path: 'Report.docx',
          originalPath: null,
          state: 'Modified',
          indexChange: 'Modified',
          workingTreeChange: 'Unmodified',
          conflictKind: 'None',
          statusCharacters: 'M ',
          absolutePath: 'C:/Docs/Report.docx',
          documentId: 'docx-1',
        },
      ],
      unstaged: [
        {
          path: 'notes.md',
          originalPath: null,
          state: 'Modified',
          indexChange: 'Unmodified',
          workingTreeChange: 'Modified',
          conflictKind: 'None',
          statusCharacters: ' M',
          absolutePath: 'C:/Docs/notes.md',
          documentId: 'doc-1',
        },
      ],
      untracked: [],
      conflicts: [],
    },
    failure: null,
  };
}

/** Route-based fetch mock covering every endpoint the shell touches. */
function installFetch(
  graph: GraphFixture = { nodes: [], edges: [] },
  documents: Array<Record<string, unknown>> = [markdownDocument],
) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : String(input);
    // Exact paths only: the single-export body must never be served to the
    // sibling `/api/pdf/export-batch` route.
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
      return jsonResponse({
        status: 'Succeeded',
        outputPath: 'C:/Docs/Report.pdf',
        sourceFingerprintBefore: 'A',
        sourceFingerprintAfter: 'A',
        errorMessage: null,
        isSuccess: true,
      });
    }
    if (url.includes('/api/word/open')) {
      return jsonResponse({ success: true, status: 'Success', errorMessage: '' });
    }
    if (url.includes('/markdown')) {
      return jsonResponse({ documentId: 'doc-1', source: '# Notes' });
    }
    if (url.includes('/folder-sources')) {
      return jsonResponse([
        {
          id: 'folder-1',
          workspaceId: 'default',
          canonicalPath: 'C:/Docs',
          displayName: 'Docs',
          availabilityStatus: 'available',
        },
      ]);
    }
    if (url.includes('/graph')) {
      return jsonResponse(graph);
    }
    if (url.includes('/api/shares')) {
      return jsonResponse([]);
    }
    if (url.includes('/api/source-control/status')) {
      return jsonResponse(scmStatusPayload());
    }
    if (url.includes('/api/source-control/remote')) {
      return jsonResponse({
        status: 'Available',
        hasRepository: true,
        worktreeRoot: 'C:/Docs',
        branchName: 'main',
        isDetached: false,
        headCommit: 'abcdef0123456789abcdef0123456789abcdef01',
        remoteName: 'origin',
        upstreamReference: 'origin/main',
        hasUpstream: true,
        ahead: 0,
        behind: 0,
        failure: null,
      });
    }
    if (url.includes('/api/source-control/conflicts')) {
      return jsonResponse({
        status: 'NoConflicts',
        hasRepository: true,
        hasConflicts: false,
        worktreeRoot: 'C:/Docs',
        branchName: 'main',
        isDetached: false,
        headCommit: 'abcdef0123456789abcdef0123456789abcdef01',
        conflicts: [],
        blockedActions: [],
        message: null,
        failure: null,
      });
    }
    if (url.includes('/api/source-control/worktrees')) {
      return jsonResponse({
        status: 'Succeeded',
        isSuccess: true,
        worktrees: [
          {
            path: 'C:/Docs',
            headCommit: 'abcdef0',
            branchRef: 'refs/heads/main',
            branchName: 'main',
            isDetached: false,
            isBare: false,
            isLocked: false,
            isPrunable: false,
            isMainWorktree: true,
          },
        ],
        errorCode: null,
        message: null,
        failure: null,
      });
    }
    if (url.includes('/api/source-control/history')) {
      return jsonResponse({
        status: 'Available',
        source: 'Git',
        errorCode: null,
        message: null,
        isAvailable: true,
        page: { commits: [], skip: 0, limit: 50, hasMore: false, nextSkip: null },
        failure: null,
      });
    }
    if (url.includes('/documents')) {
      return jsonResponse(documents);
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

describe('App shell reachability', () => {
  it('G6: bootstraps the canvas from the workspace API on startup', async () => {
    installFetch();
    render(<App />);

    await waitFor(() =>
      expect(useCanvasStore.getState().objects.length).toBeGreaterThanOrEqual(2),
    );
    expect(useCanvasStore.getState().objects.some((o) => o.id === 'folder-1')).toBe(true);
    expect(useCanvasStore.getState().objects.some((o) => o.id === 'doc-1')).toBe(true);
  });

  it('G3: mounts the search panel from the tool bar', () => {
    installFetch();
    render(<App />);

    expect(screen.queryByRole('search')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));
    expect(screen.getByRole('search')).toBeInTheDocument();
  });

  it('G5: mounts the owner share panel and reaches the share API', async () => {
    const fetchMock = installFetch();
    render(<App />);

    fireEvent.click(screen.getByRole('button', { name: 'Shares' }));

    expect(screen.getByTestId('share-panel')).toBeInTheDocument();
    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => String(input).includes('/api/shares'))).toBe(
        true,
      ),
    );
  });

  it('G1/G7: double-clicking a Markdown graph node opens the Markdown editor', async () => {
    installFetch({ nodes: [{ documentId: 'doc-1', displayName: 'notes.md' }], edges: [] });
    render(<App />);

    fireEvent.click(screen.getByTestId('view-graph-btn'));

    const node = await screen.findByTestId('graph-node-doc-1');
    fireEvent.doubleClick(node);

    expect(await screen.findByLabelText('Markdown editor')).toBeInTheDocument();
    expect(await screen.findByDisplayValue('# Notes')).toBeInTheDocument();
  });

  it('G8: mounts the PDF export surface and reaches the privileged PDF route', async () => {
    const fetchMock = installFetch(undefined, [docxDocument]);
    render(<App />);

    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.id === 'docx-1')).toBe(true),
    );
    // The panel is a real, toggleable product surface — not always-on chrome.
    expect(screen.queryByTestId('pdf-export-panel')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'PDF' }));
    expect(screen.getByTestId('pdf-export-panel')).toBeInTheDocument();

    // Selecting the DOCX on the canvas gives the export surface a live selection.
    // Driven by a real card click (no store seeding) so "canvas selection → PDF
    // export" is proven at the gesture level, mirroring the G4 double-click path.
    const card = await screen.findByLabelText('Document: Report.docx');
    fireEvent.click(card);
    await waitFor(() =>
      expect(useCanvasStore.getState().selectedIds.has('docx-1')).toBe(true),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/pdf/export')),
      ).toBe(true),
    );
    const exportCall = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/pdf/export'),
    )!;
    expect(JSON.parse(String(exportCall[1]?.body))).toMatchObject({
      sourceDocxPath: 'C:/Docs/Report.docx',
    });
    expect(await screen.findByText('C:/Docs/Report.pdf')).toBeInTheDocument();
  });

  it('G8: separate export reaches the batch route (the mock does not shadow it)', async () => {
    const fetchMock = installFetch(undefined, [docxDocument, annexDocument]);
    render(<App />);

    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.id === 'docx-2')).toBe(true),
    );
    fireEvent.click(screen.getByRole('button', { name: 'PDF' }));

    // Canvas cards use plain-click replace selection (no additive modifier path
    // is exposed by DocumentNode), so a two-document selection is seeded at
    // store level here; the export gesture itself is a real button click. The
    // assertion below fails if the single-export body is served to the batch
    // route.
    act(() => {
      useCanvasStore.getState().setSelection(['docx-1', 'docx-2']);
    });
    fireEvent.click(screen.getByRole('button', { name: 'Export as separate PDFs' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) =>
          String(input).endsWith('/api/pdf/export-batch'),
        ),
      ).toBe(true),
    );
    const batchCall = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/pdf/export-batch'),
    )!;
    const batchBody = JSON.parse(String(batchCall[1]?.body)) as {
      sourceDocxPaths: string[];
      outputDirectory: string;
    };
    expect(batchBody.outputDirectory).toBe('C:/Docs');
    expect(batchBody.sourceDocxPaths).toHaveLength(2);
    // The batch response (not the single-export body) drove the results.
    expect(await screen.findByRole('status')).toHaveTextContent(
      '2 PDFs generated / 0 skipped / 0 failed',
    );
  });

  it('UI-03: the Source control sidebar row mounts screen 05 and reads GET status', async () => {
    const fetchMock = installFetch();
    render(<App />);

    fireEvent.click(screen.getByTestId('sidebar-source-control'));

    expect(await screen.findByTestId('source-control-screen')).toBeInTheDocument();
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) =>
          String(input).includes('/api/source-control/status'),
        ),
      ).toBe(true),
    );
    expect(await screen.findByTestId('source-control-section-unstaged')).toBeInTheDocument();
  });

  it('UI-03: a .docx change opens screen 06 and its primary action reaches POST /api/word/open', async () => {
    const fetchMock = installFetch();
    render(<App />);

    fireEvent.click(screen.getByTestId('sidebar-source-control'));
    fireEvent.click(await screen.findByTestId('source-control-entry-Report.docx'));
    fireEvent.click(screen.getByRole('button', { name: 'Open document diff' }));

    expect(await screen.findByTestId('docx-diff-screen')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Open in Word' }));
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/word/open')),
      ).toBe(true),
    );
    const call = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/word/open'),
    )!;
    expect(JSON.parse(String(call[1]?.body))).toEqual({ documentId: 'docx-1' });
  });

  it('UI-03: the Worktrees sidebar row mounts screen 07 and reads GET worktrees', async () => {
    const fetchMock = installFetch();
    render(<App />);

    fireEvent.click(screen.getByTestId('sidebar-worktrees'));

    expect(await screen.findByTestId('worktrees-screen')).toBeInTheDocument();
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) =>
          String(input).includes('/api/source-control/worktrees'),
        ),
      ).toBe(true),
    );
  });

  it('UI-03: the History sidebar row opens the composed History surface on its Git tab', async () => {
    const fetchMock = installFetch();
    render(<App />);

    fireEvent.click(screen.getByTestId('sidebar-history'));

    expect(await screen.findByTestId('history-screen')).toBeInTheDocument();
    expect(screen.getByTestId('git-history-screen')).toBeInTheDocument();
    expect(screen.getByTestId('git-history-panel')).toBeInTheDocument();
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) =>
          String(input).includes('/api/source-control/history'),
        ),
      ).toBe(true),
    );
  });

  it('UI-03: the History segmented control switches to Document IDE operations and states the missing listing route', async () => {
    installFetch();
    render(<App />);

    fireEvent.click(screen.getByTestId('sidebar-history'));
    const control = await screen.findByRole('radiogroup', { name: 'History source' });
    expect(within(control).getByRole('radio', { name: 'Git' })).toHaveAttribute(
      'aria-checked',
      'true',
    );

    fireEvent.click(within(control).getByRole('radio', { name: 'Operations' }));

    // No route lists Document IDE operations: a typed unavailable state, never
    // an empty list or sample rows.
    expect(await screen.findByTestId('history-operations-unavailable')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Operation history' })).not.toBeInTheDocument();
    expect(screen.queryAllByTestId(/^history-entry-/)).toHaveLength(0);
  });

  it('G4: double-clicking a DOCX canvas card opens it in Word (the deliberate QuickLook product path)', async () => {
    const fetchMock = installFetch(undefined, [docxDocument]);
    render(<App />);

    const card = await screen.findByLabelText('Document: Report.docx');
    fireEvent.doubleClick(card);

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/word/open')),
      ).toBe(true),
    );
    const openCall = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/word/open'),
    )!;
    expect(JSON.parse(String(openCall[1]?.body))).toEqual({ documentId: 'docx-1' });
  });
});
