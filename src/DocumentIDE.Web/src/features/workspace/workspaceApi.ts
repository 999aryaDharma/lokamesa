/**
 * Workspace bootstrap data source (spec 03 §2 / master spec §83).
 *
 * A named workspace references local folder sources without importing files.
 * The frontend loads the workspace's cached documents and its attached folder
 * sources through the privileged local API, then the app shell populates the
 * canvas store. This module never reads the filesystem directly and never
 * exposes arbitrary local paths to the user beyond the folder display names
 * the folder-source service already returns.
 *
 * Contracts:
 *   GET {apiUrl}/api/workspaces/{workspaceId}/documents
 *     -> 200 Document[]  (ReconciliationController.GetCachedDocuments)
 *   GET {apiUrl}/api/workspaces/{workspaceId}/folder-sources
 *     -> 200 FolderSource[]  (FolderSourcesController.GetFolderSources)
 */

export interface WorkspaceDocumentDto {
  id: string;
  filename: string;
  extension?: string;
  currentPath?: string;
  lastKnownPath?: string;
  documentType?: string;
  availabilityStatus?: string;
  indexStatus?: string;
}

export interface WorkspaceFolderSourceDto {
  id: string;
  workspaceId?: string;
  canonicalPath: string;
  displayName?: string;
  availabilityStatus?: string;
}

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

async function getJson<T>(path: string, fetchImpl: typeof fetch): Promise<T> {
  const { base, token } = apiBase();
  const response = await fetchImpl(`${base}${path}`, {
    headers: { 'X-DocumentIDE-Session-Token': token },
  });
  if (!response.ok) {
    throw new Error(`Request failed (${response.status}) for ${path}.`);
  }
  return (await response.json()) as T;
}

/** Workspace documents already reconciled/indexed by the backend. */
export function fetchWorkspaceDocuments(
  workspaceId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<WorkspaceDocumentDto[]> {
  return getJson<WorkspaceDocumentDto[]>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/documents`,
    fetchImpl,
  );
}

/** Folder sources attached to the workspace. */
export function fetchFolderSources(
  workspaceId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<WorkspaceFolderSourceDto[]> {
  return getJson<WorkspaceFolderSourceDto[]>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/folder-sources`,
    fetchImpl,
  );
}

export interface AttachFolderSourceResult {
  folderSource: WorkspaceFolderSourceDto;
  supportedFileCount: number;
}

/**
 * Attach a local folder source (FS-01).
 *
 * This must go through the injected API base and session token, exactly like
 * the bootstrap calls above. A relative `/api/...` path resolves against the
 * WebView's `https://appassets.html2sav` origin in the packaged app and fails
 * with "Failed to fetch" (it only works behind the dev-server proxy).
 */
export async function attachFolderSource(
  workspaceId: string,
  folderPath: string,
  fetchImpl: typeof fetch = fetch,
): Promise<AttachFolderSourceResult> {
  const { base, token } = apiBase();
  const response = await fetchImpl(
    `${base}/api/workspaces/${encodeURIComponent(workspaceId)}/folder-sources`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-DocumentIDE-Session-Token': token,
      },
      body: JSON.stringify({ folderPath }),
    },
  );

  if (!response.ok) {
    let message = `Failed to attach folder source (${response.status}).`;
    try {
      const body = (await response.json()) as { error?: string };
      if (body?.error) {
        message = body.error;
      }
    } catch {
      // Keep the status-based message when the body is not JSON.
    }
    throw new Error(message);
  }

  return (await response.json()) as AttachFolderSourceResult;
}
