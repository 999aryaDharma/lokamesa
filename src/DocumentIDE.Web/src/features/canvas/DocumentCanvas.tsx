/**
 * CAN-01: Canvas Foundation — DocumentCanvas React component
 *
 * Renders the clean white spatial workspace canvas with:
 * - Subtle dot-grid background pattern (CSS background)
 * - Pan (middle-click drag, space+drag, or background drag)
 * - Mouse-wheel zoom toward cursor position
 * - Floating zoom toolbar (zoom %, zoom in, zoom out, reset)
 * - Canvas objects rendered with CSS transform (translate + scale)
 * - Memoized object cards for 100-object performance
 *
 * IMPORTANT: This MUST NOT look like a dashboard.
 * No statistic cards, no metric panels, no permanent metadata banners.
 */
import React, {
  memo,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
} from 'react';
import { openInWord } from '../files/OpenInWordAction';
import { useSearchStore } from '../search/searchStore';
import { canvasStoreInstance, useCanvasStore } from './canvasStore';
import FolderPicker, { isSupportedFolderFile, type FolderPickerFile } from './FolderPicker';
import DocumentNode from './nodes/DocumentNode';
import FolderObject from './nodes/FolderObject';
import { applyClick, groupDrag } from './selection/SelectionController';
import type { CanvasObject } from './types';
import { ZOOM_MAX, ZOOM_MIN } from './types';
import { recordDrag } from './usePersistedPosition';

// ---------------------------------------------------------------------------
// useCanvasState — subscribe to store using React 18 useSyncExternalStore
// ---------------------------------------------------------------------------
function useCanvasState() {
  return useSyncExternalStore(
    useCanvasStore.subscribe,
    useCanvasStore.getState,
    useCanvasStore.getState,
  );
}

function useSearchState() {
  return useSyncExternalStore(
    useSearchStore.subscribe,
    useSearchStore.getState,
    useSearchStore.getState,
  );
}

// ---------------------------------------------------------------------------
// CanvasCard — memoized object card
// ---------------------------------------------------------------------------
interface CanvasCardProps {
  object: CanvasObject;
  isSelected: boolean;
  isSearchActive?: boolean;
  isMatch?: boolean;
  isFaded?: boolean;
  onSelect: (id: string) => void;
  /**
   * UI-01: the documents that path-belong to this folder object, supplied by the
   * host (`folderFiles` ← `groupDocumentsByFolder`). A folder fans out exactly
   * these and nothing else.
   */
  folderDocuments?: FolderPickerFile[];
  /** CAN-04: the folder's "open picker" affordance. */
  onOpenPicker?: (id: string) => void;
  /** CAN-04: release one document out of the folder (metadata only). */
  onReleaseDocument?: (documentId: string) => void;
  onDoubleClick?: (object: CanvasObject) => void;
  /** CAN-07: begin a group drag from this card. */
  onDragStart?: (id: string, event: React.MouseEvent) => void;
}

const CanvasCard = memo(function CanvasCard({
  object,
  isSelected,
  isMatch = false,
  isFaded = false,
  onSelect,
  folderDocuments,
  onOpenPicker,
  onReleaseDocument,
  onDoubleClick,
  onDragStart,
}: CanvasCardProps) {
  const handleDoubleClick = useCallback(
    (e?: React.MouseEvent) => {
      e?.stopPropagation();
      if (onDoubleClick) {
        onDoubleClick(object);
      }
    },
    [onDoubleClick, object]
  );

  if (object.type === 'document') {
    return (
      <DocumentNode
        id={object.id}
        title={object.title}
        x={object.x}
        y={object.y}
        width={object.width}
        height={object.height}
        isSelected={isSelected}
        isMissing={object.isMissing}
        isFaded={isFaded}
        isMatch={isMatch}
        docType={object.docType}
        thumbnailState={object.thumbnailState}
        thumbnailUrl={object.thumbnailUrl}
        thumbnailContent={object.thumbnailContent}
        statusText={object.statusText}
        onSelect={onSelect}
        onDoubleClick={() => handleDoubleClick()}
        onMouseDown={(e) => onDragStart?.(object.id, e)}
      />
    );
  }

  // UI-01: a folder is an object you can open, not a look-alike card.
  return (
    <FolderObject
      id={object.id}
      title={object.title}
      x={object.x}
      y={object.y}
      width={object.width}
      height={object.height}
      isSelected={isSelected}
      containedDocuments={folderDocuments ?? []}
      onSelect={onSelect}
      onOpenPicker={onOpenPicker}
      onReleaseDocument={onReleaseDocument}
      onDoubleClick={() => handleDoubleClick()}
    />
  );
});

