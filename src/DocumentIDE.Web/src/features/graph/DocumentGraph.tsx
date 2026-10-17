// src/DocumentIDE.Web/src/features/graph/DocumentGraph.tsx
/**
 * GR-03 — Knowledge graph view (real implementation).
 *
 * Renders workspace relations with the truthful semantics required by the
 * master spec §27.2 and spec 08 §14:
 *   - explicit relations (Markdown links / wikilinks / frontmatter / manual)
 *     render as SOLID edges;
 *   - inferred / semantic suggestions render as DASHED edges.
 * An inferred relation is never presented as factual.
 *
 * Mounted helpers (no longer test-only):
 *   - GR-05 bounded rendering: `computeBoundedGraph` / `computeLayoutAsync`
 *     (graphLayoutWorker) + `GraphScopeControls`, so a large graph defaults to
 *     the selected neighborhood and the full graph is an explicit action;
 *   - AI-04 semantic suggestions: inferred edges arrive from the graph source
 *     and are top-K capped via `filterSemanticEdgesTopK`.
 *
 * Interaction: click selects a node; double click opens a Markdown node in the
 * internal editor and a DOCX node through Open in Word. Graph interaction never
 * touches the canvas store and never moves physical files.
 */
import React, { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { fetchWorkspaceGraph } from './graphApi';
import { computeBoundedGraph, computeLayoutAsync } from './graphLayoutWorker';
import { filterSemanticEdgesTopK, GraphScopeControls } from './GraphScopeControls';
import { useGraphStore, type GraphEdge, type GraphNode } from './graphStore';

const BOUNDED_NODE_THRESHOLD = 250;
const DEFAULT_TOP_K = 5;

const NODE_RADIUS = 18;
const NODE_SPACING = 180;

type Point = { x: number; y: number };

function useGraphState() {
  return useSyncExternalStore(
    useGraphStore.subscribe,
    useGraphStore.getState,
    useGraphStore.getState,
  );
}

/** Deterministic grid layout used for the first paint before async layout resolves. */
export function gridLayout<N extends { id: string }>(nodes: N[]): Map<string, Point> {
  const positions = new Map<string, Point>();
  const cols = Math.max(1, Math.ceil(Math.sqrt(nodes.length)));
  nodes.forEach((node, index) => {
    const row = Math.floor(index / cols);
    const col = index % cols;
    positions.set(node.id, { x: col * NODE_SPACING, y: row * NODE_SPACING });
  });
  return positions;
}

export function isMarkdownNode(node: GraphNode): boolean {
  if (node.docType === 'md') return true;
  if (node.docType === 'docx') return false;
  return /\.(md|markdown)$/i.test(node.title);
}

export interface DocumentGraphProps {
  /** Workspace whose graph is loaded when `nodes` is not supplied directly. */
  workspaceId?: string;
  /** Explicit graph data (overrides the store; used by the app shell/tests). */
  nodes?: GraphNode[];
  edges?: GraphEdge[];
  onSelectNode?: (id: string) => void;
  /** Double click on a Markdown node — open the internal editor. */
  onOpenDocument?: (node: GraphNode) => void;
  /** Double click on a non-Markdown node — open in Word. */
  onOpenInWord?: (documentId: string) => void;
}

export const DocumentGraph: React.FC<DocumentGraphProps> = ({
  workspaceId,
  nodes: nodesProp,
  edges: edgesProp,
  onSelectNode,
  onOpenDocument,
  onOpenInWord,
}) => {
  const store = useGraphState();
  const { setGraph, setPosition } = store;

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isFullGraphExplicit, setIsFullGraphExplicit] = useState(false);
  const [topK, setTopK] = useState(DEFAULT_TOP_K);
  const [layout, setLayout] = useState<Map<string, Point>>(() => new Map());

  const sourceNodes = nodesProp ?? store.nodes;
  const sourceEdges = edgesProp ?? store.edges;

  // Load the workspace graph from the privileged API when the shell does not
  // supply graph data directly (graceful: a failed load keeps the last graph).
  useEffect(() => {
    if (nodesProp !== undefined || !workspaceId) return;
    let cancelled = false;
    fetchWorkspaceGraph(workspaceId)
      .then((graph) => {
        if (!cancelled) setGraph(graph.nodes, graph.edges);
      })
      .catch(() => {
        /* offline / route not hosted yet — keep the current (empty) graph */
      });
    return () => {
      cancelled = true;
    };
  }, [workspaceId, nodesProp, setGraph]);

  // Truthful classification: explicit edges pass through unchanged; inferred
  // edges (AI-04 semantic suggestions) are capped to top-K to avoid an
  // all-to-all inferred graph (spec 08 §27).
  const edges = useMemo(() => {
    const explicit = sourceEdges.filter((edge) => edge.origin === 'explicit');
    const inferred = filterSemanticEdgesTopK(
      sourceEdges.filter((edge) => edge.origin === 'inferred'),
      topK,
    );
    return [...explicit, ...inferred];
  }, [sourceEdges, topK]);

  // GR-05 bounded rendering — default to the selected neighborhood above the
  // node budget unless the user explicitly asks for the full graph.
  const bounded = useMemo(
    () =>
      computeBoundedGraph(
        sourceNodes,
        edges,
        selectedId ?? undefined,
        isFullGraphExplicit,
        BOUNDED_NODE_THRESHOLD,
      ),
    [sourceNodes, edges, selectedId, isFullGraphExplicit],
  );

  // Layout runs off the interactive thread; a synchronous grid is laid down
  // first so nodes are immediately visible and selectable.
  useEffect(() => {
    let cancelled = false;
    setLayout(gridLayout(bounded.visibleNodes));
    void computeLayoutAsync(bounded.visibleNodes, bounded.visibleEdges).then((positions) => {
      if (!cancelled) setLayout(positions);
    });
    return () => {
      cancelled = true;
    };
  }, [bounded]);

  const handleSelect = useCallback(
    (id: string) => {
      setSelectedId(id);
      onSelectNode?.(id);
    },
    [onSelectNode],
  );

  const handleOpen = useCallback(
    (node: GraphNode) => {
      setSelectedId(node.id);
      if (isMarkdownNode(node)) {
        onOpenDocument?.(node);
      } else {
        onOpenInWord?.(node.id);
      }
    },
    [onOpenDocument, onOpenInWord],
  );

  const handleToggleFullGraph = useCallback((explicit: boolean) => {
    setIsFullGraphExplicit(explicit);
  }, []);

  const handleChangeTopK = useCallback(
    (k: number) => {
      const safe = Number.isFinite(k) && k > 0 ? Math.min(20, Math.floor(k)) : DEFAULT_TOP_K;
      setTopK(safe);
    },
    [],
  );

  // Persist the selected node's coordinates so graph positions survive restart
  // (spec 08 §13) without touching canvas state.
  useEffect(() => {
    if (!selectedId) return;
    const point = layout.get(selectedId);
    if (point) setPosition(selectedId, point);
  }, [selectedId, layout, setPosition]);

  const { width, height } = useMemo(() => {
    let maxX = 0;
    let maxY = 0;
    layout.forEach((point) => {
      maxX = Math.max(maxX, point.x);
      maxY = Math.max(maxY, point.y);
    });
    return {
      width: Math.max(640, maxX + NODE_SPACING),
      height: Math.max(420, maxY + NODE_SPACING),
    };
  }, [layout]);

  const center = (point: Point): Point => ({
    x: point.x + NODE_RADIUS,
    y: point.y + NODE_RADIUS,
  });

  return (
    <div
      data-testid="graph-view"
      className="relative w-full h-full overflow-auto bg-white"
      aria-label="Knowledge graph"
    >
      <GraphScopeControls
        totalNodes={sourceNodes.length}
        isBounded={bounded.isBounded}
        isFullGraphExplicit={isFullGraphExplicit}
        topK={topK}
        onToggleFullGraph={handleToggleFullGraph}
        onChangeTopK={handleChangeTopK}
      />

      <div className="px-2 pb-1 text-[11px] text-slate-500" data-testid="graph-legend">
        <span className="mr-3">
          <span aria-hidden="true">━</span> Solid = explicit relation
        </span>
        <span>
          <span aria-hidden="true">┄</span> Dashed = inferred / semantic suggestion
        </span>
      </div>

      <svg
        data-testid="graph-svg"
        role="img"
        aria-label="Workspace relation graph"
        viewBox={`0 0 ${width} ${height}`}
        className="w-full"
        style={{ minHeight: '320px' }}
      >
        <g data-testid="graph-edges">
          {bounded.visibleEdges.map((edge) => {
            const from = layout.get(edge.source);
            const to = layout.get(edge.target);
            if (!from || !to) return null;
            const a = center(from);
            const b = center(to);
            const inferred = edge.origin === 'inferred';
            return (
              <line
                key={edge.id}
                data-testid={`graph-edge-${edge.id}`}
                data-origin={edge.origin}
                data-relation-type={edge.relationType}
                x1={a.x}
                y1={a.y}
                x2={b.x}
                y2={b.y}
                stroke={inferred ? '#a855f7' : '#64748b'}
                strokeWidth={inferred ? 1.5 : 2}
                strokeDasharray={inferred ? '6 4' : undefined}
              />
            );
          })}
        </g>

        <g data-testid="graph-nodes">
          {bounded.visibleNodes.map((node) => {
            const point = layout.get(node.id) ?? { x: 0, y: 0 };
            const c = center(point);
            const selected = node.id === selectedId;
            return (
              <g
                key={node.id}
                data-testid={`graph-node-${node.id}`}
                role="button"
                tabIndex={0}
                aria-label={`${isMarkdownNode(node) ? 'Markdown' : 'Document'}: ${node.title}`}
                aria-pressed={selected}
                style={{ cursor: 'pointer' }}
                onClick={() => handleSelect(node.id)}
                onDoubleClick={() => handleOpen(node)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    handleOpen(node);
                  }
                }}
              >
                <circle
                  cx={c.x}
                  cy={c.y}
                  r={NODE_RADIUS}
                  fill={selected ? '#3b82f6' : '#e2e8f0'}
                  stroke={selected ? '#1d4ed8' : '#94a3b8'}
                  strokeWidth={2}
                />
                <text
                  x={c.x}
                  y={c.y + NODE_RADIUS + 14}
                  textAnchor="middle"
                  className="fill-slate-700"
                  style={{ fontSize: '11px' }}
                >
                  {node.title}
                </text>
              </g>
            );
          })}
        </g>
      </svg>

      {sourceNodes.length === 0 && (
        <p className="p-4 text-center text-xs text-slate-400" data-testid="graph-empty">
          No relations yet. Add Markdown links or wikilinks to build the graph.
        </p>
      )}
    </div>
  );
};

export default DocumentGraph;
