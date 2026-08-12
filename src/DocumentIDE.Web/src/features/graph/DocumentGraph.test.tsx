// src/DocumentIDE.Web/src/features/graph/DocumentGraph.test.tsx

import { render, act } from '@testing-library/react';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import DocumentGraph from './DocumentGraph';
import { useGraphStore } from './graphStore';
import { useCanvasStore } from '../canvas/canvasStore';

/**
 * Test that moving a graph node does NOT affect canvas store state.
 */
describe('GraphNode_DoesNotAffectCanvas', () => {
  beforeEach(() => {
    // reset stores
    useGraphStore.getState().reset();
    useCanvasStore.getState().reset();
  });

  afterEach(() => {
    vi.restoreAllMocks && vi.restoreAllMocks();
  });

  it('moving graph node should not change canvas viewport', () => {
    // set initial canvas viewport
    const { setViewport, viewport } = useCanvasStore.getState();
    act(() => setViewport({ x: 10, y: 20, zoom: 1 }));
    const initialViewport = { ...viewport };

    // add a graph node and move it
    const { setPosition } = useGraphStore.getState();
    act(() => setPosition('node-1', { x: 0, y: 0 }));
    act(() => setPosition('node-1', { x: 100, y: 200 }));

    // Expect canvas viewport unchanged (intentionally failing)
    const { viewport: newViewport } = useCanvasStore.getState();
    expect(newViewport).toEqual(initialViewport);
  });
});
