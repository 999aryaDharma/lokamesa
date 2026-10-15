/**
 * CAN-05: Spatially Indexed Assisted Placement — uniform spatial hash grid.
 *
 * Index choice (recorded decision): a uniform spatial hash/grid, selected over
 * an R-tree or quadtree because canvas objects are uniformly-sized cards and a
 * grid gives O(1) insert/query with minimal code. Benchmark comparison of the
 * grid against a brute-force full scan at 100/500/1,000 objects is recorded in
 * the sprint handoff (docs/sprints/handoffs/CAN-05-HANDOFF.md).
 *
 * Pure in-memory layout helper: no filesystem, network, or DOM access.
 */

/** Axis-aligned bounding rectangle in canvas-space pixels. */
export interface IndexedRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Instrumentation counters proving queries stay local (no O(n^2) full scan). */
export interface SpatialIndexStats {
  /** Number of query() calls since the last reset. */
  queries: number;
  /** Number of indexed entries visited via touched cells (pre-filter). */
  entryVisits: number;
  /** Number of exact rectangle intersection tests performed. */
  intersectionTests: number;
}

/** Returns true when two axis-aligned rectangles overlap (touching edges do not count). */
export function rectsOverlap(a: IndexedRect, b: IndexedRect): boolean {
  return (
    a.x < b.x + b.width &&
    b.x < a.x + a.width &&
    a.y < b.y + b.height &&
    b.y < a.y + a.height
  );
}

/**
 * Uniform spatial hash grid over canvas-space rectangles.
 *
 * Entries are referenced in every grid cell their bounds touch, so a query
 * only visits entries in cells overlapped by the query rectangle instead of
 * scanning the full object list.
 */
export class SpatialHashIndex {
  private readonly cellSize: number;
  private readonly cells = new Map<string, Set<string>>();
  private readonly rects = new Map<string, IndexedRect>();
  private readonly entryCells = new Map<string, string[]>();
  private stats: SpatialIndexStats = { queries: 0, entryVisits: 0, intersectionTests: 0 };

  constructor(cellSize = 256) {
    if (!Number.isFinite(cellSize) || cellSize <= 0) {
      throw new RangeError('SpatialHashIndex cellSize must be a positive number.');
    }
    this.cellSize = cellSize;
  }

  /** Number of indexed entries. */
  get size(): number {
    return this.rects.size;
  }

  /** Configured grid cell size in canvas-space pixels. */
  getCellSize(): number {
    return this.cellSize;
  }

  /** Snapshot of instrumentation counters. */
  getStats(): SpatialIndexStats {
    return { ...this.stats };
  }

  /** Zero the instrumentation counters. */
  resetStats(): void {
    this.stats = { queries: 0, entryVisits: 0, intersectionTests: 0 };
  }

  /** Stored bounds for an entry, or undefined when unknown. */
  getRect(id: string): IndexedRect | undefined {
    const rect = this.rects.get(id);
    return rect ? { ...rect } : undefined;
  }

  /** True when the id is currently indexed. */
  has(id: string): boolean {
    return this.rects.has(id);
  }

  /** Insert a new entry. Throws on duplicate id so double-release bugs surface loudly. */
  insert(id: string, rect: IndexedRect): void {
    if (this.rects.has(id)) {
      throw new Error(`SpatialHashIndex: duplicate id "${id}". Use update() to move an entry.`);
    }
    this.rects.set(id, { ...rect });
    const keys = this.keysFor(rect);
    this.entryCells.set(id, keys);
    for (const key of keys) {
      let cell = this.cells.get(key);
      if (!cell) {
        cell = new Set<string>();
        this.cells.set(key, cell);
      }
      cell.add(id);
    }
  }

  /** Move an existing entry to new bounds (insert when unknown). */
  update(id: string, rect: IndexedRect): void {
    if (this.rects.has(id)) {
      this.remove(id);
    }
    this.insert(id, rect);
  }

  /** Remove an entry; no-op when the id is unknown. */
  remove(id: string): void {
    const keys = this.entryCells.get(id);
    if (keys) {
      for (const key of keys) {
        const cell = this.cells.get(key);
        if (cell) {
          cell.delete(id);
          if (cell.size === 0) {
            this.cells.delete(key);
          }
        }
      }
      this.entryCells.delete(id);
    }
    this.rects.delete(id);
  }

  /** Remove every entry (keeps instrumentation counters). */
  clear(): void {
    this.cells.clear();
    this.rects.clear();
    this.entryCells.clear();
  }

  /**
   * Ids of indexed entries whose bounds overlap the query rectangle.
   * Only entries in grid cells touched by the query are examined — never a
   * full scan of the index.
   */
  query(rect: IndexedRect): string[] {
    this.stats.queries += 1;
    const seen = new Set<string>();
    const hits: string[] = [];
    for (const key of this.keysFor(rect)) {
      const cell = this.cells.get(key);
      if (!cell) continue;
      for (const id of cell) {
        if (seen.has(id)) continue;
        seen.add(id);
        this.stats.entryVisits += 1;
        const candidate = this.rects.get(id);
        // Counter increments even if the entry vanished mid-query so the
        // reported bound stays conservative.
        this.stats.intersectionTests += 1;
        if (candidate && rectsOverlap(rect, candidate)) {
          hits.push(id);
        }
      }
    }
    return hits;
  }

  private cellKey(cx: number, cy: number): string {
    return `${cx}:${cy}`;
  }

  private keysFor(rect: IndexedRect): string[] {
    const minCx = Math.floor(rect.x / this.cellSize);
    const maxCx = Math.floor((rect.x + rect.width) / this.cellSize);
    const minCy = Math.floor(rect.y / this.cellSize);
    const maxCy = Math.floor((rect.y + rect.height) / this.cellSize);
    const keys: string[] = [];
    for (let cx = minCx; cx <= maxCx; cx++) {
      for (let cy = minCy; cy <= maxCy; cy++) {
        keys.push(this.cellKey(cx, cy));
      }
    }
    return keys;
  }
}
