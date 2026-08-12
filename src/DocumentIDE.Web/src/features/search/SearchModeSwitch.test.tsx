import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { SearchModeSwitch, SearchMode } from './SearchModeSwitch';

describe('SearchModeSwitch Component', () => {
  it('renders Exact, Fuzzy, and Smart mode buttons', () => {
    render(<SearchModeSwitch mode="exact" onModeChange={() => {}} />);

    expect(screen.getByTestId('mode-exact-btn')).toBeDefined();
    expect(screen.getByTestId('mode-fuzzy-btn')).toBeDefined();
    expect(screen.getByTestId('mode-smart-btn')).toBeDefined();
  });

  it('indicates active mode via aria-checked', () => {
    render(<SearchModeSwitch mode="smart" onModeChange={() => {}} />);

    expect(screen.getByTestId('mode-exact-btn').getAttribute('aria-checked')).toBe('false');
    expect(screen.getByTestId('mode-fuzzy-btn').getAttribute('aria-checked')).toBe('false');
    expect(screen.getByTestId('mode-smart-btn').getAttribute('aria-checked')).toBe('true');
  });

  it('triggers onModeChange callback when mode option is clicked', () => {
    const handleModeChange = vi.fn();
    render(<SearchModeSwitch mode="exact" onModeChange={handleModeChange} />);

    fireEvent.click(screen.getByTestId('mode-smart-btn'));
    expect(handleModeChange).toHaveBeenCalledWith('smart');

    fireEvent.click(screen.getByTestId('mode-fuzzy-btn'));
    expect(handleModeChange).toHaveBeenCalledWith('fuzzy');
  });
});
