/**
 * MD-01 / MD-02 — Markdown source editing data source.
 *
 * Contract (privileged local API, FND-02/QA-03 session guard):
 *
 *   GET {apiUrl}/api/documents/{documentId}/markdown
 *     -> 200 { documentId, source }
 *
 *   PUT {apiUrl}/api/documents/{documentId}/markdown
 *     body { source }
 *     -> 200 { documentId, savedAt? }
 *
 * Document identity is the stable FS-02 DocumentId — never a filesystem path,
 * so the editor never exposes arbitrary local paths. The `.md` file on disk
 * remains the source of truth (spec 08 §2); the server owns the safe-write
 * pipeline (temp write -> validation -> atomic replace -> watcher/index).
 */

export interface MarkdownDocument {
  documentId: string;
  source: string;
}

interface ApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

function apiBase(): { base: string; token: string } {
  const globals = window as Window & ApiGlobals;
  return { base: globals.__DOCUMENTIDE_API_URL__ || '', token: globals.__DOCUMENTIDE_SESSION_TOKEN__ || '' };
}

async function asErrorMessage(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string; detail?: string };
    return body?.error || body?.detail || `Request failed (${response.status}).`;
  } catch {
    return `Request failed (${response.status}).`;
  }
}

/** Load the raw Markdown source for a document. */
export async function loadMarkdown(
  documentId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<MarkdownDocument> {
  const { base, token } = apiBase();
  const response = await fetchImpl(
    `${base}/api/documents/${encodeURIComponent(documentId)}/markdown`,
    { headers: { 'X-DocumentIDE-Session-Token': token } },
  );
  if (!response.ok) {
    throw new Error(await asErrorMessage(response));
  }
  const body = (await response.json()) as Partial<MarkdownDocument>;
  return { documentId: body.documentId ?? documentId, source: body.source ?? '' };
}

/** Save the raw Markdown source for a document through the safe-write API. */
export async function saveMarkdown(
  documentId: string,
  source: string,
  fetchImpl: typeof fetch = fetch,
): Promise<void> {
  const { base, token } = apiBase();
  const response = await fetchImpl(
    `${base}/api/documents/${encodeURIComponent(documentId)}/markdown`,
    {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        'X-DocumentIDE-Session-Token': token,
      },
      body: JSON.stringify({ source }),
    },
  );
  if (!response.ok) {
    throw new Error(await asErrorMessage(response));
  }
}
