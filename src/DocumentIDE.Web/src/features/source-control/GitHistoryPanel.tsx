import { useCallback, useEffect, useMemo, useState } from 'react';
import type {
  SourceControlFileVersionInfo,
  SourceControlFileVersionResult,
  SourceControlHistoryCommit,
  SourceControlHistoryResult,
} from './sourceControlModels';

/**
 * SCM-08 — Git commit history and file history pane (spec 13 §9.4/§12/§18/§20/§25).
 *
 * Git history comes from the privileged localhost API, never from Document IDE
 * history: the panel is labelled as Git data and reads only what Git reports.
 * Large histories load incrementally through the skip/limit window, a document
 * scope targets one selected file (`git log -- <path>`), and inspecting a
 * commit reads that document's version read-only (`git show <commit>:<path>`).
 * Every typed state stays visible: an empty list is only ever "Git reported no
 * commits", never a hidden failure.
 */

export type GitHistoryScope = 'repository' | 'document';

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

function apiUrl(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

async function readJson<T>(response: Response, fallback: string): Promise<T> {
  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `${fallback} (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as T;
}

/** Reads one page of repository or document history through the privileged API. */
export async function fetchHistory(
  path: string,
  scope: GitHistoryScope,
  filePath: string | null,
  skip: number,
  limit = 50
): Promise<SourceControlHistoryResult> {
  const route = scope === 'document' ? 'file-history' : 'history';
  const params = new URLSearchParams({ path, skip: String(skip), limit: String(limit) });
  if (scope === 'document') {
    params.set('file', filePath ?? '');
  }

  const response = await fetch(`${apiUrl()}/api/source-control/${route}?${params.toString()}`, {
    headers: { 'X-DocumentIDE-Session-Token': sessionToken() },
  });

  return readJson<SourceControlHistoryResult>(response, 'The history request failed');
}

/** Reads one document version at one commit through the privileged API. */
export async function fetchFileVersion(
  path: string,
  filePath: string,
  commitId: string
): Promise<SourceControlFileVersionResult> {
  const params = new URLSearchParams({ path, file: filePath, commit: commitId });
  const response = await fetch(
    `${apiUrl()}/api/source-control/file-version?${params.toString()}`,
    { headers: { 'X-DocumentIDE-Session-Token': sessionToken() } }
  );

  return readJson<SourceControlFileVersionResult>(response, 'The version request failed');
}

export interface GitHistoryPanelProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string;
  /** Worktree-relative path of the selected document; enables the document scope. */
  selectedPath?: string | null;
  /** True when Git is not installed/discoverable; the panel then queries nothing. */
  gitUnavailable?: boolean;
  /** Receives a document version the user inspected (read-only). */
  onVersionInspected?: (version: SourceControlFileVersionInfo) => void;
}

function resultMessage(result: SourceControlHistoryResult): string {
  if (result.failure) {
    const detail =
      result.failure.errorMessage ?? result.message ?? `Git reported ${result.status}.`;
    return `${result.status} (${result.failure.errorKind}): ${detail}`;
  }

  return result.message ?? `Git history is unavailable (${result.status}).`;
}

function versionMessage(result: SourceControlFileVersionResult): string {
  if (result.failure) {
    const detail =
      result.failure.errorMessage ?? result.message ?? `Git reported ${result.status}.`;
    return `${result.status} (${result.failure.errorKind}): ${detail}`;
  }

  return result.message ?? `The document version is unavailable (${result.status}).`;
}

function formatCommitterTime(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

const scopeButtonClassName =
  'px-2 py-1 text-xs font-medium text-slate-700 bg-white border border-slate-300 rounded-lg ' +
  'hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

const activeScopeButtonClassName =
  'px-2 py-1 text-xs font-medium text-white bg-slate-700 border border-slate-700 rounded-lg ' +
  'hover:bg-slate-800 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

export default function GitHistoryPanel({
  path,
  selectedPath = null,
  gitUnavailable = false,
  onVersionInspected,
}: GitHistoryPanelProps) {
  const [scope, setScope] = useState<GitHistoryScope>('repository');
  const [commits, setCommits] = useState<SourceControlHistoryCommit[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [nextSkip, setNextSkip] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [pageLimit, setPageLimit] = useState(50);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [version, setVersion] = useState<SourceControlFileVersionInfo | null>(null);
  const [versionError, setVersionError] = useState<string | null>(null);
  const [busyCommit, setBusyCommit] = useState<string | null>(null);

  const documentPath = selectedPath ?? null;
  const canScopeToDocument = !gitUnavailable && documentPath !== null;
  const effectiveScope: GitHistoryScope = scope === 'document' && canScopeToDocument ? 'document' : 'repository';

  const loadPage = useCallback(
    async (skip: number, append: boolean) => {
      if (gitUnavailable) {
        return;
      }

      setLoading(true);
      setErrorMessage(null);
      try {
        const result = await fetchHistory(path, effectiveScope, documentPath, skip, pageLimit);
        if (!result.isAvailable || result.page === null) {
          // A typed non-success is never rendered as an empty history.
          setCommits(append ? commits : []);
          setHasMore(false);
          setNextSkip(null);
          setErrorMessage(resultMessage(result));
          return;
        }

        setCommits(append ? [...commits, ...result.page.commits] : result.page.commits);
        setHasMore(result.page.hasMore);
        setNextSkip(result.page.nextSkip);
        // Continue the window Git actually served, so "Load more" appends the
        // same page size without gaps (spec 13 §25).
        setPageLimit(result.page.limit);
      } catch (error) {
        setHasMore(false);
        setNextSkip(null);
        setErrorMessage(error instanceof Error ? error.message : 'The history request failed.');
      } finally {
        setLoading(false);
      }
    },
    [commits, documentPath, effectiveScope, gitUnavailable, path, pageLimit]
  );

  useEffect(() => {
    if (gitUnavailable) {
      setCommits([]);
      setHasMore(false);
      setNextSkip(null);
      setErrorMessage(null);
      return;
    }

    setVersion(null);
    setVersionError(null);
    void loadPage(0, false);
    // Re-reading from the first page whenever the source, the scope or the
    // selected document changes keeps the list a projection of Git state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, effectiveScope, documentPath, gitUnavailable]);

  async function inspectVersion(commit: SourceControlHistoryCommit) {
    if (!documentPath) {
      return;
    }

    setBusyCommit(commit.commitId);
    setVersionError(null);
    try {
      const result = await fetchFileVersion(path, documentPath, commit.commitId);
      if (!result.isAvailable || result.version === null) {
        setVersion(null);
        setVersionError(versionMessage(result));
        return;
      }

      setVersion(result.version);
      onVersionInspected?.(result.version);
    } catch (error) {
      setVersion(null);
      setVersionError(error instanceof Error ? error.message : 'The version request failed.');
    } finally {
      setBusyCommit(null);
    }
  }

  const scopeNote = useMemo(
    () => (effectiveScope === 'document' && documentPath ? documentPath : null),
    [documentPath, effectiveScope]
  );

  return (
    <section data-testid="git-history-panel" className="space-y-3 p-3">
      <header className="space-y-1">
        <h3
          data-testid="git-history-heading"
          className="text-sm font-semibold text-slate-700"
        >
          Git History
        </h3>
        <p data-testid="git-history-source-note" className="text-xs text-slate-500">
          Source: Git — this history comes from the repository and is separate from Document IDE
          History.
        </p>
      </header>

      <div className="flex items-center gap-2 flex-wrap">
        <button
          type="button"
          aria-pressed={effectiveScope === 'repository'}
          disabled={gitUnavailable || loading}
          onClick={() => setScope('repository')}
          className={
            effectiveScope === 'repository' ? activeScopeButtonClassName : scopeButtonClassName
          }
        >
          Repository
        </button>
        <button
          type="button"
          aria-pressed={effectiveScope === 'document'}
          disabled={!canScopeToDocument || loading}
          onClick={() => setScope('document')}
          className={
            effectiveScope === 'document' ? activeScopeButtonClassName : scopeButtonClassName
          }
        >
          This document
        </button>
      </div>

      {gitUnavailable && (
        <div
          data-testid="git-history-unavailable"
          className="p-2 text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg"
        >
          Git is not available, so no commit history can be read.
        </div>
      )}

      {scopeNote && (
        <div
          data-testid="git-history-scope-note"
          className="p-2 text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg"
        >
          File history for <span className="font-mono">{scopeNote}</span> — renames are not
          followed, so history before a move may not appear.
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

      {!gitUnavailable && commits.length === 0 && !errorMessage && !loading && (
        <p className="text-xs text-slate-500">Git reports no commits for this source.</p>
      )}

      {commits.length > 0 && (
        <ul className="space-y-2">
          {commits.map((commit) => (
            <li
              key={commit.commitId}
              data-testid="git-history-commit"
              className="p-2 text-xs bg-white border border-slate-200 rounded-lg space-y-1"
            >
              <div className="flex items-center gap-2 flex-wrap">
                <span className="font-mono text-slate-700">{commit.shortId}</span>
                <span className="text-slate-800">{commit.summary}</span>
              </div>
              <div className="text-slate-500">
                {commit.authorName} &lt;{commit.authorEmail}&gt; ·{' '}
                {formatCommitterTime(commit.committerTime)} · {commit.fileCount}{' '}
                {commit.fileCount === 1 ? 'file' : 'files'}
              </div>
              {documentPath && (
                <button
                  type="button"
                  disabled={busyCommit !== null}
                  onClick={() => void inspectVersion(commit)}
                  className="px-2 py-1 text-xs font-medium text-slate-700 bg-white border border-slate-300 rounded-lg hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 transition-colors"
                >
                  Inspect version {commit.shortId}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {hasMore && (
        <button
          type="button"
          disabled={loading}
          onClick={() => void loadPage(nextSkip ?? commits.length, true)}
          className="px-3 py-1.5 text-sm font-medium text-slate-700 bg-white border border-slate-300 rounded-lg hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 transition-colors"
        >
          Load more
        </button>
      )}

      {versionError && (
        <div
          role="alert"
          className="p-2 text-xs text-red-700 bg-red-50 border border-red-200 rounded-lg"
        >
          {versionError}
        </div>
      )}

      {version && (
        <div
          data-testid="git-history-version-preview"
          className="p-2 text-xs bg-slate-50 border border-slate-200 rounded-lg space-y-1"
        >
          <div className="text-slate-600">
            <span className="font-mono">{version.path}</span> at{' '}
            <span className="font-mono">{version.commitId.slice(0, 7)}</span>
          </div>
          {version.isBinary ? (
            <div className="text-slate-600">
              Binary document ({version.sizeBytes} bytes) — no text preview is shown.
            </div>
          ) : version.isTruncated && version.content === null ? (
            <div className="text-slate-600">
              Document is too large to preview ({version.sizeBytes} bytes).
            </div>
          ) : (
            <>
              <pre className="max-h-48 overflow-auto whitespace-pre-wrap font-mono text-slate-800">
                {version.content ?? ''}
              </pre>
              <div className="text-slate-500">
                {version.sizeBytes} bytes
                {version.isTruncated ? ' — truncated preview (the file is larger)' : ''}
              </div>
            </>
          )}
        </div>
      )}
    </section>
  );
}
