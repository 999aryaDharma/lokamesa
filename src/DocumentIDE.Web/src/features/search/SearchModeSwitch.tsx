/**
 * AI-03: Semantic Search Mode — SearchModeSwitch component
 *
 * Renders mode toggle tabs ("Exact", "Fuzzy", "Smart") for search query execution.
 * Allows explicitly selecting exploratory semantic search mode independent from Exact mode.
 */
import React from 'react';

export type SearchMode = 'exact' | 'fuzzy' | 'smart';

export interface SearchModeSwitchProps {
  mode: SearchMode;
  onModeChange: (mode: SearchMode) => void;
  disabled?: boolean;
}

export const SearchModeSwitch: React.FC<SearchModeSwitchProps> = ({
  mode,
  onModeChange,
  disabled = false,
}) => {
  return (
    <div
      role="radiogroup"
      aria-label="Search Mode Switch"
      className="inline-flex items-center p-0.5 bg-mac-chip dark:bg-slate-800 rounded-md border border-mac-hairline dark:border-slate-700 text-xs select-none"
    >
      <button
        type="button"
        role="radio"
        aria-checked={mode === 'exact'}
        disabled={disabled}
        onClick={() => onModeChange('exact')}
        data-testid="mode-exact-btn"
        className={`px-2 py-1 rounded transition-colors font-medium cursor-pointer ${
          mode === 'exact'
            ? 'bg-mac-content dark:bg-slate-700 text-mac-text dark:text-slate-100 shadow-xs'
            : 'text-mac-text-3 dark:text-slate-400 hover:text-mac-text dark:hover:text-slate-200'
        }`}
      >
        Exact
      </button>

      <button
        type="button"
        role="radio"
        aria-checked={mode === 'fuzzy'}
        disabled={disabled}
        onClick={() => onModeChange('fuzzy')}
        data-testid="mode-fuzzy-btn"
        className={`px-2 py-1 rounded transition-colors font-medium cursor-pointer ${
          mode === 'fuzzy'
            ? 'bg-mac-content dark:bg-slate-700 text-mac-text dark:text-slate-100 shadow-xs'
            : 'text-mac-text-3 dark:text-slate-400 hover:text-mac-text dark:hover:text-slate-200'
        }`}
      >
        Fuzzy
      </button>

      <button
        type="button"
        role="radio"
        aria-checked={mode === 'smart'}
        disabled={disabled}
        onClick={() => onModeChange('smart')}
        data-testid="mode-smart-btn"
        className={`px-2 py-1 rounded transition-colors font-medium cursor-pointer flex items-center gap-1 ${
          mode === 'smart'
            ? 'bg-purple-600 text-white shadow-xs'
            : 'text-purple-600 dark:text-purple-400 hover:text-purple-800 dark:hover:text-purple-300'
        }`}
      >
        <span>✨</span> Smart
      </button>
    </div>
  );
};

export default SearchModeSwitch;
