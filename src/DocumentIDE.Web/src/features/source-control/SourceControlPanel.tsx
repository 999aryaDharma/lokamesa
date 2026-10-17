import { useEffect, useState, type ReactNode } from 'react';
import type {
  SourceControlChangeEntry,
  SourceControlStatusModel,
} from './sourceControlModels';

export interface SourceControlPanelProps {
  /** Latest status payload, or null before the first load completes. */
  status: SourceControlStatusModel | null;
  isLoading?: boolean;
  /** Transport-level failure (e.g. the local API is unreachable); Git state stays visible. */
  errorMessage?: string | null;
  /** Reports the selected change (or null when the selection is cleared). */
  onSelectionChange?: (entry: SourceControlChangeEntry | null) => void;
  /** Opens an already-registered document through the existing document flow. */
  onOpenDocument?: (documentId: string) => void;
  /**
   * Optional per-entry actions (SCM-06 Stage/Unstage). The panel hosts the
   * caller-provided control inside the row without owning any mutation logic:
   * interacting with it never changes the selection or opens the document.
   */
  renderChangeActions?: (entry: SourceControlChangeEntry) => ReactNode;
  /**
   * Optional SCM-07 commit surface. The panel places the caller-provided
   * control above the change list; the commit logic (and its API calls) stays
   * with the caller, so the pane owns no commit state and never pushes.
   */
  renderCommitBox?: () => ReactNode;
}

interface SectionDefinition {
  key: 'staged' | 'unstaged' | 'untracked' | 'conflicts';
  title: string;
}

/** Spec 13 §7/§8 — primary groups, in the order the pane presents them. */
const sectionDefinitions: SectionDefinition[] = [
  { key: 'staged', title: 'Staged Changes' },
  { key: 'unstaged', title: 'Changes' },
  { key: 'untracked', title: 'Untracked' },
  { key: 'conflicts', title: 'Conflicts' },
];

function branchLabel(status: SourceControlStatusModel): string {
  if (status.branchName) {
    return status.branchName;
  }
  return status.isDetached ? 'Detached' : 'Unknown';
}

/** Spec 13 §14.2 — without a configured upstream the counters are unknown, not 0/0. */
function aheadBehindLabel(status: SourceControlStatusModel): string {
  if (!status.hasUpstream) {
    return 'No upstream';
  }
  return `\u2191 ${status.ahead ?? 0} \u2193 ${status.behind ?? 0}`;
}

/**
 * SCM-03/SCM-06 — Source Control changes pane (spec 13 §7/§8). Shows the
 * current Git state: staged/unstaged/untracked/conflict groups plus read-only
 * branch context and ahead/behind counters. Single click selects a change,
 * double click opens an already-registered document. The pane owns no
 * mutation logic; callers may host their SCM-06 Stage/Unstage controls per
 * entry through `renderChangeActions`. SCM-07 adds an optional commit surface
 * the caller hosts through `renderCommitBox`; the pane still owns no mutation
 * logic and there is no branch-management surface here.
 */
