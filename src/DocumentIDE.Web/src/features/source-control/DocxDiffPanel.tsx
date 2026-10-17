/**
 * SCM-05 — read-only review surface for one DOCX file's document-aware Git
 * diff (spec 13 §9.2/§9.3).
 *
 * This is a pure presentation component: it renders the typed report that the
 * SCM-05 provider produced (staged HEAD↔index, working-tree HEAD↔working tree,
 * or unstaged index↔working tree). It never invokes Git, never writes to the
 * repository, never stages partial content and never interprets file content
 * as HTML: React escapes every value, so a document containing `<script>` or
 * `<img>` markup is shown as inert text (no `dangerouslySetInnerHTML`).
 *
 * A version that cannot be parsed as a DOCX degrades to a binary-change
 * summary (sizes + SHA-256 digests) instead of a faked semantic diff, and a
 * missing side (new/deleted document) is shown as `/dev/null`.
 */

/** Spec 13 §9.2 — the three version pairs the UI must distinguish. */
export type DocxDiffTargetView = 'staged' | 'working-tree' | 'unstaged';

/** Typed outcome of one document diff request (mirrors the Core result). */
export type DocxDiffStatusView =
  | 'available'
  | 'binary-fallback'
  | 'no-repository'
  | 'git-unavailable'
  | 'path-rejected'
  | 'failed';

/** The document-aware change vocabulary (mirrors the Core change kinds). */
export type DocxChangeKindView =
  | 'paragraph-added'
  | 'paragraph-removed'
  | 'paragraph-text-changed'
  | 'heading-changed'
  | 'table-cell-added'
  | 'table-cell-removed'
  | 'table-cell-changed'
  | 'formatting-changed'
  | 'metadata-changed';

