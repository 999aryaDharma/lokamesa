import { render, screen, fireEvent } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CommitCorrelationPanel, {
  correlateWorkSessionCommit,
  fetchWorkSession,
  startWorkSession,
} from './CommitCorrelationPanel';
import type { WorkSessionModel } from './sourceControlModels';

/**
 * SCM-09 — Document IDE History ↔ Git correlation pane (spec 13 §4.3/§13/§24).
 *
 * The panel renders a correlation, not a unified timeline: Document IDE History
 * (the session and the documents it changed) and Git History (the correlated
 * commit ids) stay visually distinguishable, in separate labelled lists. Git
 * commit ids are derived metadata only, so they stay readable even when Git is
 * unavailable, and the panel never claims Git state it does not have.
 */

function workSession(overrides: Partial<WorkSessionModel> = {}): WorkSessionModel {
  return {
    id: 'session-1',
    workspaceId: 'ws-1',
    repositoryRoot: 'C:\\workspace',
    worktreeRoot: 'C:\\workspace',
    startedAt: '2026-05-01T10:00:00.0000000Z',
    endedAt: null,
    label: 'edit the release notes',
    changedDocuments: [{ documentId: 'doc-1', path: 'docs/release-notes.md' }],
    commitCorrelations: [
      { commitId: 'abcdef0123456789abcdef0123456789abcdef01', shortId: 'abcdef0', summary: 'edit release notes' },
    ],
    ...overrides,
  };
}

function jsonResponse(status: number, data: unknown): Response {
  return { ok: status < 400, status, json: async () => data } as Response;
}

function stubFetch(handler: (url: string, init: RequestInit) => Response | Promise<Response>) {
  const fetchMock = vi.fn(handler);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

beforeEach(() => {
  Object.assign(window, {
    __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
    __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
  });
});

describe('CommitCorrelationPanel', () => {
  it('renders Document IDE History and Git History as separate labelled lists', () => {
    render(<CommitCorrelationPanel session={workSession()} />);

    const ideHistory = screen.getByTestId('correlation-document-ide-history');
    const gitHistory = screen.getByTestId('correlation-git-history');

    // Two distinct labelled sections, never one merged list.
    expect(ideHistory).toHaveTextContent('Document IDE History');
    expect(gitHistory).toHaveTextContent('Git History');

    // The changed document lives only in the Document IDE History section...
    expect(ideHistory).toHaveTextContent('docs/release-notes.md');
    expect(gitHistory).not.toHaveTextContent('docs/release-notes.md');

    // ...and the correlated commit id lives only in the Git History section.
    expect(gitHistory).toHaveTextContent('abcdef0123456789abcdef0123456789abcdef01');
    expect(ideHistory).not.toHaveTextContent('abcdef0123456789abcdef0123456789abcdef01');
  });

  it('shows explicit empty states for both histories', () => {
    render(
      <CommitCorrelationPanel
        session={workSession({ changedDocuments: [], commitCorrelations: [] })}
      />
    );

    expect(screen.getByTestId('correlation-document-ide-history')).toHaveTextContent(
      'This session has not changed any documents.'
    );
    expect(screen.getByTestId('correlation-git-history')).toHaveTextContent(
      'No Git commits are correlated with this session.'
    );
  });

  it('renders the full commit id as derived metadata when Git reported no short id', () => {
    render(
      <CommitCorrelationPanel
        session={workSession({
          commitCorrelations: [
            { commitId: '0123456789abcdef0123456789abcdef01234567', shortId: null, summary: null },
          ],
        })}
      />
    );

    expect(screen.getByTestId('correlation-git-history')).toHaveTextContent(
      '0123456789abcdef0123456789abcdef01234567'
    );
  });

  it('keeps Document IDE History usable when Git context is absent', () => {
    render(
      <CommitCorrelationPanel
        session={workSession({ repositoryRoot: null, worktreeRoot: null, commitCorrelations: [] })}
      />
    );

    // The session and its changed documents are still rendered...
    expect(screen.getByTestId('correlation-document-ide-history')).toHaveTextContent(
      'docs/release-notes.md'
    );
    // ...and the pane reports the absent Git context instead of inventing one.
    expect(screen.getByTestId('correlation-git-context')).toHaveTextContent(
      'No Git repository context'
    );
  });

  it('opens a changed document through the provided callback', () => {
    const onOpenDocument = vi.fn();
    render(<CommitCorrelationPanel session={workSession()} onOpenDocument={onOpenDocument} />);

    fireEvent.click(screen.getByRole('button', { name: /open docs\/release-notes\.md/i }));

    expect(onOpenDocument).toHaveBeenCalledWith('doc-1');
  });

  it('surfaces a typed input rejection from the privileged API verbatim', async () => {
    const fetchMock = stubFetch(async () =>
      jsonResponse(400, {
        errorCode: 'CommitRequired',
        error: 'A Git commit id is required to correlate a commit with a work session.',
      })
    );

    // The helper never invents a correlation when the API rejects the input.
    await expect(correlateWorkSessionCommit('session-1', '   ')).rejects.toThrow(/CommitRequired/);

    const [url] = fetchMock.mock.calls[0];
    expect(url).toBe(
      'http://localhost:5000/api/source-control/work-sessions/session-1/commit-correlations'
    );
  });

  it('starts a work session through the privileged API with the session token', async () => {
    const fetchMock = stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'Succeeded',
        isSuccess: true,
        errorCode: null,
        message: null,
        session: workSession(),
      })
    );

    const result = await startWorkSession('C:\\workspace', 'ws-1', 'correlate');

    expect(result.session?.id).toBe('session-1');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/work-sessions');
    expect(init.headers).toMatchObject({
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': 'secret',
    });
    expect(JSON.parse(init.body as string)).toEqual({
      path: 'C:\\workspace',
      workspaceId: 'ws-1',
      label: 'correlate',
    });
  });

  it('posts a new correlation request through the privileged API with the session token', async () => {
    const fetchMock = stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'Succeeded',
        isSuccess: true,
        errorCode: null,
        message: null,
        session: workSession(),
      })
    );

    const result = await fetchWorkSession('session-1');

    expect(result.session?.id).toBe('session-1');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/work-sessions/session-1');
    expect(init.headers).toMatchObject({
      'X-DocumentIDE-Session-Token': 'secret',
    });
  });

  it('reports a typed SessionNotFound correlation without inventing a session', async () => {
    stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'SessionNotFound',
        isSuccess: false,
        errorCode: 'SessionNotFound',
        message: 'No work session was found.',
        session: null,
      })
    );

    const result = await fetchWorkSession('missing');

    expect(result.session).toBeNull();
    expect(result.outcome).toBe('SessionNotFound');
    expect(result.isSuccess).toBe(false);
  });

  it('renders a typed correlation failure without a session payload', () => {
    render(
      <CommitCorrelationPanel
        session={null}
        outcome="SessionNotFound"
        message="No work session was found."
      />
    );

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('SessionNotFound');
    expect(alert).toHaveTextContent('No work session was found.');
    expect(screen.queryByTestId('correlation-git-history')).toBeNull();
  });
});
