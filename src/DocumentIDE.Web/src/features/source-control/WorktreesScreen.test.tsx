import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WorktreesScreen from './WorktreesScreen';

const worktrees = [
  {
    path: 'C:\\docs',
    headCommit: 'abc1234',
    branchRef: 'refs/heads/main',
    branchName: 'main',
    isDetached: false,
    isBare: false,
    isLocked: false,
    isMainWorktree: true,
  },
  {
    path: 'C:\\docs-wt-feature',
    headCommit: 'def5678',
    branchRef: 'refs/heads/feature/x',
    branchName: 'feature/x',
    isDetached: false,
    isBare: false,
    isLocked: false,
    isMainWorktree: false,
  },
];

function listResponse() {
  return {
    status: 'Succeeded',
    isSuccess: true,
    worktrees,
    errorCode: null,
    message: null,
    failure: null,
  };
}

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

function installFetch(openOutcome: 'Opened' | 'RequiresAttachmentFlow' = 'RequiresAttachmentFlow') {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = String(input);
    const method = init?.method ?? 'GET';
    if (url.includes('/api/source-control/worktrees/prune')) {
      return jsonResponse({
        status: 'Succeeded',
        isSuccess: true,
        errorCode: null,
        message: null,
        worktrees,
        failure: null,
      });
    }
    if (url.includes('/api/source-control/worktrees/open')) {
      return jsonResponse({
        status: openOutcome,
        isSuccess: openOutcome === 'Opened',
        isAttachedToWorkspace: openOutcome === 'Opened',
        errorCode: openOutcome === 'Opened' ? null : null,
        message:
          openOutcome === 'RequiresAttachmentFlow'
            ? 'This worktree is registered in Git but is not attached to the workspace.'
            : null,
        worktree: worktrees[1],
        worktrees,
        failure: null,
      });
    }
    if (url.endsWith('/api/source-control/worktrees') && method === 'POST') {
      return jsonResponse({
        status: 'Succeeded',
        isSuccess: true,
        errorCode: null,
        message: null,
        worktree: worktrees[1],
        worktrees,
        failure: null,
      });
    }
    if (url.endsWith('/api/source-control/worktrees') && method === 'DELETE') {
      return jsonResponse({
        status: 'Succeeded',
        isSuccess: true,
        errorCode: null,
        message: null,
        worktree: worktrees[1],
        changes: [],
        worktrees,
        failure: null,
      });
    }
    if (url.includes('/api/source-control/worktrees')) {
      return jsonResponse(listResponse());
    }
    if (url.includes('/api/source-control/remote')) {
      return jsonResponse({
        status: 'Available',
        hasRepository: true,
        worktreeRoot: 'C:\\docs',
        branchName: 'main',
        isDetached: false,
        headCommit: 'abc1234',
        remoteName: 'origin',
        upstreamReference: 'origin/main',
        hasUpstream: true,
        ahead: 2,
        behind: 1,
        failure: null,
      });
    }
    return jsonResponse({}, false, 404);
  });
}

