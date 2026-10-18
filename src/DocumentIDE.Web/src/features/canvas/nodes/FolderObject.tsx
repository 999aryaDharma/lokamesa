/**
 * UI-01 — interactive folder object (owner directive 2026-10-18).
 *
 * The canvas folder is an *object*, not a static glyph:
 *
 *   closed (default)  → the Finder-style blue folder
 *   click             → the front panel tilts back (`rotateX ≈ -40°`, origin
 *                       bottom) and the documents that path-belong to that
 *                       folder fan out as pages (≈ -8° / 0° / +9°, translated
 *                       up ≈ -55…-75) on a `motion` spring
 *   click again       → closes
 *
 * The fanned pages are the **real** contained documents handed in by the canvas
 * (`folderFiles`, produced once by `groupDocumentsByFolder` / `isWithinFolder`
 * separator-bounded containment). No second membership rule, no decorative
 * placeholders: a folder whose containment list is empty opens to an empty
 * state.
 *
 * Gesture decision (recorded in the UI-01 handoff):
 *   - single click on the object body  → open/close
 *   - the object's own `⌄` affordance (`Open documents in <title>`) → the
 *     existing CAN-04 folder picker, so that wiring stays reachable. It is a
 *     **sibling** of the object button, never nested inside it;
 *   - double click → the existing "open all" release (unchanged). Stated
 *     exactly: a real double click is `click, click, dblclick`, so
 *     `handleClick` runs twice (select + open, then select + close) before
 *     `handleDoubleClick` (closes again — idempotent — and releases every
 *     supported file exactly once);
 *   - drag a fanned page out → the existing CAN-04 release semantic for that
 *     one document. Metadata only: canvas drag never physically moves a file.
 *
 * Selection is exposed with `aria-pressed` (valid on `role="button"`);
 * `aria-selected` is not a supported state for a button role.
 *
 * `prefers-reduced-motion` is honoured: the folder still opens and closes, just
 * without the spring/fan tween.
 */
import { useCallback, useEffect, useRef, useState, type MouseEvent as ReactMouseEvent } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import type { MarkdownDocument } from '../../markdown/markdownApi';
import { documentIconKind } from '../../shell/NodeIcon';
import FilePreview from './FilePreview';

export interface FolderObjectDocument {
  documentId: string;
  filename: string;
}

export interface FolderObjectProps {
  id: string;
  title: string;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  isSelected?: boolean;
  containedDocuments?: ReadonlyArray<FolderObjectDocument>;
  onSelect?: (id: string) => void;
  /** The existing CAN-04 single-click → folder picker wiring. */
  onOpenPicker?: (id: string) => void;
  /** The existing double-click → "open all" release. */
  onDoubleClick?: (id: string) => void;
  /** CAN-04 release of one document. Metadata only. */
  onReleaseDocument?: (documentId: string) => void;
  onOpenChange?: (id: string, open: boolean) => void;
  /** Inject the markdown loader for the fanned pages (tests / host seam). */
  loadMarkdown?: (documentId: string) => Promise<MarkdownDocument>;
}

/** Object frame from the brief: icon area on top, filename label beneath. */
export const FOLDER_OBJECT_WIDTH = 140;
export const FOLDER_OBJECT_HEIGHT = 122;

const FOLDER_BODY_WIDTH = 56;
const FOLDER_BODY_HEIGHT = 48;
const FOLDER_FRONT_HEIGHT = 36;
const FOLDER_RADIUS = 7;
const OBJECT_SHADOW = '0 2px 6px #0000001F';

/** Fan geometry from the brief, applied per page index. */
const PAGE_ROTATIONS = [-8, 0, 9];
const PAGE_LIFT = 55;

/** A drag shorter than this is a click, not a release. */
const RELEASE_DRAG_THRESHOLD_PX = 12;

