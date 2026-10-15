import { describe, expect, it } from 'vitest';
import {
  MAX_EXAMINED_PER_PLACEMENT,
  PLACEMENT_BENCHMARK_TARGETS_MS,
  placeDocumentsNearOrigin,
} from './AssistedPlacement';
import type { NewDocumentSpec, PlaceableObject } from './AssistedPlacement';

function autoObject(id: string, x: number, y: number): PlaceableObject {
  return {
    id,
    x,
    y,
    width: 160,
    height: 120,
    title: id,
    type: 'document',
    positionMode: 'auto',
  };
}

describe('CAN-05 AssistedPlacement (cycle 1: near-origin placement)', () => {
  it('new files appear near origin', () => {
    const origin = { x: 400, y: 300 };
    const result = placeDocumentsNearOrigin([], [
      { id: 'doc-1', title: 'BA Site A.docx', width: 160, height: 120 },
      { id: 'doc-2', title: 'BA Site B.docx', width: 160, height: 120 },
      { id: 'doc-3', title: 'notes.md', width: 160, height: 120 },
    ], { origin });

    expect(result.status).toBe('placed');
    if (result.status !== 'placed') return;
    expect(result.placements).toHaveLength(3);
    for (const p of result.placements) {
      const distance = Math.hypot(p.x - origin.x, p.y - origin.y);
      // Compact grid/fan near the source folder: well within one screen.
      expect(distance).toBeLessThan(800);
    }
    // Placed items must not overlap each other.
    const rects = result.placements.map((p) => ({ ...p, width: 160, height: 120 }));
    for (let i = 0; i < rects.length; i++) {
      for (let j = i + 1; j < rects.length; j++) {
        const a = rects[i];
        const b = rects[j];
        const overlaps = a.x < b.x + b.width && b.x < a.x + a.width
          && a.y < b.y + b.height && b.y < a.y + a.height;
        expect(overlaps).toBe(false);
      }
    }
  });

  it('places near origin even when auto objects already surround it', () => {
    const origin = { x: 400, y: 300 };
    const existing = [
      autoObject('old-1', 0, 0),
      autoObject('old-2', 2000, 0),
    ];
    const result = placeDocumentsNearOrigin(existing, [
      { id: 'doc-1', title: 'fresh.docx', width: 160, height: 120 },
    ], { origin });
    expect(result.status).toBe('placed');
  });
});

describe('CAN-05 AssistedPlacement (cycle 2: pins, local reflow, index-backed lookup)', () => {
  const origin = { x: 400, y: 300 };

  function pinnedObject(id: string, x: number, y: number): PlaceableObject {
    return { ...autoObject(id, x, y), positionMode: 'pinned' };
  }

  function manualObject(id: string, x: number, y: number): PlaceableObject {
    return { ...autoObject(id, x, y), positionMode: 'manual' };
  }

  it('pinned objects never move and are never overlapped', () => {
    const pinned = pinnedObject('pin-1', origin.x, origin.y);
    const existing = [pinned, autoObject('far-1', 5000, 5000)];
    const result = placeDocumentsNearOrigin(existing, [
      { id: 'doc-1', title: 'a.docx', width: 160, height: 120 },
      { id: 'doc-2', title: 'b.docx', width: 160, height: 120 },
    ], { origin });

    expect(result.status).toBe('placed');
    if (result.status !== 'placed') return;
    // Pinned object untouched by reflow.
    expect(result.reflowed.find((m) => m.id === 'pin-1')).toBeUndefined();
    expect(pinned.x).toBe(origin.x);
    expect(pinned.y).toBe(origin.y);
    // No placement overlaps the pinned rect.
    for (const p of result.placements) {
      const overlaps = p.x < pinned.x + pinned.width && pinned.x < p.x + p.width
        && p.y < pinned.y + pinned.height && pinned.y < p.y + p.height;
      expect(overlaps).toBe(false);
    }
  });

  it('manual objects never move', () => {
    const manual = manualObject('man-1', origin.x, origin.y);
    const result = placeDocumentsNearOrigin([manual], [
      { id: 'doc-1', title: 'a.docx', width: 160, height: 120 },
    ], { origin });
    expect(result.status).toBe('placed');
    if (result.status !== 'placed') return;
    expect(result.reflowed).toHaveLength(0);
    expect(manual.x).toBe(origin.x);
    expect(manual.y).toBe(origin.y);
  });

  it('no global reshuffle on local insert: distant objects are untouched', () => {
    const existing: PlaceableObject[] = [];
    for (let i = 0; i < 200; i++) {
      existing.push(autoObject(`far-${i}`, 5000 + (i % 20) * 200, 5000 + Math.floor(i / 20) * 160));
    }
    const before = existing.map((o) => ({ id: o.id, x: o.x, y: o.y }));
    const result = placeDocumentsNearOrigin(existing, [
      { id: 'doc-1', title: 'a.docx', width: 160, height: 120 },
      { id: 'doc-2', title: 'b.docx', width: 160, height: 120 },
    ], { origin });

    expect(result.status).toBe('placed');
    if (result.status !== 'placed') return;
    expect(result.reflowed).toHaveLength(0);
    for (const snapshot of before) {
      const current = existing.find((o) => o.id === snapshot.id);
      expect(current?.x).toBe(snapshot.x);
      expect(current?.y).toBe(snapshot.y);
    }
  });

  it('only local reflow: colliding auto objects near origin shift slightly', () => {
    const near = autoObject('near-1', origin.x, origin.y);
    const far = autoObject('far-1', origin.x + 5000, origin.y + 5000);
    const result = placeDocumentsNearOrigin([near, far], [
      { id: 'doc-1', title: 'a.docx', width: 160, height: 120 },
    ], { origin });

    expect(result.status).toBe('placed');
    if (result.status !== 'placed') return;
    // The auto card on the origin anchor was nudged, not teleported.
    expect(result.reflowed.map((m) => m.id)).toEqual(['near-1']);
    const move = result.reflowed[0];
    expect(Math.hypot(move.toX - move.fromX, move.toY - move.fromY)).toBeLessThanOrEqual(400);
    expect(far.x).toBe(origin.x + 5000);
    expect(far.y).toBe(origin.y + 5000);
    // Nudged card no longer overlaps the new placement.
    const placed = result.placements[0];
    const overlaps = placed.x < move.toX + near.width && move.toX < placed.x + placed.width
      && placed.y < move.toY + near.height && move.toY < placed.y + placed.height;
    expect(overlaps).toBe(false);
  });

  it('placement uses nearby candidates instead of a full scan', () => {
    const existing: PlaceableObject[] = [];
    for (let i = 0; i < 1000; i++) {
      existing.push(autoObject(`obj-${i}`, 5000 + (i % 40) * 200, 5000 + Math.floor(i / 40) * 160));
    }
    const result = placeDocumentsNearOrigin(existing, [
      { id: 'doc-1', title: 'a.docx', width: 160, height: 120 },
      { id: 'doc-2', title: 'b.docx', width: 160, height: 120 },
      { id: 'doc-3', title: 'c.docx', width: 160, height: 120 },
    ], { origin });

    expect(result.status).toBe('placed');
    if (result.status !== 'placed') return;
    const perPlacement = result.stats.entriesExamined / result.placements.length;
    // Nearby-cell lookup: a small fraction of the 1,000 objects examined.
    expect(perPlacement).toBeLessThanOrEqual(1000 * 0.25);
    // And nowhere near an O(n^2) pairwise scan (1M for n=1000).
    expect(result.stats.intersectionTests).toBeLessThan(1000 * 10);
  });

  it('returns a typed blocked outcome when pinned objects seal the origin', () => {
    const blockers = [
      pinnedObject('pin-0', origin.x, origin.y),
      pinnedObject('pin-1', origin.x, origin.y + 144),
    ];
    const result = placeDocumentsNearOrigin(blockers, [
      { id: 'doc-1', title: 'a.docx', width: 160, height: 120 },
    ], { origin, columns: 1, maxCandidates: 2 });

    expect(result.status).toBe('blocked');
    if (result.status !== 'blocked') return;
    expect(result.code).toBe('blocked-by-pinned');
    expect(result.blockingIds.length).toBeGreaterThan(0);
    // Nothing was moved and the input objects are untouched.
    expect(result.reflowed).toHaveLength(0);
    expect(blockers[0].x).toBe(origin.x);
  });
});