beforeEach(() => {
  Object.assign(window, {
    __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
    __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('WorktreesScreen (design 07)', () => {
  it('lists the real worktrees returned by GET worktrees', async () => {
    installFetch();
    render(
      <WorktreesScreen
        path={'C:\\docs'}
        workspaceId="ws"
        inspectorOpen
        onRequestAttachment={() => {}}
      />,
    );

    expect(await screen.findAllByText('C:\\docs')).not.toHaveLength(0);
    expect(screen.getByText('C:\\docs-wt-feature')).toBeInTheDocument();
    expect(screen.getByText('feature/x')).toBeInTheDocument();
    expect(screen.getAllByText('Main worktree').length).toBeGreaterThan(0);
  });

  it('runs Prune as the safe metadata refresh route', async () => {
    const fetchMock = installFetch();
    render(
      <WorktreesScreen path={'C:\\docs'} workspaceId="ws" inspectorOpen onRequestAttachment={() => {}} />,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Prune' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/source-control/worktrees/prune')),
      ).toBe(true),
    );
    const call = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/source-control/worktrees/prune'),
    )!;
    expect(call[1]?.method).toBe('POST');
    expect(JSON.parse(String(call[1]?.body))).toEqual({ path: 'C:\\docs' });
  });

  it('surfaces RequiresAttachmentFlow as a typed action and never auto-attaches', async () => {
    const fetchMock = installFetch('RequiresAttachmentFlow');
    const onRequestAttachment = vi.fn();
    render(
      <WorktreesScreen
        path={'C:\\docs'}
        workspaceId="ws"
        inspectorOpen
        onRequestAttachment={onRequestAttachment}
      />,
    );

    fireEvent.click(
      await screen.findByRole('button', { name: 'Open worktree C:\\docs-wt-feature' }),
    );

    const action = await screen.findByTestId('worktree-attachment-required');
    expect(action).toHaveTextContent('not attached');
    expect(
      fetchMock.mock.calls.some(
        ([input, init]) =>
          String(input).includes('/folder-sources') && (init?.method ?? 'GET') === 'POST',
      ),
    ).toBe(false);

    fireEvent.click(within(action).getByRole('button', { name: 'Attach folder source' }));
    expect(onRequestAttachment).toHaveBeenCalledWith('C:\\docs-wt-feature');
  });

  it('creates a worktree from an existing ref through POST worktrees', async () => {
    const fetchMock = installFetch();
    render(
      <WorktreesScreen path={'C:\\docs'} workspaceId="ws" inspectorOpen onRequestAttachment={() => {}} />,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'New worktree' }));
    const dialog = screen.getByRole('dialog', { name: 'Create worktree' });
    fireEvent.change(within(dialog).getByLabelText('Target path'), {
      target: { value: 'C:\\docs-wt-payments' },
    });
    fireEvent.change(within(dialog).getByLabelText('Existing ref (branch, tag, or commit)'), {
      target: { value: 'main' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create worktree' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([input, init]) =>
            String(input).endsWith('/api/source-control/worktrees') && init?.method === 'POST',
        ),
      ).toBe(true),
    );
    const call = fetchMock.mock.calls.find(
      ([input, init]) =>
        String(input).endsWith('/api/source-control/worktrees') && init?.method === 'POST',
    )!;
    expect(JSON.parse(String(call[1]?.body))).toEqual({
      workingDirectory: 'C:\\docs',
      targetPath: 'C:\\docs-wt-payments',
      ref: 'main',
    });
  });

  it('removes a clean worktree only after an explicit confirmation', async () => {
    const fetchMock = installFetch();
    render(
      <WorktreesScreen path={'C:\\docs'} workspaceId="ws" inspectorOpen onRequestAttachment={() => {}} />,
    );

    fireEvent.click(
      await screen.findByRole('button', { name: 'Remove worktree C:\\docs-wt-feature' }),
    );
    expect(screen.getByRole('dialog', { name: 'Remove worktree' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Confirm remove' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([input, init]) =>
            String(input).endsWith('/api/source-control/worktrees') && init?.method === 'DELETE',
        ),
      ).toBe(true),
    );
    const call = fetchMock.mock.calls.find(
      ([input, init]) =>
        String(input).endsWith('/api/source-control/worktrees') && init?.method === 'DELETE',
    )!;
    expect(JSON.parse(String(call[1]?.body))).toEqual({
      workingDirectory: 'C:\\docs',
      worktreePath: 'C:\\docs-wt-feature',
    });
  });

  it('renders the accurate worktree rules card', async () => {
    installFetch();
    render(
      <WorktreesScreen path={'C:\\docs'} workspaceId="ws" inspectorOpen onRequestAttachment={() => {}} />,
    );

    const rules = await screen.findByTestId('worktree-rules');
    expect(rules).toHaveTextContent('Main worktree');
    expect(rules).toHaveTextContent('Not removable');
    expect(rules).toHaveTextContent('Clean required');
    expect(rules).toHaveTextContent('Existing ref');
    expect(rules).toHaveTextContent('Never moved');
  });
});
