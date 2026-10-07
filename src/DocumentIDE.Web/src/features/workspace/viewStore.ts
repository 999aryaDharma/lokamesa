/**
 * CAN-08: List View & View Switch — active main-view store
 *
 * Lightweight external store compatible with React's useSyncExternalStore.
 * Owns only the active main view (Canvas | List | Graph). Document data and
 * selection stay in the existing canvas store — this is NOT a parallel
 * document store.
 *
 * KEY REQUIREMENT (same as canvasStore): getSnapshot() MUST return the same
 * reference when state has not changed, or useSyncExternalStore loops.
 */
type Listener = () => void;

/** The three required main views. */
export type MainView = 'canvas' | 'list' | 'graph';

/** Runtime list of supported main views (used for stored-value validation). */
export const MAIN_VIEWS: readonly MainView[] = ['canvas', 'list', 'graph'];

/** Default main view on first launch. */
export const DEFAULT_MAIN_VIEW: MainView = 'canvas';

/** localStorage key for the persisted active view. */
export const ACTIVE_VIEW_STORAGE_KEY = 'documentide_active_view';

export interface ViewState {
  /** Currently active main view. */
  activeView: MainView;
}

export interface ViewActions {
  /** Switch the active main view. Automatically persists to localStorage. */
  setActiveView: (view: MainView) => void;
  /** Load the persisted active view from localStorage (call on startup). */
  loadPersistedView: () => void;
  /** Reset to defaults (used in tests). */
  reset: () => void;
}

export type ViewStoreState = ViewState & ViewActions;

function createInitialState(): ViewState {
  return { activeView: DEFAULT_MAIN_VIEW };
}

function isMainView(value: unknown): value is MainView {
  return typeof value === 'string' && (MAIN_VIEWS as readonly string[]).includes(value);
}

function persistActiveView(view: MainView): void {
  try {
    localStorage.setItem(ACTIVE_VIEW_STORAGE_KEY, JSON.stringify(view));
  } catch {
    // localStorage may be unavailable (e.g. private browsing, storage full)
  }
}

function readPersistedActiveView(): MainView | null {
  try {
    const raw = localStorage.getItem(ACTIVE_VIEW_STORAGE_KEY);
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    return isMainView(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

class ViewStore {
  private state: ViewState = createInitialState();
  private listeners = new Set<Listener>();
  private cachedSnapshot: ViewStoreState | null = null;

  private readonly actions: ViewActions = {
    setActiveView: (view: MainView): void => {
      this.state = { activeView: view };
      persistActiveView(view);
      this.notify();
    },

    loadPersistedView: (): void => {
      const persisted = readPersistedActiveView();
      if (persisted && persisted !== this.state.activeView) {
        this.state = { activeView: persisted };
        this.notify();
      }
    },

    reset: (): void => {
      this.state = createInitialState();
      this.notify();
    },
  };

  private buildSnapshot(): ViewStoreState {
    return {
      ...this.state,
      ...this.actions,
    };
  }

  getState = (): ViewStoreState => {
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
    this.cachedSnapshot = this.buildSnapshot();
    this.listeners.forEach((l) => l());
  }
}

export const viewStoreInstance = new ViewStore();

export const useViewStore = {
  getState: (): ViewStoreState => viewStoreInstance.getState(),
  subscribe: (listener: Listener): (() => void) => viewStoreInstance.subscribe(listener),
};
