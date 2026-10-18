/**
 * SRCH-05: Realtime Search UI & Selection — searchStore
 *
 * Lightweight external store compatible with React's useSyncExternalStore.
 * Manages query state, debounced execution, search results, visual canvas highlights,
 * result selection, filter chips, and batch scope creation.
 */
import { canvasStoreInstance } from '../canvas/canvasStore';
import { toggleFilterQuery, type FilterChipData } from './MetadataFilterChips';

export interface SearchResult {
  documentId: string;
  filename: string;
  score: number;
  matchSources: string[];
  snippets: string[];
  metadataSummary?: string;
  path?: string;
}

export interface SearchExecutionResult {
  results: SearchResult[];
  totalCount: number;
  executionTimeMs: number;
  activeFilterChips?: FilterChipData[];
  error?: string | null;
}

export interface SearchBatchScope {
  type: 'search';
  query: string;
  documentIds: string[];
  resultsCount: number;
}

export type SearchApiFn = (query: string) => Promise<SearchExecutionResult>;

export interface SearchState {
  query: string;
  debouncedQuery: string;
  results: SearchResult[];
  totalCount: number;
  executionTimeMs: number;
  isLoading: boolean;
  isSearchActive: boolean;
  error: string | null;
  activeFilterChips: FilterChipData[];
  selectedResultIds: Set<string>;
}

export interface SearchActions {
  setQuery: (query: string, immediate?: boolean) => void;
  executeSearch: (queryToRun: string) => Promise<void>;
  clearSearch: () => void;
  closeSearch: () => void;
  openSearch: () => void;
  selectAllResults: () => void;
  toggleSelectResult: (documentId: string) => void;
  clearResultSelection: () => void;
  removeFilterChip: (field: string, value: string) => void;
  addFilterChip: (field: string, value: string) => void;
  getBatchScope: () => SearchBatchScope;
  setSearchApi: (fn: SearchApiFn) => void;
  reset: () => void;
}

export type SearchStoreState = SearchState & SearchActions;

type Listener = () => void;

const defaultSearchApi: SearchApiFn = async (query: string): Promise<SearchExecutionResult> => {
  const encoded = encodeURIComponent(query);
  const response = await fetch(`/api/search?q=${encoded}`);
  if (!response.ok) {
    if (response.status === 400) {
      throw new Error('Malformed query syntax');
    }
    if (response.status === 409 || response.status === 503) {
      throw new Error('Index is stale or incomplete');
    }
    throw new Error(`Search failed with status ${response.status}`);
  }
  return (await response.json()) as SearchExecutionResult;
};

function createInitialState(): SearchState {
  return {
    query: '',
    debouncedQuery: '',
    results: [],
    totalCount: 0,
    executionTimeMs: 0,
    isLoading: false,
    isSearchActive: false,
    error: null,
    activeFilterChips: [],
    selectedResultIds: new Set<string>(),
  };
}

class SearchStore {
  private state: SearchState = createInitialState();
  private listeners = new Set<Listener>();
  private cachedSnapshot: SearchStoreState | null = null;
  private searchApiFn: SearchApiFn = defaultSearchApi;
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;

