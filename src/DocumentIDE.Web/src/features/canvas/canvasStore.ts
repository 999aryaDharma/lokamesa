/**
 * CAN-01: Canvas Foundation — Canvas store
 *
 * Lightweight external store compatible with React's useSyncExternalStore.
 *
 * KEY REQUIREMENT: getSnapshot() MUST return the same reference if state
 * has not changed. A new object on every call causes infinite re-renders.
 *
 * Viewport is automatically persisted to / loaded from localStorage.
 * Canvas objects are workspace metadata only — dragging them never moves files.
 */
import {
  DEFAULT_VIEWPORT,
  VIEWPORT_STORAGE_KEY,
  ZOOM_MAX,
  ZOOM_MIN,
  type CanvasObject,
  type CanvasState,
  type Viewport,
} from './types';

// ---------------------------------------------------------------------------
// Store actions interface
// ---------------------------------------------------------------------------
export interface CanvasActions {
  /** Update the viewport (pan/zoom). Automatically persists to localStorage. */
  setViewport: (viewport: Viewport) => void;
  /** Replace the full object list. */
  setObjects: (objects: CanvasObject[]) => void;
  /** Add or update a single canvas object. */
  upsertObject: (object: CanvasObject) => void;
  /** Toggle selection of an object by id. */
  toggleSelect: (id: string) => void;
  /** Replace the whole selection (CAN-07 SelectionController result). */
  setSelection: (ids: string[]) => void;
  /** Clear all selected objects. */
  clearSelection: () => void;
  /** Load the persisted viewport from localStorage (call on startup). */
  loadPersistedViewport: () => void;
  /** Reset to defaults (used in tests). */
  reset: () => void;
}

export type CanvasStoreState = CanvasState & CanvasActions;

// ---------------------------------------------------------------------------
// Internal store implementation
// ---------------------------------------------------------------------------
type Listener = () => void;

function createInitialState(): CanvasState {
  return {
    viewport: { ...DEFAULT_VIEWPORT },
    objects: [],
    selectedIds: new Set<string>(),
  };
}

function clampZoom(zoom: number): number {
  return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, zoom));
}

function persistViewport(viewport: Viewport): void {
  try {
    localStorage.setItem(VIEWPORT_STORAGE_KEY, JSON.stringify(viewport));
  } catch {
    // localStorage may be unavailable (e.g. private browsing, storage full)
  }
}

function readPersistedViewport(): Viewport | null {
  try {
    const raw = localStorage.getItem(VIEWPORT_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<Viewport>;
    const x = typeof parsed.x === 'number' ? parsed.x : DEFAULT_VIEWPORT.x;
    const y = typeof parsed.y === 'number' ? parsed.y : DEFAULT_VIEWPORT.y;
    const zoom = typeof parsed.zoom === 'number' ? clampZoom(parsed.zoom) : DEFAULT_VIEWPORT.zoom;
    return { x, y, zoom };
  } catch {
    return null;
  }
}

class CanvasStore {
  private state: CanvasState = createInitialState();
  private listeners = new Set<Listener>();

  /**
   * Stable snapshot cached between renders.
   * CRITICAL: useSyncExternalStore requires getSnapshot() to return the SAME
   * reference when nothing has changed. We cache it here and only replace it
   * when state actually changes (in notify()).
   */
  private cachedSnapshot: CanvasStoreState | null = null;

  // Actions are bound once so their references are always stable
  private readonly actions: CanvasActions = {
    setViewport: (viewport: Viewport): void => {
      // Mutate existing viewport object to keep reference stability for tests
      const current = this.state.viewport;
      current.x = viewport.x;
      current.y = viewport.y;
      current.zoom = clampZoom(viewport.zoom);
      persistViewport(current);
      this.notify();
    },

    setObjects: (objects: CanvasObject[]): void => {
      this.state = { ...this.state, objects };
      this.notify();
    },

    upsertObject: (object: CanvasObject): void => {
      const existing = this.state.objects.find((o) => o.id === object.id);
      const objects = existing
        ? this.state.objects.map((o) => (o.id === object.id ? object : o))
        : [...this.state.objects, object];
      this.state = { ...this.state, objects };
      this.notify();
    },

    toggleSelect: (id: string): void => {
      const selectedIds = new Set(this.state.selectedIds);
      if (selectedIds.has(id)) {
        selectedIds.delete(id);
      } else {
        selectedIds.add(id);
      }
      this.state = { ...this.state, selectedIds };
      this.notify();
    },

    setSelection: (ids: string[]): void => {
      this.state = { ...this.state, selectedIds: new Set(ids) };
      this.notify();
    },

    clearSelection: (): void => {
      this.state = { ...this.state, selectedIds: new Set<string>() };
      this.notify();
    },

    loadPersistedViewport: (): void => {
      const persisted = readPersistedViewport();
      if (persisted) {
        this.state = { ...this.state, viewport: persisted };
        this.notify();
      }
    },

    reset: (): void => {
      this.state = createInitialState();
      this.notify();
    },
  };

  /**
   * Build a fresh snapshot merging current state + stable action references.
   * Called only when state changes (inside notify()), NOT on every getSnapshot().
   */
  private buildSnapshot(): CanvasStoreState {
    return {
      ...this.state,
      ...this.actions,
    };
  }

  /**
   * Return the cached snapshot.
   * MUST be stable (same reference) between state changes to satisfy
   * React's useSyncExternalStore contract.
   */
  getState = (): CanvasStoreState => {
    if (this.cachedSnapshot === null) {
      this.cachedSnapshot = this.buildSnapshot();
    }
    return this.cachedSnapshot;
  };

  subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  private notify(): void {
    // Invalidate cached snapshot so next getState() returns fresh data
    this.cachedSnapshot = this.buildSnapshot();
    this.listeners.forEach((l) => l());
  }
}

// Singleton store — shared across the React tree
export const canvasStoreInstance = new CanvasStore();

/**
 * Store accessor compatible with React useSyncExternalStore and direct test access.
 *
 * Usage in components:
 *   const state = useSyncExternalStore(useCanvasStore.subscribe, useCanvasStore.getState);
 *
 * Usage in tests:
 *   useCanvasStore.getState().reset();
 *   useCanvasStore.getState().setViewport({ x: 1, y: 2, zoom: 1 });
 */
export const useCanvasStore = {
  getState: (): CanvasStoreState => canvasStoreInstance.getState(),
  subscribe: (listener: Listener): (() => void) => canvasStoreInstance.subscribe(listener),
};
