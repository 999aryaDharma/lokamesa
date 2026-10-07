/**
 * DOCX-04 — Document Lens Metadata: right-side DOCX inspector.
 *
 * Displays derived DOCX metadata (counts and summaries) without opening Word.
 * Summaries are labeled as summaries in the UI copy because they are derived
 * values, not source facts.
 */
import React, { useCallback } from 'react';

export type DocxMetadataStatus = 'ready' | 'stale' | 'error';

/** Filter fields supported by the SRCH-03 metadata filters (see MetadataFilterCompiler). */
export type DocxMetadataFilterField = 'paper' | 'margin' | 'font' | 'font-size' | 'heading';

export interface DocxMetadataFilterRequest {
  field: DocxMetadataFilterField;
  value: string;
}

export interface DocxLensHeading {
  level: number;
  text: string;
}

export interface DocxLensMetadataDto {
  documentId?: string;
  filename: string;
  filePath?: string | null;
  modifiedAt?: string | null;
  sizeBytes?: number | null;

  pageCount?: number | null;
  sectionCount?: number | null;
  tableCount?: number | null;
  imageCount?: number | null;

  paperSummary?: string | null;
  orientationSummary?: string | null;
  marginSummary?: string | null;
  primaryFontSummary?: string | null;
  primaryFontSizeSummary?: string | null;

  headings: DocxLensHeading[];
  headerPresent: boolean;
  footerPresent: boolean;

  status?: DocxMetadataStatus;
  errorCode?: string | null;
  errorMessage?: string | null;
}

export interface DocxInspectorProps {
  metadata: DocxLensMetadataDto | null;
  loading?: boolean;
  /**
   * Invoked when the user clicks a metadata value that maps to a supported
   * SRCH-03 metadata filter. Unsupported values stay plain text.
   */
  onApplyMetadataFilter?: (filter: DocxMetadataFilterRequest) => void;
}

function formatCount(count: number | null | undefined, noun: string): string | null {
  if (count === null || count === undefined) return null;
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}

