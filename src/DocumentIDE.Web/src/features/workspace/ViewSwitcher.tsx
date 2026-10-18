/**
 * CAN-08: List View & View Switch — ViewSwitcher component
 *
 * Segmented Canvas | List | Graph main-view switch. Controlled component: the
 * active view lives in viewStore and routing is done by the App shell.
 *
 * UI-01: the markup now comes from the shared `SegmentedControl` primitive
 * (same `role="radiogroup"` / `role="radio"` / `aria-checked` semantics and the
 * same `view-*-btn` testids), so the toolbar, the search inspector and the
 * Markdown editor all render one segmented-control idiom.
 */
import React from 'react';
import SegmentedControl from '../shell/SegmentedControl';
import type { MainView } from './viewStore';

export interface ViewSwitcherProps {
  activeView: MainView;
  onViewChange: (view: MainView) => void;
  disabled?: boolean;
}

const VIEW_OPTIONS: ReadonlyArray<{ value: MainView; label: string; testId: string }> = [
  { value: 'canvas', label: 'Canvas', testId: 'view-canvas-btn' },
  { value: 'list', label: 'List', testId: 'view-list-btn' },
  { value: 'graph', label: 'Graph', testId: 'view-graph-btn' },
];

export const ViewSwitcher: React.FC<ViewSwitcherProps> = ({
  activeView,
  onViewChange,
  disabled = false,
}) => (
  <SegmentedControl
    ariaLabel="Main view switch"
    value={activeView}
    options={VIEW_OPTIONS}
    onChange={onViewChange}
    disabled={disabled}
  />
);

export default ViewSwitcher;
