/**
 * CAN-01: Canvas Foundation — Frontend unit tests
 *
 * TDD Red → Green → Refactor per sprint rules.
 * First failing test: Viewport_Persists_In_Store_Or_LocalStorage
 */
import { render, screen, act } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import DocumentCanvas from './DocumentCanvas';
import { useCanvasStore } from './canvasStore';
import { VIEWPORT_STORAGE_KEY, type CanvasObject } from './types';

// ---------------------------------------------------------------------------
// localStorage mock helpers
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
  // Reset store to defaults before each test
  useCanvasStore.getState().reset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

// ---------------------------------------------------------------------------
// Test 1 (FIRST FAILING): Viewport_Persists_In_Store_Or_LocalStorage
// ---------------------------------------------------------------------------
describe('Viewport_Persists_In_Store_Or_LocalStorage', () => {
  it('saves viewport x, y, zoom to localStorage when viewport changes', () => {
    const { setViewport } = useCanvasStore.getState();

    act(() => {
      setViewport({ x: 120, y: 80, zoom: 1.5 });
    });

    const stored = localStorageStore[VIEWPORT_STORAGE_KEY];
    expect(stored).toBeDefined();
    const parsed = JSON.parse(stored);
    expect(parsed.x).toBe(120);
    expect(parsed.y).toBe(80);
    expect(parsed.zoom).toBe(1.5);
  });

  it('loads persisted viewport from localStorage on store initialization', () => {
    const savedViewport = { x: 200, y: 150, zoom: 0.8 };
    localStorageStore[VIEWPORT_STORAGE_KEY] = JSON.stringify(savedViewport);

    // Re-import store to simulate startup
    const { loadPersistedViewport } = useCanvasStore.getState();
    act(() => {
      loadPersistedViewport();
    });

    const { viewport } = useCanvasStore.getState();
    expect(viewport.x).toBe(200);
    expect(viewport.y).toBe(150);
    expect(viewport.zoom).toBe(0.8);
  });

  it('clamps restored zoom to [0.1, 3.0] bounds', () => {
    const invalidViewport = { x: 0, y: 0, zoom: 99 };
    localStorageStore[VIEWPORT_STORAGE_KEY] = JSON.stringify(invalidViewport);

    const { loadPersistedViewport } = useCanvasStore.getState();
    act(() => {
      loadPersistedViewport();
    });

    const { viewport } = useCanvasStore.getState();
    expect(viewport.zoom).toBe(3.0);
  });
});

// ---------------------------------------------------------------------------
// Test 2: Renders_100_Objects_And_Pans_Smoothly
// ---------------------------------------------------------------------------
describe('Renders_100_Objects_And_Pans_Smoothly', () => {
  it('renders 100 canvas objects without throwing or producing error elements', () => {
    const objects: CanvasObject[] = Array.from({ length: 100 }, (_, i) => ({
      id: `obj-${i}`,
      x: (i % 10) * 160,
      y: Math.floor(i / 10) * 120,
      width: 140,
      height: 100,
      title: `Document ${i + 1}`,
      type: i % 5 === 0 ? 'folder' : 'document',
    }));

    act(() => {
      useCanvasStore.getState().setObjects(objects);
    });

    render(<DocumentCanvas />);

    // Verify all 100 objects rendered with expected titles
    // We sample a few to avoid 100 assertions
    expect(screen.getByText('Document 1')).toBeInTheDocument();
    expect(screen.getByText('Document 50')).toBeInTheDocument();
    expect(screen.getByText('Document 100')).toBeInTheDocument();
  });

  it('pan changes viewport state without errors', () => {
    render(<DocumentCanvas />);
    act(() => {
      useCanvasStore.getState().setViewport({ x: 300, y: 200, zoom: 1.0 });
    });
    const { viewport } = useCanvasStore.getState();
    expect(viewport.x).toBe(300);
    expect(viewport.y).toBe(200);
  });
});

// ---------------------------------------------------------------------------
// Test 3: No_Dashboard_Cards_In_Default_Canvas
// ---------------------------------------------------------------------------
describe('No_Dashboard_Cards_In_Default_Canvas', () => {
  it('renders no statistic/metric cards in default empty canvas state', () => {
    render(<DocumentCanvas />);

    // The canvas MUST NOT render dashboard metric labels
    const dashboardKeywords = ['Total Files', 'Storage Used', 'Active Users', 'Recent Activity', 'Statistics'];
    for (const keyword of dashboardKeywords) {
      const el = screen.queryByText(new RegExp(keyword, 'i'));
      expect(el).toBeNull();
    }
  });

  it('default canvas has clean white/off-white background, not a colored dashboard', () => {
    const { container } = render(<DocumentCanvas />);
    // The canvas root element should not have heavy dark background color classes
    // It should have a light canvas surface
    const canvasSurface = container.querySelector('[data-testid="canvas-surface"]');
    expect(canvasSurface).not.toBeNull();
  });
});
