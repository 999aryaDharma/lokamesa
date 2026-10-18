/**
 * CAN-07: Multi-Select, Group Drag & Tidy — TidyLayout tests (colocated per CAN-05 ruling).
 *
 * Tidy is organizational only: pure geometry over an input snapshot, no
 * filesystem mutation. Canvas drag never moves physical files.
 */
import { describe, expect, it } from 'vitest';
import { centroid, tidyCanvas, tidySelection } from './TidyLayout';
import type { TidyableObject } from './TidyLayout';
import { rectsOverlap } from './SpatialIndex';

function doc(id: string, x: number, y: number, mode: TidyableObject['positionMode'] = 'auto'): TidyableObject {
  return { id, x, y, width: 160, height: 100, title: id, type: 'document', positionMode: mode };
}

describe('tidyCanvas', () => {
  it('excludes pinned objects from tidy (positions byte-identical)', () => {
    const objects = [
      doc('pinned-1', 0, 0, 'pinned'),
      doc('a', 10, 10),
      doc('b', 20, 20),
    ];
    const result = tidyCanvas(objects);
    const pinned = result.moves.find((m) => m.id === 'pinned-1');
    expect(pinned).toBeUndefined();
    expect(result.positions['pinned-1']).toEqual({ x: 0, y: 0 });
  });

  it('tidy selection stays in its approximate region', () => {
    const objects = [
      doc('s1', 1000, 1000),
      doc('s2', 1010, 1010),
      doc('s3', 1020, 1005),
      doc('far', 0, 0),
    ];
    const before = centroid(objects.filter((o) => o.id !== 'far'));
    const result = tidySelection(objects, ['s1', 's2', 's3']);
    // Non-vacuous: the overlapping selection actually had to move.
    expect(result.moves.length).toBeGreaterThan(0);
    const after = centroid(['s1', 's2', 's3'].map((id) => ({ ...result.positions[id], width: 160, height: 100 })));
    expect(Math.hypot(after.x - before.x, after.y - before.y)).toBeLessThanOrEqual(600);
    for (const id of ['s1', 's2', 's3']) {
      const original = objects.find((o) => o.id === id) as TidyableObject;
      const moved = Math.hypot(
        result.positions[id].x - original.x,
        result.positions[id].y - original.y,
      );
      expect(moved).toBeLessThanOrEqual(800);
    }
    // Unselected objects are untouched.
    expect(result.positions['far']).toEqual({ x: 0, y: 0 });
    expect(result.moves.find((m) => m.id === 'far')).toBeUndefined();
  });

  it('tidy selection never moves pinned objects even when selected', () => {
    const objects = [doc('pinned-1', 500, 500, 'pinned'), doc('a', 510, 510)];
    const result = tidySelection(objects, ['pinned-1', 'a']);
    expect(result.moves.find((m) => m.id === 'pinned-1')).toBeUndefined();
    expect(result.positions['pinned-1']).toEqual({ x: 500, y: 500 });
  });

  it('tidy canvas resolves overlap without touching pinned objects', () => {
    const objects = [
      doc('pinned-1', 0, 0, 'pinned'),
      doc('a', 500, 500),
      doc('b', 510, 510),
      doc('c', 520, 520),
    ];
    const result = tidyCanvas(objects);
    const rects = objects.map((o) => ({ id: o.id, ...result.positions[o.id], width: o.width, height: o.height }));
    for (let i = 0; i < rects.length; i++) {
      for (let j = i + 1; j < rects.length; j++) {
        expect(rectsOverlap(rects[i], rects[j])).toBe(false);
      }
    }
    expect(result.positions['pinned-1']).toEqual({ x: 0, y: 0 });
    // Collision resolution went through the spatial index, not a full scan.
    expect(result.stats.indexQueries).toBeGreaterThan(0);
  });

  it('tidy is organizational only: inputs are never mutated', () => {
    const objects = [doc('pinned-1', 0, 0, 'pinned'), doc('a', 10, 10), doc('b', 20, 20)];
    const frozen = JSON.parse(JSON.stringify(objects)) as TidyableObject[];
    tidyCanvas(objects);
    tidySelection(objects, ['a', 'b']);
    expect(objects).toEqual(frozen);
  });

  it('benchmark probe for QA-06: tidy at 100/500/1,000 objects', () => {
    const targets = { objects100: 2000, objects500: 5000, objects1000: 10000 };
    const run = (n: number): number => {
      const objects: TidyableObject[] = [];
      for (let i = 0; i < n; i++) {
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
      const startedAt = Date.now();
      const result = tidyCanvas(objects);
      const elapsedMs = Date.now() - startedAt;
      // Sanity: tidy actually moved things and stayed index-backed.
      expect(result.moves.length).toBeGreaterThan(0);
      expect(result.stats.indexQueries).toBeGreaterThan(0);
      return elapsedMs;
    };
    const ms100 = run(100);
    const ms500 = run(500);
    const ms1000 = run(1000);
    console.log(`TIDY_BENCHMARK_MS objects=100 elapsed=${ms100}`);
    console.log(`TIDY_BENCHMARK_MS objects=500 elapsed=${ms500}`);
    console.log(`TIDY_BENCHMARK_MS objects=1000 elapsed=${ms1000}`);
    expect(ms100).toBeLessThan(targets.objects100);
    expect(ms500).toBeLessThan(targets.objects500);
    expect(ms1000).toBeLessThan(targets.objects1000);
  });
});
