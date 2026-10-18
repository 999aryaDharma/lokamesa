import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ContextPackRequestError,
  fetchContextPack,
  toContextSources,
  type ContextPackApiSuccess,
} from './contextPackApi';

const pack: ContextPackApiSuccess = {
  status: 'Ok',
  documents: [
    {
      documentId: 'md-1',
      title: 'notes.md',
      textSource: 'markdown-source',
      contentLabel: 'Markdown source',
      content: '# Notes\nreal markdown source',
      characterCount: 29,
      byteCount: 29,
    },
    {
      documentId: 'docx-1',
      title: 'Report.docx',
      textSource: 'extracted-docx-text',
      contentLabel: 'Extracted DOCX text',
      content:
        '> Extracted DOCX text (from the document index, not the original .docx bytes): Report.docx\n\nREAL-INDEXED-BODY',
      characterCount: 110,
      byteCount: 110,
    },
  ],
  markdown:
    '# Notes\nreal markdown source\n\n> Extracted DOCX text (from the document index, not the original .docx bytes): Report.docx\n\nREAL-INDEXED-BODY',
  tokenEstimate: {
    modelLabel: 'Generic Tokenizer (4 chars/token)',
    totalEstimatedTokens: 35,
    totalCharacterCount: 139,
    tokenBudget: null,
    exceedsBudget: false,
    warningMessage: null,
    sourceContributions: [],
  },
};

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

beforeEach(() => {
  vi.restoreAllMocks();
  (window as any).__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311';
  (window as any).__DOCUMENTIDE_SESSION_TOKEN__ = 'session-token-abc';
});

describe('fetchContextPack', () => {
  it('POSTs the explicit ordered ids to the loopback API with the session token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(pack));

    const result = await fetchContextPack(['md-1', 'docx-1'], { fetchImpl: fetchMock });

    expect(result).toBe(pack);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://127.0.0.1:5311/api/context/pack');
    expect(init?.method).toBe('POST');
    expect(((init?.headers ?? {}) as Record<string, string>)['X-DocumentIDE-Session-Token']).toBe(
      'session-token-abc',
    );
    expect(JSON.parse(String(init?.body))).toEqual({
      documentIds: ['md-1', 'docx-1'],
      tokenBudget: null,
    });
  });

  it('passes an explicit token budget through to the route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(pack));

    await fetchContextPack(['md-1'], { tokenBudget: 64, fetchImpl: fetchMock });

    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({
      documentIds: ['md-1'],
      tokenBudget: 64,
    });
  });

  it('surfaces a typed refusal instead of a partial pack', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(
        {
          status: 'NoTextAvailable',
          errorCode: 'NoTextAvailable',
          documentId: 'docx-empty',
          detail: 'no-indexed-text',
        },
        false,
        409,
      ),
    );

    await expect(
      fetchContextPack(['docx-empty'], { fetchImpl: fetchMock }),
    ).rejects.toMatchObject({
      name: 'ContextPackRequestError',
      status: 409,
      errorCode: 'NoTextAvailable',
      documentId: 'docx-empty',
      detail: 'no-indexed-text',
    } satisfies Partial<ContextPackRequestError>);
  });
});

describe('toContextSources', () => {
  it('preserves the route order and content verbatim, including the DOCX label', () => {
    const sources = toContextSources(pack);

    expect(sources.map((source) => source.id)).toEqual(['md-1', 'docx-1']);
    expect(sources[0].content).toBe(pack.documents[0].content);
    expect(sources[1].content).toContain('Extracted DOCX text');
    expect(sources[1].content).toContain('REAL-INDEXED-BODY');
  });
});
