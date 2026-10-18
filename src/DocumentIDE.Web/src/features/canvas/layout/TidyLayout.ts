/**
 * CAN-07: Multi-Select, Group Drag & Tidy — tidy layout engine.
 *
 * `Tidy selection` (spec 03 section 12) and `Tidy canvas` (spec 03 section 13):
 * - tidy selection affects only selected objects and preserves their
 *   approximate region;
 * - tidy canvas operates on unpinned objects only (pinned objects are never
 *   moved and never overlapped);
 * - both resolve overlap through the CAN-05 `SpatialHashIndex` — never a
 *   full O(n^2) canvas scan per candidate;
 * - both preserve object order when practical (stable row-major flow) and
 *   preserve groups (members of the same `groupId` stay consecutive).
 *
 * Organizational only: pure functions over an input snapshot returning new
 * coordinates. No filesystem, network, or DOM access — canvas drag and tidy
 * never move physical files, and tidy performs no filesystem mutation.
 *
 * Position-mode vocabulary is reused from CAN-05/CAN-06 (`PositionMode` in
 * `./AssistedPlacement`): `pinned` objects are immovable; `auto` and `manual`
 * objects are tidy-eligible when selected (tidy selection) or unpinned
 * (tidy canvas). Tidy is always an explicit user command, never automatic
 * layout, so user-placed `manual` objects move only when the user asks.
 */
import type { CanvasObject } from '../types';
import type { PositionMode } from './AssistedPlacement';
import { SpatialHashIndex } from './SpatialIndex';

/** A canvas object tidy may consider. `positionMode` defaults to `auto`. */
export interface TidyableObject extends CanvasObject {
  positionMode?: PositionMode;
  /** Optional virtual-group membership; members stay consecutive. */
  groupId?: string;
}

/** One object relocation produced by a tidy run. */
export interface TidyMove {
  id: string;
  fromX: number;
  fromY: number;
  toX: number;
  toY: number;
}

/** Evidence a tidy run stayed index-backed and local. */
export interface TidyStats {
  indexQueries: number;
  entriesExamined: number;
  intersectionTests: number;
  movedCount: number;
  elapsedMs: number;
}

/** Result of a tidy run: final coordinates for every input object. */
export interface TidyResult {
  /** Final `{ x, y }` per input object id (moved and unmoved alike). */
  positions: Record<string, { x: number; y: number }>;
  /** Only the objects whose coordinates changed. Never contains pinned ids. */
  moves: TidyMove[];
  stats: TidyStats;
}

export interface TidyOptions {
  /** Whitespace between tidied cards in pixels; default 24. */
  gap?: number;
  /** Grid columns for the compact flow; default ceil(sqrt(n)). */
  columns?: number;
  /** Spatial hash cell size in pixels; default 256. */
  cellSize?: number;
}

interface PlacedRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

function modeOf(object: TidyableObject): PositionMode {
  return object.positionMode ?? 'auto';
}

/** Row-major order preserves the user's approximate arrangement. */
function byReadingOrder(a: TidyableObject, b: TidyableObject): number {
  if (a.y !== b.y) return a.y - b.y;
  return a.x - b.x;
}

/**
 * Order tidy-eligible objects so virtual-group members stay consecutive
 * (first-seen group order) while keeping reading order inside each group.
 */
function orderForTidy(eligible: TidyableObject[]): TidyableObject[] {
  const groups = new Map<string, TidyableObject[]>();
  const order: string[] = [];
  for (const object of eligible) {
    const key = object.groupId ?? `\0${object.id}`;
    let bucket = groups.get(key);
    if (!bucket) {
      bucket = [];
      groups.set(key, bucket);
      order.push(key);
    }
    bucket.push(object);
  }
  return order.flatMap((key) => groups.get(key)?.sort(byReadingOrder) ?? []);
}

function collectStats(index: SpatialHashIndex, movedCount: number, startedAt: number): TidyStats {
  const { queries, entryVisits, intersectionTests } = index.getStats();
  return {
    indexQueries: queries,
    entriesExamined: entryVisits,
    intersectionTests,
    movedCount,
    elapsedMs: Date.now() - startedAt,
  };
}

function snapshotPositions(objects: TidyableObject[]): Record<string, { x: number; y: number }> {
  const positions: Record<string, { x: number; y: number }> = {};
  for (const object of objects) {
    positions[object.id] = { x: object.x, y: object.y };
  }
  return positions;
}

/**
 * Compact row-major flow anchored at `anchor`, skipping cells that collide
 * (via the spatial index) with immovable or already-placed objects.
 */
