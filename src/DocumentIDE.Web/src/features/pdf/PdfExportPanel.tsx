/**
 * G8 — PDF finalization UX container (gap "PdfActions imported only by its own
 * test").
 *
 * Mounts the presentational `PdfActions` surface on top of the live privileged
 * PDF route (`pdfApi.ts` → `PdfController`) so the owner can actually export a
 * DOCX to PDF, export a selection as separate PDFs, and merge documents into a
 * single PDF.
 *
 * Honesty rules (mirrors the PDF-01/02/03 engines):
 *   - The request carries a real source path resolved from the workspace
 *     document the canvas already knows; an unresolvable path is a typed
 *     refusal, never a fabricated export.
 *   - Engine statuses (`OutputCollision`, `SourceNotFound`, `DiskFull`, …) are
 *     surfaced verbatim. An existing output file is never reported as success.
 *   - Collisions require an explicit decision (Overwrite/Rename/Skip/Cancel);
 *     the default policy stays fail-closed.
 *   - The output location is a visible, user-editable choice; it is never
 *     invented silently by the client.
 */
import { useCallback, useMemo, useState } from 'react';
import {
  getAvailablePdfActions,
  PdfActions,
  type CollisionDecision,
  type PdfCollision,
  type PdfExportFileOutcome,
  type PdfExportProgressState,
  type PdfSelectableDocument,
} from './PdfActions';
import {
  exportSeparatePdfs,
  exportSinglePdf,
  mergeDocumentsToPdf,
  PdfRequestError,
  type BatchPdfExportFileResponse,
  type BatchPdfExportRequestDto,
  type BatchPdfExportResponse,
  type PdfCollisionBehaviorValue,
  type PdfMergeRequestDto,
  type PdfMergeResponse,
  type PdfStatus,
  type SinglePdfExportRequestDto,
  type SinglePdfExportResponse,
} from './pdfApi';

export interface PdfExportPanelApi {
  exportSingle: (request: SinglePdfExportRequestDto) => Promise<SinglePdfExportResponse>;
  exportSeparate: (request: BatchPdfExportRequestDto) => Promise<BatchPdfExportResponse>;
  merge: (request: PdfMergeRequestDto) => Promise<PdfMergeResponse>;
}

const defaultApi: PdfExportPanelApi = {
  exportSingle: (request) => exportSinglePdf(request),
  exportSeparate: (request) => exportSeparatePdfs(request),
  merge: (request) => mergeDocumentsToPdf(request),
};

type CollisionPolicy = 'fail' | 'overwrite' | 'rename';

export interface PdfExportPanelProps {
  /** Current canvas selection, mapped to the export surface. */
  selection: PdfSelectableDocument[];
  /** Resolves a document's local source path; `undefined` when unavailable. */
  resolveSourcePath: (documentId: string) => string | undefined;
  onClose?: () => void;
  /** Injectable engine client for the shell/tests; defaults to `pdfApi`. */
  api?: Partial<PdfExportPanelApi>;
}

/** Directory portion of a path, preserving the original separators. */
function directoryOf(path: string): string {
  const index = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  return index > 0 ? path.slice(0, index) : '';
}

/** File name (with extension) portion of a path. */
function fileNameOf(path: string): string {
  const index = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  return index >= 0 ? path.slice(index + 1) : path;
}

/** File name without its extension (used for default PDF names). */
function stemOf(path: string): string {
  const name = fileNameOf(path);
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(0, dot) : name;
}

function outcomeForStatus(status: PdfStatus): PdfExportFileOutcome['status'] {
  switch (status) {
    case 'Succeeded':
      return 'succeeded';
    case 'OutputCollision':
      return 'skipped';
    case 'Cancelled':
      return 'cancelled';
    default:
      return 'failed';
  }
}

function messageFor(status: PdfStatus, errorMessage: string | null, outputPath: string | null): string | undefined {
  if (errorMessage) return errorMessage;
  if (status === 'Succeeded' && outputPath) return outputPath;
  return undefined;
}

function describeError(error: unknown): string {
  if (error instanceof PdfRequestError) return error.errorCode;
  return error instanceof Error ? error.message : 'PDF request failed.';
}

