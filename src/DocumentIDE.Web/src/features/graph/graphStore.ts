// src/DocumentIDE.Web/src/features/graph/graphStore.ts

/**
 * Graph state store – persists node coordinates independently from canvas and
 * holds the loaded relation graph (nodes + truthful edges) for the Graph view.
 *
 * Truthfulness note (master spec §27.2 / spec 08 §14): an edge's `origin`
 * classifies it as `explicit` (solid) or `inferred` (dashed). The store never
 * reclassifies; rendering must not present an inferred relation as factual.
 */
type Listener = () => void;

const POSITION_STORAGE_KEY = "graphNodePositions";

type NodeId = string;

type Position = {
  x: number;
  y: number;
};

/** A document node in the knowledge graph. */
export interface GraphNode {
  id: string;
  title: string;
  docType?: 'docx' | 'md' | 'unknown';
}

/** Edge origin, mapped from the backend RelationOrigin vocabulary. */
export type GraphEdgeOrigin = 'explicit' | 'inferred';

/**
 * A relation edge. `origin` is `explicit` for Markdown links / wikilinks /
 * frontmatter / manual relations and `inferred` for semantic suggestions.
 */
export interface GraphEdge {
  id: string;
  source: string;
  target: string;
  relationType: string;
  origin: GraphEdgeOrigin;
  confidence?: number;
}

export interface GraphActions {
  /** Set position for a node */
  setPosition: (id: NodeId, pos: Position) => void;
  /** Replace the loaded graph (nodes + edges) in one atomic update. */
  setGraph: (nodes: GraphNode[], edges: GraphEdge[]) => void;
  /** Clear the loaded graph (used on workspace switch / tests). */
  clearGraph: () => void;
  /** Reset all positions + graph (used in tests) */
  reset: () => void;
  /** Load persisted positions from localStorage (call on startup) */
  loadPersistedPositions: () => void;
}

export type GraphStoreState = {
  positions: Map<NodeId, Position>;
  nodes: GraphNode[];
  edges: GraphEdge[];
} & GraphActions;

class GraphStore {
  private positions = new Map<NodeId, Position>();
  private nodes: GraphNode[] = [];
  private edges: GraphEdge[] = [];
  private listeners = new Set<Listener>();
  private cachedSnapshot: GraphStoreState | null = null;

  private actions: GraphActions = {
    setPosition: (id: NodeId, pos: Position) => {
      this.positions.set(id, pos);
      // persist to localStorage
      this.persist();
      
      this.notify();
    },
    setGraph: (nodes: GraphNode[], edges: GraphEdge[]) => {
      this.nodes = [...nodes];
      this.edges = [...edges];
      this.notify();
    },
    clearGraph: () => {
      this.nodes = [];
      this.edges = [];
      this.notify();
    },
    reset: () => {
      this.positions.clear();
      this.nodes = [];
      this.edges = [];
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
      nodes: this.nodes,
      edges: this.edges,
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
  getState: (): GraphStoreState => graphStoreInstance.getState(),
  subscribe: (listener: Listener) => graphStoreInstance.subscribe(listener),
  setGraph: (nodes: GraphNode[], edges: GraphEdge[]): void =>
    graphStoreInstance.getState().setGraph(nodes, edges),
};
