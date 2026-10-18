import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PushButton from './PushButton';
import type { GitPushOutcome, SourceControlRemoteModel } from './sourceControlModels';

/**
 * SCM-13 — explicit Push in the changes pane (spec 13 §14.4/§18/§20).
 *
 * Push is always user initiated: the component reads nothing and runs no Git on
 * mount, and it only posts the explicit push when the user clicks. The push is
 * scoped to the branch's configured upstream, so it is gated on a configured
 * upstream (a branch with no upstream is the typed `NoUpstream` state and runs
 * no Git at all — never a guessed default remote). Every non-success outcome
 * renders in the `role="alert"` region typed — never blank or raw — and the
 * access token is sent as a header, never in the URL or body.
 */

function remoteModel(overrides: Partial<SourceControlRemoteModel> = {}): SourceControlRemoteModel {
  return {
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

function typedFailure(
  outcome: GitPushOutcome,
  errorKind = 'Unknown',
  errorMessage = `fatal: ${outcome}`
) {
  return {
    status: 'Failed',
    errorKind,
    errorMessage,
    exitCode: 128,
  };
}

beforeEach(() => {
  Object.assign(window, {
    __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
    __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
  });
});

describe('PushButton', () => {
  it('never pushes automatically and only pushes when the user clicks', async () => {
    const onPushed = vi.fn();
    const fetchMock = stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'Succeeded',
        isSuccess: true,
        errorCode: null,
        message: null,
        remote: remoteModel(),
        failure: null,
      })
    );

    render(<PushButton path={'C:\\workspace'} onPushed={onPushed} />);

    // Rendering is inert: no request of any kind runs until the user acts.
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByRole('alert')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Push' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/push');
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({ 'X-DocumentIDE-Session-Token': 'secret' });
    // Exactly the path — no force, refspec, branch or remote argument is ever sent.
    expect(JSON.parse(init.body as string)).toEqual({ path: 'C:\\workspace' });
    // The token travels in the header, never in the URL.
    expect(url).not.toContain('secret');

    await waitFor(() =>
      expect(onPushed).toHaveBeenCalledWith(expect.objectContaining({ upstreamReference: 'origin/main' }))
    );
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByTestId('push-result')).toHaveTextContent('origin/main');
  });

  it('surfaces a rejected push typed and states that local commits are unchanged', async () => {
    const onPushed = vi.fn();
    stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'Rejected',
        isSuccess: false,
        errorCode: 'Rejected',
        message:
          'The remote rejected the push (not a fast-forward or rejected by a repository hook); ' +
          'no local commit was changed.',
        remote: null,
        failure: typedFailure('Rejected', 'GitPushRejected', 'fatal: failed to push some refs'),
      })
    );

    render(<PushButton path={'C:\\workspace'} onPushed={onPushed} />);
    fireEvent.click(screen.getByRole('button', { name: 'Push' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Rejected');
    expect(alert).toHaveTextContent(/no local commit was changed/i);
    // A rejected push hands no refreshed remote back to the pane.
    expect(onPushed).not.toHaveBeenCalled();
  });

  it('surfaces authentication-required as a typed alert instead of a raw or blank state', async () => {
    stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'AuthenticationRequired',
        isSuccess: false,
        errorCode: 'AuthenticationRequired',
        message:
          'The remote requires authentication, which is unavailable to this background operation; ' +
          'no credentials were requested interactively and nothing was pushed.',
        remote: null,
        failure: typedFailure('AuthenticationRequired', 'GitAuthRequired', 'fatal: Authentication failed'),
      })
    );

    render(<PushButton path={'C:\\workspace'} />);
    fireEvent.click(screen.getByRole('button', { name: 'Push' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('AuthenticationRequired');
    expect(alert).toHaveTextContent(/authentication/i);
    // Typed and inspectable — never "undefined" or an empty region.
    expect(alert.textContent ?? '').not.toMatch(/undefined|null/);
  });

  it.each<[GitPushOutcome]>([
    ['NoUpstream'],
    ['NoRepository'],
    ['GitUnavailable'],
    ['AuthenticationRequired'],
    ['Rejected'],
    ['PermissionDenied'],
    ['HostUnreachable'],
    ['Failed'],
  ])('renders the %s outcome in the alert region, typed and never blank', async (outcome) => {
    stubFetch(async () =>
      jsonResponse(200, {
        outcome,
        isSuccess: false,
        errorCode: outcome,
        message: `Typed ${outcome} outcome.`,
        remote: null,
        failure: null,
      })
    );

    render(<PushButton path={'C:\\workspace'} />);
    fireEvent.click(screen.getByRole('button', { name: 'Push' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(outcome);
    expect(alert).toHaveTextContent(`Typed ${outcome} outcome.`);
  });

  it('is gated on a configured upstream and runs no Git without one', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, {}));

    render(<PushButton path={'C:\\workspace'} hasUpstream={false} />);

    const button = screen.getByRole('button', { name: 'Push' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    // No upstream: nothing runs, exactly like the API's typed NoUpstream gate.
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByTestId('push-no-upstream')).toHaveTextContent(/no upstream/i);
  });

  it('disables Push when Git is not available', () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, {}));

    render(<PushButton path={'C:\\workspace'} gitUnavailable />);

    const button = screen.getByRole('button', { name: 'Push' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('keeps typed rejections between attempts and clears them on the next successful push', async () => {
    const fetchMock = stubFetch(async () =>
      fetchMock.mock.calls.length === 1
        ? jsonResponse(401, { errorCode: 'SessionRequired', error: 'A session token is required.' })
        : jsonResponse(200, {
            outcome: 'Succeeded',
            isSuccess: true,
            errorCode: null,
            message: null,
            remote: remoteModel(),
            failure: null,
          })
    );

    render(<PushButton path={'C:\\workspace'} />);
    fireEvent.click(screen.getByRole('button', { name: 'Push' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('SessionRequired');

    fireEvent.click(screen.getByRole('button', { name: 'Push' }));

    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull());
    expect(screen.getByTestId('push-result')).toHaveTextContent('origin/main');
  });

  it('exposes only the explicit Push action and no pull/force/branch surface', () => {
    stubFetch(async () => jsonResponse(200, {}));

    render(<PushButton path={'C:\\workspace'} />);

    const buttons = screen.getAllByRole('button');
    expect(buttons).toHaveLength(1);
    expect(buttons[0]).toHaveTextContent('Push');

    expect(screen.queryByRole('button', { name: /pull/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /force/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /branch/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /fetch/i })).toBeNull();
  });
});
