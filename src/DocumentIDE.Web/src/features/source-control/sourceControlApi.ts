/**
 * UI-03 — typed client for the privileged Source Control routes that already
 * exist (status, remote, conflicts, worktrees, file version, markdown source,
 * worktree mutations).
 *
 * Every call carries the host-injected session token in the header, never in
 * the URL. Responses are the API's typed contracts (spec 13 §20); this module
 * only parses and forwards them — it never invents a change, a counter or a
 * worktree.
 */

import type {
  SourceControlConflictModel,
  SourceControlFailure,
  SourceControlFileVersionResult,
  SourceControlRemoteModel,
  SourceControlStatusModel,
} from './sourceControlModels';

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

function apiUrl(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

async function readJson<T>(response: Response, fallback: string): Promise<T> {
  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `${fallback} (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }
  return data as T;
}

function getHeaders(): HeadersInit {
  return { 'X-DocumentIDE-Session-Token': sessionToken() };
}

function postHeaders(): HeadersInit {
  return {
    'Content-Type': 'application/json',
    'X-DocumentIDE-Session-Token': sessionToken(),
  };
}

/** `GET /api/source-control/status` (spec 13 §7/§8). */
export async function fetchSourceControlStatus(path: string): Promise<SourceControlStatusModel> {
  const params = new URLSearchParams({ path });
  const response = await fetch(`${apiUrl()}/api/source-control/status?${params.toString()}`, {
    headers: getHeaders(),
  });
  return readJson<SourceControlStatusModel>(response, 'The source control status request failed');
}

/** `GET /api/source-control/remote` (spec 13 §14.1/§14.2). */
export async function fetchRemoteState(path: string): Promise<SourceControlRemoteModel> {
  const params = new URLSearchParams({ path });
  const response = await fetch(`${apiUrl()}/api/source-control/remote?${params.toString()}`, {
    headers: getHeaders(),
  });
  return readJson<SourceControlRemoteModel>(response, 'The remote state request failed');
}

/** `GET /api/source-control/conflicts` (spec 13 §21). */
export async function fetchConflictState(path: string): Promise<SourceControlConflictModel> {
  const params = new URLSearchParams({ path });
  const response = await fetch(`${apiUrl()}/api/source-control/conflicts?${params.toString()}`, {
    headers: getHeaders(),
  });
  return readJson<SourceControlConflictModel>(response, 'The conflict state request failed');
}

/**
 * `GET /api/source-control/file-version` (spec 13 §12). Read-only and bounded
 * by the API; used here only to materialize the HEAD side of a Markdown diff.
 */
export async function fetchFileVersion(
  path: string,
  file: string,
  commit: string,
): Promise<SourceControlFileVersionResult> {
  const params = new URLSearchParams({ path, file, commit });
  const response = await fetch(
    `${apiUrl()}/api/source-control/file-version?${params.toString()}`,
    { headers: getHeaders() },
  );
  return readJson<SourceControlFileVersionResult>(response, 'The version request failed');
}

/**
 * `GET /api/documents/{documentId}/markdown` (MD-01). The working-tree side of
 * a Markdown diff; the API reads the real file and returns it as inert text.
 */
export async function fetchMarkdownSource(documentId: string): Promise<string> {
  const response = await fetch(
    `${apiUrl()}/api/documents/${encodeURIComponent(documentId)}/markdown`,
    { headers: getHeaders() },
  );
  const data = await readJson<{ documentId: string; source: string }>(
    response,
    'The Markdown source request failed',
  );
  return data.source;
}

// ---------------------------------------------------------------------------
// SCM-18 worktree routes
// ---------------------------------------------------------------------------

export interface WorktreeApiEntry {
  path: string;
  headCommit: string;
  branchRef: string | null;
  branchName: string | null;
  isDetached: boolean;
  isBare: boolean;
  isLocked: boolean;
  lockReason?: string | null;
  isPrunable: boolean;
  pruneReason?: string | null;
  isMainWorktree: boolean;
}

export interface WorktreeListResponse {
  status: string;
  isSuccess: boolean;
  worktrees: WorktreeApiEntry[];
  errorCode: string | null;
  message: string | null;
  failure: SourceControlFailure | null;
}

export interface WorktreeOpenResponse {
  status: string;
  isSuccess: boolean;
  isAttachedToWorkspace: boolean;
  errorCode: string | null;
  message: string | null;
  worktree: WorktreeApiEntry | null;
  worktrees: WorktreeApiEntry[] | null;
  failure: SourceControlFailure | null;
}

export interface WorktreeCreateResponse {
  status: string;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  worktree: WorktreeApiEntry | null;
  worktrees: WorktreeApiEntry[] | null;
  failure: SourceControlFailure | null;
}

export interface WorktreeRemoveChange {
  path: string;
  originalPath: string | null;
  state: string;
  indexChange: string;
  workingTreeChange: string;
  conflictKind: string;
  statusCharacters: string;
}

export interface WorktreeRemoveResponse {
  status: string;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  worktree: WorktreeApiEntry | null;
  changes: WorktreeRemoveChange[];
  worktrees: WorktreeApiEntry[] | null;
  failure: SourceControlFailure | null;
}

export interface WorktreePruneResponse {
  status: string;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  worktrees: WorktreeApiEntry[] | null;
  failure: SourceControlFailure | null;
}

/** `GET /api/source-control/worktrees` (SCM-18). */
export async function fetchWorktrees(path: string): Promise<WorktreeListResponse> {
  const params = new URLSearchParams({ path });
  const response = await fetch(`${apiUrl()}/api/source-control/worktrees?${params.toString()}`, {
    headers: getHeaders(),
  });
  return readJson<WorktreeListResponse>(response, 'The worktree list request failed');
}

/** `POST /api/source-control/worktrees/open` (SCM-18). Never attaches. */
export async function openWorktree(
  workspaceId: string,
  workingDirectory: string,
  worktreePath: string,
): Promise<WorktreeOpenResponse> {
  const response = await fetch(`${apiUrl()}/api/source-control/worktrees/open`, {
    method: 'POST',
    headers: postHeaders(),
    body: JSON.stringify({ workspaceId, workingDirectory, worktreePath }),
  });
  return readJson<WorktreeOpenResponse>(response, 'The worktree open request failed');
}

/** `POST /api/source-control/worktrees` (SCM-18) — existing ref only. */
export async function createWorktree(
  workingDirectory: string,
  targetPath: string,
  ref: string,
): Promise<WorktreeCreateResponse> {
  const response = await fetch(`${apiUrl()}/api/source-control/worktrees`, {
    method: 'POST',
    headers: postHeaders(),
    body: JSON.stringify({ workingDirectory, targetPath, ref }),
  });
  return readJson<WorktreeCreateResponse>(response, 'The worktree create request failed');
}

/** `DELETE /api/source-control/worktrees` (SCM-18) — dirty worktrees refuse. */
export async function removeWorktree(
  workingDirectory: string,
  worktreePath: string,
): Promise<WorktreeRemoveResponse> {
  const response = await fetch(`${apiUrl()}/api/source-control/worktrees`, {
    method: 'DELETE',
    headers: postHeaders(),
    body: JSON.stringify({ workingDirectory, worktreePath }),
  });
  return readJson<WorktreeRemoveResponse>(response, 'The worktree remove request failed');
}

/** `POST /api/source-control/worktrees/prune` (SCM-18) — safe refresh. */
export async function pruneWorktrees(path: string): Promise<WorktreePruneResponse> {
  const response = await fetch(`${apiUrl()}/api/source-control/worktrees/prune`, {
    method: 'POST',
    headers: postHeaders(),
    body: JSON.stringify({ path }),
  });
  return readJson<WorktreePruneResponse>(response, 'The worktree refresh request failed');
}

/** Human-readable typed failure text for a worktree or status response. */
export function typedFailureMessage(
  prefix: string,
  response: { message: string | null; failure: SourceControlFailure | null },
): string {
  if (response.failure) {
    const detail =
      response.failure.errorMessage ?? response.message ?? 'Git reported an unsuccessful outcome.';
    return `${prefix}: ${response.failure.errorKind}: ${detail}`;
  }
  return response.message ? `${prefix}: ${response.message}` : `${prefix}.`;
}
