import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import RemoteStatus from './RemoteStatus';
import type { SourceControlRemoteModel } from './sourceControlModels';

/**
 * SCM-11 — Git remote state & explicit Fetch in the changes pane.
 *
 * The remote-status surface reads the worktree's upstream/tracking state from
 * the privileged API and renders ahead/behind. A branch with no configured
 * upstream is shown as an explicit "no upstream" state — never a fake 0/0
 * (spec 13 §14.2). Fetch is the only remote action: it posts through the
 * privileged API, refreshes the counters, and surfaces remote failures typed
 * (spec 13 §14.1/§20). There is no push, pull or branch-management surface.
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

beforeEach(() => {
  Object.assign(window, {
    __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
    __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
  });
});

describe('RemoteStatus', () => {
  it('reads the remote state and renders the configured upstream with ahead/behind', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, remoteModel({ ahead: 2, behind: 1 })));

    render(<RemoteStatus path={'C:\\workspace'} />);

    const upstream = await screen.findByTestId('remote-upstream');
    expect(upstream).toHaveTextContent('origin/main');

    const counters = screen.getByTestId('remote-ahead-behind');
    expect(counters).toHaveTextContent('2 ahead');
    expect(counters).toHaveTextContent('1 behind');

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/remote?path=C%3A%5Cworkspace');
    expect(init.headers).toMatchObject({
      'X-DocumentIDE-Session-Token': 'secret',
    });

    expect(screen.getByRole('button', { name: 'Fetch' })).toBeEnabled();
  });

  it('shows an explicit no-upstream state and never a fake 0/0', async () => {
    const fetchMock = stubFetch(async () =>
      jsonResponse(
        200,
        remoteModel({
          status: 'NoUpstream',
          upstreamReference: null,
          remoteName: null,
          hasUpstream: false,
          ahead: null,
          behind: null,
        })
      )
    );

    render(<RemoteStatus path={'C:\\workspace'} />);

    const status = await screen.findByTestId('remote-status');
    expect(status).toHaveTextContent(/no upstream/i);
    // No bogus 0/0 counters are rendered without an upstream.
    expect(screen.queryByTestId('remote-ahead-behind')).toBeNull();

    // Fetch is gated on a configured upstream: it runs no Git at all.
    const button = screen.getByRole('button', { name: 'Fetch' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('fetches through the privileged API and renders the refreshed ahead/behind', async () => {
    const onRemoteChanged = vi.fn();
    const fetchMock = stubFetch(async (_url, init) => {
      if ((init.method ?? 'GET') === 'POST') {
        return jsonResponse(200, {
          outcome: 'Succeeded',
          isSuccess: true,
          errorCode: null,
          message: null,
          remote: remoteModel({ behind: 1 }),
          failure: null,
        });
      }
      return jsonResponse(200, remoteModel());
    });

    render(<RemoteStatus path={'C:\\workspace'} onRemoteChanged={onRemoteChanged} />);

    const button = await screen.findByRole('button', { name: 'Fetch' });
    fireEvent.click(button);

    await waitFor(() => expect(onRemoteChanged).toHaveBeenCalledTimes(1));

    const [url, init] = fetchMock.mock.calls[1];
    expect(url).toBe('http://localhost:5000/api/source-control/fetch');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ path: 'C:\\workspace' });

    expect(screen.getByTestId('remote-ahead-behind')).toHaveTextContent('1 behind');
    expect(onRemoteChanged).toHaveBeenCalledWith(expect.objectContaining({ behind: 1 }));
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('surfaces a typed remote failure as an alert', async () => {
    stubFetch(async (_url, init) => {
      if ((init.method ?? 'GET') === 'POST') {
        return jsonResponse(200, {
          outcome: 'HostUnreachable',
          isSuccess: false,
          errorCode: 'HostUnreachable',
          message: 'The remote could not be reached; the tracking state was not updated.',
          remote: null,
          failure: {
            status: 'Failed',
            errorKind: 'Unknown',
            errorMessage: 'fatal: unable to access the remote',
            exitCode: 128,
          },
        });
      }
      return jsonResponse(200, remoteModel());
    });

    render(<RemoteStatus path={'C:\\workspace'} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Fetch' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('HostUnreachable');
  });

  it('shows a tracking-unavailable state when upstream exists but counters are null', async () => {
    // F2 — a configured upstream with no computable counters (the upstream's
    // remote-tracking ref is absent) must not render " ahead · behind" with
    // blank values; it is an explicit unavailable state.
    stubFetch(async () =>
      jsonResponse(
        200,
        remoteModel({
          status: 'Available',
          hasUpstream: true,
          upstreamReference: 'origin/main',
          ahead: null,
          behind: null,
        })
      )
    );

    render(<RemoteStatus path={'C:\\workspace'} />);

    expect(await screen.findByTestId('remote-upstream')).toHaveTextContent('origin/main');
    // No fake/blank counter row.
    expect(screen.queryByTestId('remote-ahead-behind')).toBeNull();
    expect(screen.getByTestId('remote-tracking-unavailable')).toHaveTextContent(/unavailable/i);
    // No error: this is a valid state, just without counters.
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('renders a typed remote-state read failure in the alert region', async () => {
    // F8 — status:"Failed" arrives as HTTP 200 with a failure payload; it must
    // surface as an error alert, not a neutral grey line.
    stubFetch(async () =>
      jsonResponse(
        200,
        remoteModel({
          status: 'Failed',
          hasUpstream: false,
          upstreamReference: null,
          remoteName: null,
          ahead: null,
          behind: null,
          failure: {
            status: 'Failed',
            errorKind: 'Unknown',
            errorMessage: 'fatal: bad revision',
            exitCode: 128,
          },
        })
      )
    );

    render(<RemoteStatus path={'C:\\workspace'} />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('bad revision');
    expect(screen.getByRole('button', { name: 'Fetch' })).toBeDisabled();
  });

  it('exposes only the explicit Fetch action and no push/pull/branch surface', async () => {
    stubFetch(async () => jsonResponse(200, remoteModel()));

    render(<RemoteStatus path={'C:\\workspace'} />);
    await screen.findByTestId('remote-upstream');

    const buttons = screen.getAllByRole('button');
    expect(buttons).toHaveLength(1);
    expect(buttons[0]).toHaveTextContent('Fetch');

    expect(screen.queryByRole('button', { name: /push/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /pull/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /branch/i })).toBeNull();
  });

  it('disables Fetch when Git is not available', async () => {
    stubFetch(async () => jsonResponse(200, remoteModel()));

    render(<RemoteStatus path={'C:\\workspace'} gitUnavailable />);

    expect(await screen.findByRole('button', { name: 'Fetch' })).toBeDisabled();
  });
});