  private readonly actions: SearchActions = {
    setQuery: (query: string, immediate = false): void => {
      this.state = { ...this.state, query };

      if (this.debounceTimer) {
        clearTimeout(this.debounceTimer);
        this.debounceTimer = null;
      }

      const trimmed = query.trim();
      if (!trimmed) {
        this.state = {
          ...this.state,
          debouncedQuery: '',
          results: [],
          totalCount: 0,
          executionTimeMs: 0,
          isLoading: false,
          error: null,
          activeFilterChips: [],
          selectedResultIds: new Set<string>(),
        };
        this.notify();
        return;
      }

      this.state = {
        ...this.state,
        isSearchActive: true,
        isLoading: true,
        error: null,
      };
      this.notify();

      if (immediate) {
        void this.actions.executeSearch(query);
      } else {
        this.debounceTimer = setTimeout(() => {
          void this.actions.executeSearch(query);
        }, 150);
      }
    },

    executeSearch: async (queryToRun: string): Promise<void> => {
      const trimmed = queryToRun.trim();
      if (!trimmed) {
        this.state = {
          ...this.state,
          debouncedQuery: '',
          results: [],
          totalCount: 0,
          executionTimeMs: 0,
          isLoading: false,
          error: null,
          activeFilterChips: [],
        };
        this.notify();
        return;
      }

      this.state = { ...this.state, debouncedQuery: trimmed, isLoading: true, error: null };
      this.notify();

      try {
        const res = await this.searchApiFn(trimmed);
        this.state = {
          ...this.state,
          results: res.results || [],
          totalCount: res.totalCount ?? res.results?.length ?? 0,
          executionTimeMs: res.executionTimeMs ?? 0,
          activeFilterChips: res.activeFilterChips || [],
          isLoading: false,
          error: res.error || null,
        };
      } catch (err: any) {
        this.state = {
          ...this.state,
          results: [],
          totalCount: 0,
          executionTimeMs: 0,
          isLoading: false,
          error: err.message || 'Search execution failed',
        };
      } finally {
        this.notify();
      }
    },

    clearSearch: (): void => {
      if (this.debounceTimer) {
        clearTimeout(this.debounceTimer);
        this.debounceTimer = null;
      }
      this.state = {
        ...createInitialState(),
        isSearchActive: false,
      };
      this.notify();
    },

    closeSearch: (): void => {
      this.actions.clearSearch();
    },

    openSearch: (): void => {
      this.state = { ...this.state, isSearchActive: true };
      this.notify();
    },

    selectAllResults: (): void => {
      const allIds = new Set(this.state.results.map((r) => r.documentId));
      this.state = { ...this.state, selectedResultIds: allIds };

      // Synchronize with workspace canvas store selection
      canvasStoreInstance.getState().clearSelection();
      allIds.forEach((id) => {
        canvasStoreInstance.getState().toggleSelect(id);
      });

      this.notify();
    },

    toggleSelectResult: (documentId: string): void => {
      const selected = new Set(this.state.selectedResultIds);
      if (selected.has(documentId)) {
        selected.delete(documentId);
      } else {
        selected.add(documentId);
      }
      this.state = { ...this.state, selectedResultIds: selected };
      canvasStoreInstance.getState().toggleSelect(documentId);
      this.notify();
    },

    clearResultSelection: (): void => {
      this.state = { ...this.state, selectedResultIds: new Set<string>() };
      canvasStoreInstance.getState().clearSelection();
      this.notify();
    },

    removeFilterChip: (field: string, value: string): void => {
      const updatedQuery = toggleFilterQuery(this.state.query, field, value);
      this.actions.setQuery(updatedQuery, true);
    },

    addFilterChip: (field: string, value: string): void => {
      const updatedQuery = toggleFilterQuery(this.state.query, field, value);
      this.actions.setQuery(updatedQuery, true);
    },

    getBatchScope: (): SearchBatchScope => {
      const resultDocIds = this.state.results.map((r) => r.documentId);
      return {
        type: 'search',
        query: this.state.query,
        documentIds: resultDocIds,
        resultsCount: resultDocIds.length,
      };
    },

    setSearchApi: (fn: SearchApiFn): void => {
      this.searchApiFn = fn;
    },

    reset: (): void => {
      if (this.debounceTimer) {
        clearTimeout(this.debounceTimer);
        this.debounceTimer = null;
      }
      this.searchApiFn = defaultSearchApi;
      this.state = createInitialState();
      this.notify();
    },
  };

  private buildSnapshot(): SearchStoreState {
    return {
      ...this.state,
      ...this.actions,
    };
  }

  getState = (): SearchStoreState => {
    if (this.cachedSnapshot === null) {
      this.cachedSnapshot = this.buildSnapshot();
    }
    return this.cachedSnapshot;
  };

  subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  private notify(): void {
    this.cachedSnapshot = this.buildSnapshot();
    this.listeners.forEach((l) => l());
  }
}

export const searchStoreInstance = new SearchStore();

export const useSearchStore = {
  getState: (): SearchStoreState => searchStoreInstance.getState(),
  subscribe: (listener: Listener): (() => void) => searchStoreInstance.subscribe(listener),
};
