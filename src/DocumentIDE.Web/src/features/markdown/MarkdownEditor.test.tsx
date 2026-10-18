import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MarkdownEditor } from './MarkdownEditor';

afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

function jsonResponse(body: unknown, ok = true): Response {
  return { ok, status: ok ? 200 : 500, json: async () => body } as Response;
}

describe('MarkdownEditor Component', () => {
  it('renders textarea', () => {
    render(<MarkdownEditor />);
    const textarea = screen.getByRole('textbox');
    expect(textarea).toBeInTheDocument();
  });

  it('loads source from the document API for a real document id', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ documentId: 'doc-1', source: '# Title\n\nbody' }),
    );

    render(<MarkdownEditor documentId="doc-1" />);

    await waitFor(() => {
      expect(screen.getByRole('textbox')).toHaveValue('# Title\n\nbody');
    });
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/documents/doc-1/markdown'),
      expect.anything(),
    );
  });

  it('detects changes and enables manual save only when dirty', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ documentId: 'doc-2', source: 'original' }),
    );

    render(<MarkdownEditor documentId="doc-2" />);
    await waitFor(() => expect(screen.getByRole('textbox')).toHaveValue('original'));

    const saveButton = screen.getByRole('button', { name: /save/i });
    expect(saveButton).toBeDisabled();

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'changed' } });

    expect(saveButton).toBeEnabled();
    expect(screen.getByTestId('markdown-dirty')).toBeInTheDocument();
  });

  it('saves through the document API on Ctrl+S', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(jsonResponse({ documentId: 'doc-3', source: 'v1' }))
      .mockResolvedValue(jsonResponse({ documentId: 'doc-3', savedAt: 'now' }));

    render(<MarkdownEditor documentId="doc-3" />);
    await waitFor(() => expect(screen.getByRole('textbox')).toHaveValue('v1'));

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'v2' } });
    fireEvent.keyDown(window, { key: 's', ctrlKey: true });

    await waitFor(() => {
      const putCall = fetchMock.mock.calls.find(([, init]) => (init as RequestInit)?.method === 'PUT');
      expect(putCall).toBeDefined();
      expect((putCall![0] as string)).toContain('/api/documents/doc-3/markdown');
      expect(JSON.parse((putCall![1] as RequestInit).body as string)).toEqual({ source: 'v2' });
    });
  });

  it('autosaves when enabled and leaves the manual affordance honest', async () => {
    vi.useFakeTimers();
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(jsonResponse({ documentId: 'doc-4', source: 'a' }))
      .mockResolvedValue(jsonResponse({ documentId: 'doc-4' }));

    render(<MarkdownEditor documentId="doc-4" autoSave autoSaveDelayMs={500} />);
    await act(async () => {
      await Promise.resolve();
    });

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'b' } });
    await act(async () => {
      vi.advanceTimersByTime(600);
      await Promise.resolve();
    });

    const putCall = fetchMock.mock.calls.find(([, init]) => (init as RequestInit)?.method === 'PUT');
    expect(putCall).toBeDefined();
  });

  it('surfaces a save error without dropping the user source', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(jsonResponse({ documentId: 'doc-5', source: 'keep' }))
      .mockResolvedValue(jsonResponse({ error: 'disk full' }, false));

    render(<MarkdownEditor documentId="doc-5" />);
    await waitFor(() => expect(screen.getByRole('textbox')).toHaveValue('keep'));

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'keep me' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('textbox')).toHaveValue('keep me');
  });

  it('supports Edit/Preview/Split without injecting unsanitized HTML', async () => {
    render(<MarkdownEditor initialSource={'# Hi\n\n<script>alert(1)</script>'} />);

    fireEvent.click(screen.getByRole('button', { name: /preview/i }));

    const preview = screen.getByTestId('markdown-preview');
    // Inert text only — the raw source is escaped, never executed as HTML.
    expect(preview.textContent).toContain('<script>alert(1)</script>');
    expect(preview.querySelector('script')).toBeNull();
  });
});
