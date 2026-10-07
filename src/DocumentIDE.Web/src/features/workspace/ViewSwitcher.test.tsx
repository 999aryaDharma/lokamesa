/**
 * CAN-08: List View & View Switch — ViewSwitcher + viewStore tests
 *
 * Covers the persisted active-view seam: switching persists to localStorage,
 * loadPersistedView restores it, and invalid stored values fall back to the
 * default view. The seam is tested directly (no browser involved).
 */
import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ViewSwitcher from './ViewSwitcher';
import { ACTIVE_VIEW_STORAGE_KEY, useViewStore } from './viewStore';

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

beforeEach(() => {
  localStorageStore = {};
  vi.stubGlobal('localStorage', localStorageMock);
  useViewStore.getState().reset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('ViewSwitcher component', () => {
  it('renders Canvas, List, and Graph options with the active one checked', () => {
    render(<ViewSwitcher activeView="list" onViewChange={() => {}} />);

    expect(screen.getByTestId('view-canvas-btn').getAttribute('aria-checked')).toBe('false');
    expect(screen.getByTestId('view-list-btn').getAttribute('aria-checked')).toBe('true');
    expect(screen.getByTestId('view-graph-btn').getAttribute('aria-checked')).toBe('false');
  });

  it('triggers onViewChange when an option is clicked', () => {
    const handleViewChange = vi.fn();
    render(<ViewSwitcher activeView="canvas" onViewChange={handleViewChange} />);

    fireEvent.click(screen.getByTestId('view-list-btn'));
    expect(handleViewChange).toHaveBeenCalledWith('list');

    fireEvent.click(screen.getByTestId('view-graph-btn'));
    expect(handleViewChange).toHaveBeenCalledWith('graph');
  });
});

describe('Persisted active view seam', () => {
  it('persists the active view to localStorage when it changes', () => {
    act(() => {
      useViewStore.getState().setActiveView('list');
    });

    const raw = localStorageStore[ACTIVE_VIEW_STORAGE_KEY];
    expect(raw).toBeDefined();
    expect(JSON.parse(raw)).toBe('list');
  });

  it('restores the persisted active view via loadPersistedView', () => {
    localStorageStore[ACTIVE_VIEW_STORAGE_KEY] = JSON.stringify('graph');

    act(() => {
      useViewStore.getState().loadPersistedView();
    });

    expect(useViewStore.getState().activeView).toBe('graph');
  });

  it('ignores an invalid persisted value and stays on the default view', () => {
    localStorageStore[ACTIVE_VIEW_STORAGE_KEY] = JSON.stringify('spaceship');

    act(() => {
      useViewStore.getState().loadPersistedView();
    });

    expect(useViewStore.getState().activeView).toBe('canvas');
  });

  it('ignores malformed persisted data', () => {
    localStorageStore[ACTIVE_VIEW_STORAGE_KEY] = '{not-json';

    act(() => {
      useViewStore.getState().loadPersistedView();
    });

    expect(useViewStore.getState().activeView).toBe('canvas');
  });
});
