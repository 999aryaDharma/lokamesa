/**
 * Application shell.
 *
 * UI-01 restructure: the flat top nav became the macOS three-pane shell —
 * unified toolbar, left source-list sidebar, content, right inspector — with
 * **zero behaviour change**. Every surface, store and API call below is the
 * same one the previous shell used; only their placement and skin changed.
 *
 * Composes the shipped product surfaces and is the single place that wires the
 * previously unreachable subsystems to real data (REL-03 gaps G1/G3/G5/G6/G7):
 *
 *   - G6: on startup the shell loads the workspace's documents and folder
 *     sources through the privileged API and populates the canvas store
 *     (`setObjects`); released folder files are surfaced with `upsertObject`.
 *     Dragging still never moves a physical file. The same load feeds the
 *     sidebar's `FOLDER SOURCES` rows (real sources, real counts) and the
 *     canvas folder objects' fan-out.
 *   - G3: the search panel mounts from the sidebar's `LIBRARY` section and is
 *     backed by the existing `searchStore` (its `/api/search` contract is
 *     unchanged).
 *   - G5: the owner share panel mounts from the sidebar's `LIBRARY` section and
 *     drives the share lifecycle API (create / stop / revoke / regenerate /
 *     membership).
 *   - G1: the graph view loads the workspace graph and loads edges with truthful
 *     explicit-vs-inferred semantics.
 *   - G7: double-clicking a Markdown graph node or canvas card opens the
 *     internal Markdown editor overlay (source-first, safe-write via the API).
 *   - G8: the PDF export panel mounts from the toolbar against the live
 *     privileged PDF route (single / separate / merge), driven by the current
 *     canvas selection and its workspace document paths.
 *
 * UI-03 mounts the source-control surfaces into the same shell: the sidebar's
 * `Source control`, `Worktrees` and `History` rows switch the content pane to
 * screens 05/06/07 and to the composed History surface (Document IDE
 * operations + Git history), and the toolbar's Canvas/List/Graph switch always
 * returns to the workspace surface. The SCM screens own their right-hand
 * inspectors; the workspace inspector is only shown on the workspace surface.
 *
 * Reachability rule: exactly one control carries each of the accessible names
 * the shell tests query (`Search`, `Shares`, `PDF`, `Add folder`,
 * `Batch operations`), so those queries stay unambiguous.
 *
 * G4 deliberate decision — DOCX Quick Look: the owner DOCX "preview" surface is
 * intentionally open-in-Word, not the WORD-04 high-fidelity `QuickLook`
 * overlay. `QuickLook` needs a page-render source (Word → temp PDF → page
 * images) that no shipped host exposes (`QuickLookService` has no controller;
 * `IQuickLookRenderer` is not registered in any host), and the master spec
 * (spec 06 §12) makes that pipeline the prerequisite. Building the overlay over
 * a non-existent data source would be dishonest, so the product path stays:
 * double-click a DOCX (canvas card or graph node) → `openInWord` →
 * `POST /api/word/open`. Mounting `QuickLook` is deferred until a page-render
 * route exists; this is recorded in `docs/quality/v1-gap-fixes-pdf-quicklook.md`.
 *
 * Workspace identity comes from the host-injected
 * `window.__DOCUMENTIDE_WORKSPACE_ID__` global, falling back to `default`.
 */
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import BatchPreviewPanel from './features/batch/BatchPreviewPanel';
import DocumentCanvas from './features/canvas/DocumentCanvas';
import { useCanvasStore } from './features/canvas/canvasStore';
import type { FolderPickerFile } from './features/canvas/FolderPicker';
import type { CanvasObject } from './features/canvas/types';
import { ContextPackDialog, type ContextSourceItem } from './features/context/ContextPackDialog';
import { fetchContextPack, toContextSources } from './features/context/contextPackApi';
import LocateMovedFileDialog from './features/files/LocateMovedFileDialog';
import MissingResourcesPanel from './features/files/MissingResourcesPanel';
import { removeDocumentReference } from './features/files/missingResourceApi';
import {
  missingDocumentsFrom,
  type MissingResourceDocument,
} from './features/files/missingResourceModel';
import { openInWord } from './features/files/OpenInWordAction';
import DocumentGraph from './features/graph/DocumentGraph';
import { useGraphStore, type GraphNode } from './features/graph/graphStore';
import HistoryScreen from './features/history/HistoryScreen';
import { operationHistorySource } from './features/history/historyApi';
import DocumentList from './features/list/DocumentList';
import MarkdownEditor from './features/markdown/MarkdownEditor';
import PdfExportPanel from './features/pdf/PdfExportPanel';
import type { PdfSelectableDocument } from './features/pdf/PdfActions';
import { SearchPanel } from './features/search/SearchPanel';
import { SharePanel } from './features/sharing/SharePanel';
import Inspector from './features/shell/Inspector';
import Sidebar, { type ShellSurface, type SidebarFolderSource } from './features/shell/Sidebar';
import Toolbar from './features/shell/Toolbar';
import DocxDiffScreen from './features/source-control/DocxDiffScreen';
import SourceControlScreen from './features/source-control/SourceControlScreen';
import type { SourceControlChangeEntry } from './features/source-control/sourceControlModels';
import WorktreesScreen from './features/source-control/WorktreesScreen';
import AddFolderSource from './features/workspace/AddFolderSource';
import { useViewStore } from './features/workspace/viewStore';
import { bootstrapWorkspace } from './features/workspace/workspaceBootstrap';
import {
  fetchFolderSources,
  fetchWorkspaceDocuments,
  type WorkspaceDocumentDto,
} from './features/workspace/workspaceApi';

