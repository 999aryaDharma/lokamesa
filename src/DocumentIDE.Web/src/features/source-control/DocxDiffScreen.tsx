import { useState } from 'react';
import { openInWord } from '../files/OpenInWordAction';
import InspectorCard, { InspectorField } from '../shell/InspectorCard';
import DocxDiffPanel from './DocxDiffPanel';
import type { SourceControlChangeEntry } from './sourceControlModels';

/**
 * UI-03 screen 06 — DOCX document-aware diff.
 *
 * A read-only report around the shipped `DocxDiffPanel`. The design shows a
 * unified text diff for a `.docx`; that is not implementable honestly, so this
 * screen never renders one. The document is reviewed structurally, and deep
 * editing stays in Microsoft Word: the primary action is the existing
 * `openInWord` path.
 *
 * The document-aware comparison itself needs the SCM-05 provider exposed over
 * HTTP. That route is not part of this build, so the panel reports the typed
 * limitation instead of showing a fabricated change list. No page image is
 * claimed (WORD-04 remains unimplemented).
 */

export interface DocxDiffScreenProps {
  entry: SourceControlChangeEntry;
  onBack: () => void;
  inspectorOpen: boolean;
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

const secondaryButtonClassName =
  'px-2.5 py-1 text-xs font-medium text-mac-text bg-mac-content border border-mac-hairline-strong ' +
  'rounded-lg hover:bg-mac-chip focus:outline-none focus:ring-2 focus:ring-mac-accent/40 ' +
  'disabled:opacity-50 transition-colors';

export default function DocxDiffScreen({ entry, onBack, inspectorOpen }: DocxDiffScreenProps) {
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [opening, setOpening] = useState(false);

  async function handleOpenInWord() {
    if (!entry.documentId) return;
    setOpening(true);
    setErrorMessage(null);
    const result = await openInWord(entry.documentId);
    setOpening(false);
    if (!result.success) {
      setErrorMessage(result.errorMessage);
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
          {errorMessage && (
            <div
              role="alert"
              className="mb-2 rounded-lg border border-mac-danger/40 bg-mac-danger/10 p-2 text-xs text-mac-danger"
            >
              {errorMessage}
            </div>
          )}

          <div className="mb-3 flex flex-wrap items-center gap-2">
            <span className="rounded border border-mac-hairline bg-mac-canvas px-1.5 py-0.5 font-mono text-xs text-mac-text-2">
              {entry.statusCharacters}
            </span>
            <span className="rounded border border-mac-hairline bg-mac-canvas px-1.5 py-0.5 text-xs text-mac-text-2">
              {entry.state}
            </span>
            <span className="rounded border border-mac-hairline bg-mac-canvas px-1.5 py-0.5 text-xs text-mac-text-2">
              DOCX · OOXML
            </span>
          </div>

          <DocxDiffPanel
            status="failed"
            failureMessage="A document-aware comparison is not available for this change in this build. The document is unchanged by this screen; open it in Word to review the content."
          />

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
