/**
 * CAN-08: List View & View Switch — DocumentList integration tests
 *
 * TDD RED→GREEN→REFACTOR. The first mandated behavior is
 * "Switching view does not lose workspace" and is written before the
 * ViewSwitcher / DocumentList / App routing exist.
 *
 * The list must be another view of the SAME DocumentIds held by the
 * existing canvas store — never an imported or cloned copy.
 */
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../../App';
import { useCanvasStore } from '../canvas/canvasStore';
import type { CanvasObject } from '../canvas/types';
import DocumentList from './DocumentList';

// ---------------------------------------------------------------------------
// localStorage mock helpers (same approach as DocumentCanvas.test.tsx)
// ---------------------------------------------------------------------------
let localStorageStore: Record<string, string> = {};
const localStorageMock: Storage = {
  getItem: (key) => localStorageStore[key] ?? null,
  setItem: (key, value) => { localStorageStore[key] = String(value); },
  removeItem: (key) => { delete localStorageStore[key]; },
  clear: () => { localStorageStore = {}; },
  length: 0,
  key: () => null,
};

function makeDocument(
  id: string,
  title: string,
  x: number,
  y: number,
  extra: Partial<CanvasObject> = {},
): CanvasObject {
  return { id, type: 'document', title, x, y, width: 140, height: 180, ...extra };
}

beforeEach(() => {
  localStorageStore = {};
  vi.stubGlobal('localStorage', localStorageMock);
  useCanvasStore.getState().reset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Switching view does not lose workspace', () => {
  it('keeps the same workspace documents when switching from Canvas to List and back', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 100, 200),
        makeDocument('doc-2', 'Catatan.md', 300, 400),
      ]);
    });

    render(<App />);

    // Canvas is the default view.
    expect(screen.getByTestId('canvas-surface')).toBeInTheDocument();

    // Switch to List view.
    fireEvent.click(screen.getByTestId('view-list-btn'));
    expect(screen.getByTestId('list-view')).toBeInTheDocument();
    expect(screen.queryByTestId('canvas-surface')).toBeNull();

    // List renders the SAME DocumentIds coming from the workspace store.
    const rowIds = screen
      .getAllByTestId(/^list-row-/)
      .map((row) => row.getAttribute('data-document-id'))
      .sort();
    const storeIds = useCanvasStore.getState().objects.map((o) => o.id).sort();
    expect(rowIds).toEqual(storeIds);
    expect(rowIds).toEqual(['doc-1', 'doc-2']);

    // The workspace dataset itself is untouched by the view switch.
    expect(useCanvasStore.getState().objects.map((o) => o.id)).toEqual(['doc-1', 'doc-2']);

    // Switch back to Canvas — the same objects are still there.
    fireEvent.click(screen.getByTestId('view-canvas-btn'));
    expect(screen.getByTestId('canvas-surface')).toBeInTheDocument();
    const cardIds = Array.from(document.querySelectorAll('[data-object-id]'))
      .map((el) => el.getAttribute('data-object-id'))
      .sort();
    expect(cardIds).toEqual(['doc-1', 'doc-2']);
  });

  it('routes Graph to the existing graph placeholder without losing workspace', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 100, 200),
        makeDocument('doc-2', 'Catatan.md', 300, 400),
      ]);
    });

    render(<App />);

    fireEvent.click(screen.getByTestId('view-graph-btn'));
    expect(screen.getByTestId('graph-view')).toBeInTheDocument();
    expect(screen.queryByTestId('canvas-surface')).toBeNull();
    expect(useCanvasStore.getState().objects).toHaveLength(2);

    fireEvent.click(screen.getByTestId('view-canvas-btn'));
    expect(screen.getByTestId('canvas-surface')).toBeInTheDocument();
    expect(useCanvasStore.getState().objects).toHaveLength(2);
  });
});

