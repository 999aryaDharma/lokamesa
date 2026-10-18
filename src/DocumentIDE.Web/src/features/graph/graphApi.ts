/**
 * GR-03 / GR-05 — Graph data source for the privileged local API.
 *
 * Contract (matches the ShareHost viewer graph envelope from SHR-06 so the
 * owner and viewer surfaces stay aligned):
 *
 *   GET {apiUrl}/api/workspaces/{workspaceId}/graph
 *   -> 200 { nodes: [{ documentId, displayName, documentType? }],
 *            edges: [{ sourceDocumentId, targetDocumentId,
 *                      relationType, relationOrigin, confidence? }] }
 *
 * Truthfulness (master spec §27.2, spec 08 §14, AI-04):
 *   - `Explicit` and `Manual` relations are factual -> solid edges.
 *   - `Inferred` and `Semantic` relations are suggestions -> dashed edges.
 * A malformed / unknown origin is treated as explicit ONLY when it is one of
 * the known explicit origins; anything unknown is downgraded to `inferred`
 * so the view never falsifies a relationship (fail toward the weaker claim).
 */
import type { GraphEdge, GraphEdgeOrigin, GraphNode } from './graphStore';

interface ApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

export interface GraphPayloadNode {
  documentId: string;
  displayName?: string | null;
  documentType?: string | null;
}

export interface GraphPayloadEdge {
  sourceDocumentId: string;
  targetDocumentId: string;
  relationType?: string | null;
  relationOrigin?: string | null;
  confidence?: number | null;
}

export interface GraphPayload {
  nodes: GraphPayloadNode[];
  edges: GraphPayloadEdge[];
}

export const EXPLICIT_ORIGINS = new Set(['explicit', 'manual']);
export const INFERRED_ORIGINS = new Set(['inferred', 'semantic']);

/**
 * Classify a backend RelationOrigin into the truthful rendering vocabulary.
 * Unknown origins are downgraded to `inferred` (dashed) — never promoted to a
 * factual solid relation.
 */
export function classifyEdgeOrigin(relationOrigin?: string | null): GraphEdgeOrigin {
  const normalized = (relationOrigin ?? '').trim().toLowerCase();
  if (INFERRED_ORIGINS.has(normalized)) return 'inferred';
  if (EXPLICIT_ORIGINS.has(normalized)) return 'explicit';
  return 'inferred';
}

function inferDocType(name: string, explicit?: string | null): GraphNode['docType'] {
  const lower = (explicit ?? '').toLowerCase();
  if (lower === 'docx' || lower === 'word') return 'docx';
  if (lower === 'md' || lower === 'markdown') return 'md';
  const filename = name.toLowerCase();
  if (filename.endsWith('.docx') || filename.endsWith('.doc')) return 'docx';
  if (filename.endsWith('.md') || filename.endsWith('.markdown')) return 'md';
  return 'unknown';
}

function safeId(prefix: string, source: string, target: string, relationType: string): string {
  const raw = `${prefix}:${source}->${target}:${relationType}`;
  // Deterministic, DOM-safe test id fragment.
  return raw.replace(/[^A-Za-z0-9:_>-]/g, '_');
}

/** Map the API payload to graph nodes (unknown rows skipped, never guessed). */
export function toGraphNodes(nodes: GraphPayloadNode[] | null | undefined): GraphNode[] {
  if (!Array.isArray(nodes)) return [];
  return nodes
    .filter((n) => n && typeof n.documentId === 'string' && n.documentId.length > 0)
    .map((n) => {
      const title = n.displayName ?? n.documentId;
      return {
        id: n.documentId,
        title,
        docType: inferDocType(title, n.documentType),
      };
    });
}

/**
 * Map the API payload to graph edges. An edge whose endpoints are unknown
 * (no node) is still returned — the renderer bounds visibility by node set —
 * but malformed rows (missing endpoints) are skipped.
 */
export function toGraphEdges(edges: GraphPayloadEdge[] | null | undefined): GraphEdge[] {
  if (!Array.isArray(edges)) return [];
  return edges
    .filter(
      (e) =>
        e &&
        typeof e.sourceDocumentId === 'string' &&
        e.sourceDocumentId.length > 0 &&
        typeof e.targetDocumentId === 'string' &&
        e.targetDocumentId.length > 0,
    )
    .map((e) => {
      const relationType = e.relationType ?? 'Related';
      return {
        id: safeId('edge', e.sourceDocumentId, e.targetDocumentId, relationType),
        source: e.sourceDocumentId,
        target: e.targetDocumentId,
        relationType,
        origin: classifyEdgeOrigin(e.relationOrigin),
        confidence: typeof e.confidence === 'number' ? e.confidence : undefined,
      };
    });
}

/**
 * Load the workspace graph. Throws on a non-OK response so the caller can
 * decide whether to degrade (the app shell keeps the last good graph).
 */
export async function fetchWorkspaceGraph(
  workspaceId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<{ nodes: GraphNode[]; edges: GraphEdge[] }> {
  const globals = window as Window & ApiGlobals;
  const base = globals.__DOCUMENTIDE_API_URL__ || '';
  const token = globals.__DOCUMENTIDE_SESSION_TOKEN__ || '';
  const response = await fetchImpl(
    `${base}/api/workspaces/${encodeURIComponent(workspaceId)}/graph`,
    { headers: { 'X-DocumentIDE-Session-Token': token } },
  );
  if (!response.ok) {
    throw new Error(`Graph request failed (${response.status}).`);
  }
  const payload = (await response.json()) as GraphPayload;
  return { nodes: toGraphNodes(payload?.nodes), edges: toGraphEdges(payload?.edges) };
}
