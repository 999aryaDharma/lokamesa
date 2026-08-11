import { describe, it, expect, vi, beforeEach } from 'vitest';
import { openInWord } from './OpenInWordAction';

describe('OpenInWordAction Challenger Stress Tests', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    (window as any).__DOCUMENTIDE_API_URL__ = 'http://localhost:5000';
    (window as any).__DOCUMENTIDE_SESSION_TOKEN__ = 'challenger-token-999';
  });

  it('handles empty or whitespace documentId gracefully', async () => {
    const resultEmpty = await openInWord('');
    expect(resultEmpty.success).toBe(false);
    expect(resultEmpty.status).toBe('DocumentNotFound');
    expect(resultEmpty.errorMessage).toBe('Document ID is required.');
  });

  it('handles fetch network exception cleanly', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('Network offline or connection refused'));

    const result = await openInWord('doc-net-err');

    expect(result.success).toBe(false);
    expect(result.status).toBe('ComError');
    expect(result.errorMessage).toBe('Network offline or connection refused');
    expect(result.documentId).toBe('doc-net-err');
  });

  it('handles 503 response with unparseable non-JSON body', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => {
        throw new SyntaxError('Unexpected token < in JSON at position 0');
      },
    } as unknown as Response);

    const result = await openInWord('doc-503-html');

    expect(result.success).toBe(false);
    expect(result.status).toBe('WordNotInstalled');
    expect(result.errorMessage).toBe('Microsoft Word is required for this operation.');
    expect(result.documentId).toBe('doc-503-html');
  });
});
