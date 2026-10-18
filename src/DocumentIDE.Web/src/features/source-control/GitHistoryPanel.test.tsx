import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import GitHistoryPanel from './GitHistoryPanel';
import type {
  SourceControlFileVersionResult,
  SourceControlHistoryCommit,
  SourceControlHistoryResult,
} from './sourceControlModels';

/**
 * SCM-08 — Git commit history and file history pane.
 *
 * The panel reads history from the privileged API (never from Document IDE
 * history), is visibly marked as Git data, loads large histories incrementally,
 * scopes file history to one selected document, inspects a document version at
 * a selected commit and keeps every typed failure visible instead of rendering
 * an empty list (spec 13 §4.3/§9.4/§12/§20/§25).
 */

function commit(overrides: Partial<SourceControlHistoryCommit> = {}): SourceControlHistoryCommit {
  return {
    commitId: '1111111111111111111111111111111111111111',
    shortId: '1111111',
    authorName: 'Ada Lovelace',
    authorEmail: 'ada@example.invalid',
    committerTime: '2026-01-02T03:04:05+00:00',
    summary: 'first commit',
    fileCount: 2,
    ...overrides,
  };
}

function historyResult(overrides: Partial<SourceControlHistoryResult> = {}): SourceControlHistoryResult {
  return {
    status: 'Available',
    source: 'Git',
    errorCode: null,
    message: null,
    isAvailable: true,
    page: {
      commits: [commit()],
      skip: 0,
      limit: 50,
      hasMore: false,
      nextSkip: null,
    },
    failure: null,
    ...overrides,
  };
}

function fileVersionResult(
  overrides: Partial<SourceControlFileVersionResult> = {}
): SourceControlFileVersionResult {
  return {
    status: 'Available',
    source: 'Git',
    errorCode: null,
    message: null,
    isAvailable: true,
    version: {
      commitId: '1111111111111111111111111111111111111111',
      path: 'docs/a.md',
      content: 'a v2',
      isBinary: false,
      isTruncated: false,
      sizeBytes: 4,
    },
    failure: null,
    ...overrides,
  };
}

function jsonResponse(status: number, data: unknown): Response {
  return { ok: status < 400, status, json: async () => data } as Response;
}

type Handler = (url: string, init: RequestInit) => Response | Promise<Response>;

