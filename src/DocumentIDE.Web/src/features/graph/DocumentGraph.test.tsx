// src/DocumentIDE.Web/src/features/graph/DocumentGraph.test.tsx

import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { useGraphStore, type GraphEdge, type GraphNode } from './graphStore';
import { useCanvasStore } from '../canvas/canvasStore';
import DocumentGraph from './DocumentGraph';

/**
 * Test that moving a graph node does NOT affect canvas store state.
 */
describe('GraphNode_DoesNotAffectCanvas', () => {
  beforeEach(() => {
    // reset stores
    useGraphStore.getState().reset();
    useCanvasStore.getState().reset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('moving graph node should not change canvas viewport', () => {
    // set initial canvas viewport
    const { setViewport, viewport } = useCanvasStore.getState();
    act(() => setViewport({ x: 10, y: 20, zoom: 1 }));
    const initialViewport = { ...viewport };

    // add a graph node and move it
    const { setPosition } = useGraphStore.getState();
    act(() => setPosition('node-1', { x: 0, y: 0 }));
    act(() => setPosition('node-1', { x: 100, y: 200 }));

    // Expect canvas viewport unchanged (intentionally failing)
    const { viewport: newViewport } = useCanvasStore.getState();
    expect(newViewport).toEqual(initialViewport);
  });
});

// ---------------------------------------------------------------------------
// GR-03 — truthful graph semantics (master spec §27.2 / spec 08 §14)
// ---------------------------------------------------------------------------
describe('DocumentGraph_TruthfulEdgeSemantics', () => {
  const nodes: GraphNode[] = [
    { id: 'doc-a', title: 'architecture.md', docType: 'md' },
    { id: 'doc-b', title: 'execution-engine.md', docType: 'md' },
    { id: 'doc-c', title: 'risk-management.md', docType: 'md' },
  ];
  const edges: GraphEdge[] = [
    {
      id: 'edge-explicit-1',
      source: 'doc-a',
      target: 'doc-b',
      relationType: 'Wikilink',
      origin: 'explicit',
    },
    {
      id: 'edge-inferred-1',
      source: 'doc-a',
      target: 'doc-c',
      relationType: 'SemanticSuggestion',
      origin: 'inferred',
      confidence: 0.87,
    },
  ];

  beforeEach(() => {
    useGraphStore.getState().reset();
    useCanvasStore.getState().reset();
  });

  it('Explicit edge is solid', () => {
    render(<DocumentGraph workspaceId="ws-1" nodes={nodes} edges={edges} />);

    const explicit = screen.getByTestId('graph-edge-edge-explicit-1');
    // Solid = no dash pattern.
    expect(explicit.getAttribute('stroke-dasharray')).toBeNull();
    expect(explicit.getAttribute('data-origin')).toBe('explicit');
  });

  it('Inferred / semantic edge is dashed (never rendered as factual)', () => {
    render(<DocumentGraph workspaceId="ws-1" nodes={nodes} edges={edges} />);

    const inferred = screen.getByTestId('graph-edge-edge-inferred-1');
    expect(inferred.getAttribute('stroke-dasharray')).not.toBeNull();
    expect(inferred.getAttribute('data-origin')).toBe('inferred');
  });

  it('renders every relation as an edge (nothing silently dropped)', () => {
    render(<DocumentGraph workspaceId="ws-1" nodes={nodes} edges={edges} />);
    expect(screen.getByTestId('graph-edge-edge-explicit-1')).toBeInTheDocument();
    expect(screen.getByTestId('graph-edge-edge-inferred-1')).toBeInTheDocument();
  });

  it('renders graph nodes with an accessible label', () => {
    render(<DocumentGraph workspaceId="ws-1" nodes={nodes} edges={edges} />);
    const node = screen.getByTestId('graph-node-doc-a');
    expect(node).toBeInTheDocument();
    expect(node.getAttribute('role')).toBe('button');
    expect(node.getAttribute('aria-label')).toContain('architecture.md');
  });
});

// ---------------------------------------------------------------------------
// GR-03 step 5 / spec 08 §17 — double click Markdown node opens the editor
// ---------------------------------------------------------------------------
describe('DocumentGraph_OpenFromNode', () => {
  const mdNode: GraphNode = { id: 'md-1', title: 'notes.md', docType: 'md' };
  const docxNode: GraphNode = { id: 'docx-1', title: 'report.docx', docType: 'docx' };

  beforeEach(() => {
    useGraphStore.getState().reset();
    useCanvasStore.getState().reset();
  });

  it('Double-click Markdown node opens editor', () => {
    const onOpenDocument = vi.fn();
    render(<DocumentGraph nodes={[mdNode]} edges={[]} onOpenDocument={onOpenDocument} />);

    fireEvent.doubleClick(screen.getByTestId('graph-node-md-1'));

    expect(onOpenDocument).toHaveBeenCalledTimes(1);
    expect(onOpenDocument).toHaveBeenCalledWith(expect.objectContaining({ id: 'md-1' }));
  });

  it('double-click DOCX node routes to Open in Word', () => {
    const onOpenDocument = vi.fn();
    const onOpenInWord = vi.fn();
    render(
      <DocumentGraph
        nodes={[docxNode]}
        edges={[]}
        onOpenDocument={onOpenDocument}
        onOpenInWord={onOpenInWord}
      />,
    );

    fireEvent.doubleClick(screen.getByTestId('graph-node-docx-1'));

    expect(onOpenInWord).toHaveBeenCalledWith('docx-1');
    expect(onOpenDocument).not.toHaveBeenCalled();
  });

  it('clicking a node selects it without opening an editor', () => {
    const onSelectNode = vi.fn();
    const onOpenDocument = vi.fn();
    render(
      <DocumentGraph
        nodes={[mdNode]}
        edges={[]}
        onSelectNode={onSelectNode}
        onOpenDocument={onOpenDocument}
      />,
    );

    fireEvent.click(screen.getByTestId('graph-node-md-1'));

    expect(onSelectNode).toHaveBeenCalledWith('md-1');
    expect(onOpenDocument).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// GR-05 — bounded rendering helpers are mounted by the shipped component
// ---------------------------------------------------------------------------
describe('DocumentGraph_BoundedRendering', () => {
  beforeEach(() => {
    useGraphStore.getState().reset();
    useCanvasStore.getState().reset();
  });

  it('mounts GraphScopeControls and bounds a very large graph', () => {
    const nodes: GraphNode[] = Array.from({ length: 300 }, (_, i) => ({
      id: `n-${i}`,
      title: `doc-${i}.md`,
      docType: 'md',
    }));
    const edges: GraphEdge[] = nodes.slice(1).map((n, i) => ({
      id: `e-${i}`,
      source: 'n-0',
      target: n.id,
      relationType: 'Wikilink',
      origin: 'explicit',
    }));

    render(<DocumentGraph nodes={nodes} edges={edges} />);

    expect(screen.getByTestId('graph-scope-controls')).toBeInTheDocument();
    expect(screen.getByText(/Graph bounded/i)).toBeInTheDocument();
  });

  it('loads the workspace graph from the API when no nodes are provided', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({
        nodes: [{ documentId: 'api-1', displayName: 'api.md', documentType: 'md' }],
        edges: [],
      }),
    } as Response);

    render(<DocumentGraph workspaceId="ws-api" />);

    expect(
      await screen.findByTestId('graph-node-api-1'),
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/workspaces/ws-api/graph'),
      expect.anything(),
    );
  });
});
