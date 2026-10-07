/**
 * CAN-08: List View & View Switch — ViewSwitcher component
 *
 * Segmented Canvas | List | Graph main-view switch. Controlled component:
 * the active view lives in viewStore and routing is done by the App shell.
 */
import React from 'react';
import type { MainView } from './viewStore';

export interface ViewSwitcherProps {
  activeView: MainView;
  onViewChange: (view: MainView) => void;
  disabled?: boolean;
}

const VIEW_OPTIONS: ReadonlyArray<{ view: MainView; label: string }> = [
  { view: 'canvas', label: 'Canvas' },
  { view: 'list', label: 'List' },
  { view: 'graph', label: 'Graph' },
];

export const ViewSwitcher: React.FC<ViewSwitcherProps> = ({
  activeView,
  onViewChange,
  disabled = false,
}) => {
  return (
    <div
      role="radiogroup"
      aria-label="Main view switch"
      className="inline-flex items-center p-0.5 bg-slate-100 rounded-md border border-slate-200 text-xs select-none"
    >
      {VIEW_OPTIONS.map(({ view, label }) => (
        <button
          key={view}
          type="button"
          role="radio"
          aria-checked={activeView === view}
          disabled={disabled}
          onClick={() => onViewChange(view)}
          data-testid={`view-${view}-btn`}
          className={`px-2.5 py-1 rounded transition-colors font-medium cursor-pointer ${
            activeView === view
              ? 'bg-white text-slate-900 shadow-xs'
              : 'text-slate-500 hover:text-slate-700'
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
};

export default ViewSwitcher;
