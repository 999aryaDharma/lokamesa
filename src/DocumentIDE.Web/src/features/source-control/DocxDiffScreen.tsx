import { useEffect, useMemo, useState } from 'react';
import { openInWord } from '../files/OpenInWordAction';
import InspectorCard, { InspectorField } from '../shell/InspectorCard';
import DocxDiffPanel from './DocxDiffPanel';
import {
  docxDiffTargetFor,
  fetchDocxDiff,
  toDocxDiffViewState,
  type DocxDiffViewState,
} from './docxDiffApi';
import type { SourceControlChangeEntry } from './sourceControlModels';

/**
 * UI-03/UI-04 screen 06 — DOCX document-aware diff.
 *
 * A read-only report around the shipped `DocxDiffPanel`, driven by the real
 * SCM-19 `POST /api/source-control/docx-diff` route. The design's change-class
 * chips map to our change classes; the paragraph before → after blocks, table
 * cells and formatting summaries are the engine's exact report. Nothing is
 * inferred client-side:
 *
 *   - `path` is the **true worktree root** from `GET status` (`worktreeRoot`),
 *     never a discovered workspace path;
 *   - `BinaryFallback` renders its typed size/SHA summary, never change rows;
 *   - every typed failure renders its typed state, never a fabricated list;
 *   - no page image is claimed (WORD-04 remains unimplemented).
 *
 * DOCX deep editing stays in Microsoft Word: the primary action is the
 * existing `openInWord` path and it is unchanged.
 */

export interface DocxDiffScreenProps {
  entry: SourceControlChangeEntry;
  /** The true worktree root reported by `GET /api/source-control/status`. */
  worktreeRoot: string | null;
  onBack: () => void;
  inspectorOpen: boolean;
}

interface DiffLoadState {
  loading: boolean;
  view: DocxDiffViewState | null;
}

function basename(path: string): string {
  const parts = path.replace(/\\/g, '/').split('/').filter(Boolean);
  return parts.length > 0 ? parts[parts.length - 1] : path;
}

function folderOf(path: string): string {
  const normalized = path.replace(/\\/g, '/');
  const index = normalized.lastIndexOf('/');
  return index > 0 ? normalized.slice(0, index) : '—';
}

function errorMessageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'The document diff request failed.';
}

interface ChangeClassCounts {
  text: number;
  table: number;
  formatting: number;
  metadata: number;
}

/** Derived from the engine's change classes only — no synthesized counts. */
function countChangeClasses(state: DocxDiffViewState | null): ChangeClassCounts | null {
  if (!state || state.status !== 'available' || !state.diff) return null;
  const counts: ChangeClassCounts = { text: 0, table: 0, formatting: 0, metadata: 0 };
  for (const change of state.diff.changes) {
    switch (change.kind) {
      case 'paragraph-added':
      case 'paragraph-removed':
      case 'paragraph-text-changed':
      case 'heading-changed':
        counts.text += 1;
        break;
      case 'table-cell-added':
      case 'table-cell-removed':
      case 'table-cell-changed':
        counts.table += 1;
        break;
      case 'formatting-changed':
        counts.formatting += 1;
        break;
      case 'metadata-changed':
        counts.metadata += 1;
        break;
    }
  }
  return counts;
}

function plural(count: number, singular: string, pluralForm: string): string {
  return `${count} ${count === 1 ? singular : pluralForm}`;
}

const secondaryButtonClassName =
  'px-2.5 py-1 text-xs font-medium text-mac-text bg-mac-content border border-mac-hairline-strong ' +
  'rounded-lg hover:bg-mac-chip focus:outline-none focus:ring-2 focus:ring-mac-accent/40 ' +
  'disabled:opacity-50 transition-colors';

const chipClassName =
  'rounded border border-mac-hairline bg-mac-canvas px-1.5 py-0.5 text-[11px] text-mac-text-2';

