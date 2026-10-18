import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import SearchField from './SearchField';

describe('SearchField', () => {
  it('renders a labelled search textbox', () => {
    render(<SearchField ariaLabel="Search workspace" />);
    expect(screen.getByRole('searchbox', { name: 'Search workspace' })).toBeInTheDocument();
  });

  it('submits the typed query on Enter only', () => {
    const onSubmit = vi.fn();
    render(<SearchField ariaLabel="Search workspace" onSubmit={onSubmit} />);
    const input = screen.getByRole('searchbox', { name: 'Search workspace' });

    fireEvent.change(input, { target: { value: 'nusa type:docx' } });
    expect(onSubmit).not.toHaveBeenCalled();

    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onSubmit).toHaveBeenCalledWith('nusa type:docx');
  });

  it('reports every keystroke when an onChange consumer is provided', () => {
    const onChange = vi.fn();
    render(<SearchField ariaLabel="Filter by name..." onChange={onChange} />);
    fireEvent.change(screen.getByRole('searchbox', { name: 'Filter by name...' }), {
      target: { value: 'Q3' },
    });
    expect(onChange).toHaveBeenCalledWith('Q3');
  });
});
