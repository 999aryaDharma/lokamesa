// src/DocumentIDE.Web/src/features/graph/graphStore.ts

/**
 * Graph state store – persists node coordinates independently from canvas.
 */
type Listener = () => void;


const POSITION_STORAGE_KEY = "graphNodePositions";

type NodeId = string;

type Position = {
  x: number;
  y: number;
};

export interface GraphActions {
  /** Set position for a node */
  setPosition: (id: NodeId, pos: Position) => void;
  /** Reset all positions (used in tests) */
  reset: () => void;
  /** Load persisted positions from localStorage (call on startup) */
  loadPersistedPositions: () => void;
}

export type GraphStoreState = {
  positions: Map<NodeId, Position>;
} & GraphActions;

class GraphStore {
  private positions = new Map<NodeId, Position>();
  private listeners = new Set<Listener>();
  private cachedSnapshot: GraphStoreState | null = null;

  private actions: GraphActions = {
    setPosition: (id: NodeId, pos: Position) => {
      this.positions.set(id, pos);
      // persist to localStorage
      this.persist();
      
      this.notify();
    },
    reset: () => {
      this.positions.clear();
      // also clear persisted storage for test isolation
      try {
        localStorage.removeItem(POSITION_STORAGE_KEY);
      } catch {}
      this.notify();
    },
    loadPersistedPositions: () => {
      try {
        const raw = localStorage.getItem(POSITION_STORAGE_KEY);
        if (raw) {
          const parsed: Record<string, Position> = JSON.parse(raw);
          this.positions = new Map(Object.entries(parsed));
          this.notify();
        }
      } catch {}
    },
  };

  private persist(): void {
    try {
      const obj: Record<string, Position> = {};
      this.positions.forEach((v, k) => {
        obj[k] = v;
      });
      localStorage.setItem(POSITION_STORAGE_KEY, JSON.stringify(obj));
    } catch {}
  }

  private buildSnapshot(): GraphStoreState {
    return {
      positions: new Map(this.positions),
      ...this.actions,
    };
  }

  getState = (): GraphStoreState => {
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

export const graphStoreInstance = new GraphStore();
export const useGraphStore = {
  getState: () => graphStoreInstance.getState(),
  subscribe: (listener: Listener) => graphStoreInstance.subscribe(listener),
};
