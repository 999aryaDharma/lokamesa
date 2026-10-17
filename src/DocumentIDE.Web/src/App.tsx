/**
 * Application shell.
 *
 * Composes the shipped product surfaces and is the single place that wires the
 * previously unreachable subsystems to real data (REL-03 gaps G1/G3/G5/G6/G7):
 *
 *   - G6: on startup the shell loads the workspace's documents and folder
 *     sources through the privileged API and populates the canvas store
 *     (`setObjects`); released folder files are surfaced with `upsertObject`.
 *     Dragging still never moves a physical file.
 *   - G3: the search panel mounts from the tool bar and is backed by the
 *     existing `searchStore` (its `/api/search` contract is unchanged).
 *   - G5: the owner share panel mounts from the tool bar and drives the share
 *     lifecycle API (create / stop / revoke / regenerate / membership).
 *   - G1: the graph view loads the workspace graph and loads edges with truthful
 *     explicit-vs-inferred semantics.
 *   - G7: double-clicking a Markdown graph node or canvas card opens the
 *     internal Markdown editor overlay (source-first, safe-write via the API).
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
import { openInWord } from './features/files/OpenInWordAction';
import DocumentGraph from './features/graph/DocumentGraph';
import type { GraphNode } from './features/graph/graphStore';
import DocumentList from './features/list/DocumentList';
import MarkdownEditor from './features/markdown/MarkdownEditor';
import { SearchPanel } from './features/search/SearchPanel';
import { SharePanel } from './features/sharing/SharePanel';
import AddFolderSource from './features/workspace/AddFolderSource';
import ViewSwitcher from './features/workspace/ViewSwitcher';
import { useViewStore } from './features/workspace/viewStore';
import { bootstrapWorkspace } from './features/workspace/workspaceBootstrap';
import { fetchFolderSources, fetchWorkspaceDocuments } from './features/workspace/workspaceApi';

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

export default function App() {
  const workspaceId = useMemo(resolveWorkspaceId, []);
  const { activeView, setActiveView, loadPersistedView } = useViewState();
  const { objects, selectedIds, setObjects, upsertObject } = useCanvasState();

  const [searchOpen, setSearchOpen] = useState(false);
  const [sharesOpen, setSharesOpen] = useState(false);
  const [addFolderOpen, setAddFolderOpen] = useState(false);
  const [batchOpen, setBatchOpen] = useState(false);
  const [markdownDoc, setMarkdownDoc] = useState<{ id: string; title: string } | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [folderFiles, setFolderFiles] = useState<Record<string, FolderPickerFile[]>>({});

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

  return (
    <div className="w-screen h-screen overflow-hidden bg-slate-50 flex flex-col">
      <nav
        onKeyDown={(event) => event.stopPropagation()}
        aria-label="Tools"
        className="border-b border-slate-200 bg-white p-2 flex items-center gap-3"
      >
        <ViewSwitcher activeView={activeView} onViewChange={setActiveView} />
        <button
          type="button"
          aria-pressed={searchOpen}
          onClick={() => setSearchOpen((open) => !open)}
        >
          Search
        </button>
        <button
          type="button"
          aria-pressed={sharesOpen}
          onClick={() => setSharesOpen((open) => !open)}
        >
          Shares
        </button>
        <button
          type="button"
          aria-pressed={addFolderOpen}
          onClick={() => setAddFolderOpen((open) => !open)}
        >
          Add folder
        </button>
        <button
          type="button"
          aria-expanded={batchOpen}
          onClick={() => setBatchOpen((open) => !open)}
        >
          Batch operations
        </button>
      </nav>

      {statusMessage && (
        <div
          role="status"
          className="flex items-center justify-between gap-2 px-3 py-1.5 text-xs text-amber-800 bg-amber-50 border-b border-amber-200"
        >
          <span>{statusMessage}</span>
          <button
            type="button"
            aria-label="Dismiss message"
            onClick={() => setStatusMessage(null)}
            className="text-amber-700 hover:text-amber-900"
          >
            &times;
          </button>
        </div>
      )}

      <div className="flex flex-1 min-h-0 relative">
        {searchOpen && (
          <SearchPanel
            onClosePanel={() => setSearchOpen(false)}
            onSelectDocument={() => setActiveView('canvas')}
            onInvokeBatchOperation={() => setBatchOpen(true)}
          />
        )}

        <div className="flex-1 min-w-0">
          {activeView === 'canvas' && (
            <DocumentCanvas
              workspaceId={workspaceId}
              folderFiles={folderFiles}
              onReleaseDocuments={handleReleaseDocuments}
              onOpenMarkdownDocument={(documentId) => {
                const object = objects.find((candidate) => candidate.id === documentId);
                handleOpenMarkdown({ id: documentId, title: object?.title ?? documentId });
              }}
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
        </div>

        {addFolderOpen && (
          <aside
            aria-label="Add folder source"
            className="w-96 max-w-full bg-white border-l border-slate-200 overflow-y-auto p-3"
          >
            <AddFolderSource
              workspaceId={workspaceId}
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

        {batchOpen && <BatchPreviewPanel />}

        {markdownDoc && (
          <div className="absolute inset-0 z-40 bg-white" data-testid="markdown-overlay">
            <MarkdownEditor
              documentId={markdownDoc.id}
              onClose={() => setMarkdownDoc(null)}
            />
          </div>
        )}
      </div>
    </div>
  );
}
