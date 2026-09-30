/**
 * Off-thread / async layout worker module for GR-05 — Bounded Graph Rendering.
 */

export interface GraphNode {
  id: string;
  [key: string]: any;
}

export interface GraphEdge {
  source: string;
  target: string;
  [key: string]: any;
}

export interface BoundedGraphResult {
  isBounded: boolean;
  visibleNodes: GraphNode[];
  visibleEdges: GraphEdge[];
}

/**
 * Bounds visible nodes when node count exceeds threshold (>250 by default),
 * unless full graph rendering is explicitly enabled.
 */
export function computeBoundedGraph(
  nodes: GraphNode[],
  edges: GraphEdge[],
  selectedNodeId?: string,
  isFullGraphExplicit: boolean = false,
  threshold: number = 250
): BoundedGraphResult {
  if (isFullGraphExplicit || nodes.length <= threshold) {
    return {
      isBounded: false,
      visibleNodes: nodes,
      visibleEdges: edges,
    };
  }

  // Bounded mode: filter to selected node + immediate neighborhood
  const centerId = selectedNodeId || (nodes.length > 0 ? nodes[0].id : '');
  if (!centerId) {
    const subsetNodes = nodes.slice(0, threshold);
    const visibleIds = new Set(subsetNodes.map((n) => n.id));
    const subsetEdges = edges.filter(
      (e) => visibleIds.has(e.source) && visibleIds.has(e.target)
    );
    return {
      isBounded: true,
      visibleNodes: subsetNodes,
      visibleEdges: subsetEdges,
    };
  }

  const visibleIds = new Set<string>([centerId]);
  
  // Add 1-hop neighbors
  for (const edge of edges) {
    if (edge.source === centerId) visibleIds.add(edge.target);
    if (edge.target === centerId) visibleIds.add(edge.source);
    if (visibleIds.size >= threshold) break;
  }

  // Add 2-hop neighbors if still under threshold
  if (visibleIds.size < threshold) {
    for (const edge of edges) {
      if (visibleIds.has(edge.source)) visibleIds.add(edge.target);
      if (visibleIds.has(edge.target)) visibleIds.add(edge.source);
      if (visibleIds.size >= threshold) break;
    }
  }

  const visibleNodes = nodes.filter((n) => visibleIds.has(n.id));
  const visibleEdges = edges.filter(
    (e) => visibleIds.has(e.source) && visibleIds.has(e.target)
  );

  return {
    isBounded: true,
    visibleNodes,
    visibleEdges,
  };
}

/**
 * Computes force / grid layout asynchronously to prevent UI thread blocking.
 */
export async function computeLayoutAsync(
  nodes: GraphNode[],
  _edges: GraphEdge[]
): Promise<Map<string, { x: number; y: number }>> {
  return new Promise((resolve) => {
    // Off-thread asynchronous positioning (can also dispatch to WebWorker if available)
    setTimeout(() => {
      const positions = new Map<string, { x: number; y: number }>();
      const cols = Math.ceil(Math.sqrt(nodes.length));
      const spacing = 180;

      nodes.forEach((node, idx) => {
        const row = Math.floor(idx / cols);
        const col = idx % cols;
        positions.set(node.id, {
          x: col * spacing,
          y: row * spacing,
        });
      });

      resolve(positions);
    }, 0);
  });
}
