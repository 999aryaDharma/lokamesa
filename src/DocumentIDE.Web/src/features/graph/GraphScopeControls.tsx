import React from 'react';

export interface SemanticEdge {
  source: string;
  target: string;
  type?: string;
  confidence?: number;
  [key: string]: any;
}

export interface GraphScopeOptions {
  isFullGraphExplicit: boolean;
  topKSemanticEdges: number;
}

export const defaultGraphScopeOptions: GraphScopeOptions = {
  isFullGraphExplicit: false,
  topKSemanticEdges: 5,
};

/**
 * Filters semantic / inferred edges to top-K highest confidence edges to prevent all-to-all rendering clutter.
 * Generic over any edge carrying an optional `confidence` so the graph view can
 * pass its own edge type without an unsafe cast.
 */
export function filterSemanticEdgesTopK<T extends { confidence?: number }>(
  edges: T[],
  k: number = 5
): T[] {
  if (!edges || edges.length === 0) return [];
  const sorted = [...edges].sort((a, b) => (b.confidence ?? 0) - (a.confidence ?? 0));
  return sorted.slice(0, k);
}

export interface GraphScopeControlsProps {
  totalNodes: number;
  isBounded: boolean;
  isFullGraphExplicit: boolean;
  topK: number;
  onToggleFullGraph: (explicit: boolean) => void;
  onChangeTopK: (k: number) => void;
}

export const GraphScopeControls: React.FC<GraphScopeControlsProps> = ({
  totalNodes,
  isBounded,
  isFullGraphExplicit,
  topK,
  onToggleFullGraph,
  onChangeTopK,
}) => {
  return (
    <div
      data-testid="graph-scope-controls"
      className="graph-scope-controls"
      style={{ padding: '8px', background: '#f5f5f5', borderRadius: '4px' }}
    >
      {isBounded && (
        <span style={{ marginRight: '12px', color: '#666' }}>
          Graph bounded ({totalNodes} total nodes).
        </span>
      )}
      <button
        onClick={() => onToggleFullGraph(!isFullGraphExplicit)}
        style={{ marginRight: '12px', padding: '4px 8px' }}
      >
        {isFullGraphExplicit ? 'Switch to Bounded View' : 'Show Full Graph'}
      </button>

      <label style={{ fontSize: '12px' }}>
        Top-K Semantic Edges:{' '}
        <input
          type="number"
          min={1}
          max={20}
          value={topK}
          onChange={(e) => onChangeTopK(Number(e.target.value) || 5)}
          style={{ width: '50px', marginLeft: '4px' }}
        />
      </label>
    </div>
  );
};
