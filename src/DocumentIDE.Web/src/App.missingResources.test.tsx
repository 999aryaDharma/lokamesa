/**
 * UI-05 B — missing documents render the real `MissingResourceCard` from
 * `availabilityStatus`; Remove calls the reference-only route and Locate routes
 * into the authenticated dialog.
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { useCanvasStore } from './features/canvas/canvasStore';
import { useGraphStore } from './features/graph/graphStore';
import { useSearchStore } from './features/search/searchStore';
import { useViewStore } from './features/workspace/viewStore';

const missingDocument = {
  id: 'doc-missing',
  filename: 'Missing.docx',
  extension: '.docx',
  currentPath: 'C:\\Docs\\Missing.docx',
  lastKnownPath: 'C:\\Old\\Missing.docx',
  documentType: 'docx',
  availabilityStatus: 'missing',
};

const availableDocument = {
  id: 'doc-ok',
  filename: 'ok.md',
  extension: '.md',
  currentPath: 'C:\\Docs\\ok.md',
  documentType: 'markdown',
  availabilityStatus: 'available',
};

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

interface FetchOptions {
  removed?: boolean;
}

function installFetch(options: FetchOptions = {}) {
  const state = { removed: options.removed ?? false };

  const mock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = typeof input === 'string' ? input : String(input);

    if (url.endsWith('/reference') && init?.method === 'DELETE') {
      state.removed = true;
      return jsonResponse({ status: 'Removed', documentId: 'doc-missing', removed: true });
    }
    if (url.includes('/rebind-candidates')) {
      return jsonResponse({
        documentId: 'doc-missing',
        lastKnownPath: missingDocument.lastKnownPath,
        isAmbiguous: false,
        candidates: [],
      });
    }
    if (url.includes('/folder-sources')) return jsonResponse([]);
    if (url.includes('/graph')) return jsonResponse({ nodes: [], edges: [] });
    if (url.includes('/api/shares')) return jsonResponse([]);
    if (url.includes('/documents')) {
      return jsonResponse(state.removed ? [availableDocument] : [missingDocument, availableDocument]);
    }
    return jsonResponse({}, false, 404);
  });

  return { mock, state };
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

describe('UI-05 missing resources', () => {
  it('renders the real card for an unavailable document only, then removes only the reference', async () => {
    const { mock } = installFetch();
    render(<App />);

    const card = await screen.findByRole('group', { name: 'Missing file: Missing.docx' });
    expect(card).toHaveTextContent('File missing');
    expect(card).toHaveTextContent('C:\\Old\\Missing.docx');
    // An available document never gets a missing-resource card.
    expect(screen.queryByRole('group', { name: /Missing file: ok\.md/ })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Remove reference' }));

    await waitFor(() =>
      expect(screen.queryByRole('group', { name: 'Missing file: Missing.docx' })).not.toBeInTheDocument(),
    );

    const deleteCall = mock.mock.calls.find(
      ([input, init]) => String(input).endsWith('/reference') && init?.method === 'DELETE',
    )!;
    expect(deleteCall[0]).toBe('http://127.0.0.1:5311/api/documents/doc-missing/reference');
    expect(((deleteCall[1]?.headers ?? {}) as Record<string, string>)['X-DocumentIDE-Session-Token']).toBe(
      'session-token-abc',
    );
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Removed the reference. The file on disk was not changed.',
    );
  });

  it('routes Locate into the authenticated dialog', async () => {
    const { mock } = installFetch();
    render(<App />);

    await screen.findByRole('group', { name: 'Missing file: Missing.docx' });
    fireEvent.click(screen.getByRole('button', { name: 'Locate' }));

    const dialog = await screen.findByRole('dialog', { name: 'Locate Moved File' });
    expect(dialog).toHaveTextContent('Missing.docx');
    expect(dialog).toHaveTextContent('C:\\Old\\Missing.docx');
    expect(await screen.findByText(/no candidate files automatically detected/i)).toBeInTheDocument();

    const candidatesCall = mock.mock.calls.find(([input]) =>
      String(input).endsWith('/rebind-candidates'),
    )!;
    expect(candidatesCall[0]).toBe(
      'http://127.0.0.1:5311/api/documents/doc-missing/rebind-candidates',
    );
    expect(
      ((candidatesCall[1]?.headers ?? {}) as Record<string, string>)[
        'X-DocumentIDE-Session-Token'
      ],
    ).toBe('session-token-abc');

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
  });

  it('does not fabricate a QuickLook overlay alongside the missing-resource surface', async () => {
    installFetch();
    render(<App />);

    await screen.findByRole('group', { name: 'Missing file: Missing.docx' });
    expect(screen.queryByTestId('quick-look-dialog')).not.toBeInTheDocument();
  });
});
