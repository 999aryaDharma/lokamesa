import { describe, it, expect } from 'vitest';
import { computeBoundedGraph, computeLayoutAsync } from './graphLayoutWorker';
import { filterSemanticEdgesTopK } from './GraphScopeControls';

describe('GR-05 — Bounded Graph Rendering', () => {
  it('defaults >250 nodes to bounded neighborhood mode unless full graph is explicitly requested', () => {
    const nodes = Array.from({ length: 300 }, (_, i) => ({ id: `node-${i}` }));
    const edges = nodes.slice(1).map((n) => ({ source: 'node-0', target: n.id }));

    // Default threshold is 250
    const bounded = computeBoundedGraph(nodes, edges, 'node-0', false, 250);
    expect(bounded.isBounded).toBe(true);
    expect(bounded.visibleNodes.length).toBeLessThan(300);

    // Explicit full graph action
    const full = computeBoundedGraph(nodes, edges, 'node-0', true, 250);
    expect(full.isBounded).toBe(false);
    expect(full.visibleNodes.length).toBe(300);
  });

  it('filters semantic edges to top-K (default K=5) to prevent all-to-all semantic rendering', () => {
    const semanticEdges = Array.from({ length: 20 }, (_, i) => ({
      source: 'node-0',
      target: `target-${i}`,
      type: 'Inferred',
      confidence: i * 0.05, // 0.0 to 0.95
    }));

    const topK = filterSemanticEdgesTopK(semanticEdges, 5);
    expect(topK.length).toBe(5);
    // Highest confidence edges should be kept
    expect(topK[0].confidence).toBeCloseTo(0.95);
  });

  it('computes graph layout off-main-thread asynchronously without blocking UI', async () => {
    const nodes = Array.from({ length: 100 }, (_, i) => ({ id: `node-${i}` }));
    const edges: Array<{ source: string; target: string }> = [];

    const positions = await computeLayoutAsync(nodes, edges);
    expect(positions.size).toBe(100);
    expect(positions.has('node-0')).toBe(true);
  });
});