export default function DocxDiffScreen({
  entry,
  worktreeRoot,
  onBack,
  inspectorOpen,
}: DocxDiffScreenProps) {
  const [wordError, setWordError] = useState<string | null>(null);
  const [opening, setOpening] = useState(false);
  const [requestError, setRequestError] = useState<string | null>(null);
  const [load, setLoad] = useState<DiffLoadState>({ loading: true, view: null });

  const target = docxDiffTargetFor(entry);
  const { path: entryPath, originalPath, conflictKind, indexChange, workingTreeChange, state } =
    entry;

  useEffect(() => {
    if (!worktreeRoot) {
      setRequestError(null);
      setLoad({ loading: false, view: null });
      return;
    }

    let active = true;
    setRequestError(null);
    setLoad({ loading: true, view: null });

    void (async () => {
      try {
        const payload = await fetchDocxDiff(worktreeRoot, entryPath, target, originalPath);
        if (!active) return;
        setLoad({ loading: false, view: toDocxDiffViewState(payload) });
      } catch (error) {
        if (!active) return;
        setLoad({ loading: false, view: null });
        setRequestError(errorMessageOf(error));
      }
    })();

    return () => {
      active = false;
    };
  }, [
    conflictKind,
    entryPath,
    indexChange,
    originalPath,
    state,
    target,
    workingTreeChange,
    worktreeRoot,
  ]);

  const counts = useMemo(() => countChangeClasses(load.view), [load.view]);
  const report = load.view?.diff ?? null;

  async function handleOpenInWord() {
    if (!entry.documentId) return;
    setOpening(true);
    setWordError(null);
    const result = await openInWord(entry.documentId);
    setOpening(false);
    if (!result.success) {
      setWordError(result.errorMessage);
    }
  }

  return (
    <section
      aria-label="Document diff"
      data-testid="docx-diff-screen"
      className="flex h-full min-h-0 w-full bg-mac-content"
    >
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-11 shrink-0 items-center justify-between gap-3 border-b border-mac-hairline px-3">
          <div className="flex min-w-0 items-baseline gap-2">
            <h1 className="text-sm font-semibold text-mac-text">Document diff</h1>
            <span className="truncate text-[11px] text-mac-text-2">
              {entry.path} · DOCX · read-only report
            </span>
          </div>
          <button type="button" onClick={onBack} className={secondaryButtonClassName}>
            Back
          </button>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto p-3">
          {wordError && (
            <div
              role="alert"
              className="mb-2 rounded-lg border border-mac-danger/40 bg-mac-danger/10 p-2 text-xs text-mac-danger"
            >
              {wordError}
            </div>
          )}

          <div className="mb-3 flex flex-wrap items-center gap-2">
            <span className="rounded border border-mac-hairline bg-mac-canvas px-1.5 py-0.5 font-mono text-xs text-mac-text-2">
              {entry.statusCharacters}
            </span>
            <span className={chipClassName}>{entry.state}</span>
            <span className={chipClassName}>DOCX · OOXML</span>
            <span className="ml-auto text-[10px] font-medium uppercase tracking-wide text-mac-text-3">
              Read-only
            </span>
          </div>

          {!worktreeRoot && (
            <div
              data-testid="docx-diff-worktree-unknown"
              className="w-full max-w-3xl rounded-xl border border-mac-hairline bg-mac-canvas p-4 text-xs text-mac-text-2"
            >
              The true worktree root is not known for this change, so the document-aware
              comparison cannot be requested. Reload source control to re-read the Git context.
              The document is unchanged by this screen.
            </div>
          )}

          {worktreeRoot && requestError && (
            <div
              role="alert"
              className="w-full max-w-3xl rounded-xl border border-mac-danger/40 bg-mac-danger/10 p-4 text-sm text-mac-danger"
            >
              {requestError}
            </div>
          )}

          {worktreeRoot && !requestError && load.loading && (
            <DocxDiffPanel status="available" isLoading />
          )}

          {worktreeRoot && !requestError && !load.loading && load.view && (
            <>
              {counts && report && (
                <div
                  data-testid="docx-diff-class-summary"
                  className="mb-2 flex flex-wrap items-center gap-1.5"
                >
                  <span className={chipClassName}>{plural(counts.text, 'text change', 'text changes')}</span>
                  <span className={chipClassName}>{plural(counts.table, 'table change', 'table changes')}</span>
                  <span className={chipClassName}>
                    {plural(counts.formatting, 'formatting change', 'formatting changes')}
                  </span>
                  <span className={chipClassName}>
                    {plural(counts.metadata, 'metadata change', 'metadata changes')}
                  </span>
                  <span className="ml-auto text-[11px] text-mac-text-3">
                    {report.leftLabel} ↔ {report.rightLabel}
                  </span>
                </div>
              )}
              <DocxDiffPanel
                status={load.view.status}
                diff={load.view.diff}
                failureMessage={load.view.failureMessage}
              />
            </>
          )}

          <p className="mt-3 max-w-3xl text-[11px] text-mac-text-3">
            DOCX deep editing stays in Microsoft Word Desktop. This report is read-only and never
            mutates the document. No rendered page image is claimed.
          </p>
        </div>
      </div>

      {inspectorOpen && (
        <aside
          aria-label="Document inspector"
          className="flex h-full w-[280px] shrink-0 flex-col gap-4 overflow-y-auto border-l border-mac-hairline bg-mac-inspector px-3 py-3"
        >
          <header className="flex items-center justify-between">
            <h2 className="text-[11px] font-semibold text-mac-text">Document</h2>
          </header>

          <InspectorCard title="Document" testId="docx-inspector-document">
            <InspectorField label="File" value={basename(entry.path)} />
            <InspectorField label="Folder" value={folderOf(entry.path)} />
            <InspectorField label="Format" value="DOCX · OOXML" />
            <InspectorField label="Git status" value={entry.statusCharacters} />
            <InspectorField label="Change" value={entry.state} />
            <InspectorField label="Index" value={entry.indexChange} />
            <InspectorField label="Working tree" value={entry.workingTreeChange} />
          </InspectorCard>

          <InspectorCard title="Report summary" testId="docx-inspector-report">
            {load.loading && <p className="text-[11px] text-mac-text-3">Reading document report…</p>}
            {!load.loading && !worktreeRoot && (
              <p className="text-[11px] text-mac-text-3">Worktree root unknown.</p>
            )}
            {!load.loading && worktreeRoot && requestError && (
              <p className="text-[11px] text-mac-text-3">The report could not be read.</p>
            )}
            {!load.loading && load.view && !report && (
              <p className="text-[11px] text-mac-text-3">
                {load.view.status === 'binary-fallback'
                  ? 'Binary change only; no document-aware report.'
                  : 'No document-aware report was produced.'}
              </p>
            )}
            {!load.loading && report && (
              <>
                <InspectorField
                  label="Comparison"
                  value={`${report.leftLabel} ↔ ${report.rightLabel}`}
                />
                <InspectorField label="Changes" value={report.changes.length} />
                {counts && (
                  <>
                    <InspectorField label="Text" value={counts.text} />
                    <InspectorField label="Table" value={counts.table} />
                    <InspectorField label="Formatting" value={counts.formatting} />
                    <InspectorField label="Metadata" value={counts.metadata} />
                  </>
                )}
                {report.binarySummary && (
                  <InspectorField
                    label="Binary payload"
                    value={
                      report.binarySummary.contentIdentical
                        ? 'byte-identical'
                        : `${report.binarySummary.leftSizeBytes} → ${report.binarySummary.rightSizeBytes} bytes`
                    }
                  />
                )}
              </>
            )}
          </InspectorCard>

          <InspectorCard title="Review" testId="docx-inspector-review">
            <p className="text-[11px] text-mac-text-2">
              Document-aware changes are structural: text, tables, formatting and metadata. This
              screen does not render the change list as text lines.
            </p>
            {entry.documentId ? (
              <button
                type="button"
                disabled={opening}
                onClick={() => void handleOpenInWord()}
                className="w-full rounded-lg bg-mac-accent px-3 py-1.5 text-xs font-medium text-white hover:bg-mac-accent/90 disabled:opacity-50"
              >
                {opening ? 'Opening…' : 'Open in Word'}
              </button>
            ) : (
              <p className="text-[11px] text-mac-text-3">
                This path is not a registered document, so Document IDE cannot open it. Resolve it
                outside the app.
              </p>
            )}
          </InspectorCard>
        </aside>
      )}
    </section>
  );
}
