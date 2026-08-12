// tests/frontend/graph/DocumentGraph.test.tsx

import { render, act } from '@testing-library/react';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import DocumentGraph from '../../src/DocumentIDE.Web/src/features/graph/DocumentGraph';
import { useGraphStore } from '../../src/DocumentIDE.Web/src/features/graph/graphStore';
import { useCanvasStore } from '../../src/DocumentIDE.Web/src/features/canvas/canvasStore';

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

    // add a graph node and move it via store (no UI interaction)
    const { setPosition } = useGraphStore.getState();
    act(() => setPosition('node-1', { x: 0, y: 0 }));
    act(() => setPosition('node-1', { x: 100, y: 200 }));

    // Expect canvas viewport unchanged
    const { viewport: newViewport } = useCanvasStore.getState();
    expect(newViewport).toEqual(initialViewport);
  });

  it('clicking a graph node should not affect canvas viewport', () => {
    // set initial canvas viewport
    const { setViewport, viewport } = useCanvasStore.getState();
    act(() => setViewport({ x: 5, y: 5, zoom: 1 }));
    const initialViewport = { ...viewport };

    // set up a graph node
    const { setPosition } = useGraphStore.getState();
    act(() => setPosition('node-click', { x: 0, y: 0 }));

    // render component
    const { getByTestId } = render(<DocumentGraph />);
    const node = getByTestId('graph-node-node-click');
    // simulate click
    act(() => { node.dispatchEvent(new MouseEvent('click', { bubbles: true })); });

    // viewport should remain unchanged
    const { viewport: newViewport } = useCanvasStore.getState();
    expect(newViewport).toEqual(initialViewport);
  });
});
