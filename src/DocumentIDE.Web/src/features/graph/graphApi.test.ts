import { describe, expect, it, vi, afterEach } from 'vitest';
import {
  classifyEdgeOrigin,
  fetchWorkspaceGraph,
  toGraphEdges,
  toGraphNodes,
} from './graphApi';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('classifyEdgeOrigin (spec §27.2 truthful semantics)', () => {
  it('treats Explicit and Manual relations as solid/explicit', () => {
    expect(classifyEdgeOrigin('Explicit')).toBe('explicit');
    expect(classifyEdgeOrigin('explicit')).toBe('explicit');
    expect(classifyEdgeOrigin('Manual')).toBe('explicit');
  });

  it('treats Inferred and AI-04 Semantic relations as dashed/inferred', () => {
    expect(classifyEdgeOrigin('Inferred')).toBe('inferred');
    expect(classifyEdgeOrigin('semantic')).toBe('inferred');
  });

  it('downgrades an unknown origin to inferred rather than claiming it is factual', () => {
    expect(classifyEdgeOrigin(undefined)).toBe('inferred');
    expect(classifyEdgeOrigin('')).toBe('inferred');
    expect(classifyEdgeOrigin('SomethingNew')).toBe('inferred');
  });
});

describe('payload mapping', () => {
  it('maps nodes and infers doc types without guessing paths', () => {
    const nodes = toGraphNodes([
      { documentId: 'a', displayName: 'architecture.md' },
      { documentId: 'b', displayName: 'report.docx' },
      { documentId: 'c', displayName: 'notes', documentType: 'markdown' },
    ]);
    expect(nodes).toEqual([
      { id: 'a', title: 'architecture.md', docType: 'md' },
      { id: 'b', title: 'report.docx', docType: 'docx' },
      { id: 'c', title: 'notes', docType: 'md' },
    ]);
  });

  it('skips malformed edges and preserves origin classification', () => {
    const edges = toGraphEdges([
      { sourceDocumentId: 'a', targetDocumentId: 'b', relationType: 'Wikilink', relationOrigin: 'Explicit' },
      { sourceDocumentId: 'a', targetDocumentId: 'c', relationType: 'SemanticSuggestion', relationOrigin: 'Semantic', confidence: 0.8 },
      { sourceDocumentId: '', targetDocumentId: 'c' },
    ]);
    expect(edges).toHaveLength(2);
    expect(edges[0].origin).toBe('explicit');
    expect(edges[1].origin).toBe('inferred');
    expect(edges[1].confidence).toBe(0.8);
  });

  it('tolerates a null payload', () => {
    expect(toGraphNodes(null)).toEqual([]);
    expect(toGraphEdges(undefined)).toEqual([]);
  });
});

describe('fetchWorkspaceGraph', () => {
  it('calls the privileged workspace graph route and normalizes the payload', async () => {
    (window as unknown as { __DOCUMENTIDE_API_URL__?: string }).__DOCUMENTIDE_API_URL__ = 'http://localhost:5000';
    (window as unknown as { __DOCUMENTIDE_SESSION_TOKEN__?: string }).__DOCUMENTIDE_SESSION_TOKEN__ = 'tok';
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({
        nodes: [{ documentId: 'a', displayName: 'a.md' }],
        edges: [
          { sourceDocumentId: 'a', targetDocumentId: 'b', relationType: 'MarkdownLink', relationOrigin: 'Explicit' },
        ],
      }),
    } as Response);

    const graph = await fetchWorkspaceGraph('ws-1');

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5000/api/workspaces/ws-1/graph',
      expect.objectContaining({ headers: { 'X-DocumentIDE-Session-Token': 'tok' } }),
    );
    expect(graph.nodes[0].id).toBe('a');
    expect(graph.edges[0].origin).toBe('explicit');

    delete (window as unknown as { __DOCUMENTIDE_API_URL__?: string }).__DOCUMENTIDE_API_URL__;
    delete (window as unknown as { __DOCUMENTIDE_SESSION_TOKEN__?: string }).__DOCUMENTIDE_SESSION_TOKEN__;
  });

  it('throws on a non-OK response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({ ok: false, status: 404 } as Response);
    await expect(fetchWorkspaceGraph('ws-1')).rejects.toThrow(/404/);
  });
});
