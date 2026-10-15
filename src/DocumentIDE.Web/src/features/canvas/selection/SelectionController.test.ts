/**
 * CAN-07: Multi-Select, Group Drag & Tidy — SelectionController tests
 * (colocated per CAN-05 ruling).
 *
 * Selection and group drag are organizational only: pure functions over an
 * input snapshot. No filesystem mutation; canvas drag never moves files.
 */
import { describe, expect, it } from 'vitest';
import { applyClick, groupDrag, rectSelect } from './SelectionController';
import type { SelectableObject } from './SelectionController';

function doc(id: string, x: number, y: number): SelectableObject {
  return { id, x, y, width: 160, height: 100, title: id, type: 'document' };
}

describe('rectSelect', () => {
  it('selects every object intersecting the drag rectangle', () => {
    const objects = [doc('a', 0, 0), doc('b', 500, 500), doc('c', 100, 100)];
    // Rectangle covers a and c (overlap), not b.
    const selected = rectSelect(objects, { x: 0, y: 0, width: 300, height: 300 });
    expect(selected.sort()).toEqual(['a', 'c']);
  });

  it('returns an empty selection when the rectangle hits nothing', () => {
    const objects = [doc('a', 0, 0)];
    expect(rectSelect(objects, { x: 900, y: 900, width: 50, height: 50 })).toEqual([]);
  });
});

describe('applyClick', () => {
  it('replaces the selection on a plain click', () => {
    expect(applyClick(['a', 'b'], 'c', false)).toEqual(['c']);
  });

  it('toggles a single id on Ctrl-click without disturbing the rest', () => {
    expect(applyClick(['a', 'b'], 'c', true).sort()).toEqual(['a', 'b', 'c']);
    expect(applyClick(['a', 'b'], 'b', true)).toEqual(['a']);
  });
});

describe('groupDrag', () => {
  it('preserves relative positions and never stacks objects', () => {
    const objects = [doc('a', 0, 0), doc('b', 200, 0), doc('other', 900, 900)];
    const result = groupDrag(objects, ['a', 'b'], { dx: 50, dy: 30 });
    expect(result.positions['a']).toEqual({ x: 50, y: 30 });
    expect(result.positions['b']).toEqual({ x: 250, y: 30 });
    // Relative offset unchanged: no stacking.
    expect(result.positions['b'].x - result.positions['a'].x).toBe(200);
    expect(result.positions['b'].y - result.positions['a'].y).toBe(0);
    const moved = new Set(result.moves.map((m) => `${m.toX},${m.toY}`));
    expect(moved.size).toBe(result.moves.length);
    // Unselected objects are untouched.
    expect(result.positions['other']).toEqual({ x: 900, y: 900 });
    expect(result.moves.find((m) => m.id === 'other')).toBeUndefined();
  });

  it('does not move pinned objects in a group drag', () => {
    const objects = [
      { ...doc('pinned-1', 0, 0), positionMode: 'pinned' as const },
      doc('a', 200, 0),
    ];
    const result = groupDrag(objects, ['pinned-1', 'a'], { dx: 50, dy: 0 });
    expect(result.positions['pinned-1']).toEqual({ x: 0, y: 0 });
    expect(result.positions['a']).toEqual({ x: 250, y: 0 });
  });
});