describe('Persisted active view', () => {
  it('opens the persisted view on mount instead of the default canvas', () => {
    // Mirrors ACTIVE_VIEW_STORAGE_KEY from features/workspace/viewStore.
    localStorageStore['documentide_active_view'] = JSON.stringify('list');

    render(<App />);

    expect(screen.getByTestId('list-view')).toBeInTheDocument();
    expect(screen.queryByTestId('canvas-surface')).toBeNull();
  });
});

describe('List view columns for key metadata', () => {
  it('renders Name, Type, and Status column headers', () => {
    render(<DocumentList />);

    expect(screen.getByRole('columnheader', { name: /name/i })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: /type/i })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: /status/i })).toBeInTheDocument();
  });

  it('shows Type derived from docType or the file extension', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'Laporan.docx', 0, 0, { docType: 'docx' }),
        makeDocument('doc-2', 'Catatan.md', 0, 0, { docType: 'md' }),
        makeDocument('doc-3', 'Dokumen.docx', 0, 0),
        makeDocument('doc-4', 'Misteri.bin', 0, 0),
        makeDocument('folder-1', 'Arsip', 0, 0, { type: 'folder' }),
      ]);
    });

    render(<DocumentList />);

    expect(within(screen.getByTestId('list-row-doc-1')).getByText('DOCX')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-2')).getByText('MD')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-3')).getByText('DOCX')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-4')).getByText('Document')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-folder-1')).getByText('Folder')).toBeInTheDocument();
  });

  it('shows Status from missing / thumbnail / status metadata', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'Missing.docx', 0, 0, { isMissing: true }),
        makeDocument('doc-2', 'Loading.docx', 0, 0, { thumbnailState: 'loading' }),
        makeDocument('doc-3', 'Stale.docx', 0, 0, { thumbnailState: 'stale' }),
        makeDocument('doc-4', 'Custom.docx', 0, 0, { statusText: 'Synced' }),
        makeDocument('doc-5', 'Ready.docx', 0, 0, { thumbnailState: 'ready' }),
        makeDocument('doc-6', 'Plain.docx', 0, 0),
      ]);
    });

    render(<DocumentList />);

    expect(within(screen.getByTestId('list-row-doc-1')).getByText('Missing')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-2')).getByText('Loading')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-3')).getByText('Stale')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-4')).getByText('Synced')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-5')).getByText('Ready')).toBeInTheDocument();
    expect(within(screen.getByTestId('list-row-doc-6')).getByText('—')).toBeInTheDocument();
  });
});

describe('List filtering', () => {
  it('filters rows by name (case-insensitive substring)', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 0, 0),
        makeDocument('doc-2', 'Catatan.md', 0, 0),
        makeDocument('doc-3', 'BA-September.md', 0, 0),
      ]);
    });

    render(<DocumentList />);

    fireEvent.change(screen.getByLabelText('Filter documents'), { target: { value: 'ba-' } });

    expect(screen.getByTestId('list-row-doc-1')).toBeInTheDocument();
    expect(screen.getByTestId('list-row-doc-3')).toBeInTheDocument();
    expect(screen.queryByTestId('list-row-doc-2')).toBeNull();
  });

  it('filters rows by document type', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 0, 0),
        makeDocument('doc-2', 'Catatan.md', 0, 0),
        makeDocument('folder-1', 'Arsip', 0, 0, { type: 'folder' }),
      ]);
    });

    render(<DocumentList />);

    fireEvent.change(screen.getByLabelText('Filter by type'), { target: { value: 'md' } });

    expect(screen.getByTestId('list-row-doc-2')).toBeInTheDocument();
    expect(screen.queryByTestId('list-row-doc-1')).toBeNull();
    expect(screen.queryByTestId('list-row-folder-1')).toBeNull();
  });

  it('shows an empty state when no row matches the filter', () => {
    act(() => {
      useCanvasStore.getState().setObjects([makeDocument('doc-1', 'BA-Agustus.docx', 0, 0)]);
    });

    render(<DocumentList />);

    fireEvent.change(screen.getByLabelText('Filter documents'), { target: { value: 'zzz' } });

    expect(screen.queryByTestId('list-row-doc-1')).toBeNull();
    expect(screen.getByText('No documents match the current filter')).toBeInTheDocument();
  });
});

