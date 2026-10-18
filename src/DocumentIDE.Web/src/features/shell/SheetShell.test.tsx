import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import SheetShell from './SheetShell';

describe('SheetShell', () => {
  it('is a modal dialog named after its title, over a scrim', () => {
    render(
      <SheetShell title="Batch operations" onClose={() => {}}>
        <p>Body</p>
      </SheetShell>,
    );
    expect(screen.getByRole('dialog', { name: 'Batch operations' })).toHaveAttribute(
      'aria-modal',
      'true',
    );
    expect(screen.getByTestId('sheet-scrim')).toBeInTheDocument();
  });

  it('renders body and footer content', () => {
    render(
      <SheetShell
        title="Create worktree"
        onClose={() => {}}
        footer={<button type="button">Create worktree</button>}
      >
        <p>Path</p>
      </SheetShell>,
    );
    expect(screen.getByText('Path')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create worktree' })).toBeInTheDocument();
  });

  it('closes via the header close affordance', () => {
    const onClose = vi.fn();
    render(
      <SheetShell title="Undo 12 operations?" onClose={onClose}>
        <p>Body</p>
      </SheetShell>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Close Undo 12 operations?' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('hides the close affordance when no onClose handler is provided', () => {
    render(
      <SheetShell title="Missing resource">
        <p>Body</p>
      </SheetShell>,
    );
    expect(screen.queryByRole('button', { name: /Close/ })).not.toBeInTheDocument();
  });
});
