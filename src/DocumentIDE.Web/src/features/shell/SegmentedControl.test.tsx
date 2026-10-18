import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import SegmentedControl from './SegmentedControl';

const OPTIONS = [
  { value: 'canvas', label: 'Canvas', testId: 'view-canvas-btn' },
  { value: 'list', label: 'List', testId: 'view-list-btn' },
  { value: 'graph', label: 'Graph', testId: 'view-graph-btn' },
] as const;

describe('SegmentedControl', () => {
  it('is a labelled radiogroup whose options are radios', () => {
    render(
      <SegmentedControl
        ariaLabel="Main view switch"
        value="canvas"
        options={OPTIONS}
        onChange={() => {}}
      />,
    );

    expect(screen.getByRole('radiogroup', { name: 'Main view switch' })).toBeInTheDocument();
    expect(screen.getAllByRole('radio')).toHaveLength(3);
  });

  it('marks exactly the active option as checked', () => {
    render(
      <SegmentedControl
        ariaLabel="Main view switch"
        value="list"
        options={OPTIONS}
        onChange={() => {}}
      />,
    );

    expect(screen.getByTestId('view-canvas-btn')).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByTestId('view-list-btn')).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByTestId('view-graph-btn')).toHaveAttribute('aria-checked', 'false');
  });

  it('emits the selected option value', () => {
    const onChange = vi.fn();
    render(
      <SegmentedControl
        ariaLabel="Main view switch"
        value="canvas"
        options={OPTIONS}
        onChange={onChange}
      />,
    );

    fireEvent.click(screen.getByTestId('view-graph-btn'));
    expect(onChange).toHaveBeenCalledWith('graph');
  });

  it('disables every option when disabled', () => {
    render(
      <SegmentedControl
        ariaLabel="Main view switch"
        value="canvas"
        options={OPTIONS}
        onChange={() => {}}
        disabled
      />,
    );
    for (const option of OPTIONS) {
      expect(screen.getByTestId(option.testId)).toBeDisabled();
    }
  });
});