describe('List selection can feed existing selection model', () => {
  it('feeds a row checkbox toggle into the shared canvas selection model', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 100, 200),
        makeDocument('doc-2', 'Catatan.md', 300, 400),
      ]);
    });

    render(<App />);
    fireEvent.click(screen.getByTestId('view-list-btn'));

    fireEvent.click(screen.getByLabelText('Select BA-Agustus.docx'));

    // The existing canvas store is the single selection model.
    expect(useCanvasStore.getState().selectedIds.has('doc-1')).toBe(true);
    expect(useCanvasStore.getState().selectedIds.has('doc-2')).toBe(false);
    expect(screen.getByTestId('list-row-doc-1')).toHaveAttribute('aria-selected', 'true');

    // Switching back to Canvas shows the card selected — same selection state.
    fireEvent.click(screen.getByTestId('view-canvas-btn'));
    expect(document.querySelector('[data-object-id="doc-1"]')?.getAttribute('aria-selected')).toBe('true');
    expect(document.querySelector('[data-object-id="doc-2"]')?.getAttribute('aria-selected')).toBe('false');
  });

  it('preserves a canvas-made selection when switching to the list', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 100, 200),
        makeDocument('doc-2', 'Catatan.md', 300, 400),
      ]);
      useCanvasStore.getState().toggleSelect('doc-1');
    });

    render(<App />);
    fireEvent.click(screen.getByTestId('view-list-btn'));

    expect(screen.getByLabelText('Select BA-Agustus.docx')).toBeChecked();
    expect(screen.getByLabelText('Select Catatan.md')).not.toBeChecked();
  });

  it('toggles selection when the row itself is clicked (single toggle, not double)', () => {
    act(() => {
      useCanvasStore.getState().setObjects([makeDocument('doc-1', 'BA-Agustus.docx', 0, 0)]);
    });

    render(<DocumentList />);

    fireEvent.click(screen.getByTestId('list-row-doc-1'));
    expect(useCanvasStore.getState().selectedIds.has('doc-1')).toBe(true);

    fireEvent.click(screen.getByTestId('list-row-doc-1'));
    expect(useCanvasStore.getState().selectedIds.has('doc-1')).toBe(false);
  });

  it('selects all visible rows in bulk and clears them again', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 0, 0),
        makeDocument('doc-2', 'Catatan.md', 0, 0),
        makeDocument('doc-3', 'BA-September.md', 0, 0),
      ]);
    });

    render(<DocumentList />);

    fireEvent.click(screen.getByLabelText('Select all visible'));
    expect(useCanvasStore.getState().selectedIds.size).toBe(3);
    expect(screen.getByTestId('list-selection-count')).toHaveTextContent('3 of 3 selected');

    fireEvent.click(screen.getByLabelText('Select all visible'));
    expect(useCanvasStore.getState().selectedIds.size).toBe(0);
  });

  it('bulk selection only affects rows visible under the current filter', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 0, 0),
        makeDocument('doc-2', 'Catatan.md', 0, 0),
      ]);
    });

    render(<DocumentList />);
    fireEvent.change(screen.getByLabelText('Filter documents'), { target: { value: 'ba' } });

    fireEvent.click(screen.getByLabelText('Select all visible'));

    expect(useCanvasStore.getState().selectedIds.has('doc-1')).toBe(true);
    expect(useCanvasStore.getState().selectedIds.has('doc-2')).toBe(false);
  });

  it('clears selection with the clear-selection action', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 0, 0),
        makeDocument('doc-2', 'Catatan.md', 0, 0),
      ]);
    });

    render(<DocumentList />);
    fireEvent.click(screen.getByLabelText('Select BA-Agustus.docx'));
    expect(useCanvasStore.getState().selectedIds.size).toBe(1);

    fireEvent.click(screen.getByLabelText('Clear selection'));
    expect(useCanvasStore.getState().selectedIds.size).toBe(0);
  });
});

