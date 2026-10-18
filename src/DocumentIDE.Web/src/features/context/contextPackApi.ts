/**
 * UI-05 A — typed client for `POST /api/context/pack`.
 *
 * The route builds the pack from the explicit ordered selection and returns the
 * combined Markdown plus the existing `ContextExportService` estimate. This
 * module never invents a source, a character count or an estimate: a failed
 * request is a typed error, and a DOCX item carries the route's explicit
 * "extracted text" content label.
 *
 * Transport follows the shipped privileged-client pattern
 * (`OpenInWordAction`, `docxDiffApi`): the host-injected API origin plus the
 * `X-DocumentIDE-Session-Token` header. Relative URLs cannot reach the loopback
 * API from the desktop page origin `https://appassets.html2sav`.
 */
import type { ContextSourceItem } from './ContextPackDialog';

interface ContextPackApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

export interface ContextPackApiDocument {
  documentId: string;
  title: string;
  /** `markdown-source` or `extracted-docx-text`. */
  textSource: string;
  contentLabel: string;
  content: string;
  characterCount: number;
  byteCount: number;
}

export interface ContextPackApiContribution {
  sourceId: string;
  titleOrPath: string;
  characterCount: number;
  wordCount: number;
  estimatedTokens: number;
  percentageOfTotal: number;
}

export interface ContextPackApiEstimate {
  modelLabel: string;
  totalEstimatedTokens: number;
  totalCharacterCount: number;
  tokenBudget: number | null;
  exceedsBudget: boolean;
  warningMessage: string | null;
  sourceContributions: ContextPackApiContribution[];
}

export interface ContextPackApiSuccess {
  status: 'Ok';
  documents: ContextPackApiDocument[];
  markdown: string;
  tokenEstimate: ContextPackApiEstimate;
}

interface ContextPackApiFailure {
  status?: string;
  errorCode?: string;
  documentId?: string | null;
  detail?: string | null;
  tokenEstimate?: ContextPackApiEstimate | null;
}

/** Typed refusal from the pack route. Never carries partial content. */
export class ContextPackRequestError extends Error {
  readonly status: number;
  readonly errorCode: string;
  readonly documentId: string | null;
  readonly detail: string | null;

  constructor(
    status: number,
    errorCode: string,
    detail: string | null,
    documentId: string | null = null,
  ) {
    super(detail || errorCode);
    this.name = 'ContextPackRequestError';
    this.status = status;
    this.errorCode = errorCode;
    this.documentId = documentId;
    this.detail = detail;
  }
}

function apiBase(): string {
  return (window as Window & ContextPackApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & ContextPackApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

export interface FetchContextPackOptions {
  /** Optional target budget; the route refuses typed when exceeded. */
  tokenBudget?: number;
  fetchImpl?: typeof fetch;
}

/**
 * `POST /api/context/pack` over the explicit ordered document ids.
 */
export async function fetchContextPack(
  documentIds: readonly string[],
  options: FetchContextPackOptions = {},
): Promise<ContextPackApiSuccess> {
  const fetchImpl = options.fetchImpl ?? fetch;
  const response = await fetchImpl(`${apiBase()}/api/context/pack`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': sessionToken(),
    },
    body: JSON.stringify({
      documentIds: [...documentIds],
      tokenBudget: options.tokenBudget ?? null,
    }),
  });

  const data = (await response.json()) as ContextPackApiSuccess | ContextPackApiFailure;

  if (!response.ok) {
    const failure = data as ContextPackApiFailure;
    throw new ContextPackRequestError(
      response.status,
      failure.errorCode || 'ContextPackFailed',
      failure.detail ?? null,
      failure.documentId ?? null,
    );
  }

  return data as ContextPackApiSuccess;
}

/**
 * Maps the route's ordered documents onto the dialog's source rows. The
 * content is exactly what the route returned (a DOCX item already carries its
 * extracted-text label), so copying can never silently drop the provenance.
 */
export function toContextSources(pack: ContextPackApiSuccess): ContextSourceItem[] {
  return pack.documents.map((document) => ({
    id: document.documentId,
    title: document.title,
    content: document.content,
  }));
}