export function FolderObject({
  id,
  title,
  x = 0,
  y = 0,
  width = FOLDER_OBJECT_WIDTH,
  height = FOLDER_OBJECT_HEIGHT,
  isSelected = false,
  containedDocuments = [],
  onSelect,
  onOpenPicker,
  onDoubleClick,
  onReleaseDocument,
  onOpenChange,
  loadMarkdown,
}: FolderObjectProps) {
  const [open, setOpen] = useState(false);
  const reduceMotion = useReducedMotion();

  /** Pages are real documents — never fabricated placeholders. */
  const pages = containedDocuments;

  const setOpenState = useCallback(
    (next: boolean) => {
      setOpen(next);
      onOpenChange?.(id, next);
    },
    [id, onOpenChange],
  );

  const handleClick = useCallback(() => {
    onSelect?.(id);
    setOpenState(!open);
  }, [id, onSelect, open, setOpenState]);

  const handleDoubleClick = useCallback(
    (event: ReactMouseEvent) => {
      event.stopPropagation();
      // A double click is the "open all" fast path: end open so the gesture is
      // deterministic, then hand off to the existing release wiring.
      setOpenState(false);
      onDoubleClick?.(id);
    },
    [id, onDoubleClick, setOpenState],
  );

  const handlePickerClick = useCallback(
    (event: ReactMouseEvent) => {
      event.stopPropagation();
      onOpenPicker?.(id);
    },
    [id, onOpenPicker],
  );

  // ---------------------------------------------------------------------------
  // Drag a fanned page out → the existing CAN-04 release (metadata only).
  // ---------------------------------------------------------------------------
  const dragRef = useRef<{ documentId: string; startX: number; startY: number; moved: boolean } | null>(
    null,
  );

  useEffect(() => {
    const onMove = (event: MouseEvent) => {
      const drag = dragRef.current;
      if (!drag) return;
      const distance = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY);
      if (distance >= RELEASE_DRAG_THRESHOLD_PX) drag.moved = true;
    };
    const onUp = () => {
      const drag = dragRef.current;
      dragRef.current = null;
      // Released clear of the folder: surface that document as its own canvas
      // object through the existing release semantic. No file is touched.
      if (drag?.moved) onReleaseDocument?.(drag.documentId);
    };
    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    };
  }, [onReleaseDocument]);

  const beginPageDrag = useCallback((documentId: string, event: ReactMouseEvent) => {
    if (event.button !== 0) return;
    event.stopPropagation();
    dragRef.current = { documentId, startX: event.clientX, startY: event.clientY, moved: false };
  }, []);

  const spring = reduceMotion
    ? { duration: 0 }
    : { type: 'spring' as const, stiffness: 175, damping: 22, mass: 1 };

  return (
    <div
      data-testid="folder-object-frame"
      style={{ position: 'absolute', left: x, top: y, width, height }}
      className="select-none"
    >
      <div
        role="button"
        tabIndex={0}
        aria-label={`Folder: ${title}`}
        aria-expanded={open}
        // `aria-pressed`, not `aria-selected`: the latter is not a supported
        // state on a button role.
        aria-pressed={isSelected}
        data-testid="folder-object"
        data-kind="folder"
        data-object-id={id}
        data-open={open ? 'true' : 'false'}
        style={{ willChange: 'transform' }}
        className={[
          'absolute inset-0 cursor-pointer transition-colors',
          isSelected ? 'rounded-lg ring-2 ring-mac-accent/40' : '',
        ]
          .filter(Boolean)
          .join(' ')}
        onClick={handleClick}
        onDoubleClick={handleDoubleClick}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            handleClick();
          }
        }}
      >
        {/* Fanned pages — the real documents contained in this folder */}
        {open && pages.length > 0 && (
          <div data-testid="folder-pages" className="pointer-events-none absolute inset-x-0 top-0">
            {pages.map((page, index) => (
              <motion.div
                key={page.documentId}
                data-testid={`folder-page-${page.documentId}`}
                data-part="folder-page"
                className="pointer-events-auto absolute left-1/2 h-[56px] w-[44px] -translate-x-1/2 cursor-grab rounded-[4px] border border-mac-hairline-strong bg-mac-fill-2 p-0.5"
                style={{ top: 14, boxShadow: OBJECT_SHADOW, zIndex: 10 + index }}
                initial={reduceMotion ? false : { opacity: 0, y: 0, rotate: 0 }}
                animate={{
                  opacity: 1,
                  y: -(PAGE_LIFT + (index % 3) * 10),
                  rotate: PAGE_ROTATIONS[index % PAGE_ROTATIONS.length],
                }}
                transition={spring}
                onMouseDown={(event) => beginPageDrag(page.documentId, event)}
                onClick={(event) => event.stopPropagation()}
                title={page.filename}
              >
                <FilePreview
                  documentId={page.documentId}
                  title={page.filename}
                  docType={documentIconKind(page.filename)}
                  loadMarkdown={loadMarkdown}
                />
              </motion.div>
            ))}
          </div>
        )}

        {open && pages.length === 0 && (
          <span
            data-testid="folder-open-empty"
            className="absolute left-1/2 top-1 -translate-x-1/2 text-[9px] text-mac-text-3"
          >
            No documents in this folder
          </span>
        )}

        {/* The folder itself: back panel + front panel that tilts back when open */}
        <div
          className="absolute left-1/2 -translate-x-1/2"
          style={{ top: 24, width: FOLDER_BODY_WIDTH, height: FOLDER_BODY_HEIGHT, perspective: 400 }}
        >
          <div
            data-part="folder-body"
            className="absolute left-0 top-0"
            style={{
              width: FOLDER_BODY_WIDTH,
              height: FOLDER_BODY_HEIGHT,
              borderRadius: FOLDER_RADIUS,
              background: 'var(--color-mac-folder)',
              boxShadow: OBJECT_SHADOW,
            }}
          />
          <motion.div
            data-part="folder-front"
            className="absolute left-0"
            style={{
              top: 12,
              width: FOLDER_BODY_WIDTH,
              height: FOLDER_FRONT_HEIGHT,
              borderRadius: FOLDER_RADIUS,
              background: 'var(--color-mac-folder-front)',
              transformOrigin: 'bottom',
            }}
            initial={reduceMotion ? false : { rotateX: 0 }}
            animate={{ rotateX: open ? -40 : 0 }}
            transition={spring}
          />
        </div>

        {/* Filename label beneath the object */}
        <span
          data-part="item-label"
          className="absolute bottom-1 left-0 w-full truncate px-1 text-center text-[12px] font-medium text-mac-text"
        >
          {title}
        </span>
      </div>

      {/* Existing single-click → picker wiring, kept reachable as its own
          affordance. A sibling of the object button: a real <button> nested
          inside `role="button"` is a nested interactive control. */}
      {onOpenPicker && (
        <button
          type="button"
          aria-label={`Open documents in ${title}`}
          data-testid={`folder-picker-${id}`}
          onClick={handlePickerClick}
          className="absolute z-20 rounded text-[10px] leading-none text-mac-text-3 hover:text-mac-text"
          style={{ right: 6, top: 76 }}
        >
          ⌄
        </button>
      )}
    </div>
  );
}

export default FolderObject;
