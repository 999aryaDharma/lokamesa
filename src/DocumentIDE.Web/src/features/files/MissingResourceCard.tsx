import React from 'react';

export type MissingResourceStatus = 'missing' | 'permission_denied' | 'locked' | 'unavailable';

export interface MissingResourceCardProps {
  kind: 'file' | 'folder';
  resourceId: string;
  displayName: string;
  lastKnownPath: string;
  status: MissingResourceStatus;
  onLocate: (resourceId: string) => void;
  onRemove: (resourceId: string) => void;
}

const statusLabel: Record<MissingResourceStatus, string> = {
  missing: 'File missing',
  permission_denied: 'Access denied',
  locked: 'Locked by another app',
  unavailable: 'Source unavailable',
};

export const MissingResourceCard: React.FC<MissingResourceCardProps> = ({
  kind,
  resourceId,
  displayName,
  lastKnownPath,
  status,
  onLocate,
  onRemove,
}) => {
  const isFolder = kind === 'folder';
  const label = isFolder && status === 'missing' ? 'Source unavailable' : statusLabel[status];
  const locateLabel = isFolder ? 'Locate folder' : 'Locate';
  const removeLabel = isFolder ? 'Remove source' : 'Remove reference';

  return (
    <div
      role="group"
      aria-label={`${isFolder ? 'Missing folder' : 'Missing file'}: ${displayName}`}
      className="flex flex-col items-center rounded-lg border border-dashed border-mac-hairline-strong bg-mac-content p-4 text-center dark:border-slate-600 dark:bg-slate-800"
    >
      <div
        aria-hidden="true"
        className="flex h-12 w-12 items-center justify-center rounded bg-mac-chip text-2xl dark:bg-slate-700"
      >
        {isFolder ? '📁' : '📄'}
      </div>
      <p className="mt-2 max-w-full truncate text-sm font-semibold text-mac-text dark:text-white">
        {displayName}
      </p>
      <p className="mt-1 flex items-center gap-1 text-xs font-medium text-mac-warning dark:text-amber-300">
        <span aria-hidden="true">⚠</span> {label}
      </p>
      <p className="mt-1 max-w-full break-all text-[11px] text-mac-text-3 dark:text-slate-400">
        {lastKnownPath}
      </p>
      <div className="mt-3 flex gap-2">
        <button
          type="button"
          onClick={() => onLocate(resourceId)}
          className="rounded border border-blue-600 px-3 py-1 text-xs font-medium text-mac-accent hover:bg-mac-accent-weak dark:text-blue-300 dark:hover:bg-blue-950"
        >
          {locateLabel}
        </button>
        <button
          type="button"
          onClick={() => onRemove(resourceId)}
          className="rounded border border-mac-hairline-strong px-3 py-1 text-xs font-medium text-mac-text-2 hover:bg-mac-chip dark:border-slate-600 dark:text-slate-300 dark:hover:bg-mac-text"
        >
          {removeLabel}
        </button>
      </div>
    </div>
  );
};

export default MissingResourceCard;
