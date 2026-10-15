import { describe, it, expect } from 'vitest';
import {
  placeDocumentsNearOrigin,
  PLACEMENT_BENCHMARK_TARGETS_MS,
  MAX_EXAMINED_PER_PLACEMENT,
} from './AssistedPlacement';
import type { NewDocumentSpec, PlaceableObject } from './AssistedPlacement';
import { tidyCanvas } from './TidyLayout';
import type { TidyableObject } from './TidyLayout';

/**
 * QA-06 — Performance Benchmark Gate, canvas leg.
 *
 * Re-runs the CAN-05 spatial-index placement path and the CAN-07 tidy path at
 * 100/500/1,000 objects and asserts the machine-independent regression gates:
 * per-placement index work bounded by local cell density (never canvas size),
 * examined-entries size-independence, and recorded wall-clock targets.
 * Numbers are printed as QA06_* lines for docs/quality/performance-baseline.md.
 */
describe('QA-06 canvas performance gate (CAN-05 spatial index + CAN-07 tidy)', () => {
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

  it('placement has no O(n^2) full scan at 100/500/1,000 objects', () => {
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

      // Machine-independent gate: per-placement index work is bounded by
      // local cell density (a constant), not by canvas size.
      const perPlacement = result.stats.entriesExamined / result.placements.length;
      expect(perPlacement).toBeLessThanOrEqual(MAX_EXAMINED_PER_PLACEMENT);
      // Contrast with a brute-force full scan: `count` intersection tests
      // per candidate query. The index path must stay an order of magnitude
      // below it at every canvas size.
      const bruteForceTests = result.stats.indexQueries * count;
      expect(result.stats.intersectionTests).toBeLessThan(bruteForceTests / 10);
      // eslint-disable-next-line no-console
      console.log(
        `QA06_PLACEMENT objects=${count} placements=25 elapsedMs=${elapsedMs.toFixed(1)} ` +
          `targetMs=${targets[count]} examinedPerPlacement=${perPlacement.toFixed(1)} ` +
          `queries=${result.stats.indexQueries} intersectionTests=${result.stats.intersectionTests}`
      );
      expect(elapsedMs).toBeLessThan(targets[count]);
    }

    // Index work must not grow with canvas size: 1,000 objects costs ~the
    // same examined entries as 100 (nearby-candidate lookup, no full scan).
    expect(examinedByCount[1000]).toBeLessThanOrEqual(examinedByCount[100] * 1.5);
    expect(examinedByCount[500]).toBeLessThanOrEqual(examinedByCount[100] * 1.5);
  });

  it('tidy stays index-backed and within targets at 100/500/1,000 objects', () => {
    const targets = { 100: 2000, 500: 5000, 1000: 10000 } as const;
    const run = (count: number): number => {
      const objects: TidyableObject[] = [];
      for (let i = 0; i < count; i++) {
        objects.push({
          id: `doc-${i}`,
          // Deliberately overlapping jittered rows so tidy has real work.
          x: (i % 25) * 40,
          y: Math.floor(i / 25) * 30,
          width: 160,
          height: 100,
          title: `doc-${i}`,
          type: 'document',
        });
      }
      const startedAt = performance.now();
      const result = tidyCanvas(objects);
      const elapsedMs = performance.now() - startedAt;
      // Sanity: tidy actually moved things and stayed index-backed
      // (probes went through the spatial index, never a full scan).
      expect(result.moves.length).toBeGreaterThan(0);
      expect(result.stats.indexQueries).toBeGreaterThan(0);
      // eslint-disable-next-line no-console
      console.log(
        `QA06_TIDY objects=${count} elapsedMs=${elapsedMs.toFixed(1)} ` +
          `targetMs=${targets[count as 100 | 500 | 1000]} moves=${result.moves.length} ` +
          `indexQueries=${result.stats.indexQueries}`
      );
      return elapsedMs;
    };

    expect(run(100)).toBeLessThan(targets[100]);
    expect(run(500)).toBeLessThan(targets[500]);
    expect(run(1000)).toBeLessThan(targets[1000]);
  });
});
