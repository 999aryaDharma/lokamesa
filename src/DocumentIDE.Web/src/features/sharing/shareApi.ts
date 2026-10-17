/**
 * SHR-11 — Owner share lifecycle data source (privileged local API).
 *
 * Wires the previously intent-only ShareDialog/ShareManager callbacks to real
 * endpoints (gap G5). Gated by the same FND-02/QA-03 privileged API guard as
 * the rest of the owner surface (Origin/Host/session token).
 *
 * Contracts (implemented by the privileged host, Lane B):
 *   GET    {apiUrl}/api/shares                                  -> ShareManagementState[]
 *   POST   {apiUrl}/api/shares                                  -> ShareManagementState
 *   GET    {apiUrl}/api/shares/{shareId}                        -> ShareManagementState
 *   POST   {apiUrl}/api/shares/{shareId}/stop                   -> ShareManagementState
 *   POST   {apiUrl}/api/shares/{shareId}/revoke                 -> ShareManagementState
 *   POST   {apiUrl}/api/shares/{shareId}/regenerate             -> ShareManagementState
 *   POST   {apiUrl}/api/shares/{shareId}/members                body { documentId }
 *   DELETE {apiUrl}/api/shares/{shareId}/members/{documentId}
 *
 * The share token itself is never rendered by the owner UI; only the public
 * share URL is displayed (SHR-11).
 */
import type { ShareCreatePayload } from './ShareDialog';
import type { ShareManagementState } from './ShareManager';

interface ApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

function apiBase(): { base: string; token: string } {
  const globals = window as Window & ApiGlobals;
  return {
    base: globals.__DOCUMENTIDE_API_URL__ || '',
    token: globals.__DOCUMENTIDE_SESSION_TOKEN__ || '',
  };
}

async function request<T>(
  path: string,
  init: RequestInit,
  fetchImpl: typeof fetch,
): Promise<T> {
  const { base, token } = apiBase();
  const response = await fetchImpl(`${base}/api/shares${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': token,
      ...(init.headers ?? {}),
    },
  });
  if (!response.ok) {
    let detail = `Share request failed (${response.status}).`;
    try {
      const body = (await response.json()) as { error?: string; detail?: string };
      detail = body?.error || body?.detail || detail;
    } catch {
      /* keep default detail */
    }
    throw new Error(detail);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export function listShares(fetchImpl: typeof fetch = fetch): Promise<ShareManagementState[]> {
  return request<ShareManagementState[]>('', { method: 'GET' }, fetchImpl);
}

export function createShare(
  payload: ShareCreatePayload,
  fetchImpl: typeof fetch = fetch,
): Promise<ShareManagementState> {
  return request<ShareManagementState>(
    '',
    { method: 'POST', body: JSON.stringify(payload) },
    fetchImpl,
  );
}

export function stopShare(
  shareId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<ShareManagementState> {
  return request<ShareManagementState>(
    `/${encodeURIComponent(shareId)}/stop`,
    { method: 'POST' },
    fetchImpl,
  );
}

export function revokeShare(
  shareId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<ShareManagementState> {
  return request<ShareManagementState>(
    `/${encodeURIComponent(shareId)}/revoke`,
    { method: 'POST' },
    fetchImpl,
  );
}

export function regenerateShare(
  shareId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<ShareManagementState> {
  return request<ShareManagementState>(
    `/${encodeURIComponent(shareId)}/regenerate`,
    { method: 'POST' },
    fetchImpl,
  );
}

export function addShareMember(
  shareId: string,
  documentId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<ShareManagementState> {
  return request<ShareManagementState>(
    `/${encodeURIComponent(shareId)}/members`,
    { method: 'POST', body: JSON.stringify({ documentId }) },
    fetchImpl,
  );
}

export function removeShareMember(
  shareId: string,
  documentId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<ShareManagementState> {
  return request<ShareManagementState>(
    `/${encodeURIComponent(shareId)}/members/${encodeURIComponent(documentId)}`,
    { method: 'DELETE' },
    fetchImpl,
  );
}
