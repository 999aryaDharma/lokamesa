import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PullDialog from './PullDialog';
import type { GitPullOutcome, SourceControlPullResult } from './sourceControlModels';

/**
 * SCM-12 — conservative fast-forward Pull dialog (spec 13 §14.3/§18/§20).
 *
 * Pull is always user initiated: rendering the dialog runs no Git. The dialog
 * states the fast-forward-only policy, posts exactly `{ path }` when the user
 * confirms, and renders every typed outcome — especially the blocking
 * `Diverged` state, which must never look like a successful merge. The session
 * token travels in the header, never in the URL.
 */

function pullResult(overrides: Partial<SourceControlPullResult> = {}): SourceControlPullResult {
  return {
    outcome: 'Succeeded',
    isSuccess: true,
    isBlocking: false,
    errorCode: null,
    message: null,
    operationId: 'scm-pull-1',
    previousHead: '1111111111111111111111111111111111111111',
    head: '2222222222222222222222222222222222222222',
    changedPaths: ['docs/pulled.md'],
    reindexedDocuments: 1,
    statusRefreshed: true,
    historyRecorded: true,
    remote: {
      status: 'Available',
      hasRepository: true,
      worktreeRoot: 'C:\\workspace',
      branchName: 'main',
      isDetached: false,
      headCommit: '2222222222222222222222222222222222222222',
      remoteName: 'origin',
      upstreamReference: 'origin/main',
      hasUpstream: true,
      ahead: 0,
      behind: 0,
      failure: null,
    },
    status: null,
    failure: null,
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

describe('PullDialog', () => {
  it('runs no Git while closed or on mount and only pulls after explicit confirmation', async () => {
    const onPulled = vi.fn();
    const fetchMock = stubFetch(async () => jsonResponse(200, pullResult()));

    const { rerender } = render(
      <PullDialog path={'C:\\workspace'} open={false} onPulled={onPulled} />
    );
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).toBeNull();

    rerender(<PullDialog path={'C:\\workspace'} open onPulled={onPulled} />);
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toHaveTextContent(/fast-forward only/i);
    // The policy is explicit: no merge commit, no rebase.
    expect(screen.getByRole('dialog')).toHaveTextContent(/never creates a merge commit/i);
    expect(screen.getByRole('dialog')).toHaveTextContent(/never rebase/i);

    fireEvent.click(screen.getByRole('button', { name: 'Pull' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/pull');
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({ 'X-DocumentIDE-Session-Token': 'secret' });
    // Exactly the path: no branch, remote, refspec, --rebase or --no-ff option.
    expect(JSON.parse(init.body as string)).toEqual({ path: 'C:\\workspace' });
    expect(url).not.toContain('secret');

    await waitFor(() => expect(onPulled).toHaveBeenCalledWith(expect.objectContaining({ isSuccess: true })));
    expect(screen.getByTestId('pull-result')).toHaveTextContent('docs/pulled.md');
    expect(screen.getByTestId('pull-result')).toHaveTextContent(/1 document reindexed/i);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('shows divergence as a blocking state and never as a merge/rebase success', async () => {
    const onPulled = vi.fn();
    stubFetch(async () =>
      jsonResponse(
        200,
        pullResult({
          outcome: 'Diverged',
          isSuccess: false,
          isBlocking: true,
          errorCode: 'Diverged',
          message:
            'Local and remote history have diverged. No automatic merge/rebase was performed.',
          head: '1111111111111111111111111111111111111111',
          changedPaths: [],
          reindexedDocuments: 0,
          remote: null,
        })
      )
    );

    render(<PullDialog path={'C:\\workspace'} open onPulled={onPulled} />);
    fireEvent.click(screen.getByRole('button', { name: 'Pull' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Diverged');
    expect(alert).toHaveTextContent(/Local and remote history have diverged/i);
    expect(alert).toHaveTextContent(/No automatic merge\/rebase was performed/i);
    expect(alert).toHaveTextContent(/nothing was changed/i);
    expect(onPulled).not.toHaveBeenCalled();
    expect(screen.queryByTestId('pull-result')).toBeNull();
  });

  it('treats a dirty/conflicted worktree refusal as a blocking state with no changed file', async () => {
    stubFetch(async () =>
      jsonResponse(
        200,
        pullResult({
          outcome: 'DirtyWorktree',
          isSuccess: false,
          isBlocking: true,
          errorCode: 'DirtyWorktree',
          message:
            'The worktree has uncommitted changes; pull is blocked and no file was changed. ' +
            'Affected paths: docs/a.md.',
          changedPaths: [],
          remote: null,
        })
      )
    );

    render(<PullDialog path={'C:\\workspace'} open />);
    fireEvent.click(screen.getByRole('button', { name: 'Pull' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('DirtyWorktree');
    expect(alert).toHaveTextContent(/no file was changed/i);
    expect(alert).toHaveTextContent(/docs\/a\.md/);
  });

  it.each<GitPullOutcome>([
    'NoUpstream',
    'NoRepository',
    'GitUnavailable',
    'DirtyWorktree',
    'ConflictedWorktree',
    'Diverged',
    'IndexLocked',
    'AuthenticationRequired',
    'PermissionDenied',
    'HostUnreachable',
    'WorktreeMissing',
    'Failed',
  ])('renders the typed %s outcome in the alert region', async (outcome) => {
    stubFetch(async () =>
      jsonResponse(
        200,
        pullResult({
          outcome,
          isSuccess: false,
          isBlocking: ['Diverged', 'DirtyWorktree', 'ConflictedWorktree'].includes(outcome),
          errorCode: outcome,
          message: `Typed ${outcome} context.`,
          changedPaths: [],
          remote: null,
        })
      )
    );

    render(<PullDialog path={'C:\\workspace'} open />);
    fireEvent.click(screen.getByRole('button', { name: 'Pull' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(outcome);
    expect(alert).toHaveTextContent(`Typed ${outcome} context.`);
  });

  it('disables Pull when the branch has no upstream and runs no Git', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, pullResult()));

    render(<PullDialog path={'C:\\workspace'} open hasUpstream={false} />);

    expect(screen.getByTestId('pull-no-upstream')).toHaveTextContent(/no upstream/i);
    const button = screen.getByRole('button', { name: 'Pull' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('disables Pull when Git is unavailable', () => {
    stubFetch(async () => jsonResponse(200, pullResult()));

    render(<PullDialog path={'C:\\workspace'} open gitUnavailable />);

    expect(screen.getByRole('button', { name: 'Pull' })).toBeDisabled();
  });

  it('surfaces a rejected request (400) as an alert instead of a blank state', async () => {
    stubFetch(async () =>
      jsonResponse(400, { errorCode: 'PathRequired', error: 'The path is required.' })
    );

    render(<PullDialog path={'C:\\workspace'} open />);
    fireEvent.click(screen.getByRole('button', { name: 'Pull' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/PathRequired/);
    expect(alert).toHaveTextContent(/path is required/i);
  });

  it('shows the already-up-to-date state without claiming a merge', async () => {
    stubFetch(async () =>
      jsonResponse(
        200,
        pullResult({
          previousHead: '2222222222222222222222222222222222222222',
          head: '2222222222222222222222222222222222222222',
          changedPaths: [],
          reindexedDocuments: 0,
        })
      )
    );

    render(<PullDialog path={'C:\\workspace'} open />);
    fireEvent.click(screen.getByRole('button', { name: 'Pull' }));

    const result = await screen.findByTestId('pull-result');
    expect(result).toHaveTextContent(/already up to date/i);
    expect(result).not.toHaveTextContent(/merged/i);
    expect(result).not.toHaveTextContent(/rebased/i);
  });

  it('closes through Cancel without running Git', () => {
    const onClose = vi.fn();
    const fetchMock = stubFetch(async () => jsonResponse(200, pullResult()));

    render(<PullDialog path={'C:\\workspace'} open onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