function flowIntoFreeCells(
  ordered: TidyableObject[],
  anchor: { x: number; y: number },
  gap: number,
  columns: number,
  index: SpatialHashIndex,
  positions: Record<string, { x: number; y: number }>,
  moves: TidyMove[],
  originalById: Map<string, TidyableObject>,
): void {
  if (ordered.length === 0) return;
  const maxWidth = Math.max(...ordered.map((o) => o.width));
  const maxHeight = Math.max(...ordered.map((o) => o.height));
  const strideX = maxWidth + gap;
  const strideY = maxHeight + gap;

  // Row-major probe order from the anchor: the first free cell is always
  // the closest free cell to the anchor. The index holds blockers plus
  // already-placed cards of this run, so each probe is a local index
  // lookup — never a full canvas scan.
  const probeLimit = Math.max(ordered.length * 4, 64);
  const taken = new Set<number>();
  for (const object of ordered) {
    let slot = -1;
    let slotPos: { x: number; y: number } | null = null;
    for (let cell = 0; cell < probeLimit; cell++) {
      if (taken.has(cell)) continue;
      const candidate = {
        x: anchor.x + (cell % columns) * strideX,
        y: anchor.y + Math.floor(cell / columns) * strideY,
        width: object.width,
        height: object.height,
      };
      const hits = index.query(candidate);
      if (hits.length === 0) {
        slot = cell;
        slotPos = { x: candidate.x, y: candidate.y };
        break;
      }
    }
    const original = originalById.get(object.id) as TidyableObject;
    if (slot === -1 || slotPos === null) {
      // Fully packed neighbourhood: keep the original spot (overlap
      // preserved rather than flinging the card across the canvas).
      index.insert(object.id, original);
      positions[object.id] = { x: original.x, y: original.y };
      continue;
    }
    taken.add(slot);
    index.insert(object.id, { x: slotPos.x, y: slotPos.y, width: object.width, height: object.height });
    positions[object.id] = { x: slotPos.x, y: slotPos.y };
    if (slotPos.x !== original.x || slotPos.y !== original.y) {
      moves.push({ id: object.id, fromX: original.x, fromY: original.y, toX: slotPos.x, toY: slotPos.y });
    }
  }
}

function runTidy(eligible: TidyableObject[], blockers: TidyableObject[], options: TidyOptions): TidyResult {
  const startedAt = Date.now();
  const gap = Number.isFinite(options.gap ?? 24) ? Math.max(0, options.gap ?? 24) : 24;
  const index = new SpatialHashIndex(options.cellSize ?? 256);
  const originalById = new Map<string, TidyableObject>();
  const positions = snapshotPositions([...blockers, ...eligible]);
  // Seed the index with blockers only: eligible cards are invisible until
  // placed, so probes never collide with stale pre-tidy rects of cards
  // that have not been positioned yet.
  for (const object of blockers) {
    originalById.set(object.id, object);
    index.insert(object.id, object);
  }
  for (const object of eligible) {
    originalById.set(object.id, object);
  }

  const ordered = orderForTidy(eligible);
  const anchor =
    ordered.length > 0
      ? {
          x: Math.min(...ordered.map((o) => o.x)),
          y: Math.min(...ordered.map((o) => o.y)),
        }
      : { x: 0, y: 0 };
  const columns =
    options.columns !== undefined
      ? Math.max(1, Math.floor(options.columns))
      : Math.max(1, Math.ceil(Math.sqrt(ordered.length)));

  const moves: TidyMove[] = [];
  flowIntoFreeCells(ordered, anchor, gap, columns, index, positions, moves, originalById);

  return { positions, moves, stats: collectStats(index, moves.length, startedAt) };
}

/**
 * Tidy only the selected objects. Pinned objects never move (even when
 * selected); unselected objects are blockers that are never moved and never
 * overlapped. The flow anchors at the selection's own bounding-box origin,
 * so the selection stays in its approximate region.
 */
export function tidySelection(
  objects: TidyableObject[],
  selectedIds: string[],
  options: TidyOptions = {},
): TidyResult {
  const selected = new Set(selectedIds);
  const eligible = objects.filter((o) => selected.has(o.id) && modeOf(o) !== 'pinned');
  const blockers = objects.filter((o) => !(selected.has(o.id) && modeOf(o) !== 'pinned'));
  return runTidy(eligible, blockers, options);
}

/**
 * Tidy the whole canvas: every unpinned object (`auto` + `manual`) is
 * eligible; every pinned object is an immovable blocker. Broad spatial
 * regions are preserved because the flow is anchored per group cluster at
 * current positions — cards shift locally instead of rebuilding the room.
 */
export function tidyCanvas(objects: TidyableObject[], options: TidyOptions = {}): TidyResult {
  const eligible = objects.filter((o) => modeOf(o) !== 'pinned');
  const blockers = objects.filter((o) => modeOf(o) === 'pinned');
  return runTidy(eligible, blockers, options);
}

/** Bounding-box centroid of a set of objects (region-preservation probe). */
export function centroid(objects: PlacedRect[]): { x: number; y: number } {
  if (objects.length === 0) return { x: 0, y: 0 };
  let sx = 0;
  let sy = 0;
  for (const o of objects) {
    sx += o.x + o.width / 2;
    sy += o.y + o.height / 2;
  }
  return { x: sx / objects.length, y: sy / objects.length };
}
