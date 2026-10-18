import React from 'react';
import NodeIcon from './NodeIcon';
import SourceListRow from './SourceListRow';

/**
 * UI-01 shell — left source-list sidebar (236px), the macOS three-pane idiom.
 *
 * Three sections, exactly as the design brief specifies:
 *   - `WORKSPACE`      → the main view rows (Canvas / List / Graph) that drive
 *                        `useViewStore`, i.e. the same seam the toolbar's
 *                        segmented control writes to;
 *   - `FOLDER SOURCES` → the real folder sources the shell loaded from
 *                        `GET /api/workspaces/{id}/folder-sources`, with the
 *                        `Add folder` row that opens the attach surface;
 *   - `LIBRARY`        → the `Search` and `Shares` surfaces, plus the
 *                        source-control surfaces as *inert labels* (they are
 *                        not mounted in this app, so they must not pretend to
 *                        be buttons).
 *
 * Fully controlled and presentational: it owns no state, so a restyle cannot
 * change behaviour. Every row that used to be reachable from the flat top nav
 * is still reachable here, with the same accessible name.
 */
export interface SidebarFolderSource {
  id: string;
  title: string;
  documentCount: number;
}

export interface SidebarProps {
  activeView: string;
  onViewChange: (view: 'canvas' | 'list' | 'graph') => void;
  folderSources: ReadonlyArray<SidebarFolderSource>;
  onAddFolder: () => void;
  addFolderOpen: boolean;
  onToggleSearch: () => void;
  searchOpen: boolean;
  onToggleShares: () => void;
  sharesOpen: boolean;
}

const VIEW_ROWS: ReadonlyArray<{
  view: 'canvas' | 'list' | 'graph';
  label: string;
  glyph: string;
}> = [
  { view: 'canvas', label: 'Canvas view', glyph: '▦' },
  { view: 'list', label: 'List view', glyph: '≣' },
  { view: 'graph', label: 'Graph view', glyph: '◍' },
];

/**
 * The source-control surfaces exist as shipped, restyled components (screens
 * 05/06/07) but are not mounted by this app: no route, store or DTO source
 * feeds them yet. They are listed as inert labels rather than buttons so the
 * sidebar never advertises an action that cannot happen.
 */
const SCM_ROWS: ReadonlyArray<{ label: string; glyph: string }> = [
  { label: 'Source control', glyph: '⎇' },
  { label: 'Worktrees', glyph: '⑂' },
  { label: 'History', glyph: '↻' },
];

const SidebarSection: React.FC<{ title: string; children: React.ReactNode }> = ({
  title,
  children,
}) => (
  <section className="flex flex-col gap-0.5">
    <h2 className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-[0.06em] text-mac-text-3">
      {title}
    </h2>
    {children}
  </section>
);

export const Sidebar: React.FC<SidebarProps> = ({
  activeView,
  onViewChange,
  folderSources,
  onAddFolder,
  addFolderOpen,
  onToggleSearch,
  searchOpen,
  onToggleShares,
  sharesOpen,
}) => (
  <aside
    aria-label="Sidebar"
    onKeyDown={(event) => event.stopPropagation()}
    className="flex h-full w-[236px] shrink-0 flex-col gap-4 overflow-y-auto border-r border-mac-hairline bg-mac-sidebar px-2 py-3"
  >
    <SidebarSection title="WORKSPACE">
      {VIEW_ROWS.map((row) => (
        <SourceListRow
          key={row.view}
          label={row.label}
          icon={row.glyph}
          selected={activeView === row.view}
          onClick={() => onViewChange(row.view)}
          testId={`sidebar-view-${row.view}`}
        />
      ))}
    </SidebarSection>

    <SidebarSection title="FOLDER SOURCES">
      <SourceListRow
        label="Add folder"
        icon="+"
        badge={addFolderOpen ? '−' : undefined}
        onClick={onAddFolder}
        testId="sidebar-add-folder"
      />
      {folderSources.map((source) => (
        <SourceListRow
          key={source.id}
          label={source.title}
          icon={<NodeIcon kind="folder" />}
          badge={source.documentCount}
          testId={`sidebar-folder-${source.id}`}
        />
      ))}
      {folderSources.length === 0 && (
        <p className="px-2 py-1 text-[11px] text-mac-text-3">No folder sources attached</p>
      )}
    </SidebarSection>

    <SidebarSection title="LIBRARY">
      <SourceListRow
        label="Search"
        icon="⌕"
        selected={searchOpen}
        onClick={onToggleSearch}
        testId="sidebar-search"
      />
      <SourceListRow
        label="Shares"
        icon="⇪"
        selected={sharesOpen}
        onClick={onToggleShares}
        testId="sidebar-shares"
      />
      {SCM_ROWS.map((row) => (
        <SourceListRow
          key={row.label}
          label={row.label}
          icon={row.glyph}
          testId={`sidebar-inert-${row.label.replace(/\s+/g, '-').toLowerCase()}`}
        />
      ))}
    </SidebarSection>
  </aside>
);

export default Sidebar;
