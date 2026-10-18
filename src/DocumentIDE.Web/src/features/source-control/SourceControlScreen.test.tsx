import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import SourceControlScreen from './SourceControlScreen';
import type {
  SourceControlChangeEntry,
  SourceControlConflictModel,
  SourceControlRemoteModel,
  SourceControlStatusModel,
} from './sourceControlModels';

function entry(overrides: Partial<SourceControlChangeEntry> = {}): SourceControlChangeEntry {
  return {
    path: 'docs/notes.md',
    originalPath: null,
    state: 'Modified',
    indexChange: 'Unmodified',
    workingTreeChange: 'Modified',
    conflictKind: 'None',
    statusCharacters: ' M',
    absolutePath: 'C:\\docs\\docs\\notes.md',
    documentId: null,
    ...overrides,
  };
}

function statusPayload(overrides: Partial<SourceControlStatusModel> = {}): SourceControlStatusModel {
  return {
    status: 'Available',
    hasRepository: true,
    worktreeRoot: 'C:\\docs',
    branchName: 'main',
    isDetached: false,
    headCommit: 'abcdef0123456789abcdef0123456789abcdef01',
    upstreamReference: 'origin/main',
    hasUpstream: true,
    ahead: 2,
    behind: 1,
    sections: {
      staged: [
        entry({ path: 'docs/notes.md', statusCharacters: 'M ', indexChange: 'Modified', workingTreeChange: 'Unmodified', documentId: 'doc-md' }),
        entry({ path: 'docs/Report.docx', state: 'Added', statusCharacters: 'A ', indexChange: 'Added', workingTreeChange: 'Unmodified', documentId: 'docx-1' }),
      ],
      unstaged: [
        entry({ path: 'docs/other.md', statusCharacters: ' M', documentId: 'doc-other' }),
      ],
      untracked: [entry({ path: 'docs/draft.md', state: 'Untracked', statusCharacters: '??', indexChange: 'Unmodified', workingTreeChange: 'Unmodified' })],
      conflicts: [entry({ path: 'docs/conflict.md', state: 'Conflicted', statusCharacters: 'UU', indexChange: 'Unmerged', workingTreeChange: 'Unmerged', conflictKind: 'BothModified' })],
    },
    failure: null,
    ...overrides,
  };
}

function remotePayload(overrides: Partial<SourceControlRemoteModel> = {}): SourceControlRemoteModel {
  return {
    status: 'Available',
    hasRepository: true,
    worktreeRoot: 'C:\\docs',
    branchName: 'main',
    isDetached: false,
    headCommit: 'abcdef0123456789abcdef0123456789abcdef01',
    remoteName: 'origin',
    upstreamReference: 'origin/main',
    hasUpstream: true,
    ahead: 2,
    behind: 1,
    failure: null,
    ...overrides,
  };
}

function conflictPayload(): SourceControlConflictModel {
  return {
    status: 'Available',
    hasRepository: true,
    hasConflicts: true,
    worktreeRoot: 'C:\\docs',
    branchName: 'main',
    isDetached: false,
    headCommit: 'abcdef0123456789abcdef0123456789abcdef01',
    conflicts: [
      {
        path: 'docs/conflict.md',
        conflictKind: 'BothModified',
        statusCharacters: 'UU',
        isDocx: false,
        openAction: 'None',
        documentId: null,
      },
    ],
    blockedActions: [
      { action: 'Commit', errorCode: 'ConflictedWorktree', message: 'Unresolved conflicts block the commit.', affectedPaths: ['docs/conflict.md'] },
    ],
    message: null,
    failure: null,
  };
}

function worktreesPayload() {
  return {
    status: 'Succeeded',
    isSuccess: true,
    worktrees: [
      { path: 'C:\\docs', headCommit: 'abc', branchRef: 'refs/heads/main', branchName: 'main', isDetached: false, isBare: false, isLocked: false, isMainWorktree: true },
      { path: 'C:\\docs-wt-feature', headCommit: 'def', branchRef: 'refs/heads/feature/x', branchName: 'feature/x', isDetached: false, isBare: false, isLocked: false, isMainWorktree: false },
    ],
    errorCode: null,
    message: null,
    failure: null,
  };
}

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

interface FetchOptions {
  status?: SourceControlStatusModel | 'reject';
  remote?: SourceControlRemoteModel;
}

