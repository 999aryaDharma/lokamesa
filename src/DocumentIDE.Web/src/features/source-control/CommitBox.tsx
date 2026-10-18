import { useState } from 'react';
import type {
  GitCommitFlow,
  SourceControlCommitInfo,
  SourceControlCommitResult,
  SourceControlStatusModel,
} from './sourceControlModels';

/**
 * SCM-07 — Commit / Commit All actions for the changes pane (spec 13 §11/§18).
 *
 * Commit and Commit All are deliberately separate actions: Commit records the
 * current staged state, Commit All stages the eligible changes first. Every
 * action goes through the privileged localhost API, which owns the Git work;
 * the message is required and never sent when blank, failures stay typed and
 * visible, and a successful commit hands the new commit identifier plus the
 * status the API re-read from Git back to the pane. There is no automatic push.
 */

export type CommitAction = 'commit' | 'commit-all';

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

/** Posts one commit action through the privileged API. */
export async function postCommit(
  action: CommitAction,
  path: string,
  message: string
): Promise<SourceControlCommitResult> {
  const globals = window as Window & SourceControlApiGlobals;
  const response = await fetch(
    `${globals.__DOCUMENTIDE_API_URL__ || ''}/api/source-control/${action}`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-DocumentIDE-Session-Token': globals.__DOCUMENTIDE_SESSION_TOKEN__ || '',
      },
      body: JSON.stringify({ path, message }),
    }
  );

  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `Commit request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as SourceControlCommitResult;
}

export interface CommitBoxProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string;
  /** True when Git is not installed/discoverable; both actions are disabled. */
  gitUnavailable?: boolean;
  /** Receives the status re-read from Git after a successful commit. */
  onStatusRefreshed?: (status: SourceControlStatusModel) => void;
  /** Receives the created commit plus the flow that ran. */
  onCommitSucceeded?: (commit: SourceControlCommitInfo, flow: GitCommitFlow) => void;
  /** Optional design-05 label for the staged commit action. */
  commitLabel?: string;
  /** Optional design-05 label for the commit-all action. */
  commitAllLabel?: string;
  /**
   * UI-03: Commit All stages every eligible change (including unstaged work), so
   * the design requires explicit confirmation. Defaults to false to preserve
   * the existing SCM-07 surface behaviour for other callers.
   */
  confirmCommitAll?: boolean;
}

function failureMessage(result: SourceControlCommitResult): string {
  if (result.failure) {
    const detail =
      result.failure.errorMessage ?? result.message ?? `Git reported ${result.outcome}.`;
    return `${result.outcome} (${result.failure.errorKind}): ${detail}`;
  }
  return result.message ?? `The commit request failed (${result.outcome}).`;
}

const actionButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-mac-text bg-mac-content border border-mac-hairline-strong rounded-lg ' +
  'hover:bg-mac-chip focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

const primaryButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-white bg-mac-text border border-slate-700 rounded-lg ' +
  'hover:bg-mac-text focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

export default function CommitBox({
  path,
  gitUnavailable = false,
  onStatusRefreshed,
  onCommitSucceeded,
  commitLabel = 'Commit',
  commitAllLabel = 'Commit All',
  confirmCommitAll = false,
}: CommitBoxProps) {
  const [message, setMessage] = useState('');
  const [busyAction, setBusyAction] = useState<CommitAction | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [lastCommit, setLastCommit] = useState<SourceControlCommitInfo | null>(null);
  const [confirmingCommitAll, setConfirmingCommitAll] = useState(false);

  const busy = busyAction !== null;
  // The client mirrors the API's typed rejection: a blank message never
  // reaches Git (spec 13 §11.1/§18).
  const hasMessage = message.trim().length > 0;
  const disabled = busy || gitUnavailable || !hasMessage;

  async function run(action: CommitAction) {
    if (disabled) {
      return;
    }

    setConfirmingCommitAll(false);
    setBusyAction(action);
    setErrorMessage(null);
    try {
      const result = await postCommit(action, path, message);
      if (!result.isSuccess) {
        // A typed non-success (nothing staged, hook rejection, ...) is never a
        // silent success and never clears the message.
        setErrorMessage(failureMessage(result));
        return;
      }

      setLastCommit(result.commit);
      setMessage('');
      if (result.status) {
        onStatusRefreshed?.(result.status);
      }
      if (result.commit) {
        onCommitSucceeded?.(result.commit, result.flow);
      }
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'The commit request failed.');
    } finally {
      setBusyAction(null);
    }
  }

  return (
    <div data-testid="commit-box" className="space-y-2">
      <label
        htmlFor="commit-message"
        className="block text-xs font-semibold uppercase tracking-wide text-mac-text-3"
      >
        Commit message
      </label>
      <textarea
        id="commit-message"
        aria-label="Commit message"
        rows={2}
        value={message}
        disabled={busy || gitUnavailable}
        onChange={(event) => setMessage(event.target.value)}
        placeholder="Describe the change"
        className="w-full px-2 py-1 text-sm text-mac-text bg-mac-content border border-mac-hairline-strong rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50"
      />

      {confirmingCommitAll ? (
        <div
          data-testid="commit-all-confirmation"
          className="space-y-2 rounded-lg border border-mac-warning/50 bg-mac-warning/10 p-2"
        >
          <p className="text-xs text-mac-text">
            Commit all changes? This stages and commits every eligible change, including
            unstaged files. Commit staged only records what is already staged.
          </p>
          <div className="flex items-center gap-2 flex-wrap">
            <button
              type="button"
              disabled={busy}
              onClick={() => setConfirmingCommitAll(false)}
              className={actionButtonClassName}
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={disabled}
              onClick={() => void run('commit-all')}
              className={primaryButtonClassName}
            >
              Confirm commit all
            </button>
          </div>
        </div>
      ) : (
        <div className="flex items-center gap-2 flex-wrap">
          <button
            type="button"
            disabled={disabled}
            onClick={() => void run('commit')}
            className={primaryButtonClassName}
          >
            {commitLabel}
          </button>
          <button
            type="button"
            disabled={disabled}
            onClick={() => {
              if (confirmCommitAll) {
                setConfirmingCommitAll(true);
                return;
              }
              void run('commit-all');
            }}
            className={actionButtonClassName}
          >
            {commitAllLabel}
          </button>
        </div>
      )}

      {lastCommit && (
        <div
          data-testid="commit-last-commit"
          className="p-2 text-xs text-mac-text-2 bg-mac-canvas border border-mac-hairline rounded-lg"
        >
          Committed <span className="font-mono">{lastCommit.shortId}</span>: {lastCommit.summary}
        </div>
      )}

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
