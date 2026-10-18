import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import PushButton from './PushButton';

describe('PushButton', () => {
  it('renders its children as the accessible button name', () => {
    render(<PushButton>Apply 34 changes</PushButton>);
    expect(screen.getByRole('button', { name: 'Apply 34 changes' })).toBeInTheDocument();
  });

  it('is a plain submit-free button by default', () => {
    render(<PushButton>Cancel</PushButton>);
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveAttribute('type', 'button');
  });

  it('runs the click handler', () => {
    const onClick = vi.fn();
    render(<PushButton onClick={onClick}>Stage</PushButton>);
    fireEvent.click(screen.getByRole('button', { name: 'Stage' }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('exposes the disabled state', () => {
    render(<PushButton disabled>Commit staged</PushButton>);
    expect(screen.getByRole('button', { name: 'Commit staged' })).toBeDisabled();
  });

  it('applies the requested variant/emphasis without changing the accessible name', () => {
    render(
      <PushButton variant="primary" aria-label="PDF">
        Export
      </PushButton>,
    );
    const button = screen.getByRole('button', { name: 'PDF' });
    expect(button.className).toContain('bg-mac-accent');
  });
});
