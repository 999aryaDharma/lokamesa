import type {
  SourceControlCorrelationResult,
  WorkSessionCommitModel,
  WorkSessionDocumentModel,
  WorkSessionModel,
} from './sourceControlModels';

/**
 * SCM-09 — Document IDE History ↔ Git correlation pane (spec 13 §4.3/§13/§24).
 *
 * The pane renders a correlation, never a unified timeline: Document IDE
 * History (the work session and the documents it changed) and Git History (the
 * correlated commit ids) are separate labelled lists, so a Git commit id can
 * never be mistaken for Document IDE state and vice versa. Correlated commit
 * ids are derived metadata only: they are read from the correlation store, they
 * never alter Git history, and the Document IDE History side stays usable when
 * Git is absent. No action here creates a commit.
 */

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

const WORK_SESSIONS_PATH = '/api/source-control/work-sessions';

function apiUrl(relative: string): string {
  const globals = window as Window & SourceControlApiGlobals;
  return `${globals.__DOCUMENTIDE_API_URL__ || ''}${relative}`;
}

function sessionToken(): string {
  const globals = window as Window & SourceControlApiGlobals;
  return globals.__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

/** Issues one correlation request through the privileged localhost API. */
async function requestCorrelation(
  relative: string,
  method: 'GET' | 'POST' | 'DELETE',
  body?: unknown
): Promise<SourceControlCorrelationResult> {
  const headers: Record<string, string> = {
    'X-DocumentIDE-Session-Token': sessionToken(),
  };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(apiUrl(relative), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const data: unknown = await response.json();
  if (!response.ok) {
    // A typed input rejection (blank session/document/commit id) is surfaced
    // verbatim; the client never fabricates a correlation for it.
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `Correlation request failed (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as SourceControlCorrelationResult;
}

/** Starts a Document IDE work session for a workspace/active-document path. */
export function startWorkSession(
  path: string,
  workspaceId?: string | null,
  label?: string | null
): Promise<SourceControlCorrelationResult> {
  return requestCorrelation(WORK_SESSIONS_PATH, 'POST', { path, workspaceId, label });
}

/** Records one document as changed by a session (Document IDE History). */
export function recordWorkSessionDocument(
  sessionId: string,
  documentId: string,
  path?: string | null
): Promise<SourceControlCorrelationResult> {
  return requestCorrelation(`${WORK_SESSIONS_PATH}/${encodeURIComponent(sessionId)}/documents`, 'POST', {
    documentId,
    path,
  });
}

/**
 * Correlates an already-created Git commit id with a session. The commit was
 * created by the explicit SCM-07 flow; this only records derived metadata and
 * never touches Git.
 */
export function correlateWorkSessionCommit(
  sessionId: string,
  commitId: string,
  shortId?: string | null,
  summary?: string | null
): Promise<SourceControlCorrelationResult> {
  return requestCorrelation(
    `${WORK_SESSIONS_PATH}/${encodeURIComponent(sessionId)}/commit-correlations`,
    'POST',
    { commitId, shortId, summary }
  );
}

/** Reads one work session and its correlation metadata. */
export function fetchWorkSession(sessionId: string): Promise<SourceControlCorrelationResult> {
  return requestCorrelation(`${WORK_SESSIONS_PATH}/${encodeURIComponent(sessionId)}`, 'GET');
}

/**
 * Drops one session and its correlation metadata. The Git commit it described
 * is untouched and stays valid.
 */
export function deleteWorkSession(sessionId: string): Promise<SourceControlCorrelationResult> {
  return requestCorrelation(`${WORK_SESSIONS_PATH}/${encodeURIComponent(sessionId)}`, 'DELETE');
}

export interface CommitCorrelationPanelProps {
  /** The correlation to render; null when no session could be read. */
  session: WorkSessionModel | null;
  /** Optional typed outcome to surface when no session payload is available. */
  outcome?: string;
  message?: string | null;
  /** Invoked with the document id when the user opens a changed document. */
  onOpenDocument?: (documentId: string) => void;
}

const sectionClassName = 'p-2 border border-mac-hairline rounded-lg space-y-1';
const headingClassName = 'text-xs font-semibold uppercase tracking-wide text-mac-text-3';

/** Display name for a changed document: its recorded path, else its id. */
function documentName(document: WorkSessionDocumentModel): string {
  return document.path ?? document.documentId;
}

function commitLabel(commit: WorkSessionCommitModel): string {
  return commit.shortId ?? commit.commitId;
}

export default function CommitCorrelationPanel({
  session,
  outcome,
  message,
  onOpenDocument,
}: CommitCorrelationPanelProps) {
  if (session === null) {
    if (!outcome) {
      return (
        <div data-testid="commit-correlation-panel" className="space-y-2">
          <p className="text-sm text-mac-text-3">No Document IDE work session is selected.</p>
        </div>
      );
    }

    return (
      <div data-testid="commit-correlation-panel" className="space-y-2">
        <div role="alert" className="p-2 text-xs text-mac-danger bg-mac-danger/10 border border-mac-danger/40 rounded-lg">
          <span className="font-medium">{outcome}</span>
          {message ? `: ${message}` : null}
        </div>
      </div>
    );
  }

  const hasGitContext = session.repositoryRoot !== null || session.worktreeRoot !== null;

  return (
    <div data-testid="commit-correlation-panel" className="space-y-3">
      <div className="text-sm text-mac-text">
        <span className="font-medium">{session.label ?? 'Work session'}</span>
        <span className="ml-2 text-xs text-mac-text-3">{session.id}</span>
      </div>

      {/* Read-only Git context recorded at session start; absent when Git is not
          available, in which case Document IDE History is unaffected. */}
      <div data-testid="correlation-git-context" className="text-xs text-mac-text-3">
        {hasGitContext
          ? `Git repository context: ${session.repositoryRoot ?? session.worktreeRoot}`
          : 'No Git repository context; Document IDE History is unaffected.'}
      </div>

      {/* Document IDE History: the session and the documents it changed. */}
      <section data-testid="correlation-document-ide-history" className={sectionClassName}>
        <h3 className={headingClassName}>Document IDE History</h3>
        {session.changedDocuments.length === 0 ? (
          <p className="text-xs text-mac-text-3">This session has not changed any documents.</p>
        ) : (
          <ul className="space-y-1">
            {session.changedDocuments.map((document) => (
              <li key={document.documentId} className="flex items-center justify-between gap-2 text-sm text-mac-text">
                <span className="truncate">{documentName(document)}</span>
                {onOpenDocument && (
                  <button
                    type="button"
                    onClick={() => onOpenDocument(document.documentId)}
                    className="px-2 py-0.5 text-xs font-medium text-mac-text bg-mac-content border border-mac-hairline-strong rounded hover:bg-mac-chip focus:outline-none focus:ring-2 focus:ring-slate-400"
                  >
                    Open {documentName(document)}
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* Git History: correlated commit ids, derived metadata only. */}
      <section data-testid="correlation-git-history" className={sectionClassName}>
        <h3 className={headingClassName}>Git History</h3>
        {session.commitCorrelations.length === 0 ? (
          <p className="text-xs text-mac-text-3">No Git commits are correlated with this session.</p>
        ) : (
          <ul className="space-y-1">
            {session.commitCorrelations.map((commit) => (
              <li key={commit.commitId} className="text-sm text-mac-text">
                <code className="font-mono text-xs">{commit.commitId}</code>
                <span className="ml-2 font-mono text-xs text-mac-text-3">{commitLabel(commit)}</span>
                {commit.summary && <span className="ml-2 text-xs text-mac-text-2">{commit.summary}</span>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