function installFetch(options: FetchOptions = {}) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = String(input);
    if (url.includes('/api/source-control/status')) {
      if (options.status === 'reject') {
        return jsonResponse({ errorCode: 'HostUnavailable', error: 'The local API is unreachable.' }, false, 503);
      }
      return jsonResponse(options.status ?? statusPayload());
    }
    if (url.includes('/api/source-control/remote')) {
      return jsonResponse(options.remote ?? remotePayload());
    }
    if (url.includes('/api/source-control/conflicts')) {
      return jsonResponse(conflictPayload());
    }
    if (url.includes('/api/source-control/worktrees')) {
      return jsonResponse(worktreesPayload());
    }
    if (url.includes('/api/source-control/file-version')) {
      const parsed = new URL(url, 'http://localhost');
      if (parsed.searchParams.get('file') === 'docs/notes.md') {
        return jsonResponse({
          status: 'Available',
          source: 'Git',
          errorCode: null,
          message: null,
          isAvailable: true,
          version: {
            commitId: 'HEAD',
            path: 'docs/notes.md',
            content: '# Notes\n\nold line\n',
            isBinary: false,
            isTruncated: false,
            sizeBytes: 18,
          },
          failure: null,
        });
      }
      return jsonResponse({
        status: 'NotFound',
        source: 'Git',
        errorCode: 'NotFound',
        message: 'Git reports no version of this path at that commit.',
        isAvailable: false,
        version: null,
        failure: null,
      });
    }
    if (url.endsWith('/api/documents/doc-md/markdown')) {
      return jsonResponse({ documentId: 'doc-md', source: '# Notes\n\nchanged line\n' });
    }
    if (url.endsWith('/api/documents/doc-other/markdown')) {
      return jsonResponse({ documentId: 'doc-other', source: 'other\n' });
    }
    if (url.includes('/api/source-control/commit-all')) {
      return jsonResponse({
        outcome: 'Succeeded',
        flow: 'CommitAll',
        isSuccess: true,
        errorCode: null,
        message: null,
        commit: { commitId: 'abc', shortId: 'abc', summary: 'all', branchName: 'main' },
        stagedPaths: ['docs/notes.md'],
        status: statusPayload(),
        failure: null,
      });
    }
    if (url.includes('/api/source-control/commit')) {
      return jsonResponse({
        outcome: 'Succeeded',
        flow: 'Commit',
        isSuccess: true,
        errorCode: null,
        message: null,
        commit: { commitId: 'abc', shortId: 'abc', summary: 'staged', branchName: 'main' },
        stagedPaths: null,
        status: statusPayload(),
        failure: null,
      });
    }
    if (url.includes('/api/source-control/history')) {
      return jsonResponse({
        status: 'Available',
        source: 'Git',
        errorCode: null,
        message: null,
        isAvailable: true,
        page: { commits: [], skip: 0, limit: 50, hasMore: false, nextSkip: null },
        failure: null,
      });
    }
    return jsonResponse({}, false, 404);
  });
}

const baseProps = {
  path: 'C:\\docs',
  inspectorOpen: true,
  onOpenWorktrees: () => {},
  onOpenDocxDiff: () => {},
  onOpenInWord: () => {},
  onOpenMarkdown: () => {},
  onAttachFolder: () => {},
};

