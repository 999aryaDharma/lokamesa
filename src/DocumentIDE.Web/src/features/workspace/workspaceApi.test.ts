import { describe, expect, it, vi, afterEach } from 'vitest';
import { fetchFolderSources, fetchWorkspaceDocuments } from './workspaceApi';

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
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => [{ id: 'fs-1', canonicalPath: 'C:/Docs' }],
    } as Response);

    const sources = await fetchFolderSources('ws-1');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/workspaces/ws-1/folder-sources',
      expect.anything(),
    );
    expect(sources[0].id).toBe('fs-1');
  });

  it('rejects on a non-OK response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({ ok: false, status: 500 } as Response);
    await expect(fetchWorkspaceDocuments('ws-1')).rejects.toThrow(/500/);
  });
});