describe('Acceptance: list is another view of the same DocumentIds', () => {
  it('never replaces or re-imports workspace objects during list interactions', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 100, 200),
        makeDocument('doc-2', 'Catatan.md', 300, 400),
      ]);
    });
    const objectsBefore = useCanvasStore.getState().objects;

    render(<DocumentList />);
    fireEvent.change(screen.getByLabelText('Filter documents'), { target: { value: 'ba' } });
    fireEvent.click(screen.getByLabelText('Select BA-Agustus.docx'));
    fireEvent.click(screen.getByLabelText('Select all visible'));

    // Same array reference and same ids: the list derived a view, never copied data.
    expect(useCanvasStore.getState().objects).toBe(objectsBefore);
    expect(useCanvasStore.getState().objects.map((o) => o.id)).toEqual(['doc-1', 'doc-2']);
  });
});

describe('List sorting', () => {
  const rowIds = () =>
    screen.getAllByTestId(/^list-row-/).map((row) => row.getAttribute('data-document-id'));

  it('sorts by name ascending by default and toggles direction via the column header', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-c', 'Charlie.docx', 0, 0),
        makeDocument('doc-a', 'Alpha.docx', 0, 0),
        makeDocument('doc-b', 'Bravo.docx', 0, 0),
      ]);
    });

    render(<DocumentList />);

    expect(rowIds()).toEqual(['doc-a', 'doc-b', 'doc-c']);
    expect(screen.getByRole('columnheader', { name: /name/i })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );

    fireEvent.click(screen.getByLabelText('Sort by name'));

    expect(rowIds()).toEqual(['doc-c', 'doc-b', 'doc-a']);
    expect(screen.getByRole('columnheader', { name: /name/i })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
  });

  it('sorts by status when its column header is used', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'A.docx', 0, 0),
        makeDocument('doc-2', 'B.docx', 0, 0, { thumbnailState: 'ready' }),
        makeDocument('doc-3', 'C.docx', 0, 0, { isMissing: true }),
      ]);
    });

    render(<DocumentList />);

    fireEvent.click(screen.getByLabelText('Sort by status'));

    expect(rowIds()).toEqual(['doc-3', 'doc-2', 'doc-1']);
    expect(screen.getByRole('columnheader', { name: /status/i })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
    expect(screen.getByRole('columnheader', { name: /name/i })).toHaveAttribute('aria-sort', 'none');
  });
});

describe('Returning to Canvas preserves coordinates', () => {
  it('returns to canvas with exact object coordinates, viewport, and selection intact', () => {
    act(() => {
      useCanvasStore.getState().setObjects([
        makeDocument('doc-1', 'BA-Agustus.docx', 100, 200),
        makeDocument('doc-2', 'Catatan.md', 300, 400),
      ]);
      useCanvasStore.getState().setViewport({ x: 42, y: -17, zoom: 1.25 });
    });

    render(<App />);

    fireEvent.click(screen.getByTestId('view-list-btn'));

    // Interact with the list: filter, sort, and select.
    fireEvent.change(screen.getByLabelText('Filter documents'), { target: { value: 'ba' } });
    fireEvent.click(screen.getByLabelText('Sort by name'));
    fireEvent.click(screen.getByLabelText('Select BA-Agustus.docx'));

    fireEvent.click(screen.getByTestId('view-canvas-btn'));

    // Store coordinates and viewport are exactly as they were.
    const state = useCanvasStore.getState();
    expect(state.viewport.x).toBe(42);
    expect(state.viewport.y).toBe(-17);
    expect(state.viewport.zoom).toBe(1.25);
    expect(state.objects.map((o) => ({ id: o.id, x: o.x, y: o.y }))).toEqual([
      { id: 'doc-1', x: 100, y: 200 },
      { id: 'doc-2', x: 300, y: 400 },
    ]);

    // The canvas renders the cards at the exact same coordinates.
    const card = document.querySelector('[data-object-id="doc-1"]') as HTMLElement;
    expect(card.style.left).toBe('100px');
    expect(card.style.top).toBe('200px');
    expect(card.getAttribute('aria-selected')).toBe('true');
  });
});