function stubFetch(handler: Handler) {
  const fetchMock = vi.fn(handler);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('GitHistoryPanel', () => {
  beforeEach(() => {
    Object.assign(window, {
      __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
      __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
    });
  });

  it('renders the commit history the API reports and marks it as Git data', async () => {
    const fetchMock = stubFetch(async () =>
      jsonResponse(
        200,
        historyResult({
          page: {
            commits: [
              commit({
                commitId: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                shortId: 'aaaaaaa',
                summary: 'third commit',
                fileCount: 2,
              }),
              commit({
                commitId: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
                shortId: 'bbbbbbb',
                summary: 'first commit',
                fileCount: 1,
              }),
            ],
            skip: 0,
            limit: 50,
            hasMore: false,
            nextSkip: null,
          },
        })
      )
    );

    render(<GitHistoryPanel path={'C:\\workspace'} />);

    expect(await screen.findByTestId('git-history-heading')).toHaveTextContent('Git History');
    expect(await screen.findByTestId('git-history-source-note')).toHaveTextContent('Git');

    const rows = await screen.findAllByTestId('git-history-commit');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('aaaaaaa');
    expect(rows[0]).toHaveTextContent('third commit');
    expect(rows[0]).toHaveTextContent('Ada Lovelace');
    expect(rows[0]).toHaveTextContent('2 files');

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain('/api/source-control/history?path=');
    expect(url).toContain('limit=50');
    expect(init.headers).toMatchObject({ 'X-DocumentIDE-Session-Token': 'secret' });
  });

  it('loads a large history incrementally and hides the control when exhausted', async () => {
    const fetchMock = stubFetch(async (url) => {
      if (url.includes('skip=2')) {
        return jsonResponse(
          200,
          historyResult({
            page: {
              commits: [
                commit({
                  commitId: 'cccccccccccccccccccccccccccccccccccccccc',
                  shortId: 'ccccccc',
                  summary: 'commit 03',
                }),
              ],
              skip: 2,
              limit: 2,
              hasMore: false,
              nextSkip: null,
            },
          })
        );
      }

      return jsonResponse(
        200,
        historyResult({
          page: {
            commits: [
              commit({
                commitId: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                shortId: 'aaaaaaa',
                summary: 'commit 05',
              }),
              commit({
                commitId: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
                shortId: 'bbbbbbb',
                summary: 'commit 04',
              }),
            ],
            skip: 0,
            limit: 2,
            hasMore: true,
            nextSkip: 2,
          },
        })
      );
    });

    render(<GitHistoryPanel path={'C:\\workspace'} />);

    await waitFor(() => expect(screen.getAllByTestId('git-history-commit')).toHaveLength(2));

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));

    await waitFor(() => expect(screen.getAllByTestId('git-history-commit')).toHaveLength(3));

    // The next request continues from the window Git reported, and the pages
    // append without overlap.
    const secondUrl = fetchMock.mock.calls[1][0];
    expect(secondUrl).toContain('skip=2');
    expect(secondUrl).toContain('limit=2');

    const rows = screen.getAllByTestId('git-history-commit');
    expect(rows.map((row) => row.textContent)).toEqual([
      expect.stringContaining('commit 05'),
      expect.stringContaining('commit 04'),
      expect.stringContaining('commit 03'),
    ]);

    expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull();
  });

  it('only offers the file-history scope once a document is selected', async () => {
    const fetchMock = stubFetch(async (url) =>
      jsonResponse(
        200,
        historyResult(
          url.includes('file-history')
            ? {
                page: {
                  commits: [commit({ shortId: 'ddddddd', summary: 'third: adds a unicode file' })],
                  skip: 0,
                  limit: 50,
                  hasMore: false,
                  nextSkip: null,
                },
              }
            : {}
        )
      )
    );

    const { rerender } = render(<GitHistoryPanel path={'C:\\workspace'} />);

    const documentScope = await screen.findByRole('button', { name: 'This document' });
    expect(documentScope).toBeDisabled();

    rerender(<GitHistoryPanel path={'C:\\workspace'} selectedPath="docs/a.md" />);

    await waitFor(() => expect(screen.getByRole('button', { name: 'This document' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'This document' }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([url]) => url.includes('/api/source-control/file-history'))).toBe(
        true
      )
    );

    expect(await screen.findByTestId('git-history-scope-note')).toHaveTextContent('docs/a.md');
    // A rename is not followed (`--follow` is deliberately omitted), so the
    // pane must disclose that history before a move may be missing.
    expect(screen.getByTestId('git-history-scope-note')).toHaveTextContent(
      /renames are not followed/i
    );
    const fileHistoryUrl = fetchMock.mock.calls.find(([url]) =>
      url.includes('/api/source-control/file-history')
    )?.[0];
    expect(fileHistoryUrl).toContain('file=docs%2Fa.md');
    expect(await screen.findAllByTestId('git-history-commit')).toHaveLength(1);
  });

  it('inspects the selected document version at the selected commit', async () => {
    const fetchMock = stubFetch(async (url) => {
      if (url.includes('/file-version')) {
        return jsonResponse(200, fileVersionResult());
      }

      return jsonResponse(
        200,
        historyResult({
          page: {
            commits: [commit({ shortId: 'eeeeeee', summary: 'second: edits a' })],
            skip: 0,
            limit: 50,
            hasMore: false,
            nextSkip: null,
          },
        })
      );
    });

    render(<GitHistoryPanel path={'C:\\workspace'} selectedPath="docs/a.md" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect version eeeeeee' }));

    const preview = await screen.findByTestId('git-history-version-preview');
    expect(preview).toHaveTextContent('docs/a.md');
    expect(preview).toHaveTextContent('a v2');

    const versionUrl = fetchMock.mock.calls.find(([url]) => url.includes('/file-version'))?.[0];
    expect(versionUrl).toContain('file=docs%2Fa.md');
    expect(versionUrl).toContain('commit=1111111111111111111111111111111111111111');
  });

  it('reports a binary or truncated version without pretending it is text', async () => {
    stubFetch(async (url) => {
      if (url.includes('/file-version')) {
        return jsonResponse(
          200,
          fileVersionResult({
            version: {
              commitId: '1111111111111111111111111111111111111111',
              path: 'docs/treaty.docx',
              content: null,
              isBinary: true,
              isTruncated: false,
              sizeBytes: 512,
            },
          })
        );
      }

      return jsonResponse(200, historyResult());
    });

    render(<GitHistoryPanel path={'C:\\workspace'} selectedPath="docs/treaty.docx" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect version 1111111' }));

    const preview = await screen.findByTestId('git-history-version-preview');
    expect(preview).toHaveTextContent('Binary document');
    expect(preview).toHaveTextContent('512 bytes');
    expect(preview).not.toHaveTextContent('a v2');
  });

  it('reports an over-the-bound version as too large to preview', async () => {
    stubFetch(async (url) => {
      if (url.includes('/file-version')) {
        return jsonResponse(
          200,
          fileVersionResult({
            version: {
              commitId: '1111111111111111111111111111111111111111',
              path: 'docs/large.md',
              content: null,
              isBinary: false,
              isTruncated: true,
              sizeBytes: 262208,
            },
          })
        );
      }

      return jsonResponse(200, historyResult());
    });

    render(<GitHistoryPanel path={'C:\\workspace'} selectedPath="docs/large.md" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Inspect version 1111111' }));

    const preview = await screen.findByTestId('git-history-version-preview');
    expect(preview).toHaveTextContent(/too large to preview/i);
    expect(preview).toHaveTextContent('262208 bytes');
    expect(preview.querySelector('pre')).toBeNull();
  });

  it('keeps typed failures visible instead of showing an empty history', async () => {
    stubFetch(async () =>
      jsonResponse(
        200,
        historyResult({
          status: 'NoRepository',
          isAvailable: false,
          page: null,
          message: 'The requested path is not inside any Git worktree.',
          failure: {
            status: 'Failed',
            errorKind: 'GitRepositoryNotFound',
            errorMessage: 'not a git repository',
            exitCode: 128,
          },
        })
      )
    );

    render(<GitHistoryPanel path={'C:\\workspace'} />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('NoRepository');
    expect(alert).toHaveTextContent('not a git repository');
    expect(screen.queryAllByTestId('git-history-commit')).toHaveLength(0);
  });

  it('surfaces a rejected request (HTTP 400) as a typed error', async () => {
    stubFetch(async () =>
      jsonResponse(400, {
        errorCode: 'InvalidLimit',
        error: 'The history page limit must be between 1 and 200.',
      })
    );

    render(<GitHistoryPanel path={'C:\\workspace'} />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('InvalidLimit');
  });

  it('does not query the API when Git is not available', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, historyResult()));

    render(<GitHistoryPanel path={'C:\\workspace'} gitUnavailable />);

    expect(await screen.findByTestId('git-history-source-note')).toBeInTheDocument();
    expect(screen.getByTestId('git-history-unavailable')).toHaveTextContent('Git');
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
