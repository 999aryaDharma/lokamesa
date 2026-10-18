/**
 * UI-05 B — authenticated client for the missing-resource routes.
 *
 * `LocateMovedFileDialog` previously fetched relative URLs without the session
 * token. In the shipped desktop host the page origin is
 * `https://appassets.html2sav` while the privileged API is
 * `http://127.0.0.1:<port>`, so a relative request could never reach it. Every
 * call here uses the host-injected API origin and sends
 * `X-DocumentIDE-Session-Token`, exactly like `OpenInWordAction` /
 * `docxDiffApi`.
 *
 * `removeDocumentReference` maps to `DELETE /api/documents/{id}/reference`,
 * which deletes only the database reference. It never deletes, moves or writes
 * a file.
 */
export interface RebindCandidateDto {
  targetPath: string;
  filename: string;
  sizeBytes: number;
  modifiedAt: string;
  contentFingerprint: string;
  matchScore: number;
  exactFingerprintMatch: boolean;
  sameFilenameMatch: boolean;
  matchReason: string;
  isAmbiguous: boolean;
}

export interface RebindCandidateListDto {
  documentId: string;
  lastKnownPath: string;
  isAmbiguous: boolean;
  candidates: RebindCandidateDto[];
}

export interface RebindResultDto {
  documentId: string;
  previousPath?: string | null;
  newPath?: string | null;
  success: boolean;
  errorMessage?: string | null;
}

export interface RemoveReferenceResultDto {
  status: string;
  documentId: string;
  removed: boolean;
}

interface MissingResourceApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

interface ApiFailure {
  errorCode?: string;
  error?: string;
  detail?: string | null;
}

export class MissingResourceRequestError extends Error {
  readonly status: number;
  readonly errorCode: string;

  constructor(status: number, errorCode: string, message: string) {
    super(message);
    this.name = 'MissingResourceRequestError';
    this.status = status;
    this.errorCode = errorCode;
  }
}

function apiBase(): string {
  return (window as Window & MissingResourceApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & MissingResourceApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

function authHeaders(json: boolean): Record<string, string> {
  return {
    ...(json ? { 'Content-Type': 'application/json' } : {}),
    'X-DocumentIDE-Session-Token': sessionToken(),
  };
}

async function failureFrom(response: Response, fallback: string): Promise<MissingResourceRequestError> {
  let body: ApiFailure = {};
  try {
    body = (await response.json()) as ApiFailure;
  } catch {
    // A non-JSON body still becomes a typed transport error.
  }
  const message = body.error || body.detail || `${fallback} (${response.status})`;
  return new MissingResourceRequestError(
    response.status,
    body.errorCode || 'RequestFailed',
    message,
  );
}

/** `GET /api/documents/{id}/rebind-candidates`. */
export async function fetchRebindCandidates(
  documentId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<RebindCandidateListDto> {
  const path = `/api/documents/${encodeURIComponent(documentId)}/rebind-candidates`;
  const response = await fetchImpl(`${apiBase()}${path}`, {
    headers: authHeaders(false),
  });

  if (!response.ok) {
    throw await failureFrom(response, 'Failed to fetch candidates');
  }

  return (await response.json()) as RebindCandidateListDto;
}

/** `POST /api/documents/{id}/rebind` — an explicit, confirmed rebind. */
export async function rebindDocument(
  documentId: string,
  targetPath: string,
  fetchImpl: typeof fetch = fetch,
): Promise<RebindResultDto> {
  const path = `/api/documents/${encodeURIComponent(documentId)}/rebind`;
  const response = await fetchImpl(`${apiBase()}${path}`, {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify({ targetPath, forceRebind: true }),
  });

  if (!response.ok) {
    throw await failureFrom(response, 'Failed to rebind document path');
  }

  return (await response.json()) as RebindResultDto;
}

/**
 * `DELETE /api/documents/{id}/reference`.
 *
 * Removes the database reference only. The caller must treat this as a
 * reference removal, not a file deletion.
 */
export async function removeDocumentReference(
  documentId: string,
  fetchImpl: typeof fetch = fetch,
): Promise<RemoveReferenceResultDto> {
  const path = `/api/documents/${encodeURIComponent(documentId)}/reference`;
  const response = await fetchImpl(`${apiBase()}${path}`, {
    method: 'DELETE',
    headers: authHeaders(false),
  });

  if (!response.ok) {
    throw await failureFrom(response, 'Failed to remove the document reference');
  }

  return (await response.json()) as RemoveReferenceResultDto;
}
