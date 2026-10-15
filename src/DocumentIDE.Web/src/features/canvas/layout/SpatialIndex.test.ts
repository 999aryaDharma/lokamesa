import { describe, expect, it } from 'vitest';
import { rectsOverlap, SpatialHashIndex } from './SpatialIndex';

describe('CAN-05 SpatialHashIndex (uniform grid)', () => {
  it('finds overlapping entries and ignores distant ones', () => {
    const index = new SpatialHashIndex(256);
    index.insert('a', { x: 0, y: 0, width: 160, height: 120 });
    index.insert('b', { x: 5000, y: 5000, width: 160, height: 120 });

    expect(index.query({ x: 10, y: 10, width: 160, height: 120 })).toEqual(['a']);
    expect(index.query({ x: 2000, y: 2000, width: 160, height: 120 })).toEqual([]);
  });

  it('tracks entries that span multiple cells without duplicates', () => {
    const index = new SpatialHashIndex(100);
    index.insert('wide', { x: 50, y: 50, width: 300, height: 50 });
    const hits = index.query({ x: 0, y: 0, width: 1000, height: 1000 });
    expect(hits).toEqual(['wide']);
  });

  it('update moves an entry and remove drops it', () => {
    const index = new SpatialHashIndex(256);
    index.insert('m', { x: 0, y: 0, width: 100, height: 100 });
    index.update('m', { x: 3000, y: 3000, width: 100, height: 100 });
    expect(index.query({ x: 0, y: 0, width: 200, height: 200 })).toEqual([]);
    expect(index.query({ x: 2950, y: 2950, width: 200, height: 200 })).toEqual(['m']);
    index.remove('m');
    expect(index.has('m')).toBe(false);
    expect(index.query({ x: 2950, y: 2950, width: 200, height: 200 })).toEqual([]);
  });

  it('rejects duplicate inserts so double-release bugs surface loudly', () => {
    const index = new SpatialHashIndex(256);
    index.insert('dup', { x: 0, y: 0, width: 10, height: 10 });
    expect(() => index.insert('dup', { x: 0, y: 0, width: 10, height: 10 })).toThrow();
  });

  it('examines only nearby entries, not the full index', () => {
    const index = new SpatialHashIndex(256);
    for (let i = 0; i < 1000; i++) {
      index.insert(`obj-${i}`, {
        x: 5000 + (i % 40) * 200,
        y: 5000 + Math.floor(i / 40) * 160,
        width: 160,
        height: 120,
      });
    }
    index.resetStats();
    const hits = index.query({ x: 5050, y: 5050, width: 160, height: 120 });
    expect(hits.length).toBeGreaterThan(0);
    const stats = index.getStats();
    // One card-sized query touches ~1 cell holding ~1-2 cards out of 1,000.
    expect(stats.entryVisits).toBeLessThanOrEqual(16);
    expect(stats.intersectionTests).toBeLessThanOrEqual(16);
  });

  it('rectsOverlap treats edge-touching as non-overlapping', () => {
    const a = { x: 0, y: 0, width: 100, height: 100 };
    expect(rectsOverlap(a, { x: 100, y: 0, width: 100, height: 100 })).toBe(false);
    expect(rectsOverlap(a, { x: 99, y: 0, width: 100, height: 100 })).toBe(true);
  });
});
