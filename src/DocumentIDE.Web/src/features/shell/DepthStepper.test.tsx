import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import DepthStepper from './DepthStepper';

describe('DepthStepper', () => {
  it('shows the current value between decrement and increment controls', () => {
    render(<DepthStepper value={3} min={1} max={6} onChange={() => {}} />);
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Decrease depth' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Increase depth' })).toBeInTheDocument();
  });

  it('emits value-1 / value+1', () => {
    const onChange = vi.fn();
    render(<DepthStepper value={3} min={1} max={6} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Decrease depth' }));
    expect(onChange).toHaveBeenLastCalledWith(2);
    fireEvent.click(screen.getByRole('button', { name: 'Increase depth' }));
    expect(onChange).toHaveBeenLastCalledWith(4);
  });

  it('clamps at the bounds and disables the offending control', () => {
    const onChange = vi.fn();
    const { rerender } = render(
      <DepthStepper value={1} min={1} max={6} onChange={onChange} />,
    );
    expect(screen.getByRole('button', { name: 'Decrease depth' })).toBeDisabled();

    rerender(<DepthStepper value={6} min={1} max={6} onChange={onChange} />);
    expect(screen.getByRole('button', { name: 'Increase depth' })).toBeDisabled();
  });
});
