/**
 * CAN-04: Folder Picker & File Release — Frontend behavior tests
 *
 * TDD Red → Green → Refactor per AGENTS.md.
 * Mandated first failing behavior: "Single click does not dump all files".
 *
 * Canvas interactions are workspace metadata only — releasing documents
 * creates workspace objects and NEVER physically moves files.
 *
 * Click arbitration: the single-click picker open is deferred by a short
 * timer so a real double-click never has its second click swallowed by the
 * popover that its own first click opened. Tests therefore use fake timers.
 */
import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import DocumentCanvas from './DocumentCanvas';
import { useCanvasStore } from './canvasStore';
import { type CanvasObject } from './types';

// Advance past any reasonable single-click/double-click arbitration window.
const CLICK_ARBITRATION_SETTLE_MS = 500;

// ---------------------------------------------------------------------------
// localStorage mock (same pattern as DocumentCanvas.test.tsx)
// ---------------------------------------------------------------------------
let localStorageStore: Record<string, string> = {};
const localStorageMock: Storage = {
  getItem: (key) => localStorageStore[key] ?? null,
  setItem: (key, value) => {
    localStorageStore[key] = String(value);
  },
  removeItem: (key) => {
    delete localStorageStore[key];
  },
  clear: () => {
    localStorageStore = {};
  },
  length: 0,
  key: () => null,
};

