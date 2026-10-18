/**
 * CAN-08: List View & View Switch — DocumentList component
 *
 * Dense management view over the SAME workspace objects as the canvas.
 * The list reads DocumentIds directly from the shared canvas store; it never
 * imports, clones, or re-identifies documents. Rows are lightweight display
 * view-models that reference the original CanvasObject ids.
 */
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { useCanvasStore } from '../canvas/canvasStore';
import type { CanvasObject } from '../canvas/types';
import NodeIcon, { documentIconKind, type NodeIconKind } from '../shell/NodeIcon';

function useCanvasState() {
  return useSyncExternalStore(
    useCanvasStore.subscribe,
    useCanvasStore.getState,
    useCanvasStore.getState,
  );
}

type TypeLabel = 'Folder' | 'DOCX' | 'MD' | 'Document';

/**
 * Derive the Type column from explicit docType or the file extension.
 * Mirrors the inference used by the canvas paper object (DocumentNode).
 */
function deriveTypeLabel(obj: CanvasObject): TypeLabel {
  if (obj.type === 'folder') return 'Folder';
  const explicit = obj.docType;
  if (explicit === 'docx') return 'DOCX';
  if (explicit === 'md') return 'MD';
  if (!explicit || explicit === 'unknown') {
    const lower = obj.title.toLowerCase();
    if (lower.endsWith('.docx') || lower.endsWith('.doc')) return 'DOCX';
    if (lower.endsWith('.md') || lower.endsWith('.markdown')) return 'MD';
  }
  return 'Document';
}

/** Derive the Status column from document/thumbnail state metadata. */
function deriveStatusLabel(obj: CanvasObject): string {
  if (obj.isMissing || obj.thumbnailState === 'missing') return 'Missing';
  if (obj.statusText) return obj.statusText;
  switch (obj.thumbnailState) {
    case 'loading':
      return 'Loading';
    case 'stale':
      return 'Stale';
    case 'failed':
      return 'Failed';
    case 'ready':
      return 'Ready';
    default:
      return '—';
  }
}

interface ListRow {
  id: string;
  name: string;
  typeLabel: TypeLabel;
  statusLabel: string;
  /**
   * UI-01 (owner directive): list rows carry a small folder/file icon — the
   * Finder convention for lists — so a folder and a file are never the same
   * look-alike row.
   */
  kind: NodeIconKind;
}

type TypeFilter = 'all' | 'docx' | 'md' | 'folder';

type SortKey = 'name' | 'type' | 'status';
type SortDirection = 'asc' | 'desc';

function compareText(a: string, b: string): number {
  const al = a.toLowerCase();
  const bl = b.toLowerCase();
  if (al < bl) return -1;
  if (al > bl) return 1;
  return 0;
}

function sortValue(row: ListRow, key: SortKey): string {
  switch (key) {
    case 'name':
      return row.name;
    case 'type':
      return row.typeLabel;
    case 'status':
      return row.statusLabel;
  }
}

function matchesTypeFilter(label: TypeLabel, filter: TypeFilter): boolean {
  switch (filter) {
    case 'all':
      return true;
    case 'docx':
      return label === 'DOCX';
    case 'md':
      return label === 'MD';
    case 'folder':
      return label === 'Folder';
  }
}

