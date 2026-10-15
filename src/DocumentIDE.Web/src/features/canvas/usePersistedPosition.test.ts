import { beforeEach, describe, expect, it } from 'vitest';
import {
  loadPositions,
  pinPosition,
  recordDrag,
  resolveRestorePosition,
  savePositions,
  storageKey,
  unpinPosition,
  type OccupiedRect,
  type PositionMap,
} from './usePersistedPosition';

const WS = 'ws-can-06';

function occupied(id: string, x: number, y: number): OccupiedRect {
  return { id, x, y, width: 160, height: 100 };
}

describe('CAN-06 usePersistedPosition', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('manual position survives restart (localStorage roundtrip)', () => {
    recordDrag(WS, 'doc-a', 320, 240);

    // Simulate restart: fresh load from durable storage.
    const restored = loadPositions(WS);

    expect(restored['doc-a']).toMatchObject({ x: 320, y: 240, mode: 'manual' });
  });

  it('drag switches to manual without touching other entries', () => {
    savePositions(WS, { 'doc-a': { x: 0, y: 0, mode: 'auto' } });

    const next = recordDrag(WS, 'doc-a', 500, 120);

    expect(next['doc-a']).toMatchObject({ x: 500, y: 120, mode: 'manual' });
    expect(loadPositions(WS)['doc-a']).toMatchObject({ mode: 'manual' });
  });

  it('pin/unpin roundtrip retains coordinates', () => {
    recordDrag(WS, 'doc-a', 10, 20);

    const pinned = pinPosition(WS, 'doc-a');
    expect(pinned['doc-a']).toMatchObject({ x: 10, y: 20, mode: 'pinned' });

    const unpinned = unpinPosition(WS, 'doc-a');
    expect(unpinned['doc-a']).toMatchObject({ x: 10, y: 20, mode: 'manual' });
  });

  it('rename preserves position: records are keyed by DocumentId, not path', () => {
    recordDrag(WS, 'doc-a', 100, 200);

    const afterRename = loadPositions(WS)['doc-a'];

    expect(afterRename).toMatchObject({ x: 100, y: 200, mode: 'manual' });
    // No path field exists to go stale on rename.
    expect('path' in (afterRename as object)).toBe(false);
  });

  it('free restore keeps exact coordinates', () => {
    const persisted: PositionMap = { 'doc-a': { x: 100, y: 200, mode: 'manual' } };

    const resolved = resolveRestorePosition(persisted['doc-a'], 'doc-a', [], 160, 100);

    expect(resolved).toMatchObject({ x: 100, y: 200, fellBack: false });
  });

  it('occupied restore resolves to nearest-safe fallback', () => {
    const persisted: PositionMap = { 'doc-a': { x: 100, y: 200, mode: 'manual' } };

    const resolved = resolveRestorePosition(persisted['doc-a'], 'doc-a', [occupied('other', 100, 200)], 160, 100);

    expect(resolved.fellBack).toBe(true);
    expect(Math.hypot(resolved.x - 100, resolved.y - 200)).toBeLessThanOrEqual(600);
    // No overlap with the occupant.
    expect(
      resolved.x < 100 + 160 && 100 < resolved.x + 160 && resolved.y < 200 + 100 && 200 < resolved.y + 100,
    ).toBe(false);
  });

  it('corrupt storage degrades to empty map, never throws', () => {
    localStorage.setItem(storageKey(WS), '{not-json');

    expect(loadPositions(WS)).toEqual({});
  });
});
