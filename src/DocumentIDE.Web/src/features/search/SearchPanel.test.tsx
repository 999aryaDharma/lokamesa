import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import SearchPanel from './SearchPanel.tsx';
import { useSearchStore, type SearchResult } from './searchStore';
import { useCanvasStore } from '../canvas/canvasStore';

describe('SearchPanel & Realtime Search UI', () => {
  const mockResults: SearchResult[] = [
    {
      documentId: 'doc-1',
      filename: 'BA-NUSA-DUA-1.docx',
      score: 10500,
      matchSources: ['filename', 'body'],
      snippets: ['...perangkat pada site NUSA DUA telah diperiksa...'],
      metadataSummary: 'DOCX | /workspace/BA-NUSA-DUA-1.docx',
      path: '/workspace/BA-NUSA-DUA-1.docx',
    },
    {
      documentId: 'doc-2',
      filename: 'NusaDua-Notes.md',
      score: 2500,
      matchSources: ['heading', 'semantic'],
      snippets: ['# Nusa Dua Infrastructure Specs'],
      metadataSummary: 'MD | /workspace/NusaDua-Notes.md',
      path: '/workspace/NusaDua-Notes.md',
    },
  ];

  beforeEach(() => {
    useSearchStore.getState().reset();
    useCanvasStore.getState().reset();
    vi.restoreAllMocks();
  });

  it('Search UI updates without page navigation', async () => {
    const mockSearchApi = vi.fn().mockResolvedValue({
      results: mockResults,
      totalCount: 2,
      executionTimeMs: 12.5,
      activeFilterChips: [{ field: 'type', value: 'docx', count: 1 }],
    });

    useSearchStore.getState().setSearchApi(mockSearchApi);

    render(<SearchPanel />);

    const searchInput = screen.getByPlaceholderText(/search workspace/i);
    expect(searchInput).toBeDefined();

    // Type into query input without page reload/navigation
    fireEvent.change(searchInput, { target: { value: 'nusa dua' } });

    await waitFor(() => {
      expect(mockSearchApi).toHaveBeenCalledWith('nusa dua');
    });

    // Check that UI rendered search results without page navigation
    expect(await screen.findByText('BA-NUSA-DUA-1.docx')).toBeDefined();
    expect(screen.getByText('NusaDua-Notes.md')).toBeDefined();
    expect(screen.getByText('...perangkat pada site NUSA DUA telah diperiksa...')).toBeDefined();
  });

  it('Exit search restores exact canvas state', async () => {
    // Setup canvas objects
    useCanvasStore.getState().setObjects([
      { id: 'doc-1', type: 'document', title: 'BA-NUSA-DUA-1.docx', x: 100, y: 200, width: 120, height: 160 },
      { id: 'doc-3', type: 'document', title: 'Unrelated.docx', x: 300, y: 400, width: 120, height: 160 },
    ]);

    const mockSearchApi = vi.fn().mockResolvedValue({
      results: [mockResults[0]],
      totalCount: 1,
      executionTimeMs: 5,
    });
    useSearchStore.getState().setSearchApi(mockSearchApi);

    render(<SearchPanel />);

    // Perform search
    const searchInput = screen.getByPlaceholderText(/search workspace/i);
    fireEvent.change(searchInput, { target: { value: 'nusa' } });

    await waitFor(() => {
      expect(useSearchStore.getState().isSearchActive).toBe(true);
    });

    // Confirm coordinates in canvas store are unchanged
    const canvasObjects = useCanvasStore.getState().objects;
    expect(canvasObjects.find((o) => o.id === 'doc-1')?.x).toBe(100);
    expect(canvasObjects.find((o) => o.id === 'doc-3')?.x).toBe(300);

    // Clear / Exit search
    const clearButton = screen.getByLabelText(/clear search/i);
    fireEvent.click(clearButton);

    expect(useSearchStore.getState().isSearchActive).toBe(false);
    expect(useSearchStore.getState().query).toBe('');

    // Coordinates remain exact
    const restoredObjects = useCanvasStore.getState().objects;
    expect(restoredObjects.find((o) => o.id === 'doc-1')?.x).toBe(100);
    expect(restoredObjects.find((o) => o.id === 'doc-3')?.x).toBe(300);
  });

  it('Selected result IDs match backend result set', async () => {
    const mockSearchApi = vi.fn().mockResolvedValue({
      results: mockResults,
      totalCount: 2,
      executionTimeMs: 10,
    });
    useSearchStore.getState().setSearchApi(mockSearchApi);

    useCanvasStore.getState().setObjects([
      { id: 'doc-1', type: 'document', title: 'BA-NUSA-DUA-1.docx', x: 100, y: 200, width: 120, height: 160 },
      { id: 'doc-2', type: 'document', title: 'NusaDua-Notes.md', x: 250, y: 200, width: 120, height: 160 },
    ]);

    render(<SearchPanel />);

    const searchInput = screen.getByPlaceholderText(/search workspace/i);
    fireEvent.change(searchInput, { target: { value: 'nusa' } });

    await screen.findByText('BA-NUSA-DUA-1.docx');

    // Click "Select All Results" button
    const selectAllBtn = screen.getByRole('button', { name: /select all results/i });
    fireEvent.click(selectAllBtn);

    const canvasSelectedIds = Array.from(useCanvasStore.getState().selectedIds);
    const storeSelectedIds = Array.from(useSearchStore.getState().selectedResultIds);

    expect(storeSelectedIds).toEqual(['doc-1', 'doc-2']);
    expect(canvasSelectedIds).toEqual(['doc-1', 'doc-2']);
  });

  it('Renders match sources badges and snippets', async () => {
    const mockSearchApi = vi.fn().mockResolvedValue({
      results: mockResults,
      totalCount: 2,
      executionTimeMs: 8,
    });
    useSearchStore.getState().setSearchApi(mockSearchApi);

    render(<SearchPanel />);

    fireEvent.change(screen.getByPlaceholderText(/search workspace/i), { target: { value: 'nusa' } });

    await screen.findByText('BA-NUSA-DUA-1.docx');

    // Check match source badges
    expect(screen.getByTestId('match-source-filename-doc-1')).toBeDefined();
    expect(screen.getByTestId('match-source-body-doc-1')).toBeDefined();
    expect(screen.getByTestId('match-source-semantic-doc-2')).toBeDefined();
  });

  it('Handles malformed query and stale index errors gracefully', async () => {
    const mockSearchApi = vi.fn().mockRejectedValue(new Error('Malformed query syntax'));
    useSearchStore.getState().setSearchApi(mockSearchApi);

    render(<SearchPanel />);

    fireEvent.change(screen.getByPlaceholderText(/search workspace/i), { target: { value: 'type:' } });

    await waitFor(() => {
      expect(screen.getAllByText(/malformed query syntax/i).length).toBeGreaterThan(0);
    });
  });

  it('exposes results as a multi-select listbox of options, never a bare aria-selected div', async () => {
    const mockSearchApi = vi.fn().mockResolvedValue({
      results: mockResults,
      totalCount: 2,
      executionTimeMs: 10,
    });
    useSearchStore.getState().setSearchApi(mockSearchApi);

    render(<SearchPanel />);

    fireEvent.change(screen.getByPlaceholderText(/search workspace/i), {
      target: { value: 'nusa' },
    });

    const listbox = await screen.findByRole('listbox', { name: 'Search results' });
    expect(listbox).toHaveAttribute('aria-multiselectable', 'true');

    const options = within(listbox).getAllByRole('option');
    expect(options).toHaveLength(2);
    expect(options[0]).toHaveAttribute('aria-selected', 'false');

    fireEvent.click(options[0]);
    expect(useSearchStore.getState().selectedResultIds.has('doc-1')).toBe(true);
    expect(
      screen.getByRole('option', { name: /BA-NUSA-DUA-1\.docx/ }),
    ).toHaveAttribute('aria-selected', 'true');

    // Keyboard parity: Space on the focused option toggles the same selection.
    fireEvent.keyDown(screen.getByRole('option', { name: /BA-NUSA-DUA-1\.docx/ }), {
      key: ' ',
    });
    expect(useSearchStore.getState().selectedResultIds.has('doc-1')).toBe(false);
  });

  it('Preserves search scope when invoking batch operation', async () => {
    const mockSearchApi = vi.fn().mockResolvedValue({
      results: mockResults,
      totalCount: 2,
      executionTimeMs: 10,
    });
    useSearchStore.getState().setSearchApi(mockSearchApi);
    const batchHandler = vi.fn();

    render(<SearchPanel onInvokeBatchOperation={batchHandler} />);

    fireEvent.change(screen.getByPlaceholderText(/search workspace/i), { target: { value: 'nusa' } });

    await screen.findByText('BA-NUSA-DUA-1.docx');

    const batchBtn = screen.getByRole('button', { name: /batch operation/i });
    fireEvent.click(batchBtn);

    expect(batchHandler).toHaveBeenCalledWith({
      type: 'search',
      query: 'nusa',
      documentIds: ['doc-1', 'doc-2'],
      resultsCount: 2,
    });
  });
});
