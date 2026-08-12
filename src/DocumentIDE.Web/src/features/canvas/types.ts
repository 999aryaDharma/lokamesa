/**
 * CAN-01: Canvas Foundation — type definitions
 *
 * Canvas state is workspace metadata only. The local filesystem remains
 * the source of truth for actual documents.
 */

/** The pan/zoom state of the canvas viewport. */
export interface Viewport {
  /** Horizontal pan offset in pixels. Default: 0 */
  x: number;
  /** Vertical pan offset in pixels. Default: 0 */
  y: number;
  /** Zoom level (1.0 = 100%). Bounded [0.1, 3.0]. Default: 1.0 */
  zoom: number;
}

/** Minimum allowed zoom level. */
export const ZOOM_MIN = 0.1;

/** Maximum allowed zoom level. */
export const ZOOM_MAX = 3.0;

/** Default viewport. */
export const DEFAULT_VIEWPORT: Readonly<Viewport> = { x: 0, y: 0, zoom: 1.0 };

/** localStorage key for persisted viewport. */
export const VIEWPORT_STORAGE_KEY = 'documentide_canvas_viewport';

/**
 * A canvas object — workspace metadata only.
 * Drag position never physically moves the referenced file.
 */
export interface CanvasObject {
  /** Unique stable identifier (e.g. UUID). */
  id: string;
  /** Canvas X position in canvas-space pixels. */
  x: number;
  /** Canvas Y position in canvas-space pixels. */
  y: number;
  /** Width of the rendered card in canvas-space pixels. */
  width: number;
  /** Height of the rendered card in canvas-space pixels. */
  height: number;
  /** Display label for the card. */
  title: string;
  /** Whether this object represents a folder source or a document. */
  type: 'folder' | 'document';
  /** Optional document type decoration. */
  docType?: 'docx' | 'md' | 'unknown';
  /** Optional thumbnail cache state. */
  thumbnailState?: 'loading' | 'ready' | 'stale' | 'missing' | 'failed';
  /** Optional thumbnail image Data URL or path. */
  thumbnailUrl?: string;
  /** Optional text content preview snippet. */
  thumbnailContent?: string;
  /** Whether referenced file is missing on disk. */
  isMissing?: boolean;
  /** Optional status marker text. */
  statusText?: string;
}

/** Canvas store state shape. */
export interface CanvasState {
  viewport: Viewport;
  objects: CanvasObject[];
  /** Set of selected object IDs. */
  selectedIds: Set<string>;
}
