import { render, screen, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import App from '../../App';
it('opens review beside a usable canvas and closes it', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => [] })));
  render(<App />);
  fireEvent.click(screen.getByRole('button', { name: 'Batch operations' }));
  expect(screen.getByRole('region', { name: 'Batch operations' })).toBeInTheDocument();
  expect(screen.getByTestId('canvas-surface')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Batch operations' }));
  expect(screen.queryByRole('region', { name: 'Batch operations' })).not.toBeInTheDocument();
});
it('leaves Tools Space default activation available with real canvas mounted', () => {
  render(<App />);
  const button = screen.getByRole('button', { name: 'Batch operations' });
  button.focus();
  expect(fireEvent.keyDown(button, { code: 'Space', key: ' ', bubbles: true, cancelable: true })).toBe(true);
  expect(screen.getByTestId('canvas-surface')).not.toHaveStyle({ cursor: 'grab' });
});