export default function DocumentList() {
  const { objects, selectedIds, toggleSelect, clearSelection } = useCanvasState();
  const [nameFilter, setNameFilter] = useState('');
  const [typeFilter, setTypeFilter] = useState<TypeFilter>('all');
  const [sortKey, setSortKey] = useState<SortKey>('name');
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc');

  const rows: ListRow[] = useMemo(() => {
    const query = nameFilter.trim().toLowerCase();
    const result: ListRow[] = [];
    for (const obj of objects) {
      const typeLabel = deriveTypeLabel(obj);
      if (!matchesTypeFilter(typeLabel, typeFilter)) continue;
      if (query && !obj.title.toLowerCase().includes(query)) continue;
      result.push({
        id: obj.id,
        name: obj.title,
        typeLabel,
        statusLabel: deriveStatusLabel(obj),
        kind: obj.type === 'folder' ? 'folder' : documentIconKind(obj.title, obj.docType),
      });
    }
    const direction = sortDirection === 'asc' ? 1 : -1;
    result.sort((a, b) => {
      const primary = compareText(sortValue(a, sortKey), sortValue(b, sortKey));
      if (primary !== 0) return primary * direction;
      // Deterministic tie-break independent of direction.
      return compareText(a.id, b.id);
    });
    return result;
  }, [objects, nameFilter, typeFilter, sortKey, sortDirection]);

  const handleSort = useCallback(
    (key: SortKey) => {
      if (sortKey === key) {
        setSortDirection((direction) => (direction === 'asc' ? 'desc' : 'asc'));
      } else {
        setSortKey(key);
        setSortDirection('asc');
      }
    },
    [sortKey],
  );

  const selectedVisibleCount = rows.reduce(
    (count, row) => (selectedIds.has(row.id) ? count + 1 : count),
    0,
  );
  const allVisibleSelected = rows.length > 0 && selectedVisibleCount === rows.length;
  const someVisibleSelected = selectedVisibleCount > 0;

  const selectAllRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (selectAllRef.current) {
      selectAllRef.current.indeterminate = someVisibleSelected && !allVisibleSelected;
    }
  }, [someVisibleSelected, allVisibleSelected]);

  const renderSortableHeader = (key: SortKey, label: string) => (
    <th
      scope="col"
      aria-sort={
        sortKey === key ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'
      }
      className="px-2 py-1.5 font-medium"
    >
      <button
        type="button"
        onClick={() => handleSort(key)}
        aria-label={`Sort by ${key}`}
        className="inline-flex items-center gap-1 cursor-pointer transition-colors hover:text-mac-text"
      >
        {label}
        <span aria-hidden="true" className="text-[9px] text-mac-text-3">
          {sortKey === key ? (sortDirection === 'asc' ? '▲' : '▼') : ''}
        </span>
      </button>
    </th>
  );

  const handleToggleAllVisible = useCallback(() => {
    const allSelected = rows.length > 0 && rows.every((row) => selectedIds.has(row.id));
    for (const row of rows) {
      const selected = selectedIds.has(row.id);
      if (allSelected ? selected : !selected) {
        toggleSelect(row.id);
      }
    }
  }, [rows, selectedIds, toggleSelect]);

  return (
    <section
      data-testid="list-view"
      aria-label="Document list"
      className="w-full h-full overflow-auto bg-mac-content"
    >
      <div className="sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b border-mac-hairline bg-mac-content/95 px-3 py-2">
        <input
          type="search"
          value={nameFilter}
          onChange={(e) => setNameFilter(e.target.value)}
          aria-label="Filter documents"
          placeholder="Filter by name..."
          className="w-56 rounded border border-mac-hairline-strong bg-mac-canvas px-2 py-1 text-xs text-mac-text placeholder:text-mac-text-3 focus:outline-none focus:ring-1 focus:ring-mac-accent"
        />
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value as TypeFilter)}
          aria-label="Filter by type"
          className="rounded border border-mac-hairline-strong bg-mac-canvas px-2 py-1 text-xs text-mac-text-2"
        >
          <option value="all">All types</option>
          <option value="docx">DOCX</option>
          <option value="md">MD</option>
          <option value="folder">Folder</option>
        </select>
        <span data-testid="list-selection-count" className="text-[11px] text-mac-text-2">
          {selectedVisibleCount} of {rows.length} selected
        </span>
        <button
          type="button"
          onClick={clearSelection}
          aria-label="Clear selection"
          disabled={selectedIds.size === 0}
          className="cursor-pointer rounded bg-mac-chip px-2 py-0.5 text-[11px] text-mac-text-2 transition-colors hover:text-mac-text disabled:opacity-40"
        >
          Clear selection
        </button>
      </div>

      <table className="w-full text-left text-xs border-collapse">
        <thead className="bg-mac-canvas text-mac-text-3">
          <tr className="border-b border-mac-hairline">
            <th scope="col" className="w-8 px-2 py-1.5">
              <input
                ref={selectAllRef}
                type="checkbox"
                checked={allVisibleSelected}
                onChange={handleToggleAllVisible}
                disabled={rows.length === 0}
                aria-label="Select all visible"
                className="cursor-pointer"
              />
            </th>
            {renderSortableHeader('name', 'Name')}
            {renderSortableHeader('type', 'Type')}
            {renderSortableHeader('status', 'Status')}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const isSelected = selectedIds.has(row.id);
            return (
              <tr
                key={row.id}
                data-testid={`list-row-${row.id}`}
                data-document-id={row.id}
                aria-selected={isSelected}
                tabIndex={0}
                onClick={() => toggleSelect(row.id)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    toggleSelect(row.id);
                  }
                }}
                className={`border-b border-mac-hairline cursor-pointer ${
                  isSelected ? 'bg-mac-accent-weak' : 'hover:bg-mac-canvas'
                }`}
              >
                <td className="px-2 py-1.5" onClick={(e) => e.stopPropagation()}>
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => toggleSelect(row.id)}
                    aria-label={`Select ${row.name}`}
                    className="cursor-pointer"
                  />
                </td>
                <td className="px-2 py-1.5 text-mac-text font-medium truncate max-w-0 w-1/2">
                  <span className="flex min-w-0 items-center gap-1.5">
                    <NodeIcon kind={row.kind} />
                    <span className="truncate">{row.name}</span>
                  </span>
                </td>
                <td className="px-2 py-1.5 text-mac-text-2">{row.typeLabel}</td>
                <td className="px-2 py-1.5 text-mac-text-2">{row.statusLabel}</td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {objects.length === 0 ? (
        <p data-testid="list-empty" className="p-4 text-center text-xs text-mac-text-3">
          No documents in this workspace yet
        </p>
      ) : rows.length === 0 ? (
        <p data-testid="list-no-matches" className="p-4 text-center text-xs text-mac-text-3">
          No documents match the current filter
        </p>
      ) : null}
    </section>
  );
}