// ---------------------------------------------------------------------------
// ZoomToolbar — floating zoom controls
// ---------------------------------------------------------------------------
interface ZoomToolbarProps {
  zoom: number;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onReset: () => void;
}

function ZoomToolbar({ zoom, onZoomIn, onZoomOut, onReset }: ZoomToolbarProps) {
  return (
    <div
      className="absolute bottom-4 right-4 z-10 flex items-center gap-1 bg-mac-content/90 backdrop-blur-sm border border-mac-hairline rounded-lg shadow-sm px-2 py-1.5"
      aria-label="Zoom controls"
    >
      <button
        type="button"
        aria-label="Zoom out"
        onClick={onZoomOut}
        disabled={zoom <= ZOOM_MIN}
        className="w-7 h-7 flex items-center justify-center rounded hover:bg-mac-chip disabled:opacity-40 text-mac-text-2 text-base font-medium transition-colors"
      >
        −
      </button>
      <button
        type="button"
        aria-label={`Zoom level ${Math.round(zoom * 100)}%. Click to reset.`}
        onClick={onReset}
        className="min-w-[3.5rem] text-center text-xs font-mono text-mac-text hover:bg-mac-chip rounded px-1 py-0.5 transition-colors"
      >
        {Math.round(zoom * 100)}%
      </button>
      <button
        type="button"
        aria-label="Zoom in"
        onClick={onZoomIn}
        disabled={zoom >= ZOOM_MAX}
        className="w-7 h-7 flex items-center justify-center rounded hover:bg-mac-chip disabled:opacity-40 text-mac-text-2 text-base font-medium transition-colors"
      >
        +
      </button>
    </div>
  );
}

// ---------------------------------------------------------------------------
// DocumentCanvas — main component
// ---------------------------------------------------------------------------
const ZOOM_STEP = 0.1;
const WHEEL_SENSITIVITY = 0.001;

/**
 * MD-01 (gap G7): a document object is Markdown when its explicit docType is
 * `md`, or (when the type is unknown) when its title ends in a Markdown
 * extension. An explicit `docx` is never treated as Markdown.
 */
function isMarkdownObject(obj: CanvasObject): boolean {
  if (obj.docType === 'md') return true;
  if (obj.docType === 'docx') return false;
  return /\.(md|markdown)$/i.test(obj.title);
}

export interface DocumentCanvasProps {
  /**
   * CAN-04: supported files per folder object id, supplied by the host.
   * The canvas itself never reads the filesystem.
   */
  folderFiles?: Record<string, FolderPickerFile[]>;
  /**
   * CAN-04: invoked when the user releases documents from a folder picker.
   * `documentIds` are the released workspace documents (subset or all);
   * this never physically moves files.
   */
  onReleaseDocuments?: (folderId: string, documentIds: string[]) => void;
  /**
   * CAN-06: workspace id used to persist manually dragged positions to
   * localStorage (keyed by stable DocumentId). Drag here is metadata only.
   */
  workspaceId?: string;
  /**
   * MD-01 (gap G7): double-click on a Markdown document opens the internal
   * source editor instead of launching Word. Omitted ⇒ Word open (legacy).
   */
  onOpenMarkdownDocument?: (documentId: string) => void;
}

