import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ChangeActions from './ChangeActions';
import type { SourceControlChangeEntry, SourceControlStatusModel } from './sourceControlModels';

/**
 * SCM-06 — Git Stage & Unstage pane actions.
 *
 * The actions talk to the privileged API through the documented globals
 * (`__DOCUMENTIDE_API_URL__` / `__DOCUMENTIDE_SESSION_TOKEN__`), stay disabled
 * while a request is in flight, surface typed failures instead of a silent
 * fallback, and hand the refreshed status payload of a successful mutation to
 * the pane so it never keeps a proprietary staging area (spec 13 §10/§18/§20).
 */

function change(overrides: Partial<SourceControlChangeEntry> = {}): SourceControlChangeEntry {
  return {
    path: 'docs/a.md',
    originalPath: null,
    state: 'Modified',
    indexChange: 'Unmodified',
    workingTreeChange: 'Modified',
    conflictKind: 'None',
    statusCharacters: '.M',
    absolutePath: 'C:\\workspace\\docs\\a.md',
    documentId: null,
    ...overrides,
  };
}

function statusModel(overrides: Partial<SourceControlStatusModel> = {}): SourceControlStatusModel {
  return {
    status: 'Available',
    hasRepository: true,
    worktreeRoot: 'C:\\workspace',
    branchName: 'main',
    isDetached: false,
    headCommit: '1111111111111111111111111111111111111111',
    upstreamReference: null,
    hasUpstream: false,
    ahead: null,
    behind: null,
    sections: { staged: [], unstaged: [], untracked: [], conflicts: [] },
    failure: null,
    ...overrides,
  };
}

function jsonResponse(status: number, data: unknown): Response {
  return { ok: status < 400, status, json: async () => data } as Response;
}