beforeEach(() => {
  Object.assign(window, {
    __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
    __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('SourceControlScreen (design 05)', () => {
  it('renders real staged / changes / untracked / conflicts groups from GET status', async () => {
    installFetch();
    render(<SourceControlScreen {...baseProps} />);

    expect(await screen.findByTestId('source-control-section-staged')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-section-unstaged')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-section-untracked')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-section-conflicts')).toBeInTheDocument();

    expect(screen.getByTestId('source-control-entry-docs/notes.md')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-entry-docs/other.md')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-entry-docs/draft.md')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-entry-docs/conflict.md')).toBeInTheDocument();
  });

  it('shows the branch and ahead/behind counters from GET remote', async () => {
    installFetch();
    render(<SourceControlScreen {...baseProps} />);

    const context = await screen.findByTestId('scm-context');
    await waitFor(() => expect(context).toHaveTextContent('main'));
    expect(context).toHaveTextContent('2 ahead');
    expect(context).toHaveTextContent('1 behind');
  });

  it('renders a real text diff for a .md selection built from the read-only routes', async () => {
    const fetchMock = installFetch();
    render(<SourceControlScreen {...baseProps} />);

    fireEvent.click(await screen.findByTestId('source-control-entry-docs/notes.md'));

    expect(await screen.findByTestId('markdown-diff')).toBeInTheDocument();
    expect(screen.getByText('old line', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('changed line', { exact: false })).toBeInTheDocument();

    const urls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(urls.some((url) => url.includes('/api/source-control/file-version'))).toBe(true);
    expect(urls.some((url) => url.endsWith('/api/documents/doc-md/markdown'))).toBe(true);
  });

  it('never renders a text diff for a .docx selection, and routes to the document diff screen', async () => {
    const fetchMock = installFetch();
    const onOpenDocxDiff = vi.fn();
    render(<SourceControlScreen {...baseProps} onOpenDocxDiff={onOpenDocxDiff} />);

    fireEvent.click(await screen.findByTestId('source-control-entry-docs/Report.docx'));

    expect(screen.getByTestId('docx-change-summary')).toBeInTheDocument();
    expect(screen.queryByTestId('markdown-diff')).not.toBeInTheDocument();
    expect(screen.queryByTestId('docx-diff')).not.toBeInTheDocument();

    // The .docx path never asks Git for a text version or the Markdown source.
    const urls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(urls.some((url) => url.includes('/api/source-control/file-version'))).toBe(false);
    expect(urls.some((url) => url.includes('/api/documents/docx-1/markdown'))).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Open document diff' }));
    expect(onOpenDocxDiff).toHaveBeenCalledWith(
      expect.objectContaining({ path: 'docs/Report.docx' }),
      'C:\\docs',
    );
  });

  it('commits the staged state through POST commit', async () => {
    const fetchMock = installFetch();
    render(<SourceControlScreen {...baseProps} />);

    fireEvent.change(await screen.findByLabelText('Commit message'), { target: { value: 'staged work' } });
    fireEvent.click(screen.getByRole('button', { name: 'Commit staged' }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/source-control/commit'))).toBe(true),
    );
    const call = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/api/source-control/commit'))!;
    expect(JSON.parse(String(call[1]?.body))).toEqual({
      path: 'C:\\docs',
      message: 'staged work',
    });
  });

  it('asks for explicit confirmation before calling POST commit-all', async () => {
    const fetchMock = installFetch();
    render(<SourceControlScreen {...baseProps} />);

    fireEvent.change(await screen.findByLabelText('Commit message'), { target: { value: 'everything' } });
    fireEvent.click(screen.getByRole('button', { name: 'Commit all' }));

    expect(screen.getByTestId('commit-all-confirmation')).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/source-control/commit-all')),
    ).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Confirm commit all' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/source-control/commit-all')),
      ).toBe(true),
    );
    const call = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/source-control/commit-all'),
    )!;
    expect(JSON.parse(String(call[1]?.body))).toEqual({
      path: 'C:\\docs',
      message: 'everything',
    });
  });

  it('renders the real repository inspector including worktree counts', async () => {
    installFetch();
    const onOpenWorktrees = vi.fn();
    render(<SourceControlScreen {...baseProps} onOpenWorktrees={onOpenWorktrees} />);

    const inspector = await screen.findByLabelText('Source control inspector');
    await waitFor(() => expect(within(inspector).getByText('C:\\docs')).toBeInTheDocument());
    expect(within(inspector).getAllByText('main').length).toBeGreaterThan(0);
    expect(within(inspector).getAllByText('origin/main').length).toBeGreaterThan(0);
    expect(within(inspector).getByTestId('inspector-staged-count')).toHaveTextContent('2');
    expect(within(inspector).getByTestId('inspector-change-count')).toHaveTextContent('1');
    expect(within(inspector).getByTestId('inspector-worktree-count')).toHaveTextContent('2');

    fireEvent.click(within(inspector).getByRole('button', { name: 'Open worktrees' }));
    expect(onOpenWorktrees).toHaveBeenCalledTimes(1);
  });

  it('renders the ConflictPanel for a real unmerged state', async () => {
    installFetch();
    render(<SourceControlScreen {...baseProps} />);

    expect(await screen.findByTestId('conflict-panel')).toBeInTheDocument();
    expect(screen.getByTestId('conflict-entry-docs/conflict.md')).toBeInTheDocument();
  });

  it('renders the clean state without inventing rows', async () => {
    installFetch({
      status: statusPayload({
        sections: { staged: [], unstaged: [], untracked: [], conflicts: [] },
      }),
    });
    render(<SourceControlScreen {...baseProps} />);

    expect(await screen.findByTestId('source-control-clean')).toBeInTheDocument();
    expect(screen.queryByTestId('source-control-entry-docs/notes.md')).not.toBeInTheDocument();
  });

  it('surfaces a transport failure instead of showing a clean tree', async () => {
    installFetch({ status: 'reject' });
    render(<SourceControlScreen {...baseProps} />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('unreachable');
    expect(screen.queryByTestId('source-control-clean')).not.toBeInTheDocument();
  });

  it('offers the existing FS-01 attachment flow when no repository path exists', () => {
    installFetch();
    const onAttachFolder = vi.fn();
    render(<SourceControlScreen {...baseProps} path={null} onAttachFolder={onAttachFolder} />);

    fireEvent.click(screen.getByRole('button', { name: 'Attach folder source' }));
    expect(onAttachFolder).toHaveBeenCalledTimes(1);
  });
});