export default function DocumentCanvas({
  folderFiles,
  onReleaseDocuments,
  workspaceId,
  onOpenMarkdownDocument,
}: DocumentCanvasProps = {}) {
  const state = useCanvasState();
  const searchState = useSearchState();
  const {
    viewport,
    objects,
    selectedIds,
    setViewport,
    toggleSelect,
    setSelection,
    upsertObject,
    loadPersistedViewport,
  } = state;
  const [pickerFolderId, setPickerFolderId] = useState<string | null>(null);

  const isSearchHighlighting = searchState.isSearchActive && searchState.query.trim().length > 0;
  const matchingDocIds = useMemo(() => {
    if (!isSearchHighlighting) return new Set<string>();
    return new Set(searchState.results.map((r) => r.documentId));
  }, [isSearchHighlighting, searchState.results]);

  // Load persisted viewport on first mount
  useEffect(() => {
    loadPersistedViewport();
  }, [loadPersistedViewport]);

  // Pan state tracking
  const isPanning = useRef(false);
  const panStart = useRef({ x: 0, y: 0, vpX: 0, vpY: 0 });
  const isSpaceDown = useRef(false);
  const surfaceRef = useRef<HTMLDivElement>(null);

  // ---------------------------------------------------------------------------
  // Keyboard: Space for pan mode
  // ---------------------------------------------------------------------------
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.code === 'Space' && !e.repeat) {
        e.preventDefault();
        isSpaceDown.current = true;
        surfaceRef.current?.style.setProperty('cursor', 'grab');
      }
    };
    const onKeyUp = (e: KeyboardEvent) => {
      if (e.code === 'Space') {
        isSpaceDown.current = false;
        if (!isPanning.current) {
          surfaceRef.current?.style.setProperty('cursor', 'default');
        }
      }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
    };
  }, []);

  // ---------------------------------------------------------------------------
  // Mouse wheel zoom toward cursor
  // ---------------------------------------------------------------------------
  const onWheel = useCallback(
    (e: React.WheelEvent<HTMLDivElement>) => {
      e.preventDefault();
      const rect = (e.currentTarget as HTMLDivElement).getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      const mouseY = e.clientY - rect.top;

      const delta = -e.deltaY * WHEEL_SENSITIVITY;
      const rawZoom = viewport.zoom * (1 + delta * 10);
      const newZoom = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, rawZoom));

      // Zoom toward mouse position: adjust pan so the point under cursor stays fixed
      const scaleFactor = newZoom / viewport.zoom;
      const newX = mouseX - scaleFactor * (mouseX - viewport.x);
      const newY = mouseY - scaleFactor * (mouseY - viewport.y);

      setViewport({ x: newX, y: newY, zoom: newZoom });
    },
    [viewport, setViewport],
  );

  // ---------------------------------------------------------------------------
  // Mouse down — start pan (middle button, space+left, or background left drag)
  // ---------------------------------------------------------------------------
  const onMouseDown = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      const isMiddle = e.button === 1;
      const isSpaceDrag = isSpaceDown.current && e.button === 0;
      const isBackgroundDrag = e.button === 0 && (e.target as HTMLElement).dataset['testid'] === 'canvas-surface';

      if (isMiddle || isSpaceDrag || isBackgroundDrag) {
        e.preventDefault();
        isPanning.current = true;
        panStart.current = { x: e.clientX, y: e.clientY, vpX: viewport.x, vpY: viewport.y };
        surfaceRef.current?.style.setProperty('cursor', 'grabbing');
      }
    },
    [viewport.x, viewport.y],
  );

  // ---------------------------------------------------------------------------
  // Mouse move — apply pan
  // ---------------------------------------------------------------------------
  const onMouseMove = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (!isPanning.current) return;
      const dx = e.clientX - panStart.current.x;
      const dy = e.clientY - panStart.current.y;
      setViewport({
        x: panStart.current.vpX + dx,
        y: panStart.current.vpY + dy,
        zoom: viewport.zoom,
      });
    },
    [setViewport, viewport.zoom],
  );

  // ---------------------------------------------------------------------------
  // Mouse up — end pan
  // ---------------------------------------------------------------------------
  const onMouseUp = useCallback(() => {
    if (isPanning.current) {
      isPanning.current = false;
      surfaceRef.current?.style.setProperty('cursor', isSpaceDown.current ? 'grab' : 'default');
    }
  }, []);

  // ---------------------------------------------------------------------------
  // Zoom controls
  // ---------------------------------------------------------------------------
  const handleZoomIn = useCallback(() => {
    const newZoom = Math.min(ZOOM_MAX, viewport.zoom + ZOOM_STEP);
    setViewport({ ...viewport, zoom: newZoom });
  }, [viewport, setViewport]);

  const handleZoomOut = useCallback(() => {
    const newZoom = Math.max(ZOOM_MIN, viewport.zoom - ZOOM_STEP);
    setViewport({ ...viewport, zoom: newZoom });
  }, [viewport, setViewport]);

  const handleResetViewport = useCallback(() => {
    setViewport({ x: 0, y: 0, zoom: 1.0 });
  }, [setViewport]);

  const handleObjectSelect = useCallback(
    (id: string) => {
      // CAN-07 SelectionController: a plain click replaces the selection.
      setSelection(applyClick(Array.from(selectedIds), id, false));
    },
    [selectedIds, setSelection],
  );

  // ---------------------------------------------------------------------------
  // CAN-06 / CAN-07: object drag — multi-select group drag + position persistence
  // ---------------------------------------------------------------------------
  const objectsRef = useRef(objects);
  useEffect(() => {
    objectsRef.current = objects;
  }, [objects]);

  const dragRef = useRef<{
    startX: number;
    startY: number;
    startPositions: Map<string, { x: number; y: number }>;
  } | null>(null);
  const lastMovesRef = useRef<Array<{ id: string; toX: number; toY: number }>>([]);

  const beginObjectDrag = useCallback(
    (id: string, e: React.MouseEvent) => {
      if (e.button !== 0) return;
      e.stopPropagation();
      const currentSelected = Array.from(selectedIds);
      const nextSelection = selectedIds.has(id)
        ? currentSelected
        : applyClick(currentSelected, id, false);
      if (!selectedIds.has(id)) setSelection(nextSelection);

      const startPositions = new Map<string, { x: number; y: number }>();
      for (const object of objectsRef.current) {
        if (nextSelection.includes(object.id)) {
          startPositions.set(object.id, { x: object.x, y: object.y });
        }
      }
      dragRef.current = { startX: e.clientX, startY: e.clientY, startPositions };
      lastMovesRef.current = [];
    },
    [selectedIds, setSelection],
  );

  useEffect(() => {
    const onWindowMove = (e: MouseEvent) => {
      const drag = dragRef.current;
      if (!drag) return;
      const delta = { dx: e.clientX - drag.startX, dy: e.clientY - drag.startY };
      const base = objectsRef.current
        .filter((o) => drag.startPositions.has(o.id))
        .map((o) => ({
          ...o,
          x: drag.startPositions.get(o.id)!.x,
          y: drag.startPositions.get(o.id)!.y,
        }));
      const result = groupDrag(base, Array.from(drag.startPositions.keys()), delta);
      lastMovesRef.current = result.moves.map((move) => ({
        id: move.id,
        toX: move.toX,
        toY: move.toY,
      }));
      for (const move of result.moves) {
        const object = base.find((o) => o.id === move.id);
        if (object) upsertObject({ ...object, x: move.toX, y: move.toY });
      }
    };
    const onWindowUp = () => {
      if (dragRef.current && workspaceId) {
        // CAN-06: manual positions persist across restart, keyed by DocumentId.
        for (const move of lastMovesRef.current) {
          recordDrag(workspaceId, move.id, move.toX, move.toY);
        }
      }
      dragRef.current = null;
      lastMovesRef.current = [];
    };
    window.addEventListener('mousemove', onWindowMove);
    window.addEventListener('mouseup', onWindowUp);
    return () => {
      window.removeEventListener('mousemove', onWindowMove);
      window.removeEventListener('mouseup', onWindowUp);
    };
  }, [upsertObject, workspaceId]);

  // ---------------------------------------------------------------------------
  // CAN-04 / UI-01: the folder object's *picker affordance* opens the
  // lightweight picker instead of dumping every file onto the canvas. The
  // object's own click now opens/closes the folder (the documents fan out), so
  // the picker needs no single-vs-double-click arbitration: it sits behind an
  // explicit control, and `onOpenPicker` can open it synchronously.
  // ---------------------------------------------------------------------------
  const handleFolderPickerOpen = useCallback(
    (id: string) => {
      toggleSelect(id);
      setPickerFolderId(id);
    },
    [toggleSelect],
  );

  const handlePickerClose = useCallback(() => {
    setPickerFolderId(null);
  }, []);

  const handleReleaseDocuments = useCallback(
    (folderId: string, documentIds: string[]) => {
      setPickerFolderId(null);
      onReleaseDocuments?.(folderId, documentIds);
    },
    [onReleaseDocuments],
  );

  const pickerFolder = useMemo(
    () =>
      pickerFolderId
        ? objects.find((o) => o.id === pickerFolderId && o.type === 'folder')
        : undefined,
    [objects, pickerFolderId],
  );

  const supportedFolderDocumentIds = useCallback(
    (folderId: string): string[] =>
      (folderFiles?.[folderId] ?? [])
        .filter(isSupportedFolderFile)
        .map((file) => file.documentId),
    [folderFiles],
  );

  const handleCardDoubleClick = useCallback(
    async (obj: CanvasObject) => {
      if (obj.type === 'folder') {
        // CAN-04: double-click is the intentional "Open all" fast path. The
        // picker never opens and the folder's selection state is left
        // unchanged (no double toggle).
        setPickerFolderId(null);
        onReleaseDocuments?.(obj.id, supportedFolderDocumentIds(obj.id));
        return;
      }
      // MD-01 (gap G7): Markdown opens the internal source editor. DOCX (and
      // legacy .doc) still launch Word.
      if (isMarkdownObject(obj)) {
        if (onOpenMarkdownDocument) {
          onOpenMarkdownDocument(obj.id);
          return;
        }
      }
      if (obj.type === 'document' || obj.title.endsWith('.docx') || obj.title.endsWith('.doc')) {
        const res = await openInWord(obj.id);
        if (!res.success) {
          alert(res.errorMessage);
        }
      }
    },
    [onOpenMarkdownDocument, onReleaseDocuments, supportedFolderDocumentIds],
  );

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------
  return (
    <div
      className="relative w-full h-full overflow-hidden"
      style={{ background: 'var(--color-mac-canvas)' }}
      onWheel={onWheel}
      onMouseDown={onMouseDown}
      onMouseMove={onMouseMove}
      onMouseUp={onMouseUp}
      onMouseLeave={onMouseUp}
    >
      {/* Canvas surface — the scrollable/pannable area with subtle dot grid */}
      <div
        ref={surfaceRef}
        data-testid="canvas-surface"
        className="absolute inset-0 w-full h-full"
        style={{
          backgroundImage:
            'radial-gradient(circle, var(--color-mac-hairline-strong) 1px, transparent 1px)',
          backgroundSize: '24px 24px',
          cursor: 'default',
        }}
      >
        {/* Transform container — all canvas objects live here */}
        <div
          data-testid="canvas-transform"
          style={{
            transform: `translate(${viewport.x}px, ${viewport.y}px) scale(${viewport.zoom})`,
            transformOrigin: '0 0',
            position: 'absolute',
            top: 0,
            left: 0,
            width: 0,
            height: 0,
            willChange: 'transform',
          }}
        >
          {objects.map((obj: CanvasObject) => {
            const isMatch = isSearchHighlighting && obj.type === 'document' && matchingDocIds.has(obj.id);
            const isFaded = isSearchHighlighting && obj.type === 'document' && !isMatch;
            return (
              <CanvasCard
                key={obj.id}
                object={obj}
                isSelected={selectedIds.has(obj.id)}
                isSearchActive={isSearchHighlighting}
                isMatch={isMatch}
                isFaded={isFaded}
                onSelect={handleObjectSelect}
                folderDocuments={obj.type === 'folder' ? folderFiles?.[obj.id] : undefined}
                onOpenPicker={obj.type === 'folder' ? handleFolderPickerOpen : undefined}
                onReleaseDocument={
                  obj.type === 'folder'
                    ? (documentId) => onReleaseDocuments?.(obj.id, [documentId])
                    : undefined
                }
                onDoubleClick={handleCardDoubleClick}
                onDragStart={obj.type === 'document' ? beginObjectDrag : undefined}
              />
            );
          })}
        </div>

        {/* Empty state — only shown when no objects on canvas */}
        {objects.length === 0 && (
          <div
            className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none"
            aria-label="Empty canvas"
          >
            <div className="text-5xl mb-4 opacity-20" aria-hidden="true">📂</div>
            <p className="text-mac-text-3 text-sm font-medium opacity-60 select-none">
              Your workspace canvas is empty
            </p>
            <p className="text-mac-text-3 text-xs mt-1 opacity-50 select-none">
              Attach a folder source to get started
            </p>
          </div>
        )}
      </div>

      {/* CAN-04: folder picker popover (single click on a folder opens it) */}
      {pickerFolder && (
        <div className="absolute top-16 left-6 z-30">
          <FolderPicker
            isOpen
            folderName={pickerFolder.title}
            files={folderFiles?.[pickerFolder.id] ?? []}
            onOpenSelected={(documentIds) => handleReleaseDocuments(pickerFolder.id, documentIds)}
            onOpenAll={() =>
              handleReleaseDocuments(pickerFolder.id, supportedFolderDocumentIds(pickerFolder.id))
            }
            onClose={handlePickerClose}
          />
        </div>
      )}

      {/* Zoom toolbar — floating bottom-right */}
      <ZoomToolbar
        zoom={viewport.zoom}
        onZoomIn={handleZoomIn}
        onZoomOut={handleZoomOut}
        onReset={handleResetViewport}
      />
    </div>
  );
}

// Export store reference for external use and testing
export { canvasStoreInstance };