describe('CAN-05 AssistedPlacement (cycle 3: 100/500/1,000-object benchmark)', () => {
  function scatteredField(count: number): PlaceableObject[] {
    const objects: PlaceableObject[] = [];
    for (let i = 0; i < count; i++) {
      objects.push({
        id: `obj-${i}`,
        x: 5000 + (i % 40) * 200,
        y: 5000 + Math.floor(i / 40) * 160,
        width: 160,
        height: 120,
        title: `obj-${i}`,
        type: 'document',
        positionMode: 'auto',
      });
    }
    return objects;
  }

  function newDocs(count: number): NewDocumentSpec[] {
    return Array.from({ length: count }, (_, i) => ({
      id: `new-${i}`,
      title: `released-${i}.docx`,
      width: 160,
      height: 120,
    }));
  }

  it('open-all release cost is independent of canvas size (100/500/1,000)', () => {
    const origin = { x: 400, y: 300 };
    const counts = [100, 500, 1000] as const;
    const targets = {
      100: PLACEMENT_BENCHMARK_TARGETS_MS.objects100,
      500: PLACEMENT_BENCHMARK_TARGETS_MS.objects500,
      1000: PLACEMENT_BENCHMARK_TARGETS_MS.objects1000,
    } as const;
    const examinedByCount: Record<number, number> = {};

    for (const count of counts) {
      const startedAt = performance.now();
      const result = placeDocumentsNearOrigin(scatteredField(count), newDocs(25), { origin });
      const elapsedMs = performance.now() - startedAt;

      expect(result.status).toBe('placed');
      if (result.status !== 'placed') return;
      expect(result.placements).toHaveLength(25);
      examinedByCount[count] = result.stats.entriesExamined;

      const perPlacement = result.stats.entriesExamined / result.placements.length;
      // Machine-independent gate: per-placement index work is bounded by
      // local cell density (a constant), not by canvas size.
      expect(perPlacement).toBeLessThanOrEqual(MAX_EXAMINED_PER_PLACEMENT);
      // Contrast with a brute-force full scan, which would perform
      // `count` intersection tests per candidate query.
      const bruteForceTests = result.stats.indexQueries * count;
      expect(result.stats.intersectionTests).toBeLessThan(bruteForceTests / 10);
      // Wall-clock guard with a generous recorded target (actuals in handoff).
      // eslint-disable-next-line no-console
      console.log(`CAN-05 benchmark: ${count} objects, 25 placements → ${elapsedMs.toFixed(1)}ms (target ${targets[count]}ms, examined/placement ${perPlacement.toFixed(1)}, queries ${result.stats.indexQueries})`);
      expect(elapsedMs).toBeLessThan(targets[count]);
    }

    // Index work must not grow with canvas size: 1,000 objects costs ~the
    // same examined entries as 100 (nearby-candidate lookup, no full scan).
    expect(examinedByCount[1000]).toBeLessThanOrEqual(examinedByCount[100] * 1.5);
    expect(examinedByCount[500]).toBeLessThanOrEqual(examinedByCount[100] * 1.5);
  });
});
