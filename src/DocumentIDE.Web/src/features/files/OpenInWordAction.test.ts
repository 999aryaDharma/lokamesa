import { describe, it, expect, vi, beforeEach } from 'vitest';
import { openInWord } from './OpenInWordAction';

describe('OpenInWordAction', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    (window as any).__DOCUMENTIDE_API_URL__ = 'http://localhost:5000';
    (window as any).__DOCUMENTIDE_SESSION_TOKEN__ = 'test-session-token-123';
  });

  it('calls POST /api/word/open with session token and documentId', async () => {
    const mockResponse = {
      success: true,
      status: 'Success',
      errorMessage: '',
      documentId: 'doc-100',
      filePath: 'C:\\docs\\contract.docx',
    };

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockResponse,
    } as Response);

    const result = await openInWord('doc-100');

    expect(fetchSpy).toHaveBeenCalledWith('http://localhost:5000/api/word/open', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-DocumentIDE-Session-Token': 'test-session-token-123',
      },
      body: JSON.stringify({ documentId: 'doc-100' }),
    });

    expect(result.success).toBe(true);
    expect(result.status).toBe('Success');
    expect(result.documentId).toBe('doc-100');
  });

  it('returns WordNotInstalled with typed error message when Word is missing (503)', async () => {
    const mock503Body = {
      success: false,
      status: 'WordNotInstalled',
      errorMessage: 'Microsoft Word is required for this operation.',
      documentId: 'doc-101',
    };

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => mock503Body,
    } as Response);

    const result = await openInWord('doc-101');

    expect(result.success).toBe(false);
    expect(result.status).toBe('WordNotInstalled');
    expect(result.errorMessage).toBe('Microsoft Word is required for this operation.');
  });

  it('returns DocumentNotFound when backend returns 404', async () => {
    const mock404Body = {
      success: false,
      status: 'DocumentNotFound',
      errorMessage: "Document with ID 'doc-missing' was not found.",
      documentId: 'doc-missing',
    };

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 404,
      json: async () => mock404Body,
    } as Response);

    const result = await openInWord('doc-missing');

    expect(result.success).toBe(false);
    expect(result.status).toBe('DocumentNotFound');
    expect(result.errorMessage).toContain('doc-missing');
  });
});
