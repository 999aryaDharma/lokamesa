/**
 * CAN-06: Position Persistence & Pinning (frontend durable side).
 *
 * Persists canvas positions (mode + coordinates) per workspace in
 * localStorage, keyed by stable DocumentId — never by filesystem path, so
 * rename and physical moves (FS-05 identity) preserve spatial memory.
 *
 * Adopts the CAN-05 `PositionMode` vocabulary (`auto`/`manual`/`pinned`) as
 * the persisted mode. Pure helpers over an explicit map plus thin
 * localStorage I/O; no filesystem/network access, and canvas drag here only
 * writes metadata — it never moves physical files.
 */
import type { PositionMode } from './layout/AssistedPlacement';

/** A persisted canvas position: mode + canvas-space coordinates. */
export interface PersistedPosition {
  x: number;
  y: number;
  mode: PositionMode;
}

/** Positions for one workspace, keyed by stable DocumentId. */
export type PositionMap = Record<string, PersistedPosition>;

/** An already-occupied canvas rectangle competing with a restore. */
export interface OccupiedRect {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** A resolved restore: exact coordinates, or the nearest-safe fallback. */
export interface RestoredPosition {
  x: number;
  y: number;
  fellBack: boolean;
}

/** Maximum restore-fallback displacement in pixels (mirrors the backend gate). */
export const MAX_RESTORE_DISPLACEMENT = 600;

/** localStorage key namespace for persisted canvas positions. */
export function storageKey(workspaceId: string): string {
  return `documentide_canvas_positions:${workspaceId}`;
}

function isValidMode(mode: unknown): mode is PositionMode {
  return mode === 'auto' || mode === 'manual' || mode === 'pinned';
}

function sanitizeEntry(entry: unknown): PersistedPosition | null {
  if (typeof entry !== 'object' || entry === null) return null;
  const { x, y, mode } = entry as { x?: unknown; y?: unknown; mode?: unknown };
  if (typeof x !== 'number' || typeof y !== 'number' || !isValidMode(mode)) return null;
  if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
  return { x, y, mode };
}

/**
 * Restore all persisted positions for a workspace after restart/reopen.
 * Corrupt or missing storage degrades to an empty map — never throws.
 */
export function loadPositions(workspaceId: string): PositionMap {
  try {
    const raw = localStorage.getItem(storageKey(workspaceId));
    if (!raw) return {};
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    if (typeof parsed !== 'object' || parsed === null) return {};
    const result: PositionMap = {};
    for (const [id, entry] of Object.entries(parsed)) {
      const clean = sanitizeEntry(entry);
      if (clean) result[id] = clean;
    }
    return result;
  } catch {
    return {};
  }
}

/** Durably store a workspace position map (restart persistence). */
export function savePositions(workspaceId: string, map: PositionMap): PositionMap {
  const snapshot: PositionMap = { ...map };
  try {
    localStorage.setItem(storageKey(workspaceId), JSON.stringify(snapshot));
  } catch {
    // Storage may be unavailable (private browsing, quota) — the in-memory
    // map is still returned so the canvas keeps working this session.
  }
  return snapshot;
}

/**
 * Record a user drag: coordinates update and the mode switches to `manual`
 * (spec 03 section 9). Persists immediately.
 */
export function recordDrag(workspaceId: string, documentId: string, x: number, y: number): PositionMap {
  const map = loadPositions(workspaceId);
  map[documentId] = { x, y, mode: 'manual' };
  return savePositions(workspaceId, map);
}

/** Pin a document: mode becomes `pinned`, coordinates retained. */
export function pinPosition(workspaceId: string, documentId: string): PositionMap {
  const map = loadPositions(workspaceId);
  const current = map[documentId] ?? { x: 0, y: 0, mode: 'auto' as PositionMode };
  map[documentId] = { ...current, mode: 'pinned' };
  return savePositions(workspaceId, map);
}

/**
 * Unpin a document: mode returns to `manual` so spatial memory is retained
 * and assisted placement still must not move it. Coordinates retained.
 */
export function unpinPosition(workspaceId: string, documentId: string): PositionMap {
  const map = loadPositions(workspaceId);
  const current = map[documentId] ?? { x: 0, y: 0, mode: 'auto' as PositionMode };
  map[documentId] = { ...current, mode: 'manual' };
  return savePositions(workspaceId, map);
}

function overlaps(
  x: number, y: number, width: number, height: number,
  rect: OccupiedRect,
): boolean {
  return x < rect.x + rect.width && rect.x < x + width && y < rect.y + rect.height && rect.y < y + height;
}

/**
 * Resolve where a persisted position lands on reopen: exact coordinates
 * when free, otherwise the nearest-safe fallback via a short spiral probe
 * (32px steps, same discipline as the CAN-05 nudge search). Throws a typed
 * `Error` naming the blockers when no free cell exists nearby instead of
 * silently stacking cards.
 */
export function resolveRestorePosition(
  persisted: PersistedPosition,
  documentId: string,
  occupied: OccupiedRect[],
  width: number,
  height: number,
): RestoredPosition {
  const clashes = (x: number, y: number): boolean =>
    occupied.some((rect) => rect.id !== documentId && overlaps(x, y, width, height, rect));

  if (!clashes(persisted.x, persisted.y)) {
    return { x: persisted.x, y: persisted.y, fellBack: false };
  }

  const step = 32;
  const maxRings = 8;
  const directions = [
    { dx: 1, dy: 0 },
    { dx: 0, dy: 1 },
    { dx: -1, dy: 0 },
    { dx: 0, dy: -1 },
  ];
  for (let ring = 1; ring <= maxRings; ring++) {
    for (const { dx, dy } of directions) {
      const cx = persisted.x + ring * step * dx;
      const cy = persisted.y + ring * step * dy;
      if (Math.hypot(cx - persisted.x, cy - persisted.y) > MAX_RESTORE_DISPLACEMENT) continue;
      if (!clashes(cx, cy)) {
        return { x: cx, y: cy, fellBack: true };
      }
    }
  }

  const blockingIds = occupied.map((rect) => rect.id).join(', ');
  throw new Error(`No free restore location for '${documentId}' near its persisted position (blocked by: ${blockingIds}).`);
}
