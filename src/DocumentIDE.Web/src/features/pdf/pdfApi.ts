/**
 * G8 — privileged PDF finalization data source.
 *
 * Wires the previously test-only `PdfActions` UX to the live privileged route
 * `PdfController` (`src/DocumentIDE.Api/Controllers/PdfController.cs`), served
 * by the shipped desktop host. Gated by the same FND-02/QA-03 privileged API
 * guard as the rest of the owner surface (Origin/Host/session token).
 *
 * Contracts (implemented by the privileged host):
 *   POST {apiUrl}/api/pdf/export        -> SinglePdfExportResponse
 *   POST {apiUrl}/api/pdf/export-batch  -> BatchPdfExportResponse
 *   POST {apiUrl}/api/pdf/merge         -> PdfMergeResponse
 *
 * Engine statuses are returned verbatim as readable strings (the controller
 * projects the `PdfExportStatus` enum as a name, never an ordinal), so the UI
 * surfaces a typed refusal instead of a fake success. The engine never
 * silently overwrites an existing PDF: `fail` is the default collision policy.
 */
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

export type PdfStatus =
  | 'Succeeded'
  | 'OutputCollision'
  | 'SourceNotFound'
  | 'SourceInvalid'
  | 'ExportFailed'
  | 'DiskFull'
  | 'Cancelled';

/** Explicit collision policy; omitted ⇒ engine default `fail` (never overwrite). */
export type PdfCollisionBehaviorValue = 'overwrite' | 'rename';

export interface SinglePdfExportRequestDto {
  sourceDocxPath: string;
  outputDirectory?: string;
  outputFileName?: string;
  collisionBehavior?: PdfCollisionBehaviorValue;
}

export interface BatchPdfExportRequestDto {
  sourceDocxPaths: string[];
  outputDirectory: string;
  collisionBehavior?: PdfCollisionBehaviorValue;
}

export interface PdfMergeRequestDto {
  orderedSourcePaths: string[];
  outputPath: string;
  collisionBehavior?: PdfCollisionBehaviorValue;
}

export interface SinglePdfExportResponse {
  status: PdfStatus;
  outputPath: string | null;
  sourceFingerprintBefore: string | null;
  sourceFingerprintAfter: string | null;
  errorMessage: string | null;
  isSuccess: boolean;
}

export interface BatchPdfExportFileResponse {
  sourceDocxPath: string;
  status: PdfStatus;
  outputPath: string | null;
  errorMessage: string | null;
}

export interface BatchPdfExportResponse {
  totalCount: number;
  succeededCount: number;
  skippedCount: number;
  failedCount: number;
  cancelledCount: number;
  isSuccess: boolean;
  summary: string;
  files: BatchPdfExportFileResponse[];
}

export interface PdfMergeResponse {
  status: PdfStatus;
  outputPath: string | null;
  pageCount: number;
  orderedSources: string[];
  errorMessage: string | null;
  isSuccess: boolean;
}

/** Typed backend rejection (400/404/500); carries the controller `errorCode`. */
export class PdfRequestError extends Error {
  readonly status: number;
  readonly errorCode: string;

  constructor(status: number, errorCode: string) {
    super(errorCode);
    this.name = 'PdfRequestError';
    this.status = status;
    this.errorCode = errorCode;
  }
}

async function post<T>(
  path: string,
  body: unknown,
  fetchImpl: typeof fetch,
): Promise<T> {
  const { base, token } = apiBase();
  const response = await fetchImpl(`${base}/api/pdf${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': token,
    },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    let errorCode = `Pdf request failed (${response.status}).`;
    try {
      const parsed = (await response.json()) as { errorCode?: string };
      errorCode = parsed?.errorCode || errorCode;
    } catch {
      /* keep the status-derived fallback */
    }
    throw new PdfRequestError(response.status, errorCode);
  }
  return (await response.json()) as T;
}

/** Single DOCX → PDF. Output defaults to the source folder when omitted. */
export function exportSinglePdf(
  request: SinglePdfExportRequestDto,
  fetchImpl: typeof fetch = fetch,
): Promise<SinglePdfExportResponse> {
  return post<SinglePdfExportResponse>('/export', request, fetchImpl);
}

/** Ordered separate-PDF export; `outputDirectory` is the explicit user choice. */
export function exportSeparatePdfs(
  request: BatchPdfExportRequestDto,
  fetchImpl: typeof fetch = fetch,
): Promise<BatchPdfExportResponse> {
  return post<BatchPdfExportResponse>('/export-batch', request, fetchImpl);
}

/** Ordered DOCX/PDF → single merged PDF at the explicit `outputPath`. */
export function mergeDocumentsToPdf(
  request: PdfMergeRequestDto,
  fetchImpl: typeof fetch = fetch,
): Promise<PdfMergeResponse> {
  return post<PdfMergeResponse>('/merge', request, fetchImpl);
}