function succeededPayload(status: SourceControlStatusModel = statusModel()) {
  return {
    outcome: 'Succeeded',
    isSuccess: true,
    errorCode: null,
    message: null,
    status,
    failure: null,
  };
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

describe('ChangeActions', () => {
  it('stages the selected change and reports the refreshed status', async () => {
    const onStatusRefreshed = vi.fn();
    const refreshed = statusModel({ ahead: null });
    const fetchMock = stubFetch(async () => jsonResponse(200, succeededPayload(refreshed)));

    render(
      <ChangeActions
        path={'C:\\workspace'}
        change={change()}
        onStatusRefreshed={onStatusRefreshed}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(onStatusRefreshed).toHaveBeenCalledTimes(1));
    expect(onStatusRefreshed).toHaveBeenCalledWith(refreshed);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/stage');
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': 'secret',
    });
    expect(JSON.parse(init.body as string)).toEqual({
      path: 'C:\\workspace',
      changePath: 'docs/a.md',
    });
  });

  it('unstages a staged change through the same contract', async () => {
    const onStatusRefreshed = vi.fn();
    const fetchMock = stubFetch(async () => jsonResponse(200, succeededPayload()));

    render(
      <ChangeActions
        path={'C:\\workspace'}
        change={change({
          indexChange: 'Modified',
          workingTreeChange: 'Unmodified',
          statusCharacters: 'M.',
        })}
        onStatusRefreshed={onStatusRefreshed}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Unstage' }));

    await waitFor(() => expect(onStatusRefreshed).toHaveBeenCalledTimes(1));

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5000/api/source-control/unstage');
    expect(JSON.parse(init.body as string)).toEqual({
      path: 'C:\\workspace',
      changePath: 'docs/a.md',
    });
  });

  it('renders whole-worktree actions when no change is selected', async () => {
    const onStatusRefreshed = vi.fn();
    const fetchMock = stubFetch(async () => jsonResponse(200, succeededPayload()));

    render(
      <ChangeActions
        path={'C:\\workspace'}
        change={null}
        onStatusRefreshed={onStatusRefreshed}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Stage All' }));
    await waitFor(() => expect(onStatusRefreshed).toHaveBeenCalledTimes(1));

    const [stageAllUrl, stageAllInit] = fetchMock.mock.calls[0];
    expect(stageAllUrl).toBe('http://localhost:5000/api/source-control/stage-all');
    expect(JSON.parse(stageAllInit.body as string)).toEqual({ path: 'C:\\workspace' });

    fireEvent.click(screen.getByRole('button', { name: 'Unstage All' }));
    await waitFor(() => expect(onStatusRefreshed).toHaveBeenCalledTimes(2));

    const [unstageAllUrl, unstageAllInit] = fetchMock.mock.calls[1];
    expect(unstageAllUrl).toBe('http://localhost:5000/api/source-control/unstage-all');
    expect(JSON.parse(unstageAllInit.body as string)).toEqual({ path: 'C:\\workspace' });
  });

  it('disables every action while a request is in flight', async () => {
    let resolveFetch: (value: Response) => void = () => undefined;
    stubFetch(
      () =>
        new Promise<Response>((resolve) => {
          resolveFetch = resolve;
        })
    );

    render(<ChangeActions path={'C:\\workspace'} change={change()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled());
    expect(screen.getByRole('button', { name: 'Unstage' })).toBeDisabled();

    resolveFetch(jsonResponse(200, succeededPayload()));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled());
  });

  it('surfaces typed Git failures without reporting a refreshed status', async () => {
    const onStatusRefreshed = vi.fn();
    stubFetch(async () =>
      jsonResponse(200, {
        outcome: 'Failed',
        isSuccess: false,
        errorCode: null,
        message: 'The Git stage operation failed.',
        status: null,
        failure: {
          status: 'Failed',
          errorKind: 'GitLockContention',
          errorMessage: 'Another Git process seems to be running in this repository.',
          exitCode: 128,
        },
      })
    );

    render(
      <ChangeActions
        path={'C:\\workspace'}
        change={change()}
        onStatusRefreshed={onStatusRefreshed}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Stage' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('GitLockContention');
    expect(onStatusRefreshed).not.toHaveBeenCalled();
  });

  it('surfaces a rejected path (400) as a typed error', async () => {
    stubFetch(async () =>
      jsonResponse(400, {
        errorCode: 'PathNotNormalized',
        error: "The path '../outside.md' is not a normalized worktree-relative file path.",
      })
    );

    render(<ChangeActions path={'C:\\workspace'} change={change({ path: '../outside.md' })} />);

    fireEvent.click(screen.getByRole('button', { name: 'Stage' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('PathNotNormalized');
  });

  it('disables all actions when Git is not available', () => {
    render(<ChangeActions path={'C:\\workspace'} change={change()} gitUnavailable />);

    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Unstage' })).toBeDisabled();
  });

  it('offers stage only for entries with working-tree work and unstage only for stageable index entries', () => {
    const { rerender } = render(<ChangeActions path={'C:\\workspace'} change={change()} />);
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Unstage' })).toBeDisabled();

    rerender(
      <ChangeActions
        path={'C:\\workspace'}
        change={change({
          indexChange: 'Modified',
          workingTreeChange: 'Unmodified',
          statusCharacters: 'M.',
        })}
      />
    );
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Unstage' })).toBeEnabled();

    rerender(
      <ChangeActions
        path={'C:\\workspace'}
        change={change({ state: 'Untracked', statusCharacters: '??' })}
      />
    );
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();

    // Unmerged (conflicted) entries carry conflict-resolution semantics that
    // are outside this sprint; both actions stay disabled.
    rerender(
      <ChangeActions
        path={'C:\\workspace'}
        change={change({
          state: 'Conflicted',
          indexChange: 'Unmerged',
          workingTreeChange: 'Unmerged',
          conflictKind: 'BothModified',
          statusCharacters: 'UU',
        })}
      />
    );
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Unstage' })).toBeDisabled();
  });
});
