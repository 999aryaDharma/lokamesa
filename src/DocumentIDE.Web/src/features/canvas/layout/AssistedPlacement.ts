/**
 * CAN-05: Spatially Indexed Assisted Placement.
 *
 * Places newly released documents near their source folder (origin anchor)
 * using a spatial-hash index for collision lookup — never a full O(n^2)
 * canvas scan per candidate.
 *
 * Rules (from docs/specs/03-workspace-canvas-spec.md sections 8-10):
 * - origin anchor = source folder object; compact grid/fan nearby; no overlap;
 * - pinned objects are never moved and never overlapped;
 * - manually positioned objects are never moved by assisted placement;
 * - only local reflow of eligible auto objects; no global reshuffle.
 *
 * Pure function over an input snapshot: the caller passes the canvas objects
 * as observed, so object state changing while layout runs cannot corrupt the
 * run — the run simply reflects the snapshot it was given. No filesystem,
 * network, or DOM access; canvas drag still never moves physical files.
 */
import type { CanvasObject } from '../types';
import { rectsOverlap, SpatialHashIndex } from './SpatialIndex';

/** Placement priority of an existing canvas object. */
export type PositionMode = 'auto' | 'manual' | 'pinned';

/**
 * An existing canvas object plus the placement priority assisted layout must
 * respect. `positionMode` is additive metadata for the layout engine — durable
 * persistence of pins/positions belongs to CAN-06.
 */
export interface PlaceableObject extends CanvasObject {
  positionMode?: PositionMode;
}

/** A new document released from a folder, awaiting a canvas position. */
export interface NewDocumentSpec {
  id: string;
  title: string;
  width: number;
  height: number;
}

/** Origin anchor in canvas-space pixels (normally the source folder position). */
export interface OriginPoint {
  x: number;
  y: number;
}

