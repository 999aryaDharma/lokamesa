/**
 * UI-04 screen 06 — typed client + view-model mapper for the SCM-19
 * `POST /api/source-control/docx-diff` route.
 *
 * The route is strictly read-only (spec 13 §9.2/§9.3): it materializes the
 * compared document versions as temporary read-only artifacts, classifies the
 * change structurally and deletes the artifacts before responding. This module
 * only parses that typed contract and maps it onto `DocxDiffPanel`'s view
 * model. It never invents a change, a paragraph, a table cell or a digest:
 *
 *   - an unknown status or change class is a typed failure, not a row;
 *   - a missing report body is a typed failure, not an empty diff;
 *   - per-kind strings are the API's serialized enum names (PascalCase) and are
 *     the only vocabulary accepted here.
 */

import type {
  DocxBinarySummaryView,
  DocxChangeKindView,
  DocxChangeView,
  DocxDiffStatusView,
  DocxDiffTargetView,
  DocxDiffView,
} from './DocxDiffPanel';
import type { SourceControlChangeEntry, SourceControlFailure } from './sourceControlModels';

interface DocxDiffApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

/** The version pair the route compares (`GitDiffTarget`). */
export type DocxDiffApiTarget = 'Staged' | 'WorkingTree' | 'Unstaged';

export interface DocxDiffApiChange {
  kind: string;
  origin: string;
  partName: string;
  summary: string;
  before: string | null;
  after: string | null;
  property: string | null;
  oldParagraphIndex: number | null;
  newParagraphIndex: number | null;
  runIndex: number | null;
  tableIndex: number | null;
  rowIndex: number | null;
  cellIndex: number | null;
}

export interface DocxDiffApiBinarySummary {
  leftExists: boolean;
  rightExists: boolean;
  leftSizeBytes: number;
  rightSizeBytes: number;
  leftSha256: string | null;
  rightSha256: string | null;
  contentIdentical: boolean;
}

export interface DocxDiffApiReport {
  target: string;
  path: string;
  worktreeRoot: string;
  leftLabel: string;
  rightLabel: string;
  leftExists: boolean;
  rightExists: boolean;
  changeCount: number;
  hasChanges: boolean;
  changes: DocxDiffApiChange[];
  binarySummary: DocxDiffApiBinarySummary | null;
}

/** `SourceControlDocxDiffResponse` (SCM-19). */
export interface DocxDiffApiResponse {
  status: string;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  report: DocxDiffApiReport | null;
  failure: SourceControlFailure | null;
}

/** What `DocxDiffPanel` renders: typed status + typed view model. */
export interface DocxDiffViewState {
  status: DocxDiffStatusView;
  diff: DocxDiffView | null;
  failureMessage: string | null;
}

const statusByApiName: Record<string, DocxDiffStatusView> = {
  Available: 'available',
  BinaryFallback: 'binary-fallback',
  NoRepository: 'no-repository',
  GitUnavailable: 'git-unavailable',
  PathRejected: 'path-rejected',
  Failed: 'failed',
};

const kindByApiName: Record<string, DocxChangeKindView> = {
  ParagraphAdded: 'paragraph-added',
  ParagraphRemoved: 'paragraph-removed',
  ParagraphTextChanged: 'paragraph-text-changed',
  HeadingChanged: 'heading-changed',
  TableCellAdded: 'table-cell-added',
  TableCellRemoved: 'table-cell-removed',
  TableCellChanged: 'table-cell-changed',
  FormattingChanged: 'formatting-changed',
  MetadataChanged: 'metadata-changed',
};

const targetByApiName: Record<string, DocxDiffTargetView> = {
  Staged: 'staged',
  WorkingTree: 'working-tree',
  Unstaged: 'unstaged',
};

/**
 * Which adjacent version pair screen 06 should compare for one Git entry.
 *
 * This mirrors the SCM-05 contract (spec 13 §9.2) using only the fields Git
 * reported:
 *   - an untracked document has no HEAD/index version → HEAD ↔ working tree;
 *   - any index change (including an unmerged index) → HEAD ↔ index, which is
 *     also where an unmerged path surfaces its typed conflict;
 *   - otherwise → index ↔ working tree.
 */