export function PdfExportPanel({
  selection,
  resolveSourcePath,
  onClose,
  api,
}: PdfExportPanelProps) {
  const deps = useMemo<PdfExportPanelApi>(() => ({ ...defaultApi, ...api }), [api]);

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [results, setResults] = useState<PdfExportFileOutcome[]>([]);
  const [collisions, setCollisions] = useState<PdfCollision[]>([]);
  const [progress, setProgress] = useState<PdfExportProgressState | undefined>(undefined);
  const [summary, setSummary] = useState<string | null>(null);
  const [outputDirectory, setOutputDirectory] = useState<string | null>(null);
  const [mergeOutputPath, setMergeOutputPath] = useState<string | null>(null);
  const [collisionPolicy, setCollisionPolicy] = useState<CollisionPolicy>('fail');
  // A single export that collided, held so the owner's explicit decision can
  // retry it with the chosen policy. No export is retried without a decision.
  const [pendingCollision, setPendingCollision] = useState<{
    documentId: string;
    sourceName: string;
  } | null>(null);

  const actions = getAvailablePdfActions(selection);
  const exportable = selection.filter(
    (doc) => doc.kind === 'docx' && resolveSourcePath(doc.id),
  );

  const derivedOutputDirectory = useMemo(() => {
    const firstPath = selection
      .map((doc) => resolveSourcePath(doc.id))
      .find((path): path is string => Boolean(path));
    return firstPath ? directoryOf(firstPath) : '';
  }, [selection, resolveSourcePath]);

  const derivedMergeOutputPath = useMemo(() => {
    const firstPath = selection
      .map((doc) => resolveSourcePath(doc.id))
      .find((path): path is string => Boolean(path));
    if (!firstPath) return '';
    const directory = directoryOf(firstPath);
    const name = `${stemOf(firstPath)}-merged.pdf`;
    return directory ? `${directory}/${name}` : name;
  }, [selection, resolveSourcePath]);

  const effectiveOutputDirectory = outputDirectory ?? derivedOutputDirectory;
  const effectiveMergeOutputPath = mergeOutputPath ?? derivedMergeOutputPath;

  const behaviorForPolicy = (policy: CollisionPolicy): PdfCollisionBehaviorValue | undefined =>
    policy === 'fail' ? undefined : policy;

  const runSingleExport = useCallback(
    async (documentId: string, collisionBehavior?: PdfCollisionBehaviorValue) => {
      const doc = selection.find((candidate) => candidate.id === documentId);
      const sourcePath = resolveSourcePath(documentId);
      const name = doc?.name ?? documentId;
      if (!sourcePath) {
        setError(`No source path available for ${name}.`);
        return;
      }

      setBusy(true);
      setError(null);
      setSummary(null);
      try {
        const response = await deps.exportSingle({ sourceDocxPath: sourcePath, collisionBehavior });
        setResults([
          {
            documentId,
            sourceName: name,
            status: outcomeForStatus(response.status),
            errorCode: response.status,
            message: messageFor(response.status, response.errorMessage, response.outputPath),
          },
        ]);
        if (response.status === 'OutputCollision') {
          // Typed refusal: surface the collision and wait for an explicit
          // decision. Never reported as success, never auto-overwritten.
          setCollisions([
            {
              documentId,
              sourceName: name,
              targetPath: response.outputPath ?? '',
            },
          ]);
          setPendingCollision({ documentId, sourceName: name });
        } else {
          setCollisions([]);
          setPendingCollision(null);
          if (response.isSuccess) setSummary(`PDF written to ${response.outputPath}.`);
        }
      } catch (err: unknown) {
        setError(describeError(err));
      } finally {
        setBusy(false);
      }
    },
    [deps, resolveSourcePath, selection],
  );

  const runSeparateExport = useCallback(
    async (documentIds: string[]) => {
      const requests: { documentId: string; sourceName: string; path: string }[] = [];
      const missing: string[] = [];
      for (const id of documentIds) {
        const path = resolveSourcePath(id);
        const name = selection.find((doc) => doc.id === id)?.name ?? id;
        if (path) requests.push({ documentId: id, sourceName: name, path });
        else missing.push(name);
      }
      // Never silently drop a selected document: an unresolvable source is a
      // typed refusal for the whole request, matching the single-export path.
      if (missing.length > 0) {
        setError(`No source path available for ${missing.join(', ')}.`);
        return;
      }
      const directory = effectiveOutputDirectory;
      if (!directory) {
        setError('Choose an output folder for the exported PDFs.');
        return;
      }

      setBusy(true);
      setError(null);
      setSummary(null);
      try {
        const response = await deps.exportSeparate({
          sourceDocxPaths: requests.map((request) => request.path),
          outputDirectory: directory,
          collisionBehavior: behaviorForPolicy(collisionPolicy),
        });
        const byPath = new Map(requests.map((request) => [request.path, request]));
        setResults(
          response.files.map((file) => outcomeFromBatchFile(file, byPath)),
        );
        setProgress({
          completed: response.totalCount,
          total: response.totalCount,
          succeeded: response.succeededCount,
          skipped: response.skippedCount,
          failed: response.failedCount,
        });
        setSummary(response.summary);
      } catch (err: unknown) {
        setError(describeError(err));
      } finally {
        setBusy(false);
      }
    },
    [collisionPolicy, deps, effectiveOutputDirectory, resolveSourcePath, selection],
  );

  const runMerge = useCallback(
    async (orderedIds: string[]) => {
      const orderedPaths: string[] = [];
      for (const id of orderedIds) {
        const path = resolveSourcePath(id);
        if (!path) {
          setError(`No source path available for ${id}.`);
          return;
        }
        orderedPaths.push(path);
      }
      const outputPath = effectiveMergeOutputPath;
      if (!outputPath) {
        setError('Choose an output path for the merged PDF.');
        return;
      }

      setBusy(true);
      setError(null);
      setSummary(null);
      try {
        const response = await deps.merge({
          orderedSourcePaths: orderedPaths,
          outputPath,
          collisionBehavior: behaviorForPolicy(collisionPolicy),
        });
        setResults([
          {
            documentId: 'merged-pdf',
            sourceName: fileNameOf(response.outputPath ?? outputPath),
            status: outcomeForStatus(response.status),
            errorCode: response.status,
            message: messageFor(response.status, response.errorMessage, response.outputPath),
          },
        ]);
        setProgress({
          completed: 1,
          total: 1,
          succeeded: response.isSuccess ? 1 : 0,
          skipped: 0,
          failed: response.isSuccess ? 0 : 1,
        });
        setSummary(
          response.isSuccess
            ? `${response.pageCount} pages written to ${response.outputPath}.`
            : response.errorMessage ?? `Merge ${response.status}.`,
        );
      } catch (err: unknown) {
        setError(describeError(err));
      } finally {
        setBusy(false);
      }
    },
    [collisionPolicy, deps, effectiveMergeOutputPath, resolveSourcePath],
  );

  const handleCollisionResolve = useCallback(
    (decision: CollisionDecision) => {
      setCollisions([]);
      const pending = pendingCollision;
      setPendingCollision(null);
      if (!pending) return;
      if (decision === 'Overwrite') {
        void runSingleExport(pending.documentId, 'overwrite');
      } else if (decision === 'Rename') {
        void runSingleExport(pending.documentId, 'rename');
      } else {
        // Skip / Cancel: the engine's fail-closed `OutputCollision` outcome
        // stands; nothing is written.
        setResults([
          {
            documentId: pending.documentId,
            sourceName: pending.sourceName,
            status: 'skipped',
            errorCode: 'OutputCollision',
            message: 'Output PDF already exists; export skipped.',
          },
        ]);
      }
    },
    [pendingCollision, runSingleExport],
  );

  const showSeparate = actions.includes('export-separate');
  const showMerge = actions.includes('merge-pdf');

  return (
    <section
      data-testid="pdf-export-panel"
      aria-label="PDF export"
      className="w-96 max-w-full shrink-0 overflow-auto border-l border-slate-200 bg-white p-4"
    >
      <header className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-slate-800">PDF export</h2>
        {onClose && (
          <button
            type="button"
            aria-label="Close PDF export"
            onClick={onClose}
            className="text-slate-400 hover:text-slate-600 text-lg leading-none"
          >
            &times;
          </button>
        )}
      </header>

      {showSeparate && (
        <label className="block mt-2 text-xs text-slate-600">
          Output folder
          <input
            type="text"
            value={effectiveOutputDirectory}
            onChange={(event) => setOutputDirectory(event.target.value)}
            className="block w-full border border-slate-300 rounded px-2 py-1 text-sm"
          />
        </label>
      )}

      {showMerge && (
        <label className="block mt-2 text-xs text-slate-600">
          Merged PDF file
          <input
            type="text"
            value={effectiveMergeOutputPath}
            onChange={(event) => setMergeOutputPath(event.target.value)}
            className="block w-full border border-slate-300 rounded px-2 py-1 text-sm"
          />
        </label>
      )}

      {(showSeparate || showMerge) && (
        <label className="block mt-2 text-xs text-slate-600">
          If a PDF already exists
          <select
            value={collisionPolicy}
            onChange={(event) => setCollisionPolicy(event.target.value as CollisionPolicy)}
            className="block w-full border border-slate-300 rounded px-2 py-1 text-sm"
          >
            <option value="fail">Stop and report</option>
            <option value="rename">Create a renamed copy</option>
            <option value="overwrite">Overwrite</option>
          </select>
        </label>
      )}

      <PdfActions
        selection={selection}
        onExportSingle={(documentId) => void runSingleExport(documentId)}
        onExportSeparate={(documentIds) => void runSeparateExport(documentIds)}
        onMerge={(orderedIds) => void runMerge(orderedIds)}
        progress={progress}
        results={results}
        collisions={collisions}
        onCollisionResolve={handleCollisionResolve}
      />

      {busy && <p role="status">Generating PDFs…</p>}
      {summary && <p role="status">{summary}</p>}
      {error && (
        <p role="alert" className="text-xs text-red-700">
          {error}
        </p>
      )}
      {exportable.length > 0 && (
        <p className="mt-2 text-[11px] text-slate-400" data-testid="pdf-export-hint">
          PDF is generated from DOCX only; existing files are never overwritten without an explicit choice.
        </p>
      )}
    </section>
  );
}

function outcomeFromBatchFile(
  file: BatchPdfExportFileResponse,
  byPath: Map<string, { documentId: string; sourceName: string; path: string }>,
): PdfExportFileOutcome {
  const request = byPath.get(file.sourceDocxPath);
  return {
    documentId: request?.documentId ?? file.sourceDocxPath,
    sourceName: request?.sourceName ?? file.sourceDocxPath,
    status: outcomeForStatus(file.status),
    errorCode: file.status,
    message: messageFor(file.status, file.errorMessage, file.outputPath),
  };
}

export default PdfExportPanel;
