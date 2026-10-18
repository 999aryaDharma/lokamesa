/**
 * UI-05 A — the context-pack overlay is reachable from the real canvas
 * selection and is copy-only: the dialog must render no export control when the
 * shell supplies no export callback.
 */
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { useCanvasStore } from './features/canvas/canvasStore';
import { useGraphStore } from './features/graph/graphStore';
import { useSearchStore } from './features/search/searchStore';
import { useViewStore } from './features/workspace/viewStore';

const reportDocument = {
  id: 'docx-1',
  filename: 'Report.docx',
  extension: '.docx',
  currentPath: 'C:/Docs/Report.docx',
  documentType: 'docx',
  availabilityStatus: 'available',
};

const packPayload = {
  status: 'Ok',
  documents: [
    {
      documentId: 'docx-1',
      title: 'Report.docx',
      textSource: 'extracted-docx-text',
      contentLabel: 'Extracted DOCX text',
      content:
        '> Extracted DOCX text (from the document index, not the original .docx bytes): Report.docx\n\nREAL-INDEXED-BODY-TOKEN',
      characterCount: 121,
      byteCount: 121,
    },
  ],
  markdown:
    '> Extracted DOCX text (from the document index, not the original .docx bytes): Report.docx\n\nREAL-INDEXED-BODY-TOKEN',
  tokenEstimate: {
    modelLabel: 'Generic Tokenizer (4 chars/token)',
    totalEstimatedTokens: 31,
    totalCharacterCount: 121,
    tokenBudget: null,
    exceedsBudget: false,
    warningMessage: null,
    sourceContributions: [
      {
        sourceId: 'docx-1',
        titleOrPath: 'Report.docx',
        characterCount: 121,
        wordCount: 14,
        estimatedTokens: 31,
        percentageOfTotal: 100,
      },
    ],
  },
};

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

function installFetch(packResponse: () => Response = () => jsonResponse(packPayload)) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : String(input);
    if (url.endsWith('/api/context/pack')) return packResponse();
    if (url.includes('/folder-sources')) return jsonResponse([]);
    if (url.includes('/graph')) return jsonResponse({ nodes: [], edges: [] });
    if (url.includes('/api/shares')) return jsonResponse([]);
    if (url.includes('/documents')) return jsonResponse([reportDocument]);
    return jsonResponse({}, false, 404);
  });
}

beforeEach(() => {
  localStorage.clear();
  useCanvasStore.getState().reset();
  useSearchStore.getState().reset();
  useViewStore.getState().reset();
  useGraphStore.getState().reset();
  (window as any).__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311';
  (window as any).__DOCUMENTIDE_SESSION_TOKEN__ = 'session-token-abc';
});

afterEach(() => {
  vi.restoreAllMocks();
  delete (window as any).__DOCUMENTIDE_API_URL__;
  delete (window as any).__DOCUMENTIDE_SESSION_TOKEN__;
});

describe('UI-05 context pack', () => {
  it('mounts from the real canvas selection and requests the selected document order', async () => {
    const fetchMock = installFetch();
    render(<App />);

    const card = await screen.findByLabelText('Document: Report.docx');
    fireEvent.click(card);
    await waitFor(() =>
      expect(useCanvasStore.getState().selectedIds.has('docx-1')).toBe(true),
    );

    const trigger = screen.getByRole('button', { name: 'Context pack' });
    expect(trigger).not.toBeDisabled();
    fireEvent.click(trigger);

    const dialog = await screen.findByRole('dialog', {
      name: 'Context Pack Export & Token Estimate',
    });
    expect(within(dialog).getByTestId('source-row-docx-1')).toHaveTextContent('Report.docx');
    // Copy-only: the dialog was mounted without an export callback and must not
    // present a button that claims to write a file.
    expect(
      within(dialog).queryByRole('button', { name: /export context\.md/i }),
    ).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /copy context/i })).toBeInTheDocument();

    const packCall = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/context/pack'),
    )!;
    expect(packCall[0]).toBe('http://127.0.0.1:5311/api/context/pack');
    expect(((packCall[1]?.headers ?? {}) as Record<string, string>)['X-DocumentIDE-Session-Token']).toBe(
      'session-token-abc',
    );
    expect(JSON.parse(String(packCall[1]?.body))).toEqual({
      documentIds: ['docx-1'],
      tokenBudget: null,
    });
  });

  it('keeps the trigger disabled with an empty selection', async () => {
    installFetch();
    render(<App />);

    await screen.findByLabelText('Document: Report.docx');
    expect(screen.getByRole('button', { name: 'Context pack' })).toBeDisabled();
    expect(
      screen.queryByRole('dialog', { name: 'Context Pack Export & Token Estimate' }),
    ).not.toBeInTheDocument();
  });

  it('surfaces a typed refusal and opens no dialog instead of showing an empty pack', async () => {
    installFetch(() =>
      jsonResponse(
        {
          status: 'NoTextAvailable',
          errorCode: 'NoTextAvailable',
          documentId: 'docx-1',
          detail: 'no-indexed-text',
        },
        false,
        409,
      ),
    );
    render(<App />);

    fireEvent.click(await screen.findByLabelText('Document: Report.docx'));
    await waitFor(() =>
      expect(useCanvasStore.getState().selectedIds.has('docx-1')).toBe(true),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Context pack' }));

    expect(await screen.findByRole('status')).toHaveTextContent('no-indexed-text');
    expect(
      screen.queryByRole('dialog', { name: 'Context Pack Export & Token Estimate' }),
    ).not.toBeInTheDocument();
  });
});
