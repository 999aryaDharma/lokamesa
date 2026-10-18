import React from 'react';

/**
 * UI-01 primitive — source-list row.
 *
 * The sidebar's 28px rows: optional leading glyph, a label, a trailing count
 * badge, accent-weak selection fill. Used by `WORKSPACE`, `FOLDER SOURCES` and
 * `LIBRARY` sections. When `onClick` is absent the row is presentational (a
 * plain div) — that is how the folder-source rows render while they are only
 * labels, so no accidental button appears in the accessibility tree.
 */
export interface SourceListRowProps {
  label: string;
  icon?: React.ReactNode;
  badge?: React.ReactNode;
  selected?: boolean;
  onClick?: () => void;
  /** Extra accessible context, e.g. the full path for a folder-source row. */
  title?: string;
  indent?: boolean;
  testId?: string;
}

export const SourceListRow: React.FC<SourceListRowProps> = ({
  label,
  icon,
  badge,
  selected = false,
  onClick,
  title,
  indent = false,
  testId,
}) => {
  const classes = [
    'flex w-full items-center gap-2 rounded-md h-7 px-2 text-xs text-left',
    indent ? 'pl-5' : '',
    selected
      ? 'bg-mac-accent-weak text-mac-text font-medium'
      : onClick
        ? 'text-mac-text hover:bg-mac-chip'
        : 'text-mac-text-2',
  ]
    .filter(Boolean)
    .join(' ');

  const content = (
    <>
      {icon && (
        <span aria-hidden="true" className="w-3.5 shrink-0 text-center text-[11px] text-mac-text-3">
          {icon}
        </span>
      )}
      <span className="truncate flex-1">{label}</span>
      {badge !== undefined && badge !== null && badge !== '' && (
        <span
          aria-hidden="true"
          className="shrink-0 rounded bg-mac-chip px-1 text-[10px] tabular-nums text-mac-text-2"
        >
          {badge}
        </span>
      )}
    </>
  );

  if (!onClick) {
    return (
      <div className={classes} title={title} data-testid={testId}>
        {content}
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      title={title}
      data-testid={testId}
      className={classes}
    >
      {content}
    </button>
  );
};

export default SourceListRow;
