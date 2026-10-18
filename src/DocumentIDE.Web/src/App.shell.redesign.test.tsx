/**
 * UI-01 — macOS shell redesign acceptance tests.
 *
 * The restyle must not move the product: these tests pin the *new* structure
 * (three panes, one control per accessible name) and prove the behaviour still
 * comes from the same stores, on the same data, as before:
 *
 *   - the toolbar's segmented control drives `useViewStore` (the single view
 *     seam) and the sidebar's rows drive the same seam;
 *   - the sidebar lists the *real* folder sources the shell bootstrapped, with
 *     their real document counts;
 *   - the inspector follows the active view and the live canvas selection;
 *   - on the canvas a folder object and a file object stay structurally
 *     distinct objects.
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { useCanvasStore } from './features/canvas/canvasStore';
import { useGraphStore } from './features/graph/graphStore';
import { useSearchStore } from './features/search/searchStore';
import { useViewStore } from './features/workspace/viewStore';

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

const markdownDocument = {
  id: 'doc-md',
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

function installFetch(nodes: Array<{ documentId: string; displayName?: string }> = []) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : String(input);
    if (url.includes('/markdown')) {
      return jsonResponse({ documentId: docxDocument.id, source: '# Notes' });
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
      return jsonResponse({ nodes, edges: [] });
    }
    if (url.includes('/documents')) {
      return jsonResponse([markdownDocument, docxDocument]);
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

describe('UI-01 shell structure', () => {
  it('renders the three panes with one control per accessible name', () => {
    installFetch();
    render(<App />);

    expect(screen.getByRole('banner', { name: 'Toolbar' })).toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: 'Sidebar' })).toBeInTheDocument();
    expect(screen.getByRole('main', { name: 'Workspace content' })).toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: 'Inspector' })).toBeInTheDocument();

    // Exactly one reachable control per name the shell tests query.
    for (const name of ['Search', 'Shares', 'PDF', 'Add folder', 'Batch operations']) {
      expect(screen.getAllByRole('button', { name })).toHaveLength(1);
    }
  });

  it('lists the real folder sources with their real document counts', async () => {
    installFetch();
    render(<App />);

    const row = await screen.findByTestId('sidebar-folder-folder-1');
    expect(row).toHaveTextContent('Docs');
    // Both bootstrap documents live under `C:/Docs`, so the containment count
    // the sidebar shows is 2 — read from `groupDocumentsByFolder`, not guessed.
    expect(row).toHaveTextContent('2');
  });

  it('drives the single view seam from the toolbar and from the sidebar', async () => {
    installFetch();
    render(<App />);

    fireEvent.click(screen.getByTestId('view-list-btn'));
    expect(useViewStore.getState().activeView).toBe('list');
    expect(screen.getByRole('region', { name: 'Document list' })).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('sidebar-view-graph'));
    expect(useViewStore.getState().activeView).toBe('graph');
    expect(screen.getByTestId('graph-view')).toBeInTheDocument();
  });

  it('follows the live canvas selection in the inspector once a document is selected', async () => {
    installFetch();
    render(<App />);

    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.id === 'docx-1')).toBe(true),
    );

    const card = await screen.findByLabelText('Document: Report.docx');
    fireEvent.click(card);

    await waitFor(() => expect(useCanvasStore.getState().selectedIds.has('docx-1')).toBe(true));
    expect(screen.getByRole('region', { name: 'Selection' })).toBeInTheDocument();
    expect(screen.getByText('DOCX')).toBeInTheDocument();
  });

  it('renders folder and file objects as structurally distinct canvas objects', async () => {
    installFetch();
    render(<App />);

    await waitFor(() =>
      expect(useCanvasStore.getState().objects.some((o) => o.type === 'folder')).toBe(true),
    );

    const folder = screen.getByTestId('folder-object');
    expect(folder).toHaveAttribute('data-kind', 'folder');
    expect(folder.querySelector('[data-part="folder-body"]')).not.toBeNull();
    expect(folder.querySelector('[data-part="file-sheet"]')).toBeNull();

    const file = await screen.findByLabelText('Document: Report.docx');
    expect(file).toHaveAttribute('data-kind', 'docx');
    expect(file.querySelector('[data-part="file-sheet"]')).not.toBeNull();
    expect(file.querySelector('[data-part="folder-body"]')).toBeNull();
  });

  it('shows the graph scope and edge legend in the inspector for the graph view', async () => {
    installFetch([{ documentId: 'doc-md', displayName: 'notes.md' }]);
    render(<App />);

    fireEvent.click(screen.getByTestId('view-graph-btn'));

    const node = await screen.findByTestId('graph-node-doc-md');
    // The graph node carries its type glyph/kind like every other surface.
    await waitFor(() =>
      expect(useGraphStore.getState().nodes.length).toBeGreaterThan(0),
    );
    expect(node).toHaveAttribute('data-kind', 'md');

    expect(screen.getByRole('region', { name: 'Scope' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Edge legend' })).toBeInTheDocument();
  });
});
