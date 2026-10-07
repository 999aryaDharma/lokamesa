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
import type { CanvasObject } from './types';
import { ZOOM_MAX, ZOOM_MIN } from './types';

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
  /** CAN-04: single-click handler for folder objects (opens the picker). */
  onSingleClick?: (id: string) => void;
  onDoubleClick?: (object: CanvasObject) => void;
}

const CanvasCard = memo(function CanvasCard({
  object,
  isSelected,
  isMatch = false,
  isFaded = false,
  onSelect,
  onSingleClick,
  onDoubleClick,
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
      />
    );
  }

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={`${object.type === 'folder' ? 'Folder' : 'Document'}: ${object.title}`}
      aria-selected={isSelected}
      data-object-id={object.id}
      style={{
        position: 'absolute',
        left: object.x,
        top: object.y,
        width: object.width,
        height: object.height,
        willChange: 'transform',
        opacity: isFaded ? 0.35 : 1.0,
      }}
      className={[
        'rounded-lg border select-none cursor-pointer',
        'bg-white shadow-sm flex flex-col items-start justify-start p-3',
        'transition-all duration-100',
        isMatch
          ? 'border-blue-500 ring-2 ring-blue-400 shadow-md'
          : isSelected
          ? 'border-blue-400 ring-2 ring-blue-200 shadow-md'
          : 'border-slate-200 hover:border-slate-300 hover:shadow-md',
      ].join(' ')}
      onClick={() => {
        if (onSingleClick) {
          onSingleClick(object.id);
        } else {
          onSelect(object.id);
        }
      }}
      onDoubleClick={handleDoubleClick}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          if (onSingleClick) {
            onSingleClick(object.id);
          } else {
            onSelect(object.id);
          }
        }
      }}
    >
      {/* Search match pill indicator */}
      {isMatch && (
        <span
          data-testid={`match-badge-${object.id}`}
          className="absolute top-1.5 right-1.5 text-[9px] font-bold bg-blue-500 text-white px-1.5 py-0.5 rounded shadow-xs"
        >
          MATCH
        </span>
      )}
      {/* Object type icon */}
      <span className="text-xl mb-1" aria-hidden="true">
        {object.type === 'folder' ? '📁' : '📄'}
      </span>
      {/* Title — capped so it doesn't overflow */}
      <span className="text-sm font-medium text-slate-800 leading-snug line-clamp-2 w-full">
        {object.title}
      </span>
      <span className="text-xs text-slate-400 mt-auto">
        {object.type === 'folder' ? 'Folder' : 'Document'}
      </span>
    </div>
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
      className="absolute bottom-4 right-4 z-10 flex items-center gap-1 bg-white/90 backdrop-blur-sm border border-slate-200 rounded-lg shadow-sm px-2 py-1.5"
      aria-label="Zoom controls"
    >
      <button
        type="button"
        aria-label="Zoom out"
        onClick={onZoomOut}
        disabled={zoom <= ZOOM_MIN}
        className="w-7 h-7 flex items-center justify-center rounded hover:bg-slate-100 disabled:opacity-40 text-slate-600 text-base font-medium transition-colors"
      >
        −
      </button>
      <button
        type="button"
        aria-label={`Zoom level ${Math.round(zoom * 100)}%. Click to reset.`}
        onClick={onReset}
        className="min-w-[3.5rem] text-center text-xs font-mono text-slate-700 hover:bg-slate-100 rounded px-1 py-0.5 transition-colors"
      >
        {Math.round(zoom * 100)}%
      </button>
      <button
        type="button"
        aria-label="Zoom in"
        onClick={onZoomIn}
        disabled={zoom >= ZOOM_MAX}
        className="w-7 h-7 flex items-center justify-center rounded hover:bg-slate-100 disabled:opacity-40 text-slate-600 text-base font-medium transition-colors"
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
 * CAN-04: delay used to arbitrate a folder single click vs. double click.
 * Opening the picker synchronously on the first click would swallow the second
 * click of a real double-click (the popover overlays the folder card and
 * intercepts it), so the single-click action is deferred by this window; a
 * dblclick on the same folder cancels it and runs "Open all" instead.
 */
const FOLDER_CLICK_OPEN_DELAY_MS = 250;

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
}

export default function DocumentCanvas({ folderFiles, onReleaseDocuments }: DocumentCanvasProps = {}) {
  const state = useCanvasState();
  const searchState = useSearchState();
  const { viewport, objects, selectedIds, setViewport, toggleSelect, loadPersistedViewport } = state;
  const [pickerFolderId, setPickerFolderId] = useState<string | null>(null);

  /**
   * CAN-04: pending deferred single-click action for one folder, cancelled by
   * a second click / dblclick on the same folder.
   */
  const pendingFolderClickRef = useRef<{
    folderId: string;
    timer: ReturnType<typeof setTimeout>;
  } | null>(null);

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
      toggleSelect(id);
    },
    [toggleSelect],
  );

  // CAN-04: cancel a pending deferred folder single-click action (if any).
  // Called when the second click of a double click arrives, on dblclick, and
  // on unmount.
  const cancelPendingFolderClick = useCallback(() => {
    if (pendingFolderClickRef.current) {
      clearTimeout(pendingFolderClickRef.current.timer);
      pendingFolderClickRef.current = null;
    }
  }, []);

  useEffect(() => cancelPendingFolderClick, [cancelPendingFolderClick]);

  // CAN-04: single click on a folder opens the lightweight picker instead of
  // dumping every file onto the canvas. The action is deferred by a short
  // window so a real double-click keeps its second click on the card (the
  // popover would otherwise intercept it) and cancels the deferred open.
  const handleFolderSingleClick = useCallback(
    (id: string) => {
      // Second click of a real double click: cancel the deferred single-click
      // action; the dblclick handler performs "Open all" instead. Selection is
      // therefore never toggled twice for one double click.
      if (pendingFolderClickRef.current?.folderId === id) {
        cancelPendingFolderClick();
        return;
      }

      cancelPendingFolderClick();

      pendingFolderClickRef.current = {
        folderId: id,
        timer: setTimeout(() => {
          pendingFolderClickRef.current = null;
          toggleSelect(id);
          setPickerFolderId(id);
        }, FOLDER_CLICK_OPEN_DELAY_MS),
      };
    },
    [cancelPendingFolderClick, toggleSelect],
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
        // CAN-04: double-click is the intentional "Open all" fast path. Any
        // deferred single-click open is cancelled so the picker never opens and
        // the folder's selection state is left unchanged (no double toggle).
        cancelPendingFolderClick();
        setPickerFolderId(null);
        onReleaseDocuments?.(obj.id, supportedFolderDocumentIds(obj.id));
        return;
      }
      if (obj.type === 'document' || obj.title.endsWith('.docx') || obj.title.endsWith('.doc')) {
        const res = await openInWord(obj.id);
        if (!res.success) {
          alert(res.errorMessage);
        }
      }
    },
    [cancelPendingFolderClick, onReleaseDocuments, supportedFolderDocumentIds],
  );

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------
  return (
    <div
      className="relative w-full h-full overflow-hidden"
      style={{ background: '#F8FAFC' }}
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
            'radial-gradient(circle, #CBD5E1 1px, transparent 1px)',
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
                onSingleClick={obj.type === 'folder' ? handleFolderSingleClick : undefined}
                onDoubleClick={handleCardDoubleClick}
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
            <p className="text-slate-400 text-sm font-medium opacity-60 select-none">
              Your workspace canvas is empty
            </p>
            <p className="text-slate-300 text-xs mt-1 opacity-50 select-none">
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
