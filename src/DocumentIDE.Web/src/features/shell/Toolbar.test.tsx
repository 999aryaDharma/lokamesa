import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import Toolbar from './Toolbar';

function renderToolbar(overrides: Partial<React.ComponentProps<typeof Toolbar>> = {}) {
  const props: React.ComponentProps<typeof Toolbar> = {
    activeView: 'canvas',
    onViewChange: () => {},
    sidebarOpen: true,
    onToggleSidebar: () => {},
    inspectorOpen: true,
    onToggleInspector: () => {},
    onSearchSubmit: () => {},
    ...overrides,
  };
  return render(<Toolbar {...props} />);
}

describe('Toolbar', () => {
  it('renders the Canvas | List | Graph segmented control', () => {
    renderToolbar();
    expect(screen.getByRole('radiogroup', { name: 'Main view switch' })).toBeInTheDocument();
    expect(screen.getByTestId('view-canvas-btn')).toHaveAttribute('aria-checked', 'true');
  });

  it('routes a view change through the caller', () => {
    const onViewChange = vi.fn();
    renderToolbar({ onViewChange });
    fireEvent.click(screen.getByTestId('view-graph-btn'));
    expect(onViewChange).toHaveBeenCalledWith('graph');
  });

  it('submits the toolbar search field on Enter', () => {
    const onSearchSubmit = vi.fn();
    renderToolbar({ onSearchSubmit });
    const field = screen.getByRole('searchbox', { name: 'Search workspace' });
    fireEvent.change(field, { target: { value: 'nusa' } });
    fireEvent.keyDown(field, { key: 'Enter' });
    expect(onSearchSubmit).toHaveBeenCalledWith('nusa');
  });

  it('exposes the sidebar and inspector toggles with their state', () => {
    renderToolbar({ sidebarOpen: false, inspectorOpen: true });
    expect(screen.getByRole('button', { name: 'Toggle sidebar' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    expect(screen.getByRole('button', { name: 'Toggle inspector' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('keeps the Batch operations and PDF surfaces reachable from the toolbar', () => {
    const onToggleBatch = vi.fn();
    const onTogglePdf = vi.fn();
    renderToolbar({ onToggleBatch, onTogglePdf, batchOpen: false, pdfOpen: false });

    fireEvent.click(screen.getByRole('button', { name: 'Batch operations' }));
    fireEvent.click(screen.getByRole('button', { name: 'PDF' }));

    expect(onToggleBatch).toHaveBeenCalledTimes(1);
    expect(onTogglePdf).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Batch operations' })).toHaveAttribute(
      'aria-expanded',
      'false',
    );
    expect(screen.getByRole('button', { name: 'PDF' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('decorates the window with the macOS traffic lights, hidden from AT', () => {
    const { container } = renderToolbar();
    const lights = container.querySelector('[data-testid="traffic-lights"]');
    expect(lights).not.toBeNull();
    expect(lights?.getAttribute('aria-hidden')).toBe('true');
    expect(lights?.children).toHaveLength(3);
  });
});
