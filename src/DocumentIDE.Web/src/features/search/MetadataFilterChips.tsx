import React from 'react';

export interface FilterChipData {
  field: string;
  value: string;
  querySnippet?: string;
  count?: number;
}

export interface MetadataFilterChipsProps {
  activeFilters: FilterChipData[];
  availableFilters?: FilterChipData[];
  onRemoveFilter?: (field: string, value: string) => void;
  onAddFilter?: (field: string, value: string) => void;
  onClearAll?: () => void;
}

export const MetadataFilterChips: React.FC<MetadataFilterChipsProps> = ({
  activeFilters,
  availableFilters = [],
  onRemoveFilter,
  onAddFilter,
  onClearAll,
}) => {
  if (activeFilters.length === 0 && availableFilters.length === 0) {
    return null;
  }

  return (
    <div className="metadata-filter-chips flex flex-wrap items-center gap-2 py-2 px-1">
      {activeFilters.map((filter) => {
        const chipKey = `${filter.field}:${filter.value}`;
        const displayLabel = filter.querySnippet || `${filter.field}:${filter.value}`;
        return (
          <span
            key={chipKey}
            data-testid={`active-chip-${filter.field}-${filter.value}`}
            className="inline-flex items-center gap-1 rounded-full bg-blue-100 dark:bg-blue-900/40 text-blue-800 dark:text-blue-200 text-xs px-2.5 py-1 font-medium border border-blue-300 dark:border-blue-700"
          >
            <span>{displayLabel}</span>
            {filter.count !== undefined && (
              <span className="ml-1 rounded-full bg-blue-200 dark:bg-blue-800 text-blue-900 dark:text-blue-100 text-[10px] px-1.5 py-0.2 font-semibold">
                {filter.count}
              </span>
            )}
            {onRemoveFilter && (
              <button
                type="button"
                aria-label={`Remove filter ${filter.field}:${filter.value}`}
                onClick={() => onRemoveFilter(filter.field, filter.value)}
                className="ml-1 hover:text-blue-600 dark:hover:text-blue-300 focus:outline-none cursor-pointer"
              >
                &times;
              </button>
            )}
          </span>
        );
      })}

      {activeFilters.length > 1 && onClearAll && (
        <button
          type="button"
          onClick={onClearAll}
          className="text-xs text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 underline cursor-pointer ml-1"
        >
          Clear all
        </button>
      )}

      {availableFilters.length > 0 && (
        <div className="flex items-center gap-1.5 ml-2 border-l border-gray-200 dark:border-gray-700 pl-2">
          {availableFilters.map((filter) => {
            const chipKey = `avail-${filter.field}:${filter.value}`;
            return (
              <button
                key={chipKey}
                type="button"
                data-testid={`available-chip-${filter.field}-${filter.value}`}
                onClick={() => onAddFilter?.(filter.field, filter.value)}
                className="inline-flex items-center gap-1 rounded-full bg-gray-100 dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 text-xs px-2.5 py-1 cursor-pointer transition-colors"
              >
                <span>{filter.field}:{filter.value}</span>
                {filter.count !== undefined && (
                  <span className="rounded-full bg-gray-200 dark:bg-gray-700 text-gray-800 dark:text-gray-200 text-[10px] px-1.5 py-0.2 font-medium">
                    {filter.count}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
};

export function buildFilterQuery(currentQuery: string, field: string, value: string): string {
  if (!field || !value) return currentQuery || '';
  const normField = field.toLowerCase().trim();
  const normValue = value.trim();
  const token = normValue.includes(' ') ? `${normField}:"${normValue}"` : `${normField}:${normValue}`;

  if (!currentQuery || !currentQuery.trim()) {
    return token;
  }

  const regex = new RegExp(`\\b${normField}:(?:"[^"]+"\\S+)`, 'gi');
  if (regex.test(currentQuery)) {
    return currentQuery.replace(regex, token).replace(/\s+/g, ' ').trim();
  }

  return `${currentQuery.trim()} ${token}`;
}

export function toggleFilterQuery(currentQuery: string, field: string, value: string): string {
  if (!currentQuery) return buildFilterQuery('', field, value);
  const normField = field.toLowerCase().trim();
  const normValue = value.trim();

  const exactToken1 = `${normField}:${normValue}`;
  const exactToken2 = `${normField}:"${normValue}"`;

  if (currentQuery.toLowerCase().includes(exactToken1.toLowerCase()) || currentQuery.toLowerCase().includes(exactToken2.toLowerCase())) {
    const escaped1 = exactToken1.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const escaped2 = exactToken2.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const removeRegex = new RegExp(`\\b(?:${escaped1}|${escaped2})\\b`, 'gi');
    return currentQuery.replace(removeRegex, '').replace(/\s+/g, ' ').trim();
  }

  return buildFilterQuery(currentQuery, field, value);
}
