/**
 * CAN-07: Multi-Select, Group Drag & Tidy — selection controller.
 *
 * Rectangular multi-select, Ctrl-click additive selection, and group drag
 * preserving relative positions (spec 03 sections 14-15):
 * - dragging multiple selected documents preserves relative positions and
 *   never stacks objects at one coordinate (uniform translation);
 * - pinned objects never move, even inside a dragged selection.
 *
 * Organizational only: pure functions over an input snapshot returning new
 * selection/coordinates. No filesystem, network, or DOM access — selection
 * and drag never move physical files.
 */
import type { CanvasObject } from '../types';
import type { PositionMode } from '../layout/AssistedPlacement';
import { rectsOverlap } from '../layout/SpatialIndex';

/** A canvas object selection may consider. `positionMode` defaults to `auto`. */
export interface SelectableObject extends CanvasObject {
  positionMode?: PositionMode;
}

/** Drag selection rectangle in canvas-space pixels. */
export interface SelectionRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Group-drag translation in canvas-space pixels. */
export interface DragDelta {
  dx: number;
  dy: number;
}

/** One object relocation produced by a group drag. */
export interface GroupDragMove {
  id: string;
  fromX: number;
  fromY: number;
  toX: number;
  toY: number;
}

export interface GroupDragResult {
  /** Final `{ x, y }` per input object id (moved and unmoved alike). */
  positions: Record<string, { x: number; y: number }>;
  /** Only the objects whose coordinates changed. Never contains pinned ids. */
  moves: GroupDragMove[];
}

/**
 * Ids of all objects intersecting the drag rectangle. Touching edges do not
 * count as intersecting (same predicate as the spatial index).
 */
export function rectSelect(objects: SelectableObject[], rect: SelectionRect): string[] {
  return objects.filter((o) => rectsOverlap(o, rect)).map((o) => o.id);
}

/**
 * Plain click replaces the selection with the clicked id; Ctrl-click
 * (additive) toggles the clicked id and keeps the rest.
 */
export function applyClick(current: string[], id: string, additive: boolean): string[] {
  if (!additive) return [id];
  return current.includes(id) ? current.filter((c) => c !== id) : [...current, id];
}

/**
 * Translate every movable selected object by the same delta, preserving
 * relative positions exactly — uniform translation cannot stack distinct
 * objects. Pinned selections stay put; unselected objects are untouched.
 */
export function groupDrag(
  objects: SelectableObject[],
  selectedIds: string[],
  delta: DragDelta,
): GroupDragResult {
  const selected = new Set(selectedIds);
  const positions: Record<string, { x: number; y: number }> = {};
  const moves: GroupDragMove[] = [];
  for (const object of objects) {
    const movable = selected.has(object.id) && object.positionMode !== 'pinned';
    if (movable) {
      const toX = object.x + delta.dx;
      const toY = object.y + delta.dy;
      positions[object.id] = { x: toX, y: toY };
      if (toX !== object.x || toY !== object.y) {
        moves.push({ id: object.id, fromX: object.x, fromY: object.y, toX, toY });
      }
    } else {
      positions[object.id] = { x: object.x, y: object.y };
    }
  }
  return { positions, moves };
}
