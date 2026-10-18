import React from 'react';
import DepthStepper from '../shell/DepthStepper';
import PushButton from '../shell/PushButton';

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

/**
 * GR-05 scope bar.
 *
 * UI-01 restyle: the raw inline-styled bar became the macOS scope bar (hairline
 * rule, `$mac-canvas` fill, a secondary push button and a labelled stepper).
 * The controls themselves are unchanged — bounded/full toggle and top-K — and
 * so is the `graph-scope-controls` testid and the "Graph bounded" message.
 * The scope *segments* shown in screen 03 (Workspace / Folder / Selection) are
 * deliberately not rendered here: the graph store has no per-scope source, and
 * inventing segments that do nothing would be dishonest.
 */
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
      className="flex flex-wrap items-center gap-3 border-b border-mac-hairline bg-mac-canvas px-3 py-2 text-xs text-mac-text-2"
    >
      {isBounded && <span>Graph bounded ({totalNodes} total nodes).</span>}

      <PushButton
        size="sm"
        variant="secondary"
        onClick={() => onToggleFullGraph(!isFullGraphExplicit)}
      >
        {isFullGraphExplicit ? 'Switch to Bounded View' : 'Show Full Graph'}
      </PushButton>

      <DepthStepper
        label="Top-K edges"
        value={topK}
        min={1}
        max={20}
        onChange={onChangeTopK}
      />
    </div>
  );
};
