import { useState } from 'react';
import type {
  SourceControlChangeEntry,
  SourceControlStageResult,
  SourceControlStatusModel,
} from './sourceControlModels';

/**
 * SCM-06 — Stage/Unstage actions for the changes pane (spec 13 §9.4/§10/§18).
 *
 * Every action goes through the privileged localhost API: the change path is
 * sent exactly as Git reported it, Document IDE keeps no staging snapshot of
 * its own, and a successful mutation hands the status the API re-read from Git
 * back to the pane through `onStatusRefreshed`. Failures stay typed and
 * visible — never a silent fallback. Conflicted (unmerged) entries are outside
 * this sprint's semantics and their actions stay disabled.
 */

export type ChangeAction = 'stage' | 'unstage' | 'stage-all' | 'unstage-all';

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

/** Posts one stage/unstage action through the privileged API. */
export async function postChangeAction(
  action: ChangeAction,
  path: string,
  changePath?: string
): Promise<SourceControlStageResult> {
  const globals = window as Window & SourceControlApiGlobals;
  const response = await fetch(
    `${globals.__DOCUMENTIDE_API_URL__ || ''}/api/source-control/${action}`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-DocumentIDE-Session-Token': globals.__DOCUMENTIDE_SESSION_TOKEN__ || '',
      },
      body: JSON.stringify(
        action === 'stage' || action === 'unstage' ? { path, changePath } : { path }
      ),
    }
  );

  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `Stage request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as SourceControlStageResult;
}

export interface ChangeActionsProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string;
  /** The selected change entry; null renders the whole-worktree actions. */
  change: SourceControlChangeEntry | null;
  /** True when Git is not installed/discoverable; all actions are disabled. */
  gitUnavailable?: boolean;
  /** Receives the status re-read from Git after a successful mutation. */
  onStatusRefreshed?: (status: SourceControlStatusModel) => void;
}

function failureMessage(result: SourceControlStageResult): string {
  if (result.failure) {
    const detail =
      result.failure.errorMessage ?? result.message ?? `Git reported ${result.outcome}.`;
    return `${result.failure.errorKind}: ${detail}`;
  }
  return result.message ?? `The stage request failed (${result.outcome}).`;
}

/** A stage action makes sense when the entry has working-tree work (or is untracked). */
function hasWorkingTreeWork(change: SourceControlChangeEntry): boolean {
  return (
    change.state === 'Untracked' ||
    (change.workingTreeChange !== 'Unmodified' && change.workingTreeChange !== 'Unmerged')
  );
}

/** An unstage action makes sense when the index side carries a stageable change. */
function hasStageableIndexEntry(change: SourceControlChangeEntry): boolean {
  return change.indexChange !== 'Unmodified' && change.indexChange !== 'Unmerged';
}

const actionButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-mac-text bg-mac-content border border-mac-hairline-strong rounded-lg ' +
  'hover:bg-mac-chip focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

export default function ChangeActions({
  path,
  change,
  gitUnavailable = false,
  onStatusRefreshed,
}: ChangeActionsProps) {
  const [busyAction, setBusyAction] = useState<ChangeAction | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const busy = busyAction !== null;

  async function run(action: ChangeAction) {
    if (busy) {
      return;
    }

    setBusyAction(action);
    setErrorMessage(null);
    try {
      const result = await postChangeAction(action, path, change?.path);
      if (!result.isSuccess) {
        setErrorMessage(failureMessage(result));
        return;
      }

      if (result.status) {
        onStatusRefreshed?.(result.status);
      }
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'The stage request failed.');
    } finally {
      setBusyAction(null);
    }
  }

  const canStage = !busy && !gitUnavailable && change !== null && hasWorkingTreeWork(change);
  const canUnstage =
    !busy && !gitUnavailable && change !== null && hasStageableIndexEntry(change);
  const canRunBulk = !busy && !gitUnavailable && change === null;

  return (
    <div data-testid="change-actions" className="space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        {change === null ? (
          <>
            <button
              type="button"
              disabled={!canRunBulk}
              onClick={() => void run('stage-all')}
              className={actionButtonClassName}
            >
              Stage All
            </button>
            <button
              type="button"
              disabled={!canRunBulk}
              onClick={() => void run('unstage-all')}
              className={actionButtonClassName}
            >
              Unstage All
            </button>
          </>
        ) : (
          <>
            <button
              type="button"
              disabled={!canStage}
              onClick={() => void run('stage')}
              className={actionButtonClassName}
            >
              Stage
            </button>
            <button
              type="button"
              disabled={!canUnstage}
              onClick={() => void run('unstage')}
              className={actionButtonClassName}
            >
              Unstage
            </button>
          </>
        )}
      </div>

      {errorMessage && (
        <div
          role="alert"
          className="p-2 text-xs text-mac-danger bg-mac-danger/10 border border-mac-danger/40 rounded-lg"
        >
          {errorMessage}
        </div>
      )}
    </div>
  );
}
