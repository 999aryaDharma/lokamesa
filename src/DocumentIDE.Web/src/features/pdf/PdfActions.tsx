import { useState } from 'react';
import { BatchExportProgress } from './BatchExportProgress';
import { MergeOrderDialog } from './MergeOrderDialog';

export interface PdfSelectableDocument {
  id: string;
  name: string;
  /** Only 'docx' is exportable. PDF is output-only and never an input. */
  kind: string;
}

export type PdfExportMode = 'single' | 'separate' | 'merge';

export type PdfActionId = 'export-single' | 'export-separate' | 'merge-pdf';

export type CollisionDecision = 'Overwrite' | 'Rename' | 'Skip' | 'Cancel';

export interface PdfExportFileOutcome {
  documentId: string;
  sourceName: string;
  status: 'succeeded' | 'skipped' | 'failed' | 'cancelled';
  errorCode?: string;
  message?: string;
}

export interface PdfExportProgressState {
  completed: number;
  total: number;
  currentFileName?: string | null;
  succeeded: number;
  skipped: number;
  failed: number;
  onCancel?: () => void;
}

export interface PdfCollision {
  documentId: string;
  sourceName: string;
  targetPath: string;
}

export interface PdfCommandPaletteEntry {
  id: 'pdf.export-single' | 'pdf.export-separate' | 'pdf.merge';
  title: string;
  category: 'PDF';
  enabled: boolean;
}

export interface PdfActionsProps {
  selection: PdfSelectableDocument[];
  onExportSingle?: (documentId: string) => void;
  onExportSeparate?: (documentIds: string[]) => void;
  onMerge?: (orderedIds: string[]) => void;
  progress?: PdfExportProgressState;
  results?: PdfExportFileOutcome[];
  collisions?: PdfCollision[];
  onCollisionResolve?: (decision: CollisionDecision, applyToRemaining: boolean) => void;
}

export function isPdfExportable(doc: PdfSelectableDocument): boolean {
  return doc.kind === 'docx';
}

/** Context/inspector action rules: single DOCX → single export; multiple → separate + merge. */
export function getAvailablePdfActions(selection: PdfSelectableDocument[]): PdfActionId[] {
  const exportable = selection.filter(isPdfExportable);
  if (exportable.length === 0) {
    return [];
  }
  if (exportable.length === 1 && selection.length === 1) {
    return ['export-single'];
  }
  return ['export-separate', 'merge-pdf'];
}

/** One selected DOCX defaults to single export; anything else has no single default. */
export function resolveDefaultExportMode(
  selection: PdfSelectableDocument[],
): PdfExportMode | null {
  if (selection.length === 1 && isPdfExportable(selection[0])) {
    return 'single';
  }
  return null;
}

export function getPdfCommandPaletteEntries(
  selection: PdfSelectableDocument[],
): PdfCommandPaletteEntry[] {
  const actions = getAvailablePdfActions(selection);
  const entries: PdfCommandPaletteEntry[] = [];
  if (actions.includes('export-single')) {
    entries.push({
      id: 'pdf.export-single',
      title: 'PDF: Export as PDF',
      category: 'PDF',
      enabled: true,
    });
  }
  if (actions.includes('export-separate')) {
    entries.push({
      id: 'pdf.export-separate',
      title: 'PDF: Export as separate PDFs',
      category: 'PDF',
      enabled: true,
    });
  }
  if (actions.includes('merge-pdf')) {
    entries.push({
      id: 'pdf.merge',
      title: 'PDF: Merge to single PDF',
      category: 'PDF',
      enabled: true,
    });
  }
  return entries;
}

export function PdfActions({
  selection,
  onExportSingle,
  onExportSeparate,
  onMerge,
  progress,
  results,
  collisions,
  onCollisionResolve,
}: PdfActionsProps) {
  const actions = getAvailablePdfActions(selection);
  const exportable = selection.filter(isPdfExportable);
  const [mergeOpen, setMergeOpen] = useState(false);
  const [mergeOrder, setMergeOrder] = useState<string[]>([]);
  const [collisionDecision, setCollisionDecision] = useState<CollisionDecision>('Rename');
  const [applyToRemaining, setApplyToRemaining] = useState(false);

  const openMerge = () => {
    setMergeOrder(exportable.map((doc) => doc.id));
    setMergeOpen(true);
  };

  return (
    <section aria-label="PDF export actions">
      {actions.length === 0 ? (
        <p>Select one or more DOCX documents to export.</p>
      ) : null}
      {actions.includes('export-single') && exportable.length === 1 ? (
        <button
          onClick={() => onExportSingle?.(exportable[0].id)}
          type="button"
        >
          Export PDF
        </button>
      ) : null}
      {actions.includes('export-separate') ? (
        <button
          onClick={() => onExportSeparate?.(exportable.map((doc) => doc.id))}
          type="button"
        >
          Export as separate PDFs
        </button>
      ) : null}
      {actions.includes('merge-pdf') ? (
        <button onClick={openMerge} type="button">
          Merge to PDF
        </button>
      ) : null}

      {mergeOpen ? (
        <MergeOrderDialog
          files={exportable.map((doc) => ({ id: doc.id, name: doc.name }))}
          order={mergeOrder}
          onOrderChange={setMergeOrder}
          onConfirm={(orderedIds) => {
            setMergeOpen(false);
            onMerge?.(orderedIds);
          }}
          onCancel={() => setMergeOpen(false)}
        />
      ) : null}

      {collisions && collisions.length > 0 ? (
        <fieldset>
          <legend>Output file already exists</legend>
          <p>
            {collisions[0].sourceName} would overwrite {collisions[0].targetPath}.
            Choose how to proceed. Existing files are never overwritten silently.
          </p>
          {(['Overwrite', 'Rename', 'Skip', 'Cancel'] as CollisionDecision[]).map(
            (decision) => (
              <label key={decision}>
                <input
                  checked={collisionDecision === decision}
                  name="pdf-collision-decision"
                  onChange={() => setCollisionDecision(decision)}
                  type="radio"
                  value={decision}
                />
                {decision}
              </label>
            ),
          )}
          <label>
            <input
              checked={applyToRemaining}
              onChange={(event) => setApplyToRemaining(event.target.checked)}
              type="checkbox"
            />
            Apply choice to remaining collisions
          </label>
          <button
            onClick={() => onCollisionResolve?.(collisionDecision, applyToRemaining)}
            type="button"
          >
            Apply collision decision
          </button>
        </fieldset>
      ) : null}

      {progress ? (
        <BatchExportProgress
          completed={progress.completed}
          currentFileName={progress.currentFileName}
          failed={progress.failed}
          onCancel={progress.onCancel}
          skipped={progress.skipped}
          succeeded={progress.succeeded}
          total={progress.total}
        />
      ) : null}

      {results && results.length > 0 ? (
        <ul aria-label="PDF export results">
          {results.map((result) => (
            <li key={result.documentId}>
              <span>{result.sourceName}</span> <span>{result.status}</span>
              {result.message ? <span>{result.message}</span> : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
