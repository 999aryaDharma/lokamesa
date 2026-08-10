import { render, screen } from '@testing-library/react';
import App from './App';
import { useCanvasStore } from './features/canvas/canvasStore';
import { beforeEach } from 'vitest';

beforeEach(() => {
  useCanvasStore.getState().reset();
});

describe('App', () => {
  it('renders the canvas surface', () => {
    render(<App />);
    expect(screen.getByTestId('canvas-surface')).toBeInTheDocument();
  });
});
