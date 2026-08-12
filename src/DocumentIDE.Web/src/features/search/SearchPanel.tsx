/**
 * SRCH-05: Realtime Search UI & Selection — SearchPanel component
 *
 * Renders debounced query-as-you-type search interface, match origins/snippets,
 * metadata filter chips, select-all result action, and batch operation trigger.
 */
import React, { useSyncExternalStore, useCallback } from 'react';
import { MetadataFilterChips } from './MetadataFilterChips';
import { SearchModeSwitch, SearchMode } from './SearchModeSwitch';
import { searchStoreInstance, useSearchStore, SearchResult, SearchBatchScope } from './searchStore';

export interface SearchPanelProps {
  onInvokeBatchOperation?: (scope: SearchBatchScope) => void;
  onSelectDocument?: (documentId: string) => void;
  onClosePanel?: () => void;
}

function useSearchState() {
  return useSyncExternalStore(
    useSearchStore.subscribe,
    useSearchStore.getState,
    useSearchStore.getState,
  );
}

function MatchSourceBadge({ source, docId }: { source: string; docId: string }) {
  const isSemantic = source.toLowerCase() === 'semantic';
  const badgeClass = isSemantic
    ? 'bg-purple-100 dark:bg-purple-900/40 text-purple-800 dark:text-purple-200 border-purple-300'
    : 'bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border-slate-200';

  return (
    <span
      data-testid={`match-source-${source.toLowerCase()}-${docId}`}
      className={`text-[10px] font-mono px-1.5 py-0.5 rounded border ${badgeClass}`}
    >
      {source}
    </span>
  );
}

