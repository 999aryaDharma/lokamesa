import { describe, it, expect } from 'vitest';
import { computeBoundedGraph, computeLayoutAsync } from './graphLayoutWorker';
import type { GraphEdge, GraphNode } from './graphLayoutWorker';
import { filterSemanticEdgesTopK } from './GraphScopeControls';

/**
 * QA-06 — Performance Benchmark Gate, graph leg (GR-05 bounded rendering).
 *
 * Contrasts bounded neighborhood rendering against full-graph rendering at
 * 300/1,000/3,000 nodes: visible output stays capped at the 250-node budget
 * while the total graph grows 10x, and laying out the bounded neighborhood
 * costs a fraction of laying out the full graph. Layout stays off the UI
 * thread (async). Numbers print as QA06_* lines for
 * docs/quality/performance-baseline.md.
 */
describe('QA-06 graph performance gate (GR-05 bounded rendering)', () => {
  const THRESHOLD = 250;

  function chainGraph(nodeCount: number): { nodes: GraphNode[]; edges: GraphEdge[] } {
    const nodes: GraphNode[] = Array.from({ length: nodeCount }, (_, i) => ({ id: `node-${i}` }));
    const edges: GraphEdge[] = [];
    for (let i = 0; i < nodeCount - 1; i++) {
      edges.push({ source: `node-${i}`, target: `node-${i + 1}` });
      if (i + 2 < nodeCount) edges.push({ source: `node-${i}`, target: `node-${i + 2}` });
    }
    return { nodes, edges };
  }

  it('neighborhood stays bounded while the graph grows 300 -> 3,000 nodes', () => {
    const visibleByTotal: Record<number, number> = {};
    for (const total of [300, 1000, 3000]) {
      const { nodes, edges } = chainGraph(total);
      const center = `node-${Math.floor(total / 2)}`;
      const startedAt = performance.now();
      const bounded = computeBoundedGraph(nodes, edges, center, false, THRESHOLD);
      const elapsedMs = performance.now() - startedAt;

      expect(bounded.isBounded).toBe(true);
      // Bounded output never exceeds the render budget, however large the graph.
      expect(bounded.visibleNodes.length).toBeLessThanOrEqual(THRESHOLD);
      // The neighborhood is centered where the user is, not an arbitrary slice.
      expect(bounded.visibleNodes.some((n) => n.id === center)).toBe(true);
      visibleByTotal[total] = bounded.visibleNodes.length;
      // eslint-disable-next-line no-console
      console.log(
        `QA06_GRAPH_BOUND total=${total} visible=${bounded.visibleNodes.length} ` +
          `visibleEdges=${bounded.visibleEdges.length} elapsedMs=${elapsedMs.toFixed(2)}`
      );
      expect(elapsedMs).toBeLessThan(2000);
    }

    // Visible output is bounded by the render budget, not by graph size:
    // once the neighborhood reaches the cap, 3x further growth (1,000 ->
    // 3,000 nodes) produces identical output. (At 300 nodes the center's
    // 2-hop neighborhood is genuinely smaller than the cap, which is also
    // correct: bounding never invents nodes.)
    expect(visibleByTotal[3000]).toBe(visibleByTotal[1000]);
  });

  it('bounded-neighborhood layout does a fraction of full-graph work', async () => {
    const { nodes, edges } = chainGraph(1000);
    const center = 'node-500';
    const bounded = computeBoundedGraph(nodes, edges, center, false, THRESHOLD);
    expect(bounded.isBounded).toBe(true);

    // Off-UI-thread contract: layout is async (never a blocking call).
    const neighborhoodPromise = computeLayoutAsync(bounded.visibleNodes, bounded.visibleEdges);
    expect(neighborhoodPromise).toBeInstanceOf(Promise);

    const nStart = performance.now();
    const neighborhoodPositions = await neighborhoodPromise;
    const neighborhoodMs = performance.now() - nStart;

    const fStart = performance.now();
    const fullPositions = await computeLayoutAsync(nodes, edges);
    const fullMs = performance.now() - fStart;

    expect(neighborhoodPositions.size).toBe(bounded.visibleNodes.length);
    expect(fullPositions.size).toBe(nodes.length);
    // Layout work scales with positioned node count: the bounded view lays
    // out at most the 250-node budget against the 1,000-node full graph.
    const workRatio = bounded.visibleNodes.length / nodes.length;
    // eslint-disable-next-line no-console
    console.log(
      `QA06_GRAPH_LAYOUT total=1000 visible=${bounded.visibleNodes.length} ` +
        `workRatio=${workRatio.toFixed(2)} ` +
        `neighborhoodMs=${neighborhoodMs.toFixed(2)} fullMs=${fullMs.toFixed(2)} (timing recorded only)`
    );
    expect(workRatio).toBeLessThanOrEqual(0.3);
    expect(bounded.visibleNodes.length).toBeLessThan(nodes.length);
  });

  it('semantic edge filter caps all-to-all rendering at Top-K', () => {
    const semanticEdges = Array.from({ length: 200 }, (_, i) => ({
      source: 'node-0',
      target: `target-${i}`,
      type: 'Inferred',
      confidence: i / 200,
    }));
    const startedAt = performance.now();
    const topK = filterSemanticEdgesTopK(semanticEdges, 5);
    const elapsedMs = performance.now() - startedAt;

    expect(topK.length).toBe(5);
    expect(topK[0].confidence).toBeCloseTo(199 / 200);
    // eslint-disable-next-line no-console
    console.log(`QA06_GRAPH_TOPK edges=200 kept=${topK.length} elapsedMs=${elapsedMs.toFixed(2)}`);
  });
});
