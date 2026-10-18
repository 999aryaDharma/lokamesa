import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { MetadataFilterChips, toggleFilterQuery, buildFilterQuery } from './MetadataFilterChips';

describe('MetadataFilterChips', () => {
  it('renders active filter chips with counts and remove buttons', () => {
    const onRemove = vi.fn();
    render(
      <MetadataFilterChips
        activeFilters={[
          { field: 'font', value: 'Arial', count: 5 },
          { field: 'paper', value: 'Letter', count: 3 },
        ]}
        onRemoveFilter={onRemove}
      />
    );

    expect(screen.getByTestId('active-chip-font-Arial')).toBeDefined();
    expect(screen.getByTestId('active-chip-paper-Letter')).toBeDefined();

    const removeBtn = screen.getByRole('button', { name: 'Remove filter font:Arial' });
    fireEvent.click(removeBtn);

    expect(onRemove).toHaveBeenCalledWith('font', 'Arial');
  });

  it('renders available filter chips and handles selection', () => {
    const onAdd = vi.fn();
    render(
      <MetadataFilterChips
        activeFilters={[]}
        availableFilters={[{ field: 'margin', value: 'mixed', count: 2 }]}
        onAddFilter={onAdd}
      />
    );

    const availableChip = screen.getByTestId('available-chip-margin-mixed');
    expect(availableChip).toBeDefined();

    fireEvent.click(availableChip);
    expect(onAdd).toHaveBeenCalledWith('margin', 'mixed');
  });

  it('toggleFilterQuery correctly builds and toggles filter strings for inspector clicks', () => {
    let query = 'nusa dua';
    query = toggleFilterQuery(query, 'font', 'Arial');
    expect(query).toBe('nusa dua font:Arial');

    // Toggling identical filter removes it
    query = toggleFilterQuery(query, 'font', 'Arial');
    expect(query).toBe('nusa dua');

    // Building filter for paper:Letter
    query = buildFilterQuery('nusa dua', 'paper', 'Letter');
    expect(query).toBe('nusa dua paper:Letter');
  });
});