export default function SourceControlPanel({
  status,
  isLoading = false,
  errorMessage = null,
  onSelectionChange,
  onOpenDocument,
  renderChangeActions,
  renderCommitBox,
}: SourceControlPanelProps) {
  const [selectedPath, setSelectedPath] = useState<string | null>(null);

  const entries: SourceControlChangeEntry[] = status
    ? [
        ...status.sections.staged,
        ...status.sections.unstaged,
        ...status.sections.untracked,
        ...status.sections.conflicts,
      ]
    : [];
  const selectionLost =
    selectedPath !== null && !entries.some((entry) => entry.path === selectedPath);

  // A refresh that no longer contains the selected file must not leave a stale
  // selection behind. A refresh that still contains it keeps the selection.
  useEffect(() => {
    if (selectionLost) {
      setSelectedPath(null);
      onSelectionChange?.(null);
    }
  }, [selectionLost, onSelectionChange]);

  const handleSelect = (entry: SourceControlChangeEntry) => {
    setSelectedPath(entry.path);
    onSelectionChange?.(entry);
  };

  const handleOpen = (entry: SourceControlChangeEntry) => {
    if (entry.documentId) {
      onOpenDocument?.(entry.documentId);
    }
  };

  if (status === null) {
    return (
      <div
        data-testid={isLoading ? 'source-control-loading' : 'source-control-no-status'}
        className="w-full max-w-xl p-4 text-sm text-slate-600 bg-slate-50 border border-slate-200 rounded-xl"
      >
        {isLoading ? 'Loading source control status...' : 'No source control status available yet.'}
      </div>
    );
  }

  if (!status.hasRepository) {
    return (
      <RepositoryUnavailable status={status} errorMessage={errorMessage} />
    );
  }

  return (
    <div className="w-full max-w-xl p-4 bg-white rounded-xl border border-slate-200 shadow-sm space-y-3">
      <h3 className="text-sm font-semibold text-slate-700">Source Control</h3>

      {errorMessage && (
        <div
          role="alert"
          className="p-3 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg"
        >
          {errorMessage}
        </div>
      )}

      {/* Read-only context: the branch name is metadata, never an editable control. */}
      <div className="flex items-center gap-3 flex-wrap">
        <span
          data-testid="source-control-branch"
          className="text-sm font-medium text-slate-700"
        >
          {branchLabel(status)}
        </span>
        <span
          data-testid="source-control-ahead-behind"
          className="text-xs font-mono text-slate-500"
        >
          {aheadBehindLabel(status)}
        </span>
      </div>

      {renderCommitBox && <div data-testid="source-control-commit-host">{renderCommitBox()}</div>}

      {entries.length === 0 ? (
        <div data-testid="source-control-clean" className="text-sm text-slate-500">
          Working tree clean - no changes.
        </div>
      ) : (
        <div className="space-y-3">
          {sectionDefinitions.map((definition) => (
            <ChangeSection
              key={definition.key}
              definition={definition}
              entries={status.sections[definition.key]}
              selectedPath={selectedPath}
              onSelect={handleSelect}
              onOpen={handleOpen}
              renderChangeActions={renderChangeActions}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function RepositoryUnavailable({
  status,
  errorMessage,
}: {
  status: SourceControlStatusModel;
  errorMessage: string | null;
}) {
  if (status.status === 'GitUnavailable') {
    return (
      <div
        data-testid="source-control-git-unavailable"
        className="w-full max-w-xl p-4 text-sm text-slate-600 bg-slate-50 border border-slate-200 rounded-xl"
      >
        Git is not available on this machine. Install Git to see source control status.
      </div>
    );
  }

  if (status.status === 'NoRepository') {
    return (
      <div
        data-testid="source-control-no-repository"
        className="w-full max-w-xl p-4 text-sm text-slate-600 bg-slate-50 border border-slate-200 rounded-xl"
      >
        This folder is not inside a Git repository.
      </div>
    );
  }

  return (
    <div className="w-full max-w-xl p-4 bg-white rounded-xl border border-slate-200 shadow-sm space-y-3">
      <h3 className="text-sm font-semibold text-slate-700">Source Control</h3>
      {errorMessage && (
        <div
          role="alert"
          className="p-3 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg"
        >
          {errorMessage}
        </div>
      )}
      <div
        role="alert"
        data-testid="source-control-failure"
        className="p-3 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg"
      >
        <p className="font-medium">Source control status could not be determined.</p>
        {status.failure && (
          <p>
            {status.failure.errorKind}
            {status.failure.errorMessage ? `: ${status.failure.errorMessage}` : ''}
          </p>
        )}
      </div>
    </div>
  );
}

function ChangeSection({
  definition,
  entries,
  selectedPath,
  onSelect,
  onOpen,
  renderChangeActions,
}: {
  definition: SectionDefinition;
  entries: SourceControlChangeEntry[];
  selectedPath: string | null;
  onSelect: (entry: SourceControlChangeEntry) => void;
  onOpen: (entry: SourceControlChangeEntry) => void;
  renderChangeActions?: (entry: SourceControlChangeEntry) => ReactNode;
}) {
  return (
    <section data-testid={`source-control-section-${definition.key}`}>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-500">
        {definition.title} ({entries.length})
      </h4>
      <ul className="mt-1 space-y-1">
        {entries.map((entry) => {
          const isSelected = selectedPath === entry.path;
          return (
            <li
              key={entry.path}
              data-testid={`source-control-entry-${entry.path}`}
              aria-selected={isSelected}
              onClick={() => onSelect(entry)}
              onDoubleClick={() => onOpen(entry)}
              className={`flex items-center gap-2 px-2 py-1 rounded-lg cursor-pointer transition-colors ${
                isSelected
                  ? 'bg-blue-50 border border-blue-300'
                  : 'border border-transparent hover:bg-slate-50'
              }`}
            >
              <span className="shrink-0 text-xs font-mono text-slate-500 bg-slate-100 border border-slate-200 rounded px-1.5 py-0.5">
                {entry.statusCharacters}
              </span>
              <span className="text-sm font-mono text-slate-800 truncate">{entry.path}</span>
              {renderChangeActions && (
                // Hosted actions are not row interactions: stop both the
                // selection click and the open double-click from bubbling.
                <span
                  className="ml-auto shrink-0"
                  onClick={(event) => event.stopPropagation()}
                  onDoubleClick={(event) => event.stopPropagation()}
                >
                  {renderChangeActions(entry)}
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
