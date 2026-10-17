/**
 * App shell reachability tests (gaps G1, G3, G5, G6, G7).
 *
 * These render the shipped `App` shell and assert that the previously
 * unreachable surfaces are actually mounted and driven by the app:
 *   - G6: workspace bootstrap populates the canvas store from the API;
 *   - G3: the search panel mounts from the tool bar;
 *   - G5: the owner share panel mounts and reaches the share API;
 *   - G1/G7: a graph node double-click opens the Markdown editor.
 *
 * No test-only props are used — `App` owns the wiring, so a pass here is
 * evidence of product reachability.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
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

/** Route-based fetch mock covering every endpoint the shell touches. */
function installFetch(graph: GraphFixture = { nodes: [], edges: [] }) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : String(input);
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
    if (url.includes('/documents')) {
      return jsonResponse([
        {
          id: 'doc-1',
          filename: 'notes.md',
          currentPath: 'C:/Docs/notes.md',
          availabilityStatus: 'available',
        },
      ]);
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
});