interface ShellGlobals {
  __DOCUMENTIDE_WORKSPACE_ID__?: string;
}

/** Host-injected workspace id; `default` keeps the shell usable standalone. */
function resolveWorkspaceId(): string {
  const globals = window as Window & ShellGlobals;
  return globals.__DOCUMENTIDE_WORKSPACE_ID__ || 'default';
}

function useViewState() {
  return useSyncExternalStore(
    useViewStore.subscribe,
    useViewStore.getState,
    useViewStore.getState,
  );
}

function useCanvasState() {
  return useSyncExternalStore(
    useCanvasStore.subscribe,
    useCanvasStore.getState,
    useCanvasStore.getState,
  );
}

function useGraphState() {
  return useSyncExternalStore(
    useGraphStore.subscribe,
    useGraphStore.getState,
    useGraphStore.getState,
  );
}

export default function App() {
  const workspaceId = useMemo(resolveWorkspaceId, []);
  const { activeView, setActiveView, loadPersistedView } = useViewState();
  const { objects, selectedIds, setObjects, upsertObject, clearSelection } = useCanvasState();
  const graph = useGraphState();

  const [activeSurface, setActiveSurface] = useState<ShellSurface>('workspace');
  const [searchOpen, setSearchOpen] = useState(false);
  const [sharesOpen, setSharesOpen] = useState(false);
  const [addFolderOpen, setAddFolderOpen] = useState(false);
  /** UI-03: path prefilled into the existing FS-01 attachment flow. */
  const [attachmentInitialPath, setAttachmentInitialPath] = useState('');
  /**
   * UI-03: the repository path the SCM screens read. It is the first attached
   * folder source (or the first document's contained path when no folder source
   * exists); the screens never scan arbitrary paths.
   */
  const [repositoryPath, setRepositoryPath] = useState<string | null>(null);
  const [docxDiffEntry, setDocxDiffEntry] = useState<SourceControlChangeEntry | null>(null);
  /** UI-04: the true worktree root `GET status` reported for the selected change. */
  const [docxDiffWorktreeRoot, setDocxDiffWorktreeRoot] = useState<string | null>(null);
  const [batchOpen, setBatchOpen] = useState(false);
  const [pdfOpen, setPdfOpen] = useState(false);
  const [markdownDoc, setMarkdownDoc] = useState<{ id: string; title: string } | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [folderFiles, setFolderFiles] = useState<Record<string, FolderPickerFile[]>>({});
  /** Real folder sources from the same bootstrap, for the sidebar. */
  const [folderSources, setFolderSources] = useState<SidebarFolderSource[]>([]);
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [inspectorOpen, setInspectorOpen] = useState(true);
  /**
   * G8: workspace document id → local source path, from the same bootstrap that
   * populates the canvas. Used to resolve the PDF export source; never a new
   * local-path surface (the DTO already carries it for the owner app only).
   */
  const [documentPaths, setDocumentPaths] = useState<Record<string, string>>({});
  /** UI-05 B: the live workspace documents, including availability + last-known path. */
  const [workspaceDocuments, setWorkspaceDocuments] = useState<WorkspaceDocumentDto[]>([]);
  /** UI-05 A: the real pack sources returned for the current selection. */
  const [contextSources, setContextSources] = useState<ContextSourceItem[] | null>(null);
  /** UI-05 B: the document whose locate dialog is open. */
  const [locateDocument, setLocateDocument] = useState<MissingResourceDocument | null>(null);

  /**
   * Document objects produced by the bootstrap, keyed by DocumentId. Folder
   * "release" re-surfaces the referenced documents without re-reading disk.
   */
  const documentObjectsRef = useRef<Map<string, CanvasObject>>(new Map());

  // Restore the last active view on startup (CAN-08 persistence seam).
  useEffect(() => {
    loadPersistedView();
  }, [loadPersistedView]);

  // G6: load the workspace and populate the canvas. A failed load degrades to
  // an empty but valid canvas (bootstrapWorkspace swallows loader failures).
  const loadWorkspace = useCallback(async () => {
    const result = await bootstrapWorkspace(workspaceId, {
      documents: fetchWorkspaceDocuments,
      folderSources: fetchFolderSources,
    });
    documentObjectsRef.current = new Map(result.documentObjects.map((doc) => [doc.id, doc]));
    setFolderFiles(result.folderFiles);
    setFolderSources(
      result.folderSources.map((source) => ({
        id: source.id,
        title: source.displayName || source.id,
        documentCount: (result.folderFiles[source.id] ?? []).length,
      })),
    );
    setDocumentPaths(
      Object.fromEntries(
        result.documents
          .filter((doc) => Boolean(doc.currentPath))
          .map((doc) => [doc.id, doc.currentPath as string]),
      ),
    );
    setWorkspaceDocuments(result.documents);
    const primaryFolder = result.folderSources.find((source) => Boolean(source.canonicalPath));
    const primaryDocument = result.documents.find((doc) => Boolean(doc.currentPath));
    setRepositoryPath(primaryFolder?.canonicalPath ?? primaryDocument?.currentPath ?? null);
    setObjects(result.objects);
  }, [workspaceId, setObjects]);

  useEffect(() => {
    void loadWorkspace();
  }, [loadWorkspace]);

  // G6: releasing files from a folder picker surfaces the referenced documents
  // as canvas objects (metadata only — never a physical move).
  const handleReleaseDocuments = useCallback(
    (_folderId: string, documentIds: string[]) => {
      for (const id of documentIds) {
        const object = documentObjectsRef.current.get(id);
        if (object) upsertObject(object);
      }
    },
    [upsertObject],
  );

  const handleOpenInWord = useCallback(async (documentId: string) => {
    const result = await openInWord(documentId);
    if (!result.success) {
      setStatusMessage(result.errorMessage);
    }
  }, []);

  const handleOpenMarkdown = useCallback((node: GraphNode | { id: string; title: string }) => {
    setStatusMessage(null);
    setMarkdownDoc({ id: node.id, title: node.title });
  }, []);

  // G5: documents selectable as explicit share members come from the same
  // canvas objects the rest of the app uses — no parallel document store.
  const shareDocuments = useMemo(
    () =>
      objects
        .filter((object) => object.type === 'document')
        .map((object) => ({ id: object.id, displayName: object.title })),
    [objects],
  );
  const selectedDocumentIds = useMemo(() => Array.from(selectedIds), [selectedIds]);

  /**
   * UI-05 A: the ordered document selection that feeds the context pack. The
   * explicit order is the canvas object order the user sees, and folders are
   * excluded (the pack route takes document ids only).
   */
  const selectedPackDocumentIds = useMemo(
    () =>
      objects
        .filter((object) => object.type === 'document' && selectedIds.has(object.id))
        .map((object) => object.id),
    [objects, selectedIds],
  );

  /** UI-05 B: documents the workspace itself reports as unavailable. */
  const missingDocuments = useMemo(
    () => missingDocumentsFrom(workspaceDocuments),
    [workspaceDocuments],
  );

  const handleOpenContextPack = useCallback(async () => {
    if (selectedPackDocumentIds.length === 0) return;

    setStatusMessage(null);
    try {
      const pack = await fetchContextPack(selectedPackDocumentIds);
      setContextSources(toContextSources(pack));
    } catch (error) {
      // A typed refusal is surfaced as-is; no empty dialog is opened.
      setStatusMessage(
        error instanceof Error ? error.message : 'Failed to build the context pack.',
      );
    }
  }, [selectedPackDocumentIds]);

  const handleLocateMissing = useCallback(
    (documentId: string) => {
      const document = workspaceDocuments.find((candidate) => candidate.id === documentId);
      if (!document) return;

      setLocateDocument({
        id: document.id,
        filename: document.filename,
        currentPath: document.currentPath,
        lastKnownPath: document.lastKnownPath,
      });
    },
    [workspaceDocuments],
  );

  const handleRemoveReference = useCallback(
    async (documentId: string) => {
      setStatusMessage(null);
      try {
        await removeDocumentReference(documentId);
        if (selectedIds.has(documentId)) clearSelection();
        await loadWorkspace();
        setStatusMessage('Removed the reference. The file on disk was not changed.');
      } catch (error) {
        setStatusMessage(
          error instanceof Error ? error.message : 'Failed to remove the document reference.',
        );
      }
    },
    [clearSelection, loadWorkspace, selectedIds],
  );

  const handleRebound = useCallback(async () => {
    setLocateDocument(null);
    await loadWorkspace();
  }, [loadWorkspace]);

  /**
   * G8: the PDF export selection is the current canvas selection, mapped to the
   * presentational surface. `kind` comes from the canvas doc type, so a
   * non-DOCX selection simply offers no export (PDF stays output-only).
   */
  const pdfSelection = useMemo<PdfSelectableDocument[]>(
    () =>
      objects
        .filter((object) => object.type === 'document' && selectedIds.has(object.id))
        .map((object) => ({
          id: object.id,
          name: object.title,
          kind: object.docType ?? 'unknown',
        })),
    [objects, selectedIds],
  );

  /** The same selection the canvas renders, as inspector-ready objects. */
  const selectedObjects = useMemo(
    () => objects.filter((object) => selectedIds.has(object.id)),
    [objects, selectedIds],
  );

  const resolveDocumentPath = useCallback(
    (documentId: string) => documentPaths[documentId],
    [documentPaths],
  );

  /** The toolbar/sidebar view switch always returns to the workspace surface. */
  const handleViewChange = useCallback(
    (view: 'canvas' | 'list' | 'graph') => {
      setActiveView(view);
      setActiveSurface('workspace');
    },
    [setActiveView],
  );

  const handleNavigate = useCallback((surface: ShellSurface) => {
    setActiveSurface(surface);
  }, []);

  const handleAttachFolder = useCallback((path = '') => {
    setAttachmentInitialPath(path);
    setAddFolderOpen(true);
  }, []);

  const handleOpenMarkdownById = useCallback(
    (documentId: string) => {
      const object = objects.find((candidate) => candidate.id === documentId);
      handleOpenMarkdown({ id: documentId, title: object?.title ?? documentId });
    },
    [handleOpenMarkdown, objects],
  );

  /** UI-03 part B: document name for history rows (the same title the app shows). */
  const resolveDocumentName = useCallback(
    (documentId: string) =>
      objects.find((candidate) => candidate.id === documentId)?.title ?? documentId,
    [objects],
  );

  /**
   * UI-03 part B: a history row links a changed file back to its document. DOCX
   * goes through the existing open-in-Word action; everything else opens the
   * Markdown editor. The history payload carries only ids, so this resolver is
   * what keeps the link honest for each document type.
   */
  const handleOpenHistoryDocument = useCallback(
    (documentId: string) => {
      const object = objects.find((candidate) => candidate.id === documentId);
      if (object?.docType === 'docx') {
        void handleOpenInWord(documentId);
        return;
      }
      handleOpenMarkdownById(documentId);
    },
    [handleOpenInWord, handleOpenMarkdownById, objects],
  );

  return (
    <div className="flex h-screen w-screen flex-col overflow-hidden bg-mac-content text-mac-text">
      <Toolbar
        activeView={activeView}
        onViewChange={handleViewChange}
        sidebarOpen={sidebarOpen}
        onToggleSidebar={() => setSidebarOpen((open) => !open)}
        inspectorOpen={inspectorOpen}
        onToggleInspector={() => setInspectorOpen((open) => !open)}
        onSearchSubmit={() => setSearchOpen(true)}
        onToggleBatch={() => setBatchOpen((open) => !open)}
        batchOpen={batchOpen}
        onTogglePdf={() => setPdfOpen((open) => !open)}
        pdfOpen={pdfOpen}
        onOpenContextPack={() => void handleOpenContextPack()}
        contextPackEnabled={selectedPackDocumentIds.length > 0}
      />

      {statusMessage && (
        <div
          role="status"
          className="flex items-center justify-between gap-2 border-b border-mac-warning/40 bg-mac-warning/10 px-3 py-1.5 text-xs text-mac-text"
        >
          <span>{statusMessage}</span>
          <button
            type="button"
            aria-label="Dismiss message"
            onClick={() => setStatusMessage(null)}
            className="text-mac-text-2 hover:text-mac-text"
          >
            &times;
          </button>
        </div>
      )}

      <div className="relative flex min-h-0 flex-1">
        {sidebarOpen && (
          <Sidebar
            activeView={activeView}
            onViewChange={handleViewChange}
            folderSources={folderSources}
            onAddFolder={() => {
              setAttachmentInitialPath('');
              setAddFolderOpen((open) => !open);
            }}
            addFolderOpen={addFolderOpen}
            onToggleSearch={() => setSearchOpen((open) => !open)}
            searchOpen={searchOpen}
            onToggleShares={() => setSharesOpen((open) => !open)}
            sharesOpen={sharesOpen}
            activeSurface={activeSurface}
            onNavigate={handleNavigate}
          />
        )}

        <main
          aria-label="Workspace content"
          className="relative flex min-w-0 flex-1 bg-mac-content"
        >
          <div className="min-w-0 flex-1">
            {activeSurface === 'workspace' && (
              <>
                {activeView === 'canvas' && (
                  <DocumentCanvas
                    workspaceId={workspaceId}
                    folderFiles={folderFiles}
                    onReleaseDocuments={handleReleaseDocuments}
                    onOpenMarkdownDocument={handleOpenMarkdownById}
                  />
                )}
                {activeView === 'list' && <DocumentList />}
                {activeView === 'graph' && (
                  <DocumentGraph
                    workspaceId={workspaceId}
                    onOpenDocument={handleOpenMarkdown}
                    onOpenInWord={(documentId) => void handleOpenInWord(documentId)}
                  />
                )}
              </>
            )}

            {activeSurface === 'source-control' && (
              <SourceControlScreen
                path={repositoryPath}
                inspectorOpen={inspectorOpen}
                onOpenWorktrees={() => setActiveSurface('worktrees')}
                onOpenDocxDiff={(entry, worktreeRoot) => {
                  setDocxDiffEntry(entry);
                  setDocxDiffWorktreeRoot(worktreeRoot);
                  setActiveSurface('docx-diff');
                }}
                onOpenInWord={(documentId) => void handleOpenInWord(documentId)}
                onOpenMarkdown={handleOpenMarkdownById}
                onAttachFolder={() => handleAttachFolder('')}
              />
            )}

            {activeSurface === 'worktrees' && (
              <WorktreesScreen
                path={repositoryPath}
                workspaceId={workspaceId}
                inspectorOpen={inspectorOpen}
                onRequestAttachment={(path) => handleAttachFolder(path)}
              />
            )}

            {activeSurface === 'docx-diff' && docxDiffEntry && (
              <DocxDiffScreen
                entry={docxDiffEntry}
                worktreeRoot={docxDiffWorktreeRoot}
                inspectorOpen={inspectorOpen}
                onBack={() => setActiveSurface('source-control')}
              />
            )}
            {activeSurface === 'docx-diff' && !docxDiffEntry && (
              <div className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center">
                <p className="text-xs text-mac-text-2">
                  No document change is selected for the document diff.
                </p>
                <button
                  type="button"
                  onClick={() => setActiveSurface('source-control')}
                  className="rounded-lg border border-mac-hairline-strong bg-mac-content px-3 py-1.5 text-xs font-medium text-mac-text hover:bg-mac-chip"
                >
                  Back to source control
                </button>
              </div>
            )}

            {activeSurface === 'history' && (
              <HistoryScreen
                path={repositoryPath}
                inspectorOpen={inspectorOpen}
                onAttachFolder={() => handleAttachFolder('')}
                onOpenDocument={handleOpenHistoryDocument}
                documentName={resolveDocumentName}
                // HIST-04: the real read-only listing + storage routes. Rows,
                // outcome chips and the meter only ever come from that payload;
                // a failed read falls back to the typed unavailable state.
                source={operationHistorySource}
              />
            )}
          </div>

          {/* UI-05 B: real missing documents from `availabilityStatus`, over the
              workspace surface where documents are already rendered. */}
          {activeSurface === 'workspace' && missingDocuments.length > 0 && (
            <MissingResourcesPanel
              documents={missingDocuments}
              onLocate={handleLocateMissing}
              onRemove={(documentId) => void handleRemoveReference(documentId)}
            />
          )}

          {searchOpen && (
            <SearchPanel
              onClosePanel={() => setSearchOpen(false)}
              onSelectDocument={() => handleViewChange('canvas')}
              onInvokeBatchOperation={() => setBatchOpen(true)}
            />
          )}

          {addFolderOpen && (
            <aside
              aria-label="Add folder source"
              className="w-96 max-w-full overflow-y-auto border-l border-mac-hairline bg-mac-content p-3"
            >
              <AddFolderSource
                workspaceId={workspaceId}
                initialPath={attachmentInitialPath}
                onFolderAttached={() => void loadWorkspace()}
              />
            </aside>
          )}

          {sharesOpen && (
            <SharePanel
              availableDocuments={shareDocuments}
              initialSelectedIds={selectedDocumentIds}
              onClose={() => setSharesOpen(false)}
            />
          )}

          {batchOpen && <BatchPreviewPanel onClose={() => setBatchOpen(false)} />}

          {pdfOpen && (
            <PdfExportPanel
              selection={pdfSelection}
              resolveSourcePath={resolveDocumentPath}
              onClose={() => setPdfOpen(false)}
            />
          )}

          {markdownDoc && (
            <div className="absolute inset-0 z-40 bg-mac-content" data-testid="markdown-overlay">
              <MarkdownEditor documentId={markdownDoc.id} onClose={() => setMarkdownDoc(null)} />
            </div>
          )}

          {/* UI-05 A: copy-only context pack. No export callback is supplied,
              so the dialog renders no export control at all. */}
          {contextSources && (
            <ContextPackDialog
              isOpen
              onClose={() => setContextSources(null)}
              sources={contextSources}
            />
          )}

          {/* UI-05 B: the locate flow, now using the authenticated loopback client. */}
          {locateDocument && (
            <LocateMovedFileDialog
              isOpen
              documentId={locateDocument.id}
              filename={locateDocument.filename}
              lastKnownPath={
                locateDocument.lastKnownPath ||
                locateDocument.currentPath ||
                locateDocument.filename
              }
              onClose={() => setLocateDocument(null)}
              onRebound={() => void handleRebound()}
            />
          )}
        </main>

        {activeSurface === 'workspace' && inspectorOpen && (
          <Inspector
            activeView={activeView}
            objects={objects}
            selection={selectedObjects}
            graphNodeCount={graph.nodes.length}
            graphEdgeCount={graph.edges.length}
          />
        )}
      </div>
    </div>
  );
}
