import { useState } from 'react';
import type { SourceControlPushResult, SourceControlRemoteModel } from './sourceControlModels';

/**
 * SCM-13 — explicit Push action for the changes pane (spec 13 §14.4/§18/§20).
 *
 * Push is always user initiated: this component reads nothing and runs no Git
 * on mount, and only the explicit button posts the push. The push is scoped to
 * the current branch's configured upstream, so the action is gated on a
 * configured upstream — a branch with no upstream is the typed `NoUpstream`
 * state and runs no Git at all, never a push to a guessed default remote
 * (spec 13 §14.2/§14.4). The request body carries exactly the path: no force,
 * refspec, branch or remote argument is ever sent, so the pane cannot widen the
 * pushed scope (spec 13 §14.4/§23). Every non-success outcome is rendered typed
 * in the alert region — never blank or raw — and a rejected push is reported
 * with the local commits preserved. The session token travels in the header,
 * never in the URL.
 */

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

/** Posts one explicit push through the privileged API. */
export async function postPush(path: string): Promise<SourceControlPushResult> {
  const globals = window as Window & SourceControlApiGlobals;
  const response = await fetch(`${globals.__DOCUMENTIDE_API_URL__ || ''}/api/source-control/push`, {
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
    const detail = rejection.error ?? `Push request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as SourceControlPushResult;
}

export interface PushButtonProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string;
  /** False when the branch has no configured upstream; Push is disabled and runs no Git (spec 13 §14.2). */
  hasUpstream?: boolean;
  /** True when Git is not installed/discoverable; Push is disabled. */
  gitUnavailable?: boolean;
  /** Receives the tracking state re-read from Git after a successful push. */
  onPushed?: (remote: SourceControlRemoteModel) => void;
}

/**
 * The typed, human-readable failure text for a non-success push. The API's
 * curated `message` is always shown (it carries the safety statement, e.g. that
 * a rejected push changed no local commit); the originating Git diagnostic is
 * appended when it adds detail, so the region is never blank or raw
 * (spec 13 §20).
 */
function pushFailureMessage(result: SourceControlPushResult): string {
  const label = result.failure?.errorKind
    ? `${result.outcome} (${result.failure.errorKind})`
    : result.outcome;
  const message =
    result.message ?? result.failure?.errorMessage ?? `The push failed (${result.outcome}).`;
  const diagnostic = result.failure?.errorMessage;
  return diagnostic && diagnostic !== message ? `${label}: ${message} ${diagnostic}` : `${label}: ${message}`;
}

const pushButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-white bg-slate-700 border border-slate-700 rounded-lg ' +
  'hover:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

export default function PushButton({
  path,
  hasUpstream = true,
  gitUnavailable = false,
  onPushed,
}: PushButtonProps) {
  const [busy, setBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [pushedRemote, setPushedRemote] = useState<SourceControlRemoteModel | null>(null);

  // Push is gated on a configured upstream: with no upstream nothing runs
  // (spec 13 §14.2), exactly like the API's typed NoUpstream outcome.
  const disabled = busy || gitUnavailable || !hasUpstream;

  async function runPush() {
    if (disabled) {
      return;
    }

    setBusy(true);
    setErrorMessage(null);
    try {
      const result = await postPush(path);
      if (!result.isSuccess) {
        // A typed non-success is never a silent success, and it never hands a
        // refreshed remote back to the pane.
        setPushedRemote(null);
        setErrorMessage(pushFailureMessage(result));
        return;
      }

      setPushedRemote(result.remote ?? null);
      if (result.remote) {
        onPushed?.(result.remote);
      }
    } catch (error) {
      setPushedRemote(null);
      setErrorMessage(error instanceof Error ? error.message : 'The push request failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div data-testid="push-button" className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">Push</span>
        <button type="button" disabled={disabled} onClick={() => void runPush()} className={pushButtonClassName}>
          {busy ? 'Pushing…' : 'Push'}
        </button>
      </div>

      {!hasUpstream && (
        <p data-testid="push-no-upstream" className="text-xs text-slate-500">
          No upstream configured for this branch; push is unavailable and nothing is pushed to a
          default remote.
        </p>
      )}

      {pushedRemote && (
        <div
          data-testid="push-result"
          className="p-2 text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg"
        >
          Pushed to <span className="font-mono">{pushedRemote.upstreamReference}</span>.
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
    </div>
  );
}
