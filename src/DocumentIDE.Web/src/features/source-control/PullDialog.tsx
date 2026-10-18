import { useState } from 'react';
import type { SourceControlPullResult } from './sourceControlModels';

/**
 * SCM-12 — conservative fast-forward Pull dialog (spec 13 §14.3/§18/§20).
 *
 * Pull is always user initiated: the dialog reads nothing and runs no Git on
 * mount, and only the explicit confirmation posts the pull. The policy is
 * shown before the action: fast-forward only, never a merge commit, never a
 * rebase. The backend refuses a dirty/conflicted worktree before Git runs and
 * reports divergence as a typed blocking state, which this dialog renders as a
 * blocking alert (never as a successful merge). The request body carries
 * exactly the path — no branch, refspec, remote, `--rebase` or `--no-ff` option
 * can be sent to widen the operation. The session token travels in the header,
 * never in the URL.
 */

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

/** Posts one conservative pull through the privileged API. */
export async function postPull(path: string): Promise<SourceControlPullResult> {
  const globals = window as Window & SourceControlApiGlobals;
  const response = await fetch(`${globals.__DOCUMENTIDE_API_URL__ || ''}/api/source-control/pull`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': globals.__DOCUMENTIDE_SESSION_TOKEN__ || '',
    },
    body: JSON.stringify({ path }),
  });

  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `Pull request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as SourceControlPullResult;
}

export interface PullDialogProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string;
  /** The dialog is mounted but inert unless open. */
  open: boolean;
  /** False when the branch has no configured upstream; Pull is disabled and runs no Git. */
  hasUpstream?: boolean;
  /** True when Git is not installed/discoverable; Pull is disabled. */
  gitUnavailable?: boolean;
  /** Receives the typed result after a pull actually ran. */
  onPulled?: (result: SourceControlPullResult) => void;
  /** Dismissed without any request. */
  onClose?: () => void;
}

/**
 * The typed, human-readable text for a non-success pull. The API's curated
 * message is always shown; a blocking state additionally states that nothing
 * was changed, so divergence can never read like a completed merge
 * (spec 13 §14.3/§20).
 */
function pullFailureMessage(result: SourceControlPullResult): string {
  const label = result.failure?.errorKind
    ? `${result.outcome} (${result.failure.errorKind})`
    : result.outcome;
  const message =
    result.message ?? result.failure?.errorMessage ?? `The pull failed (${result.outcome}).`;
  const diagnostic = result.failure?.errorMessage;
  const detail = diagnostic && diagnostic !== message ? `${message} ${diagnostic}` : message;
  return result.isBlocking ? `${label}: ${detail} Nothing was changed.` : `${label}: ${detail}`;
}

function shortCommit(commit: string | null): string {
  return commit ? commit.slice(0, 7) : '—';
}

const secondaryButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-slate-700 bg-white border border-slate-300 rounded-lg ' +
  'hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

const primaryButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-white bg-slate-700 border border-slate-700 rounded-lg ' +
  'hover:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

export default function PullDialog({
  path,
  open,
  hasUpstream = true,
  gitUnavailable = false,
  onPulled,
  onClose,
}: PullDialogProps) {
  const [busy, setBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [result, setResult] = useState<SourceControlPullResult | null>(null);

  if (!open) {
    return null;
  }

  // Pull is gated on a configured upstream and on Git being available: with
  // either missing nothing runs, exactly like the API's typed NoUpstream state.
  const disabled = busy || gitUnavailable || !hasUpstream;

  async function runPull() {
    if (disabled) {
      return;
    }

    setBusy(true);
    setErrorMessage(null);
    setResult(null);
    try {
      const pulled = await postPull(path);
      setResult(pulled);
      if (!pulled.isSuccess) {
        // A blocking/typed non-success is never silently treated as a success.
        setErrorMessage(pullFailureMessage(pulled));
        return;
      }

      onPulled?.(pulled);
    } catch (error) {
      setResult(null);
      setErrorMessage(error instanceof Error ? error.message : 'The pull request failed.');
    } finally {
      setBusy(false);
    }
  }

  const upToDate =
    result?.isSuccess === true &&
    result.changedPaths.length === 0 &&
    result.previousHead === result.head;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/30 p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="pull-dialog-title"
        data-testid="pull-dialog"
        className="w-full max-w-lg space-y-3 p-4 bg-white border border-slate-200 rounded-xl shadow-lg"
      >
        <div className="space-y-1">
          <h2 id="pull-dialog-title" className="text-sm font-semibold text-slate-800">
            Pull changes
          </h2>
          <p className="text-xs text-slate-600">
            Fast-forward only. Pull never creates a merge commit and never rebases; if the local and
            remote histories have diverged it stops and changes nothing.
          </p>
          <p className="text-xs text-slate-500">
            A dirty or conflicted worktree is refused before Git runs, so local work is never
            overwritten or autostashed.
          </p>
        </div>

        {!hasUpstream && (
          <p data-testid="pull-no-upstream" className="text-xs text-slate-500">
            No upstream configured for this branch; pull is unavailable and nothing runs against a
            default remote.
          </p>
        )}

        {gitUnavailable && (
          <p data-testid="pull-git-unavailable" className="text-xs text-slate-500">
            Git is not installed or discoverable; pull is unavailable.
          </p>
        )}

        {result?.isSuccess && (
          <div
            data-testid="pull-result"
            className="space-y-1 p-2 text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg"
          >
            {upToDate ? (
              <p>
                Already up to date at <span className="font-mono">{shortCommit(result.head)}</span>.
              </p>
            ) : (
              <p>
                Fast-forwarded to <span className="font-mono">{shortCommit(result.head)}</span>{' '}
                without a merge commit or rebase.
              </p>
            )}
            {result.changedPaths.length > 0 && (
              <ul className="list-disc pl-4">
                {result.changedPaths.map((changed) => (
                  <li key={changed} className="font-mono">
                    {changed}
                  </li>
                ))}
              </ul>
            )}
            <p>
              {result.reindexedDocuments}{' '}
              {result.reindexedDocuments === 1 ? 'document' : 'documents'} reindexed
              {result.statusRefreshed ? '; status refreshed.' : '.'}
            </p>
          </div>
        )}

        {errorMessage && (
          <div
            role="alert"
            className="p-2 text-xs text-red-700 bg-red-50 border border-red-200 rounded-lg"
          >
            {errorMessage}
          </div>
        )}

        <div className="flex items-center justify-end gap-2">
          <button type="button" onClick={() => onClose?.()} className={secondaryButtonClassName}>
            Cancel
          </button>
          <button
            type="button"
            disabled={disabled}
            onClick={() => void runPull()}
            className={primaryButtonClassName}
          >
            {busy ? 'Pulling…' : 'Pull'}
          </button>
        </div>
      </div>
    </div>
  );
}