function formatFileSize(bytes: number | null | undefined): string | null {
  if (bytes === null || bytes === undefined) return null;
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * Renders an ISO-8601 timestamp deterministically in UTC without depending on
 * the test/browser locale: "2026-01-31T08:15:00Z" -> "2026-01-31 08:15 UTC".
 */
function formatModifiedAt(value: string | null | undefined): string | null {
  if (!value) return null;
  const match = value.match(/^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/);
  return match ? `${match[1]} ${match[2]} UTC` : value;
}

/**
 * SRCH-03 font-size filters compare numeric points (e.g. `font-size:>12`), so a
 * display summary such as "12 pt" is normalized before it is offered as a filter.
 * Returns null when the summary has no single numeric size.
 */
export function toFontSizeFilterValue(summary: string | null | undefined): string | null {
  if (!summary) return null;
  const match = summary.match(/-?\d+(?:\.\d+)?/);
  return match ? match[0] : null;
}

interface SummaryRowProps {
  label: string;
  testId?: string;
  filterTestId?: string;
  value: string | null | undefined;
  filter?: DocxMetadataFilterRequest | null;
  onApplyMetadataFilter?: (filter: DocxMetadataFilterRequest) => void;
}

const SummaryRow: React.FC<SummaryRowProps> = ({
  label,
  testId,
  filterTestId,
  value,
  filter,
  onApplyMetadataFilter,
}) => {
  const handleClick = useCallback(() => {
    if (filter && onApplyMetadataFilter) {
      onApplyMetadataFilter(filter);
    }
  }, [filter, onApplyMetadataFilter]);

  const isClickable = Boolean(filter && onApplyMetadataFilter);

  return (
    <div className="summary-row flex justify-between gap-2">
      <span className="text-slate-500 dark:text-slate-400">{label}</span>
      {isClickable ? (
        <button
          type="button"
          data-testid={filterTestId}
          onClick={handleClick}
          title={`Filter by ${label.replace(' (summary)', '').toLowerCase()}`}
          className="font-medium text-blue-700 dark:text-blue-300 hover:underline cursor-pointer text-right"
        >
          {value ?? 'Not available'}
        </button>
      ) : (
        <span data-testid={testId} className="font-medium text-right">
          {value ?? 'Not available'}
        </span>
      )}
    </div>
  );
};

export const DocxInspector: React.FC<DocxInspectorProps> = ({
  metadata,
  loading = false,
  onApplyMetadataFilter,
}) => {
  const handleHeadingClick = useCallback(
    (text: string) => {
      if (onApplyMetadataFilter) {
        onApplyMetadataFilter({ field: 'heading', value: text });
      }
    },
    [onApplyMetadataFilter],
  );

  if (loading) {
    return <div className="docx-inspector-loading">Loading document metadata...</div>;
  }

  if (!metadata) {
    return <div className="docx-inspector-empty">No document selected.</div>;
  }

  if (metadata.status === 'error') {
    return (
      <aside
        data-testid="docx-inspector"
        aria-label="DOCX inspector"
        className="docx-inspector border-l border-slate-200 dark:border-slate-800 p-4 space-y-4 text-sm w-full max-w-sm"
      >
        <header className="border-b border-slate-200 dark:border-slate-800 pb-2">
          <h2 className="text-base font-semibold truncate">{metadata.filename}</h2>
        </header>
        <div className="docx-inspector-error rounded-md bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 p-3 space-y-1">
          <p className="font-semibold text-red-700 dark:text-red-300">Metadata unavailable</p>
          {metadata.errorCode && (
            <code className="block text-[11px] text-red-600 dark:text-red-400">{metadata.errorCode}</code>
          )}
          {metadata.errorMessage && (
            <p className="text-xs text-red-700 dark:text-red-300">{metadata.errorMessage}</p>
          )}
        </div>
      </aside>
    );
  }

  const counts = [
    formatCount(metadata.pageCount, 'page'),
    formatCount(metadata.sectionCount, 'section'),
    formatCount(metadata.tableCount, 'table'),
    formatCount(metadata.imageCount, 'image'),
  ].filter((value): value is string => value !== null);

  const fontSizeFilterValue = toFontSizeFilterValue(metadata.primaryFontSizeSummary);
  const fileSizeText = formatFileSize(metadata.sizeBytes);
  const modifiedText = formatModifiedAt(metadata.modifiedAt);

  return (
    <aside
      data-testid="docx-inspector"
      aria-label="DOCX inspector"
      className="docx-inspector border-l border-slate-200 dark:border-slate-800 p-4 space-y-4 text-sm w-full max-w-sm"
    >
      <header className="border-b border-slate-200 dark:border-slate-800 pb-2">
        <h2 className="text-base font-semibold truncate">{metadata.filename}</h2>
        {metadata.filePath && (
          <p className="text-[11px] text-slate-400 dark:text-slate-500 break-all">{metadata.filePath}</p>
        )}
        {(fileSizeText || modifiedText) && (
          <p className="flex gap-2 text-[11px] text-slate-400 dark:text-slate-500 mt-0.5">
            {fileSizeText && <span>{fileSizeText}</span>}
            {modifiedText && <span>{modifiedText}</span>}
          </p>
        )}
        {metadata.status === 'stale' && (
          <p className="docx-inspector-stale text-[11px] text-amber-700 dark:text-amber-400 mt-1">
            Derived metadata may be stale
          </p>
        )}
      </header>

      <section className="docx-inspector-counts flex flex-wrap gap-x-3 gap-y-1 text-slate-700 dark:text-slate-300">
        {counts.map((count) => (
          <span key={count} className="font-medium">
            {count}
          </span>
        ))}
      </section>

      <section className="docx-inspector-summaries space-y-1.5">
        <SummaryRow
          label="Paper (summary)"
          value={metadata.paperSummary}
          filterTestId="docx-filter-paper"
          filter={metadata.paperSummary ? { field: 'paper', value: metadata.paperSummary } : null}
          onApplyMetadataFilter={onApplyMetadataFilter}
        />
        <SummaryRow
          label="Orientation (summary)"
          value={metadata.orientationSummary}
          testId="docx-orientation-value"
        />
        <SummaryRow
          label="Margins (summary)"
          value={metadata.marginSummary}
          filterTestId="docx-filter-margin"
          filter={metadata.marginSummary ? { field: 'margin', value: metadata.marginSummary } : null}
          onApplyMetadataFilter={onApplyMetadataFilter}
        />
        <SummaryRow
          label="Main font (summary)"
          value={metadata.primaryFontSummary}
          filterTestId="docx-filter-font"
          filter={metadata.primaryFontSummary ? { field: 'font', value: metadata.primaryFontSummary } : null}
          onApplyMetadataFilter={onApplyMetadataFilter}
        />
        <SummaryRow
          label="Main size (summary)"
          value={metadata.primaryFontSizeSummary}
          filterTestId="docx-filter-font-size"
          filter={
            fontSizeFilterValue ? { field: 'font-size', value: fontSizeFilterValue } : null
          }
          onApplyMetadataFilter={onApplyMetadataFilter}
        />
        <div className="summary-row flex justify-between gap-2">
          <span className="text-slate-500 dark:text-slate-400">Header</span>
          <span data-testid="docx-header-state" className="font-medium">
            {metadata.headerPresent ? 'Present' : 'None'}
          </span>
        </div>
        <div className="summary-row flex justify-between gap-2">
          <span className="text-slate-500 dark:text-slate-400">Footer</span>
          <span data-testid="docx-footer-state" className="font-medium">
            {metadata.footerPresent ? 'Present' : 'None'}
          </span>
        </div>
      </section>

      <section className="docx-inspector-headings">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400 mb-1">
          Detected headings ({metadata.headings.length})
        </h3>
        {metadata.headings.length === 0 ? (
          <p className="text-xs text-slate-400 italic">No headings detected</p>
        ) : (
          <ul className="space-y-1">
            {metadata.headings.map((heading, index) => (
              <li
                key={`${heading.level}-${index}-${heading.text}`}
                className="truncate"
                style={{ paddingLeft: `${(heading.level - 1) * 12}px` }}
              >
                {onApplyMetadataFilter ? (
                  <button
                    type="button"
                    data-testid={`docx-filter-heading-${index}`}
                    onClick={() => handleHeadingClick(heading.text)}
                    title="Filter by heading"
                    className="hover:underline cursor-pointer text-blue-700 dark:text-blue-300"
                  >
                    {heading.text}
                  </button>
                ) : (
                  heading.text
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </aside>
  );
};

export default DocxInspector;
