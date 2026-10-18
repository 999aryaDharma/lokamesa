import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import GitHistoryScreen from './GitHistoryScreen';

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
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

describe('GitHistoryScreen (UI-03 reachability)', () => {
  it('renders the real Git history panel and reads history from the privileged route', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({
        status: 'Available',
        source: 'Git',
        errorCode: null,
        message: null,
        isAvailable: true,
        page: {
          commits: [
            {
              commitId: 'abcdef0123456789abcdef0123456789abcdef01',
              shortId: 'abcdef0',
              authorName: 'Owner',
              authorEmail: 'owner@example.com',
              committerTime: '2026-10-18T10:00:00Z',
              summary: 'commit subject',
              fileCount: 2,
            },
          ],
          skip: 0,
          limit: 50,
          hasMore: false,
          nextSkip: null,
        },
        failure: null,
      }),
    );

    render(<GitHistoryScreen path="C:\\docs" inspectorOpen onAttachFolder={() => {}} />);

    expect(screen.getByTestId('git-history-screen')).toBeInTheDocument();
    expect(screen.getByTestId('git-history-panel')).toBeInTheDocument();
    expect(await screen.findByText('commit subject')).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([input]) => String(input).includes('/api/source-control/history')),
    ).toBe(true);
    expect(screen.getByText(/Source: Git/)).toBeInTheDocument();
  });

  it('does not invent a repository when no folder source is attached', () => {
    vi.spyOn(globalThis, 'fetch');
    const onAttachFolder = vi.fn();
    render(<GitHistoryScreen path={null} inspectorOpen onAttachFolder={onAttachFolder} />);

    expect(screen.getByTestId('git-history-no-path')).toBeInTheDocument();
    expect(screen.queryByTestId('git-history-panel')).not.toBeInTheDocument();
  });
});
