import { useEffect, useState } from 'react';
import type { SourceControlFetchResult, SourceControlRemoteModel } from './sourceControlModels';

/**
 * SCM-11 — remote state and explicit Fetch for the changes pane
 * (spec 13 §14.1/§14.2/§18/§20).
 *
 * Reads the worktree's upstream/tracking state from the privileged API and
 * renders the ahead/behind counters. A branch with no configured upstream is
 * shown as an explicit "no upstream" state — the counters are unknown, so the
 * pane never renders a fake 0/0 (spec 13 §14.2). A configured upstream whose
 * counters Git cannot compute (the upstream's remote-tracking ref is absent)
 * shows an explicit "tracking info unavailable" state, also never a fake 0/0
 * (F2). Fetch is the only remote action: it posts through the privileged API,
 * refreshes the counters from the state Git re-read after the fetch, and
 * surfaces remote failures typed. A typed remote-state read failure
 * (`status:"Failed"`) surfaces in the alert region (F8). There is deliberately
 * no push, pull or branch-management surface.
 */

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

function apiBaseUrl(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

/** Reads the worktree's remote/tracking state through the privileged API. */
export async function getRemoteStatus(path: string): Promise<SourceControlRemoteModel> {
  const response = await fetch(
    `${apiBaseUrl()}/api/source-control/remote?path=${encodeURIComponent(path)}`,
    { headers: { 'X-DocumentIDE-Session-Token': sessionToken() } }
  );

  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `Remote state request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as SourceControlRemoteModel;
}

/** Runs one explicit fetch through the privileged API. */
export async function fetchRemote(path: string): Promise<SourceControlFetchResult> {
  const response = await fetch(`${apiBaseUrl()}/api/source-control/fetch`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': sessionToken(),
    },
    body: JSON.stringify({ path }),
  });

  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `Fetch request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as SourceControlFetchResult;
}

export interface RemoteStatusProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string;
  /** True when Git is not installed/discoverable; Fetch is disabled. */
  gitUnavailable?: boolean;
  /** Receives the tracking state re-read from Git after a successful fetch. */
  onRemoteChanged?: (remote: SourceControlRemoteModel) => void;
}

function fetchFailureMessage(result: SourceControlFetchResult): string {
  if (result.failure) {
    const detail =
      result.failure.errorMessage ?? result.message ?? `Git reported ${result.outcome}.`;
    return `${result.outcome} (${result.failure.errorKind}): ${detail}`;
  }
  return result.message ?? `The fetch failed (${result.outcome}).`;
}

/**
 * The explicit "no upstream" / unavailable state, or null. `Failed` is
 * deliberately not rendered here: a typed remote-state read failure is an error
 * and shows in the alert region instead (F8).
 */
function statusLabel(remote: SourceControlRemoteModel): string | null {
  if (remote.hasUpstream) {
    return null;
  }

  switch (remote.status) {
    case 'NoUpstream':
      return 'No upstream configured for this branch.';
    case 'NoRepository':
      return 'There is no Git repository at this location.';
    case 'GitUnavailable':
      return 'Git is not available.';
    default:
      return null;
  }
}

/**
 * A typed remote-state read failure (`status:"Failed"`, HTTP 200 with a failure
 * payload) as an alert message, or null. Only fetch failures and non-2xx loads
 * reached the alert region before, so a failed read rendered as a neutral grey
 * line (F8).
 */
function remoteReadFailureMessage(remote: SourceControlRemoteModel): string | null {
  if (remote.status !== 'Failed') {
    return null;
  }

  const failure = remote.failure;
  return failure?.errorMessage
    ? `Failed (${failure.errorKind}): ${failure.errorMessage}`
    : 'The remote state could not be read.';
}

const fetchButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-slate-700 bg-white border border-slate-300 rounded-lg ' +
  'hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

export default function RemoteStatus({
  path,
  gitUnavailable = false,
  onRemoteChanged,
}: RemoteStatusProps) {
  const [remote, setRemote] = useState<SourceControlRemoteModel | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setRemote(null);
    setLoadError(null);

    void (async () => {
      try {
        const next = await getRemoteStatus(path);
        if (active) {
          setRemote(next);
        }
      } catch (error) {
        if (active) {
          setLoadError(
            error instanceof Error ? error.message : 'The remote state could not be read.'
          );
        }
      }
    })();

    return () => {
      active = false;
    };
  }, [path]);

  // Fetch is gated on a configured upstream: with no upstream nothing runs
  // (spec 13 §14.2).
  const hasUpstream = remote?.hasUpstream ?? false;
  const canFetch = !busy && !gitUnavailable && hasUpstream;

  async function runFetch() {
    if (!canFetch) {
      return;
    }

    setBusy(true);
    setErrorMessage(null);
    try {
      const result = await fetchRemote(path);
      if (result.remote) {
        setRemote(result.remote);
        onRemoteChanged?.(result.remote);
      }
      if (!result.isSuccess) {
        setErrorMessage(fetchFailureMessage(result));
      }
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'The fetch request failed.');
    } finally {
      setBusy(false);
    }
  }

  const label = remote ? statusLabel(remote) : null;
  const readFailure = remote ? remoteReadFailureMessage(remote) : null;
  // F2 — a configured upstream can still have no computable counters (the
  // upstream's remote-tracking ref is absent); render an explicit
  // "unavailable" state instead of " ahead · behind" with blank values.
  const hasCounters = hasUpstream && remote?.ahead != null && remote?.behind != null;

  return (
    <div data-testid="remote-status" className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">Remote</span>
        {hasUpstream && (
          <span data-testid="remote-upstream" className="font-mono text-xs text-slate-600">
            {remote?.upstreamReference}
          </span>
        )}
      </div>

      {label && <p className="text-xs text-slate-500">{label}</p>}

      {hasUpstream && hasCounters && (
        <div
          data-testid="remote-ahead-behind"
          className="flex items-center gap-2 text-xs text-slate-600"
        >
          <span>{remote?.ahead} ahead</span>
          <span aria-hidden="true">·</span>
          <span>{remote?.behind} behind</span>
        </div>
      )}

      {hasUpstream && !hasCounters && (
        <p data-testid="remote-tracking-unavailable" className="text-xs text-slate-500">
          Ahead/behind tracking information is unavailable; fetch to refresh the upstream.
        </p>
      )}

      <button
        type="button"
        disabled={!canFetch}
        onClick={() => void runFetch()}
        className={fetchButtonClassName}
      >
        {busy ? 'Fetching…' : 'Fetch'}
      </button>

      {readFailure && (
        <div
          role="alert"
          className="p-2 text-xs text-red-700 bg-red-50 border border-red-200 rounded-lg"
        >
          {readFailure}
        </div>
      )}

      {loadError && (
        <div
          role="alert"
          className="p-2 text-xs text-red-700 bg-red-50 border border-red-200 rounded-lg"
        >
          {loadError}
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