export const SearchPanel: React.FC<SearchPanelProps> = ({
  onInvokeBatchOperation,
  onSelectDocument,
  onClosePanel,
}) => {
  const state = useSearchState();
  const {
    query,
    results,
    totalCount,
    executionTimeMs,
    isLoading,
    error,
    activeFilterChips,
    selectedResultIds,
    setQuery,
    clearSearch,
    selectAllResults,
    toggleSelectResult,
    removeFilterChip,
    getBatchScope,
  } = state;

  const activeMode: SearchMode = React.useMemo(() => {
    if (/\bmode:(smart|semantic)\b/i.test(query)) return 'smart';
    if (/\bmode:(fuzzy|exploratory)\b/i.test(query)) return 'fuzzy';
    return 'exact';
  }, [query]);

  const handleModeChange = useCallback(
    (newMode: SearchMode) => {
      let cleanQuery = query.replace(/\bmode:\w+\b/gi, '').trim();
      if (newMode !== 'exact') {
        cleanQuery = cleanQuery ? `${cleanQuery} mode:${newMode}` : `mode:${newMode}`;
      }
      setQuery(cleanQuery, true);
    },
    [query, setQuery]
  );

  const handleInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setQuery(e.target.value);
    },
    [setQuery]
  );

  const handleClearClick = useCallback(() => {
    clearSearch();
  }, [clearSearch]);

  const handleSelectAllClick = useCallback(() => {
    selectAllResults();
  }, [selectAllResults]);

  const handleBatchOperationClick = useCallback(() => {
    const scope = getBatchScope();
    onInvokeBatchOperation?.(scope);
  }, [getBatchScope, onInvokeBatchOperation]);

  const handleResultClick = useCallback(
    (docId: string) => {
      toggleSelectResult(docId);
      onSelectDocument?.(docId);
    },
    [toggleSelectResult, onSelectDocument]
  );

  return (
    <aside
      role="search"
      aria-label="Search Panel"
      className="search-panel w-80 max-w-full bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col h-full shadow-sm select-none"
    >
      {/* Header & Search Input */}
      <div className="p-3 border-b border-slate-200 dark:border-slate-800 flex flex-col gap-2">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold text-slate-800 dark:text-slate-100 flex items-center gap-1.5">
            <span>🔍</span> Search Workspace
          </h2>
          {onClosePanel && (
            <button
              type="button"
              aria-label="Close search"
              onClick={onClosePanel}
              className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 text-lg leading-none cursor-pointer"
            >
              &times;
            </button>
          )}
        </div>

        <div className="relative flex items-center w-full">
          <input
            type="text"
            value={query}
            onChange={handleInputChange}
            placeholder="Search workspace (e.g. nusa type:docx)..."
            aria-label="Search input"
            className="w-full px-3 py-1.5 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 text-slate-900 dark:text-slate-100 placeholder-slate-400 pr-7"
          />
          {query ? (
            <button
              type="button"
              aria-label="Clear search"
              onClick={handleClearClick}
              className="absolute right-2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 text-sm cursor-pointer"
            >
              ✕
            </button>
          ) : isLoading ? (
            <span className="absolute right-2 text-xs animate-spin opacity-60">⏳</span>
          ) : null}
        </div>

        {/* Search Mode Switch (Exact / Fuzzy / Smart) */}
        <div className="flex justify-start">
          <SearchModeSwitch mode={activeMode} onModeChange={handleModeChange} />
        </div>

        {/* Metadata Filter Chips */}
        {activeFilterChips.length > 0 && (
          <MetadataFilterChips
            activeFilters={activeFilterChips}
            onRemoveFilter={removeFilterChip}
            onClearAll={clearSearch}
          />
        )}
      </div>

      {/* Status & Actions Bar */}
      {query.trim().length > 0 && (
        <div className="px-3 py-2 bg-slate-50 dark:bg-slate-800/50 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between text-[11px] text-slate-500 dark:text-slate-400">
          <span>
            {isLoading ? (
              'Searching...'
            ) : error ? (
              <span className="text-red-500 font-medium">Search error</span>
            ) : (
              `${totalCount} result${totalCount === 1 ? '' : 's'} (${Math.round(executionTimeMs)}ms)`
            )}
          </span>

          {results.length > 0 && !error && (
            <div className="flex items-center gap-1.5">
              <button
                type="button"
                aria-label="Select all results"
                onClick={handleSelectAllClick}
                className="px-2 py-0.5 rounded bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 font-medium hover:bg-blue-100 cursor-pointer transition-colors"
              >
                Select all results
              </button>

              <button
                type="button"
                aria-label="Batch operation"
                onClick={handleBatchOperationClick}
                className="px-2 py-0.5 rounded bg-slate-200 dark:bg-slate-700 text-slate-700 dark:text-slate-200 font-medium hover:bg-slate-300 cursor-pointer transition-colors"
              >
                Batch operation
              </button>
            </div>
          )}
        </div>
      )}

      {/* Results Content Area */}
      <div className="flex-1 overflow-y-auto p-2 flex flex-col gap-2">
        {error ? (
          <div className="p-3 rounded-md bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 text-xs">
            <p className="font-semibold mb-1">Search Error</p>
            <p>{error}</p>
          </div>
        ) : query.trim().length > 0 && results.length === 0 && !isLoading ? (
          <div className="p-4 text-center text-xs text-slate-400 dark:text-slate-500">
            No matching documents found for &quot;{query}&quot;
          </div>
        ) : (
          results.map((result: SearchResult) => {
            const isSelected = selectedResultIds.has(result.documentId);
            return (
              <div
                key={result.documentId}
                data-testid={`search-result-${result.documentId}`}
                aria-selected={isSelected}
                onClick={() => handleResultClick(result.documentId)}
                className={`p-2.5 rounded-lg border text-xs cursor-pointer transition-all ${
                  isSelected
                    ? 'border-blue-500 bg-blue-50/50 dark:bg-blue-900/20 ring-1 ring-blue-400'
                    : 'border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-800/80 hover:border-slate-300 dark:hover:border-slate-700 hover:shadow-xs'
                }`}
              >
                <div className="flex items-start justify-between gap-1 mb-1">
                  <span className="font-medium text-slate-800 dark:text-slate-100 truncate flex items-center gap-1">
                    <span>{result.filename.endsWith('.md') ? '📄' : '📝'}</span>
                    {result.filename}
                  </span>
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => handleResultClick(result.documentId)}
                    aria-label={`Select ${result.filename}`}
                    className="cursor-pointer"
                  />
                </div>

                {result.path && (
                  <div className="text-[10px] text-slate-400 dark:text-slate-500 truncate mb-1">
                    {result.path}
                  </div>
                )}

                {/* Match Source Badges */}
                {result.matchSources && result.matchSources.length > 0 && (
                  <div className="flex flex-wrap gap-1 mb-1.5">
                    {result.matchSources.map((src) => (
                      <MatchSourceBadge key={src} source={src} docId={result.documentId} />
                    ))}
                  </div>
                )}

                {/* Snippets */}
                {result.snippets && result.snippets.length > 0 && (
                  <div className="text-[11px] text-slate-600 dark:text-slate-300 bg-slate-50 dark:bg-slate-900/50 p-1.5 rounded border border-slate-100 dark:border-slate-800 font-mono italic">
                    {result.snippets.map((snip, idx) => (
                      <p key={idx} className="line-clamp-2">
                        {snip}
                      </p>
                    ))}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </aside>
  );
};

export default SearchPanel;
