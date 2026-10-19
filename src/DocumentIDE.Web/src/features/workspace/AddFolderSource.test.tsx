import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import AddFolderSource from './AddFolderSource';

describe('AddFolderSource Component', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    // The packaged app injects these globals; without them every request would
    // resolve against the WebView asset origin and fail.
    (window as unknown as { __DOCUMENTIDE_API_URL__?: string }).__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311';
    (window as unknown as { __DOCUMENTIDE_SESSION_TOKEN__?: string }).__DOCUMENTIDE_SESSION_TOKEN__ = 'session-abc';
    // Default fetch mock returning empty list for useEffect mount
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url) => {
      return {
        ok: true,
        json: async () => [],
      } as Response;
    });
  });

  it('renders input field and attach button', async () => {
    render(<AddFolderSource workspaceId="test-ws" />);
    
    expect(await screen.findByPlaceholderText(/enter local folder path/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /attach folder/i })).toBeInTheDocument();
  });

  it('shows error when submitting empty path', async () => {
    render(<AddFolderSource workspaceId="test-ws" />);

    const attachButton = screen.getByRole('button', { name: /attach folder/i });
    fireEvent.click(attachButton);

    expect(await screen.findByText(/please enter a folder path/i)).toBeInTheDocument();
  });

  it('calls API and displays attached folder source card on success', async () => {
    const mockFolderSource = {
      id: 'fs-123',
      workspaceId: 'test-ws',
      canonicalPath: 'C:\\Users\\User\\Documents',
      displayName: 'Documents',
      availabilityStatus: 'available',
    };

    const mockResponse = {
      folderSource: mockFolderSource,
      supportedFileCount: 5,
      displayName: 'Documents',
    };

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url, init) => {
      if (init?.method === 'POST') {
        return {
          ok: true,
          json: async () => mockResponse,
        } as Response;
      }
      return {
        ok: true,
        json: async () => [],
      } as Response;
    });

    const fetchMock = globalThis.fetch as unknown as ReturnType<typeof vi.fn>;

    render(<AddFolderSource workspaceId="test-ws" />);

    const input = screen.getByPlaceholderText(/enter local folder path/i);
    fireEvent.change(input, { target: { value: 'C:\\Users\\User\\Documents' } });

    const attachButton = screen.getByRole('button', { name: /attach folder/i });
    fireEvent.click(attachButton);

    await waitFor(() => {
      expect(screen.getByText('Documents')).toBeInTheDocument();
      expect(screen.getByText('C:\\Users\\User\\Documents')).toBeInTheDocument();
      expect(screen.getByText(/5 supported files/i)).toBeInTheDocument();
      expect(screen.getByText('available')).toBeInTheDocument();
    });

    const postCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST');
    expect(postCall?.[0]).toBe('http://127.0.0.1:5311/api/workspaces/test-ws/folder-sources');
    expect(
      (postCall?.[1]?.headers as Record<string, string>)['X-DocumentIDE-Session-Token'],
    ).toBe('session-abc');
  });

  it('reconciles the workspace after a successful attach so the folder is indexed', async () => {
    const mockResponse = {
      folderSource: {
        id: 'fs-123',
        workspaceId: 'test-ws',
        canonicalPath: 'C:\\Users\\User\\Documents',
        displayName: 'Documents',
        availabilityStatus: 'available',
      },
      supportedFileCount: 5,
    };
    const calls: Array<{ url: string; method: string }> = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
      calls.push({ url: String(url), method: init?.method ?? 'GET' });
      if (String(url).endsWith('/folder-sources') && init?.method === 'POST') {
        return { ok: true, json: async () => mockResponse } as Response;
      }
      if (String(url).endsWith('/reconcile')) {
        return { ok: true, json: async () => ({ workspaceId: 'test-ws' }) } as Response;
      }
      return { ok: true, json: async () => [] } as Response;
    });

    render(<AddFolderSource workspaceId="test-ws" />);

    fireEvent.change(screen.getByPlaceholderText(/enter local folder path/i), {
      target: { value: 'C:\\Users\\User\\Documents' },
    });
    fireEvent.click(screen.getByRole('button', { name: /attach folder/i }));

    await waitFor(() => {
      expect(
        calls.some((c) => c.url === 'http://127.0.0.1:5311/api/workspaces/test-ws/reconcile'),
      ).toBe(true);
    });

    // Reconcile must run after the attach POST (documents are only searchable
    // once the workspace has been reconciled).
    const attachIndex = calls.findIndex(
      (c) => c.url.endsWith('/folder-sources') && c.method === 'POST',
    );
    const reconcileIndex = calls.findIndex((c) => c.url.endsWith('/reconcile'));
    expect(attachIndex).toBeGreaterThanOrEqual(0);
    expect(reconcileIndex).toBeGreaterThan(attachIndex);
  });

  it('displays error message when API call fails', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url, init) => {
      if (init?.method === 'POST') {
        return {
          ok: false,
          json: async () => ({ error: "The folder 'D:\\nonexistent' does not exist." }),
        } as Response;
      }
      return {
        ok: true,
        json: async () => [],
      } as Response;
    });

    render(<AddFolderSource workspaceId="test-ws" />);

    const input = screen.getByPlaceholderText(/enter local folder path/i);
    fireEvent.change(input, { target: { value: 'D:\\nonexistent' } });

    const attachButton = screen.getByRole('button', { name: /attach folder/i });
    fireEvent.click(attachButton);

    await waitFor(() => {
      expect(screen.getByText(/the folder 'D:\\nonexistent' does not exist/i)).toBeInTheDocument();
    });
  });
});