export interface DocxChangeView {
  kind: DocxChangeKindView;
  /** Deterministic human-readable description produced by the backend. */
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

/** Binary-change summary for versions that cannot be compared as documents. */
export interface DocxBinarySummaryView {
  leftExists: boolean;
  rightExists: boolean;
  leftSizeBytes: number;
  rightSizeBytes: number;
  leftSha256: string | null;
  rightSha256: string | null;
  contentIdentical: boolean;
}

export interface DocxDiffView {
  target: DocxDiffTargetView;
  path: string;
  leftLabel: string;
  rightLabel: string;
  leftExists: boolean;
  rightExists: boolean;
  changes: DocxChangeView[];
  binarySummary: DocxBinarySummaryView | null;
}

interface DocxDiffPanelProps {
  status: DocxDiffStatusView;
  diff?: DocxDiffView | null;
  /** Typed failure message (already safe to display; never raw Git output dump). */
  failureMessage?: string | null;
  isLoading?: boolean;
}

const changeKindLabel: Record<DocxChangeKindView, string> = {
  'paragraph-added': 'Paragraph added',
  'paragraph-removed': 'Paragraph removed',
  'paragraph-text-changed': 'Paragraph text changed',
  'heading-changed': 'Heading changed',
  'table-cell-added': 'Table cell added',
  'table-cell-removed': 'Table cell removed',
  'table-cell-changed': 'Table cell changed',
  'formatting-changed': 'Formatting changed',
  'metadata-changed': 'Metadata changed',
};

const addedKinds: ReadonlySet<DocxChangeKindView> = new Set([
  'paragraph-added',
  'table-cell-added',
]);

const removedKinds: ReadonlySet<DocxChangeKindView> = new Set([
  'paragraph-removed',
  'table-cell-removed',
]);

function panelClass(extra: string): string {
  return `w-full max-w-3xl p-4 text-sm bg-white border border-slate-200 rounded-xl shadow-sm ${extra}`;
}

function changeRowClass(kind: DocxChangeKindView): string {
  if (addedKinds.has(kind)) {
    return 'bg-emerald-50';
  }
  if (removedKinds.has(kind)) {
    return 'bg-red-50';
  }
  return 'bg-amber-50';
}

/** Builds the change location line from whatever index fields the backend set. */
function locationOf(change: DocxChangeView): string | null {
  const parts: string[] = [];

  if (change.property) {
    parts.push(change.property);
  }

  const { oldParagraphIndex, newParagraphIndex } = change;
  if (oldParagraphIndex !== null || newParagraphIndex !== null) {
    parts.push(
      oldParagraphIndex !== null && newParagraphIndex !== null && oldParagraphIndex !== newParagraphIndex
        ? `paragraphs ${oldParagraphIndex} → ${newParagraphIndex}`
        : `paragraph ${newParagraphIndex ?? oldParagraphIndex}`
    );
  }

  if (change.tableIndex !== null || change.rowIndex !== null || change.cellIndex !== null) {
    parts.push(
      `table ${change.tableIndex ?? '?'}, row ${change.rowIndex ?? '?'}, cell ${change.cellIndex ?? '?'}`
    );
  }

  if (change.runIndex !== null) {
    parts.push(`run ${change.runIndex}`);
  }

  return parts.length > 0 ? parts.join(' · ') : null;
}

function describeVersion(
  exists: boolean,
  label: string,
  path: string,
): string {
  return exists ? `${label}:${path}` : '/dev/null';
}

function changeCountLabel(count: number): string {
  return count === 1 ? '1 document change' : `${count} document changes`;
}

function binarySide(
  label: string,
  exists: boolean,
  sizeBytes: number,
  sha256: string | null,
): string {
  if (!exists) {
    return `${label}: /dev/null`;
  }

  return `${label}: ${sizeBytes} bytes, sha256 ${sha256 ?? 'unavailable'}`;
}

export default function DocxDiffPanel({
  status,
  diff,
  failureMessage,
  isLoading = false,
}: DocxDiffPanelProps) {
  if (isLoading) {
    return (
      <div data-testid="docx-diff-loading" className={panelClass('text-slate-500')}>
        Loading document changes...
      </div>
    );
  }

  if (status === 'git-unavailable') {
    return (
      <div data-testid="docx-diff-git-unavailable" className={panelClass('text-slate-600')}>
        Git is not available on this machine. Install Git to review document changes.
      </div>
    );
  }

  if (status === 'no-repository') {
    return (
      <div data-testid="docx-diff-no-repository" className={panelClass('text-slate-600')}>
        This folder is not inside a Git repository.
      </div>
    );
  }

  if (status !== 'available' && status !== 'binary-fallback') {
    return (
      <div role="alert" className={panelClass('text-red-700 bg-red-50 border-red-200')}>
        {failureMessage ??
          (status === 'path-rejected'
            ? 'The requested path was rejected because it is not a worktree-relative document path.'
            : 'The document changes could not be produced.')}
      </div>
    );
  }

  if (!diff) {
    return (
      <div role="alert" className={panelClass('text-red-700 bg-red-50 border-red-200')}>
        {failureMessage ?? 'No document diff data was provided.'}
      </div>
    );
  }

  if (status === 'binary-fallback') {
    const summary = diff.binarySummary;
    return (
      <div data-testid="docx-diff-binary" className={panelClass('text-slate-600 space-y-2')}>
        <p className="font-medium text-slate-700">Binary change</p>
        <p>
          {diff.path} differs between {diff.leftLabel} and {diff.rightLabel}; at least one
          version is not a parseable DOCX document, so no document-aware comparison is shown.
        </p>
        {summary && (
          <div
            data-testid="docx-diff-binary-summary"
            className="font-mono text-xs text-slate-700 space-y-1"
          >
            {summary.contentIdentical ? (
              <p>
                Both versions are byte-identical ({summary.leftSizeBytes} bytes, sha256{' '}
                {summary.leftSha256 ?? 'unavailable'}).
              </p>
            ) : (
              <>
                <p>{binarySide(diff.leftLabel, summary.leftExists, summary.leftSizeBytes, summary.leftSha256)}</p>
                <p>{binarySide(diff.rightLabel, summary.rightExists, summary.rightSizeBytes, summary.rightSha256)}</p>
              </>
            )}
          </div>
        )}
        {failureMessage && (
          <p data-testid="docx-diff-binary-reason" className="text-xs text-slate-500">
            {failureMessage}
          </p>
        )}
      </div>
    );
  }

  if (diff.changes.length === 0) {
    return (
      <div data-testid="docx-diff-empty" className={panelClass('text-slate-500')}>
        No document changes between {diff.leftLabel} and {diff.rightLabel} for {diff.path}.
      </div>
    );
  }

  return (
    <div data-testid="docx-diff" className="w-full max-w-3xl space-y-2">
      <div data-testid="docx-diff-target" className="text-sm font-medium text-slate-700">
        {diff.leftLabel} ↔ {diff.rightLabel}
      </div>

      <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden">
        <div
          data-testid="docx-diff-file-header"
          className="px-3 py-2 bg-slate-50 border-b border-slate-200 font-mono text-xs text-slate-700"
        >
          <div>
            {describeVersion(diff.leftExists, diff.leftLabel, diff.path)} →{' '}
            {describeVersion(diff.rightExists, diff.rightLabel, diff.path)}
          </div>
        </div>

        <div
          data-testid="docx-diff-summary"
          className="px-3 py-2 text-xs text-slate-500 border-b border-slate-100"
        >
          {changeCountLabel(diff.changes.length)}
        </div>

        <div className="divide-y divide-slate-100">
          {diff.changes.map((change, index) => {
            const location = locationOf(change);
            return (
              <div
                key={index}
                data-testid={`docx-diff-change-${index}`}
                className={`px-3 py-2 space-y-1 ${changeRowClass(change.kind)}`}
              >
                <div
                  data-testid={`docx-diff-kind-${index}`}
                  className="font-medium text-slate-800"
                >
                  {changeKindLabel[change.kind]}
                </div>
                {location && (
                  <div
                    data-testid={`docx-diff-location-${index}`}
                    className="text-xs text-slate-500"
                  >
                    {location}
                  </div>
                )}
                <div data-testid={`docx-diff-text-${index}`} className="text-xs text-slate-600">
                  {change.summary}
                </div>
                {change.before !== null && (
                  <div data-testid={`docx-diff-before-${index}`} className="font-mono text-xs text-red-800 whitespace-pre-wrap break-words">
                    Before: {change.before}
                  </div>
                )}
                {change.after !== null && (
                  <div data-testid={`docx-diff-after-${index}`} className="font-mono text-xs text-emerald-800 whitespace-pre-wrap break-words">
                    After: {change.after}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
