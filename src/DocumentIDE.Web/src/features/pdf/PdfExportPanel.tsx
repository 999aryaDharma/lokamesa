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

/**
 * Single source of truth for how a typed engine status maps to a result row.
 * Both the results list and the progress counts derive from this so they can
 * never disagree about the same outcome.
 */
function countsForOutcome(
  status: PdfExportFileOutcome['status'],
): Pick<PdfExportProgressState, 'succeeded' | 'skipped' | 'failed'> {
  return {
    succeeded: status === 'succeeded' ? 1 : 0,
    skipped: status === 'skipped' ? 1 : 0,
    failed: status === 'failed' ? 1 : 0,
  };
}

/** Collision policy sent to the engine for a resolved owner decision. */
function behaviorForDecision(decision: CollisionDecision): PdfCollisionBehaviorValue | undefined {
  if (decision === 'Overwrite') return 'overwrite';
  if (decision === 'Rename') return 'rename';
  return undefined;
}

function describeError(error: unknown): string {
  // `PdfRequestError` carries an owner-readable message plus the raw controller
  // code as its typed `errorCode` detail (see `pdfApi.ownerMessageFor`).
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

  /**
   * Finding 3: every new run starts from a clean slate. Without this a previous
   * separate/merge run's progress bar and summary linger over the new export.
   */
  const beginRun = useCallback(() => {
    setError(null);
    setSummary(null);
    setResults([]);
    setCollisions([]);
    setPendingCollision(null);
    setProgress(undefined);
  }, []);

  const runSingleExport = useCallback(
    async (
      documentId: string,
      options: { collisionBehavior?: PdfCollisionBehaviorValue; standing?: CollisionDecision } = {},
    ) => {
      const doc = selection.find((candidate) => candidate.id === documentId);
      const sourcePath = resolveSourcePath(documentId);
      const name = doc?.name ?? documentId;
      if (!sourcePath) {
        setError(`No source path available for ${name}.`);
        return;
      }

      const singleRow = (response: SinglePdfExportResponse): PdfExportFileOutcome => ({
        documentId,
        sourceName: name,
        status: outcomeForStatus(response.status),
        errorCode: response.status,
        message: messageFor(response.status, response.errorMessage, response.outputPath),
      });
      // A retained decision ("apply choice to remaining collisions") supplies
      // the policy for this run; an explicit behavior always wins.
      const effectiveBehavior =
        options.collisionBehavior ??
        (options.standing ? behaviorForDecision(options.standing) : undefined);

      setBusy(true);
      setError(null);
      try {
        let response = await deps.exportSingle({
          sourceDocxPath: sourcePath,
          collisionBehavior: effectiveBehavior,
        });
        setResults([singleRow(response)]);
        if (response.status === 'OutputCollision' && options.standing) {
          // The owner already chose a standing policy for this run: resolve the
          // remaining collision with it instead of re-prompting.
          response = await deps.exportSingle({
            sourceDocxPath: sourcePath,
            collisionBehavior: behaviorForDecision(options.standing),
          });
          setResults([singleRow(response)]);
        }
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
          const name = selection.find((doc) => doc.id === id)?.name ?? id;
          setError(`No source path available for ${name}.`);
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
      try {
        const response = await deps.merge({
          orderedSourcePaths: orderedPaths,
          outputPath,
          collisionBehavior: behaviorForPolicy(collisionPolicy),
        });
        // One status → outcome mapping drives both the result row and the
        // progress counts, so a collision reads `skipped` in both places.
        const outcome = outcomeForStatus(response.status);
        setResults([
          {
            documentId: 'merged-pdf',
            sourceName: fileNameOf(response.outputPath ?? outputPath),
            status: outcome,
            errorCode: response.status,
            message: messageFor(response.status, response.errorMessage, response.outputPath),
          },
        ]);
        setProgress({
          completed: 1,
          total: 1,
          ...countsForOutcome(outcome),
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
    [collisionPolicy, deps, effectiveMergeOutputPath, resolveSourcePath, selection],
  );

  const handleCollisionResolve = useCallback(
    (decision: CollisionDecision, applyToRemaining: boolean) => {
      setCollisions([]);
      const pending = pendingCollision;
      setPendingCollision(null);
      if (!pending) return;
      if (decision === 'Overwrite' || decision === 'Rename') {
        // `applyToRemaining` retains the decision as the standing policy for the
        // remaining collisions of this run (honoured in `runSingleExport`);
        // otherwise it applies to this collision only.
        void runSingleExport(
          pending.documentId,
          applyToRemaining
            ? { standing: decision }
            : { collisionBehavior: behaviorForDecision(decision) },
        );
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
      className="w-96 max-w-full shrink-0 overflow-auto border-l border-mac-hairline bg-mac-content p-4"
    >
      <header className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-mac-text">PDF export</h2>
        {onClose && (
          <button
            type="button"
            aria-label="Close PDF export"
            onClick={onClose}
            className="text-mac-text-3 hover:text-mac-text-2 text-lg leading-none"
          >
            &times;
          </button>
        )}
      </header>

      {showSeparate && (
        <label className="block mt-2 text-xs text-mac-text-2">
          Output folder
          <input
            type="text"
            value={effectiveOutputDirectory}
            onChange={(event) => setOutputDirectory(event.target.value)}
            className="block w-full border border-mac-hairline-strong rounded px-2 py-1 text-sm"
          />
        </label>
      )}

      {showMerge && (
        <label className="block mt-2 text-xs text-mac-text-2">
          Merged PDF file
          <input
            type="text"
            value={effectiveMergeOutputPath}
            onChange={(event) => setMergeOutputPath(event.target.value)}
            className="block w-full border border-mac-hairline-strong rounded px-2 py-1 text-sm"
          />
        </label>
      )}

      {(showSeparate || showMerge) && (
        <label className="block mt-2 text-xs text-mac-text-2">
          If a PDF already exists
          <select
            value={collisionPolicy}
            onChange={(event) => setCollisionPolicy(event.target.value as CollisionPolicy)}
            className="block w-full border border-mac-hairline-strong rounded px-2 py-1 text-sm"
          >
            <option value="fail">Stop and report</option>
            <option value="rename">Create a renamed copy</option>
            <option value="overwrite">Overwrite</option>
          </select>
        </label>
      )}

      <PdfActions
        selection={selection}
        onExportSingle={(documentId) => {
          beginRun();
          void runSingleExport(documentId);
        }}
        onExportSeparate={(documentIds) => {
          beginRun();
          void runSeparateExport(documentIds);
        }}
        onMerge={(orderedIds) => {
          beginRun();
          void runMerge(orderedIds);
        }}
        progress={progress}
        results={results}
        collisions={collisions}
        onCollisionResolve={handleCollisionResolve}
      />

      {busy && <p role="status">Generating PDFs…</p>}
      {summary && <p role="status">{summary}</p>}
      {error && (
        <p role="alert" className="text-xs text-mac-danger">
          {error}
        </p>
      )}
      {exportable.length > 0 && (
        <p className="mt-2 text-[11px] text-mac-text-3" data-testid="pdf-export-hint">
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
