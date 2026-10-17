import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CommitBox from './CommitBox';
import type { SourceControlCommitInfo, SourceControlStatusModel } from './sourceControlModels';

/**
 * SCM-07 — Git Commit & Commit All pane actions.
 *
 * The commit box talks to the privileged API through the documented globals
 * (`__DOCUMENTIDE_API_URL__` / `__DOCUMENTIDE_SESSION_TOKEN__`). Commit and
 * Commit All stay explicitly distinct actions, an empty message never reaches
 * the API, failures stay typed and visible, and a successful commit reports the
 * new commit identifier plus the status the API re-read from Git (spec 13
 * §11/§18/§20).
 */

function statusModel(overrides: Partial<SourceControlStatusModel> = {}): SourceControlStatusModel {
  return {
    status: 'Available',
    hasRepository: true,
    worktreeRoot: 'C:\\workspace',
    branchName: 'main',
    isDetached: false,
    headCommit: '2222222222222222222222222222222222222222',
    upstreamReference: null,
    hasUpstream: false,
    ahead: null,
    behind: null,
    sections: { staged: [], unstaged: [], untracked: [], conflicts: [] },
    failure: null,
    ...overrides,
  };
}

function commitInfo(overrides: Partial<SourceControlCommitInfo> = {}): SourceControlCommitInfo {
  return {
    commitId: 'abcdef0123456789abcdef0123456789abcdef01',
    shortId: 'abcdef0',
    summary: 'commit the staged state',
    branchName: 'main',
    ...overrides,
  };
}

function jsonResponse(status: number, data: unknown): Response {
  return { ok: status < 400, status, json: async () => data } as Response;
}

/** Stubs global fetch with a typed mock so call arguments stay inspectable. */
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

describe('CommitBox', () => {
  it('commits the typed message through the privileged API and reports the created commit', async () => {
    const onStatusRefreshed = vi.fn();
    const onCommitSucceeded = vi.fn();
    const refreshed = statusModel({ headCommit: 'abcdef0123456789abcdef0123456789abcdef01' });
    const fetchMock = stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'Succeeded',
        flow: 'Commit',
        isSuccess: true,
        errorCode: null,
        message: null,
        commit: commitInfo(),
        stagedPaths: null,
        status: refreshed,
        failure: null,
      })
    );

    render(
      <CommitBox
        path={'C:\\workspace'}
        onStatusRefreshed={onStatusRefreshed}
        onCommitSucceeded={onCommitSucceeded}
      />
    );

    fireEvent.change(screen.getByLabelText('Commit message'), {
      target: { value: 'commit the staged state' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Commit' }));

    await waitFor(() => expect(onCommitSucceeded).toHaveBeenCalledTimes(1));

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/commit');
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': 'secret',
    });
    expect(JSON.parse(init.body as string)).toEqual({
      path: 'C:\\workspace',
      message: 'commit the staged state',
    });

    expect(onStatusRefreshed).toHaveBeenCalledWith(refreshed);
    expect(onCommitSucceeded).toHaveBeenCalledWith(commitInfo(), 'Commit');

    // The new commit is visible to the user, and nothing is reported as an error.
    expect(screen.getByTestId('commit-last-commit')).toHaveTextContent('abcdef0');
    expect(screen.getByTestId('commit-last-commit')).toHaveTextContent('commit the staged state');
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('runs Commit All as a distinct action and reports the flow it ran', async () => {
    const onCommitSucceeded = vi.fn();
    const fetchMock = stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'Succeeded',
        flow: 'CommitAll',
        isSuccess: true,
        errorCode: null,
        message: null,
        commit: commitInfo(),
        stagedPaths: ['docs/a.md', 'docs/b.md'],
        status: statusModel(),
        failure: null,
      })
    );

    render(
      <CommitBox path={'C:\\workspace'} onCommitSucceeded={onCommitSucceeded} />
    );

    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'sweep' } });
    fireEvent.click(screen.getByRole('button', { name: 'Commit All' }));

    await waitFor(() => expect(onCommitSucceeded).toHaveBeenCalledTimes(1));

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/commit-all');
    expect(JSON.parse(init.body as string)).toEqual({ path: 'C:\\workspace', message: 'sweep' });
    expect(onCommitSucceeded).toHaveBeenCalledWith(commitInfo(), 'CommitAll');
  });

  it('never sends a blank or whitespace-only message to the API', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, {}));

    render(<CommitBox path={'C:\\workspace'} />);

    // Both actions are disabled until a non-blank message exists.
    expect(screen.getByRole('button', { name: 'Commit' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Commit All' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: '   \t ' } });

    expect(screen.getByRole('button', { name: 'Commit' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Commit All' })).toBeDisabled();
    expect(fetchMock).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'real message' } });

    expect(screen.getByRole('button', { name: 'Commit' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Commit All' })).toBeEnabled();
  });

  it('disables both actions while a request is in flight and re-enables them after', async () => {
    let resolveFetch: (value: Response) => void = () => undefined;
    stubFetch(
      () =>
        new Promise<Response>((resolve) => {
          resolveFetch = resolve;
        })
    );

    render(<CommitBox path={'C:\\workspace'} />);
    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'in flight' } });

    fireEvent.click(screen.getByRole('button', { name: 'Commit' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Commit' })).toBeDisabled());
    expect(screen.getByRole('button', { name: 'Commit All' })).toBeDisabled();

    // A typed non-success keeps the message, so both actions become available
    // again once the request settles.
    resolveFetch(
      jsonResponse(200, {
        outcome: 'NothingStaged',
        flow: 'Commit',
        isSuccess: false,
        errorCode: 'NothingStaged',
        message: 'There is nothing staged to commit; no empty commit was created.',
        commit: null,
        stagedPaths: null,
        status: null,
        failure: null,
      })
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'Commit' })).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Commit All' })).toBeEnabled();
    expect(await screen.findByRole('alert')).toHaveTextContent('nothing staged');
  });

  it('surfaces a typed hook rejection without reporting a created commit', async () => {
    const onCommitSucceeded = vi.fn();
    stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'HookRejected',
        flow: 'Commit',
        isSuccess: false,
        errorCode: 'HookRejected',
        message: 'A repository hook rejected the commit; no commit was created.',
        commit: null,
        stagedPaths: null,
        status: null,
        failure: {
          status: 'Failed',
          errorKind: 'Unknown',
          errorMessage: 'policy violation: ticket id missing',
          exitCode: 1,
        },
      })
    );

    render(<CommitBox path={'C:\\workspace'} onCommitSucceeded={onCommitSucceeded} />);
    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'no ticket' } });
    fireEvent.click(screen.getByRole('button', { name: 'Commit' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('HookRejected');
    expect(alert).toHaveTextContent('policy violation: ticket id missing');
    expect(onCommitSucceeded).not.toHaveBeenCalled();
  });

  it('surfaces a blank-message 400 rejection as a typed error', async () => {
    stubFetch(async () =>
      jsonResponse(400, {
        errorCode: 'MessageRequired',
        error: 'A commit message is required; no commit was created.',
      })
    );

    render(<CommitBox path={'C:\\workspace'} />);
    // The client guard blocks a blank message, so drive the request with a
    // non-blank message that the API still rejects typed.
    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'x' } });
    fireEvent.click(screen.getByRole('button', { name: 'Commit' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('MessageRequired');
  });

  it('disables both actions when Git is not available', () => {
    render(<CommitBox path={'C:\\workspace'} gitUnavailable />);

    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'x' } });

    expect(screen.getByRole('button', { name: 'Commit' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Commit All' })).toBeDisabled();
  });
});
