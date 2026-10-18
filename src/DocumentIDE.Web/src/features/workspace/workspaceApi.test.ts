import { describe, expect, it, vi, afterEach } from 'vitest';
import { attachFolderSource, fetchFolderSources, fetchWorkspaceDocuments } from './workspaceApi';

afterEach(() => {
  vi.restoreAllMocks();
  delete (window as unknown as { __DOCUMENTIDE_API_URL__?: string }).__DOCUMENTIDE_API_URL__;
  delete (window as unknown as { __DOCUMENTIDE_SESSION_TOKEN__?: string }).__DOCUMENTIDE_SESSION_TOKEN__;
});

describe('workspaceApi', () => {
  it('loads cached workspace documents from the privileged route', async () => {
    (window as unknown as { __DOCUMENTIDE_API_URL__?: string }).__DOCUMENTIDE_API_URL__ = 'http://localhost:5000';
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => [{ id: 'd-1', filename: 'a.md' }],
    } as Response);

    const docs = await fetchWorkspaceDocuments('ws-1');

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5000/api/workspaces/ws-1/documents',
      expect.anything(),
    );
    expect(docs[0].id).toBe('d-1');
  });

  it('loads attached folder sources', async () => {
    (window as unknown as { __DOCUMENTIDE_API_URL__?: string }).__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311';
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => [{ id: 'fs-1', canonicalPath: 'C:/Docs' }],
    } as Response);

    const sources = await fetchFolderSources('ws-1');

    expect(fetchMock).toHaveBeenCalledWith(
      'http://127.0.0.1:5311/api/workspaces/ws-1/folder-sources',
      expect.anything(),
    );
    expect(sources[0].id).toBe('fs-1');
  });

  it('attaches a folder through the injected base URL and session token', async () => {
    (window as unknown as { __DOCUMENTIDE_API_URL__?: string }).__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311';
    (window as unknown as { __DOCUMENTIDE_SESSION_TOKEN__?: string }).__DOCUMENTIDE_SESSION_TOKEN__ = 'session-abc';
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({
        folderSource: { id: 'fs-2', canonicalPath: 'C:/Docs' },
        supportedFileCount: 7,
      }),
    } as Response);

    const result = await attachFolderSource('ws-1', 'C:/Docs');

    expect(fetchMock).toHaveBeenCalledWith(
      'http://127.0.0.1:5311/api/workspaces/ws-1/folder-sources',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'X-DocumentIDE-Session-Token': 'session-abc' }),
        body: JSON.stringify({ folderPath: 'C:/Docs' }),
      }),
    );
    expect(result.supportedFileCount).toBe(7);
  });

  it('surfaces the server error message when the attach is refused', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ error: "The folder 'D:\\nonexistent' does not exist." }),
    } as Response);

    await expect(attachFolderSource('ws-1', 'D:\\nonexistent')).rejects.toThrow(
      /does not exist/,
    );
  });

  it('rejects on a non-OK response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({ ok: false, status: 500 } as Response);
    await expect(fetchWorkspaceDocuments('ws-1')).rejects.toThrow(/500/);
  });
});
