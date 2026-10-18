import React from 'react';
import type { MainView } from '../workspace/viewStore';
import SearchField from './SearchField';
import SegmentedControl from './SegmentedControl';

/**
 * UI-01 shell — unified toolbar.
 *
 * Mirrors the macOS series' toolbar: traffic lights + sidebar toggle on the
 * left, the `Canvas | List | Graph` segmented control, then a trailing search
 * field and icon buttons.
 *
 * Every control here already existed in the flat top-nav; this component only
 * moves them and re-skins them, so it stays fully controlled — it owns no
 * state and performs no product action by itself.
 *
 * Placement note: `Shares` lives in the sidebar's LIBRARY section (it is an
 * app surface, per the design's navigation table) and `Search` is either the
 * sidebar row or this field's Enter submit. Keeping exactly one reachable
 * control per accessible name is what keeps the G-gate queries unambiguous.
 */
export interface ToolbarProps {
  activeView: MainView;
  onViewChange: (view: MainView) => void;
  sidebarOpen: boolean;
  onToggleSidebar: () => void;
  inspectorOpen: boolean;
  onToggleInspector: () => void;
  /** Enter in the search field; opens/drives the search surface. */
  onSearchSubmit: (query: string) => void;
  onToggleBatch?: () => void;
  batchOpen?: boolean;
  onTogglePdf?: () => void;
  pdfOpen?: boolean;
}

const VIEW_OPTIONS: ReadonlyArray<{ value: MainView; label: string; testId: string }> = [
  { value: 'canvas', label: 'Canvas', testId: 'view-canvas-btn' },
  { value: 'list', label: 'List', testId: 'view-list-btn' },
  { value: 'graph', label: 'Graph', testId: 'view-graph-btn' },
];

const ICON_BUTTON_CLASSES =
  'h-6 w-6 inline-flex items-center justify-center rounded-md border border-transparent text-mac-text-2 hover:bg-mac-chip cursor-pointer text-[13px] leading-none';

export const Toolbar: React.FC<ToolbarProps> = ({
  activeView,
  onViewChange,
  sidebarOpen,
  onToggleSidebar,
  inspectorOpen,
  onToggleInspector,
  onSearchSubmit,
  onToggleBatch,
  batchOpen = false,
  onTogglePdf,
  pdfOpen = false,
}) => (
  <header
    aria-label="Toolbar"
    // Chrome controls keep their default Space/Enter activation: the canvas
    // installs a window-level Space handler for pan mode, so the shell chrome
    // stops the event at the region (same guard the old top nav used).
    onKeyDown={(event) => event.stopPropagation()}
    className="flex h-[52px] shrink-0 items-center gap-3 border-b border-mac-hairline bg-mac-toolbar px-3"
  >
    <div data-testid="traffic-lights" aria-hidden="true" className="flex items-center gap-2 pl-1">
      <span className="h-3 w-3 rounded-full bg-[#FF5F57]" />
      <span className="h-3 w-3 rounded-full bg-[#FEBC2E]" />
      <span className="h-3 w-3 rounded-full bg-[#28C840]" />
    </div>

    <button
      type="button"
      aria-label="Toggle sidebar"
      aria-pressed={sidebarOpen}
      onClick={onToggleSidebar}
      className={ICON_BUTTON_CLASSES}
    >
      ▤
    </button>

    <SegmentedControl
      ariaLabel="Main view switch"
      value={activeView}
      options={VIEW_OPTIONS}
      onChange={onViewChange}
    />

    <div className="flex-1" />

    <SearchField
      ariaLabel="Search workspace"
      placeholder="Search workspace"
      onSubmit={onSearchSubmit}
      className="w-[260px]"
    />

    {onToggleBatch && (
      <button
        type="button"
        aria-label="Batch operations"
        aria-expanded={batchOpen}
        onClick={onToggleBatch}
        className={ICON_BUTTON_CLASSES}
      >
        ≡
      </button>
    )}

    {onTogglePdf && (
      <button
        type="button"
        aria-label="PDF"
        aria-pressed={pdfOpen}
        onClick={onTogglePdf}
        className={ICON_BUTTON_CLASSES}
      >
        ⬒
      </button>
    )}

    <button
      type="button"
      aria-label="Toggle inspector"
      aria-pressed={inspectorOpen}
      onClick={onToggleInspector}
      className={ICON_BUTTON_CLASSES}
    >
      ▥
    </button>
  </header>
);

export default Toolbar;