beforeEach(() => {
  localStorageStore = {};
  vi.stubGlobal('localStorage', localStorageMock);
  vi.useFakeTimers();
  useCanvasStore.getState().reset();
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------
const FOLDER_ID = 'folder-1';

const folderObject: CanvasObject = {
  id: FOLDER_ID,
  x: 120,
  y: 90,
  width: 180,
  height: 120,
  title: 'BA Agustus',
  type: 'folder',
};

const folderFiles = {
  [FOLDER_ID]: [
    { documentId: 'doc-a', filename: 'BA Site A.docx' },
    { documentId: 'doc-b', filename: 'BA Site B.docx' },
    { documentId: 'doc-c', filename: 'catatan.md' },
    { documentId: 'doc-x', filename: 'notes.txt' },
  ],
};

function renderCanvas() {
  const onRelease = vi.fn();
  act(() => {
    useCanvasStore.getState().setObjects([folderObject]);
  });
  render(<DocumentCanvas folderFiles={folderFiles} onReleaseDocuments={onRelease} />);
  return { onRelease };
}

const folderCard = () => screen.getByLabelText('Folder: BA Agustus');

/** Single click + settle the click/double-click arbitration window. */
function openFolderPicker() {
  fireEvent.click(folderCard());
  act(() => {
    vi.advanceTimersByTime(CLICK_ARBITRATION_SETTLE_MS);
  });
}

// ---------------------------------------------------------------------------
// Required behaviors
// ---------------------------------------------------------------------------
describe('FolderPicker — CAN-04 required behaviors', () => {
  it('Single click does not dump all files', () => {
    const { onRelease } = renderCanvas();

    openFolderPicker();

    // A lightweight picker opens…
    expect(screen.getByTestId('folder-picker')).toBeInTheDocument();
    expect(screen.getByText('BA Site A.docx')).toBeInTheDocument();
    expect(screen.getByText('BA Site B.docx')).toBeInTheDocument();
    expect(screen.getByText('catatan.md')).toBeInTheDocument();

    // …and the single click releases NOTHING.
    expect(onRelease).not.toHaveBeenCalled();
  });

  it('single click is deferred, then opens the picker without releasing files', () => {
    const { onRelease } = renderCanvas();

    fireEvent.click(folderCard());

    // Inside the arbitration window the picker is not open yet — otherwise a
    // real double-click's second click would land on the popover.
    expect(screen.queryByTestId('folder-picker')).not.toBeInTheDocument();
    expect(onRelease).not.toHaveBeenCalled();

    // Once the click is confirmed as a single click it selects the folder and
    // opens the picker.
    act(() => {
      vi.advanceTimersByTime(CLICK_ARBITRATION_SETTLE_MS);
    });
    expect(screen.getByTestId('folder-picker')).toBeInTheDocument();
    expect(useCanvasStore.getState().selectedIds.has(FOLDER_ID)).toBe(true);
    expect(onRelease).not.toHaveBeenCalled();
  });

  it('Double click opens all supported files', () => {
    const { onRelease } = renderCanvas();

    fireEvent.doubleClick(folderCard());

    // Fast-path shortcut: every supported file is released…
    expect(onRelease).toHaveBeenCalledTimes(1);
    // …and unsupported files (notes.txt) are never released.
    expect(onRelease).toHaveBeenCalledWith(FOLDER_ID, ['doc-a', 'doc-b', 'doc-c']);
  });

  it('realistic double click (click, click, dblclick) never opens the picker', () => {
    const { onRelease } = renderCanvas();

    // Real browser sequence for a double click.
    fireEvent.click(folderCard());
    fireEvent.click(folderCard());

    // The popover must not open during the double-click window; otherwise the
    // second click would hit the popover and dblclick would never reach the card.
    expect(screen.queryByTestId('folder-picker')).not.toBeInTheDocument();

    fireEvent.dblClick(folderCard());

    expect(onRelease).toHaveBeenCalledTimes(1);
    expect(onRelease).toHaveBeenCalledWith(FOLDER_ID, ['doc-a', 'doc-b', 'doc-c']);

    // The deferred single-click action stays cancelled: no picker, selection
    // unchanged (no double toggle).
    act(() => {
      vi.advanceTimersByTime(CLICK_ARBITRATION_SETTLE_MS * 2);
    });
    expect(screen.queryByTestId('folder-picker')).not.toBeInTheDocument();
    expect(useCanvasStore.getState().selectedIds.has(FOLDER_ID)).toBe(false);
  });

  it('Open Selected releases only selected DocumentIds', () => {
    const { onRelease } = renderCanvas();

    openFolderPicker();

    // Multi-select two of the supported files.
    fireEvent.click(screen.getByLabelText('Select BA Site A.docx'));
    fireEvent.click(screen.getByLabelText('Select catatan.md'));

    fireEvent.click(screen.getByRole('button', { name: 'Open selected' }));

    // Only the checked DocumentIds are released (listing order preserved).
    expect(onRelease).toHaveBeenCalledTimes(1);
    expect(onRelease).toHaveBeenCalledWith(FOLDER_ID, ['doc-a', 'doc-c']);

    // Picker closes after release.
    expect(screen.queryByTestId('folder-picker')).not.toBeInTheDocument();
  });

  it('search within folder picker filters the file list', () => {
    renderCanvas();

    openFolderPicker();
    fireEvent.change(screen.getByLabelText('Search files in folder'), {
      target: { value: 'catatan' },
    });

    expect(screen.getByText('catatan.md')).toBeInTheDocument();
    expect(screen.queryByText('BA Site A.docx')).not.toBeInTheDocument();
  });

  it('lists only supported .docx/.md files', () => {
    renderCanvas();

    openFolderPicker();

    expect(screen.getByText('BA Site A.docx')).toBeInTheDocument();
    expect(screen.getByText('catatan.md')).toBeInTheDocument();
    expect(screen.queryByText('notes.txt')).not.toBeInTheDocument();
  });

  it('Open all button releases every supported file', () => {
    const { onRelease } = renderCanvas();

    openFolderPicker();
    fireEvent.click(screen.getByRole('button', { name: 'Open all' }));

    expect(onRelease).toHaveBeenCalledTimes(1);
    expect(onRelease).toHaveBeenCalledWith(FOLDER_ID, ['doc-a', 'doc-b', 'doc-c']);
  });

  it('Open selected stays disabled until at least one file is checked', () => {
    const { onRelease } = renderCanvas();

    openFolderPicker();

    const openSelected = screen.getByRole('button', { name: 'Open selected' });
    expect(openSelected).toBeDisabled();

    fireEvent.click(screen.getByLabelText('Select BA Site A.docx'));
    expect(openSelected).toBeEnabled();

    // Unchecking removes it again.
    fireEvent.click(screen.getByLabelText('Select BA Site A.docx'));
    expect(openSelected).toBeDisabled();
    expect(onRelease).not.toHaveBeenCalled();
  });

  it('Escape closes the picker without releasing files', () => {
    const { onRelease } = renderCanvas();

    openFolderPicker();
    fireEvent.keyDown(screen.getByTestId('folder-picker'), { key: 'Escape' });

    expect(screen.queryByTestId('folder-picker')).not.toBeInTheDocument();
    expect(onRelease).not.toHaveBeenCalled();
  });

  it('close button dismisses the picker without releasing files', () => {
    const { onRelease } = renderCanvas();

    openFolderPicker();
    fireEvent.click(screen.getByRole('button', { name: 'Close folder picker' }));

    expect(screen.queryByTestId('folder-picker')).not.toBeInTheDocument();
    expect(onRelease).not.toHaveBeenCalled();
  });

  it('closes the picker when the folder object disappears from the canvas', () => {
    renderCanvas();

    openFolderPicker();
    expect(screen.getByTestId('folder-picker')).toBeInTheDocument();

    act(() => {
      useCanvasStore.getState().setObjects([]);
    });

    expect(screen.queryByTestId('folder-picker')).not.toBeInTheDocument();
  });
});