export function docxDiffTargetFor(entry: SourceControlChangeEntry): DocxDiffApiTarget {
  if (entry.state === 'Untracked') {
    return 'WorkingTree';
  }
  if (entry.conflictKind !== 'None' || entry.indexChange !== 'Unmodified') {
    return 'Staged';
  }
  if (entry.workingTreeChange !== 'Unmodified') {
    return 'Unstaged';
  }
  return 'WorkingTree';
}

function apiUrl(): string {
  return (window as Window & DocxDiffApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & DocxDiffApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

function messageOf(response: DocxDiffApiResponse): string | null {
  return response.message ?? response.failure?.errorMessage ?? null;
}

function failureState(message: string): DocxDiffViewState {
  return { status: 'failed', diff: null, failureMessage: message };
}

function mapChange(change: DocxDiffApiChange): DocxChangeView | null {
  const kind = kindByApiName[change.kind];
  if (!kind) {
    return null;
  }
  return {
    kind,
    summary: change.summary,
    before: change.before,
    after: change.after,
    property: change.property,
    oldParagraphIndex: change.oldParagraphIndex,
    newParagraphIndex: change.newParagraphIndex,
    runIndex: change.runIndex,
    tableIndex: change.tableIndex,
    rowIndex: change.rowIndex,
    cellIndex: change.cellIndex,
  };
}

function mapBinarySummary(summary: DocxDiffApiBinarySummary): DocxBinarySummaryView {
  return {
    leftExists: summary.leftExists,
    rightExists: summary.rightExists,
    leftSizeBytes: summary.leftSizeBytes,
    rightSizeBytes: summary.rightSizeBytes,
    leftSha256: summary.leftSha256,
    rightSha256: summary.rightSha256,
    contentIdentical: summary.contentIdentical,
  };
}

/**
 * Maps the route's typed payload onto the panel view model. Everything that
 * cannot be mapped exactly becomes a typed failure so the panel never renders
 * a partial or fabricated report.
 */
export function toDocxDiffViewState(response: DocxDiffApiResponse): DocxDiffViewState {
  const status = statusByApiName[response.status];
  if (!status) {
    return failureState(
      `The document diff returned an unknown status '${response.status}'; no report is shown.`,
    );
  }

  if (status !== 'available' && status !== 'binary-fallback') {
    return { status, diff: null, failureMessage: messageOf(response) };
  }

  const report = response.report;
  if (!report) {
    return failureState(
      messageOf(response) ??
        'The document diff response carried no document report; no changes are shown.',
    );
  }

  const target = targetByApiName[report.target];
  if (!target) {
    return failureState(
      `The document diff returned an unknown version pair '${report.target}'; no report is shown.`,
    );
  }

  const changes: DocxChangeView[] = [];
  for (const change of report.changes) {
    if (!kindByApiName[change.kind]) {
      return failureState(
        `The document diff returned an unknown change class '${change.kind}'; no report is shown.`,
      );
    }
    changes.push(mapChange(change)!);
  }

  return {
    status,
    diff: {
      target,
      path: report.path,
      leftLabel: report.leftLabel,
      rightLabel: report.rightLabel,
      leftExists: report.leftExists,
      rightExists: report.rightExists,
      changes,
      binarySummary: report.binarySummary ? mapBinarySummary(report.binarySummary) : null,
    },
    failureMessage: messageOf(response),
  };
}

/**
 * `POST /api/source-control/docx-diff` (SCM-19).
 *
 * `worktreeRoot` must be the true worktree root reported by
 * `GET /api/source-control/status` (`worktreeRoot`), never a discovered
 * workspace path; `documentPath` is the worktree-relative path exactly as Git
 * reported it.
 */
export async function fetchDocxDiff(
  worktreeRoot: string,
  documentPath: string,
  target: DocxDiffApiTarget,
  originalPath: string | null,
): Promise<DocxDiffApiResponse> {
  const response = await fetch(`${apiUrl()}/api/source-control/docx-diff`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': sessionToken(),
    },
    body: JSON.stringify({
      path: worktreeRoot,
      documentPath,
      target,
      originalPath,
    }),
  });

  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `The document diff request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }
  return data as DocxDiffApiResponse;
}