/** A successfully assigned canvas position for a new document. */
export interface PlacedItem {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** An existing auto object shifted by local reflow. */
export interface ReflowMove {
  id: string;
  fromX: number;
  fromY: number;
  toX: number;
  toY: number;
}

/** Machine-readable reason a document could not be placed near the origin. */
export type PlacementBlockedCode = 'no-free-space' | 'blocked-by-pinned';

/** Evidence that placement stayed local and index-backed. */
export interface PlacementStats {
  candidatesTested: number;
  indexQueries: number;
  entriesExamined: number;
  intersectionTests: number;
  reflowedCount: number;
  elapsedMs: number;
}

export type PlacementResult =
  | {
      status: 'placed';
      placements: PlacedItem[];
      reflowed: ReflowMove[];
      stats: PlacementStats;
    }
  | {
      status: 'blocked';
      code: PlacementBlockedCode;
      placements: PlacedItem[];
      reflowed: ReflowMove[];
      blockingIds: string[];
      stats: PlacementStats;
    };

export interface PlacementOptions {
  /** Origin anchor; defaults to canvas origin (0, 0). */
  origin?: OriginPoint;
  /** Grid columns for the compact fan; default 4. */
  columns?: number;
  /** Whitespace between placed cards in pixels; default 24. */
  gap?: number;
  /** Candidate cells probed per document before reporting blocked; default 400. */
  maxCandidates?: number;
  /** Radius in pixels around the origin inside which auto objects may reflow; default 600. */
  reflowRadius?: number;
  /** Maximum auto objects shifted per placement run; default 8. */
  maxReflowMoves?: number;
  /** Spatial hash cell size in pixels; default 256. */
  cellSize?: number;
}

/**
 * Recorded usability target for the 1,000-object benchmark (wall-clock on the
 * machine that ran it; actuals recorded in the sprint handoff). The strict,
 * machine-independent gate is algorithmic: per-placement index work must stay
 * bounded (see `MAX_EXAMINED_PER_PLACEMENT`). The wall-clock budget below is
 * intentionally generous so the test is stable across CI machines.
 */
export const PLACEMENT_BENCHMARK_TARGETS_MS = {
  objects100: 2000,
  objects500: 5000,
  objects1000: 10000,
} as const;

/**
 * Absolute ceiling for average indexed entries examined per placed document.
 * Index work is bounded by local cell density (a constant), never by canvas
 * size — the machine-independent half of the benchmark gate.
 */
export const MAX_EXAMINED_PER_PLACEMENT = 100;

export function isImmovable(object: PlaceableObject): boolean {
  return object.positionMode === 'pinned' || object.positionMode === 'manual';
}

function modeOf(object: PlaceableObject): PositionMode {
  return object.positionMode ?? 'auto';
}

/**
 * Place new documents on a compact grid fanning out from the origin anchor.
 * Existing pinned/manual objects are never moved; eligible auto objects may be
 * reflowed only inside `reflowRadius` of the origin (see `tryLocalReflow`).
 */
export function placeDocumentsNearOrigin(
  existing: PlaceableObject[],
  specs: NewDocumentSpec[],
  options: PlacementOptions = {},
): PlacementResult {
  const startedAt = Date.now();
  // Normalize programmer-error inputs to sane values instead of silently
  // producing NaN positions downstream.
  const columns = Math.max(1, Math.floor(options.columns ?? 4));
  const gap = Number.isFinite(options.gap ?? 24) ? Math.max(0, options.gap ?? 24) : 24;
  const maxCandidates = Math.max(1, Math.floor(options.maxCandidates ?? 400));
  const origin = options.origin ?? { x: 0, y: 0 };
  const reflowRadius = options.reflowRadius ?? 600;
  const maxReflowMoves = options.maxReflowMoves ?? 8;

  const index = new SpatialHashIndex(options.cellSize ?? 256);
  const byId = new Map<string, PlaceableObject>();
  for (const object of existing) {
    byId.set(object.id, object);
    index.insert(object.id, object);
  }

  // Uniform stride from the largest card keeps the fan overlap-free by
  // construction regardless of mixed document sizes.
  const maxWidth = Math.max(1, ...specs.map((s) => s.width));
  const maxHeight = Math.max(1, ...specs.map((s) => s.height));
  const strideX = maxWidth + gap;
  const strideY = maxHeight + gap;

  const placements: PlacedItem[] = [];
  const reflowed: ReflowMove[] = [];
  const placedIds = new Set<string>();
  let candidatesTested = 0;
  let remainingReflows = maxReflowMoves;

  for (const spec of specs) {
    let placed: PlacedItem | null = null;
    let lastBlocking: string[] = [];
    for (let candidate = 0; candidate < maxCandidates && !placed; candidate++) {
      candidatesTested += 1;
      const col = candidate % columns;
      const row = Math.floor(candidate / columns);
      const rect = {
        x: origin.x + col * strideX,
        y: origin.y + row * strideY,
        width: spec.width,
        height: spec.height,
      };
      const hits = index.query(rect);
      if (hits.length === 0) {
        placed = { id: spec.id, ...rect };
        break;
      }
      lastBlocking = hits;
      const blockers = hits.map((id) => byId.get(id)).filter((o) => o !== undefined);
      // Already-placed cards from this run are final: never reflow them,
      // otherwise recorded placement coordinates would go stale.
      const hitsPlaced = hits.some((id) => placedIds.has(id));
      const immovableHit = blockers.some(isImmovable);
      if (!immovableHit && !hitsPlaced) {
        const moved = tryLocalReflow(blockers, rect, index, byId, origin, reflowRadius, remainingReflows);
        if (moved) {
          remainingReflows -= moved.length;
          reflowed.push(...moved);
          placed = { id: spec.id, ...rect };
          break;
        }
      }
    }
    if (!placed) {
      const stats = collectStats(index, candidatesTested, reflowed.length, startedAt);
      const blockingModes = lastBlocking.map((id) => modeOf(byId.get(id) as PlaceableObject));
      const code: PlacementBlockedCode =
        blockingModes.length > 0 && blockingModes.every((m) => m === 'pinned' || m === 'manual')
          ? 'blocked-by-pinned'
          : 'no-free-space';
      return { status: 'blocked', code, placements, reflowed, blockingIds: lastBlocking, stats };
    }
    placements.push(placed);
    placedIds.add(placed.id);
    index.insert(placed.id, placed);
    byId.set(placed.id, {
      id: placed.id,
      x: placed.x,
      y: placed.y,
      width: placed.width,
      height: placed.height,
      title: spec.title,
      type: 'document',
      positionMode: 'auto',
    });
  }

  return {
    status: 'placed',
    placements,
    reflowed,
    stats: collectStats(index, candidatesTested, reflowed.length, startedAt),
  };
}

interface CandidateRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * Shift colliding auto objects a short distance so the candidate rect fits.
 * Only objects already within `reflowRadius` of the origin are eligible, each
 * move is capped to a short nudge, and pinned/manual objects are never
 * touched. Returns the moves, or null when reflow cannot clear the rect.
 */
function tryLocalReflow(
  blockers: PlaceableObject[],
  rect: CandidateRect,
  index: SpatialHashIndex,
  byId: Map<string, PlaceableObject>,
  origin: OriginPoint,
  reflowRadius: number,
  remainingReflows: number,
): ReflowMove[] | null {
  if (blockers.length === 0 || blockers.length > remainingReflows) return null;
  if (blockers.some(isImmovable)) return null;

  const planned: ReflowMove[] = [];
  const staged = new Map<string, CandidateRect>();
  for (const blocker of blockers) {
    const distance = Math.hypot(blocker.x - origin.x, blocker.y - origin.y);
    if (distance > reflowRadius) return null;
    const spot = findNudgeSpot(blocker, rect, staged, index);
    if (!spot) return null;
    planned.push({ id: blocker.id, fromX: blocker.x, fromY: blocker.y, toX: spot.x, toY: spot.y });
    staged.set(blocker.id, { x: spot.x, y: spot.y, width: blocker.width, height: blocker.height });
  }

  for (const move of planned) {
    const current = byId.get(move.id);
    if (!current) continue;
    const next = { ...current, x: move.toX, y: move.toY };
    byId.set(move.id, next);
    index.update(move.id, next);
  }
  return planned;
}

/** Search a small spiral around the blocker for a free nudge spot. */
function findNudgeSpot(
  blocker: PlaceableObject,
  incoming: CandidateRect,
  staged: Map<string, CandidateRect>,
  index: SpatialHashIndex,
): { x: number; y: number } | null {
  const step = 32;
  for (let ring = 1; ring <= 8; ring++) {
    const offsets: Array<{ dx: number; dy: number }> = [
      { dx: ring * step, dy: 0 },
      { dx: 0, dy: ring * step },
      { dx: -ring * step, dy: 0 },
      { dx: 0, dy: -ring * step },
    ];
    for (const { dx, dy } of offsets) {
      const candidate = {
        x: blocker.x + dx,
        y: blocker.y + dy,
        width: blocker.width,
        height: blocker.height,
      };
      // The incoming card vacates conceptually into `incoming`; the nudge
      // spot must clear it and every other staged/existing rect.
      if (rectsOverlap(candidate, incoming)) continue;
      let clashes = false;
      for (const stagedRect of staged.values()) {
        if (rectsOverlap(candidate, stagedRect)) {
          clashes = true;
          break;
        }
      }
      if (clashes) continue;
      const hits = index.query(candidate).filter((id) => id !== blocker.id);
      if (hits.length === 0) {
        return { x: candidate.x, y: candidate.y };
      }
    }
  }
  return null;
}

function collectStats(
  index: SpatialHashIndex,
  candidatesTested: number,
  reflowedCount: number,
  startedAt: number,
): PlacementStats {
  const { queries, entryVisits, intersectionTests } = index.getStats();
  return {
    candidatesTested,
    indexQueries: queries,
    entriesExamined: entryVisits,
    intersectionTests,
    reflowedCount,
    elapsedMs: Date.now() - startedAt,
  };
}
