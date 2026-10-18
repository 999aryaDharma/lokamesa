import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import DocxDiffScreen from './DocxDiffScreen';
import type { SourceControlChangeEntry } from './sourceControlModels';

const entry: SourceControlChangeEntry = {
  path: 'docs/Report.docx',
  originalPath: null,
  state: 'Modified',
  indexChange: 'Unmodified',
  workingTreeChange: 'Modified',
  conflictKind: 'None',
  statusCharacters: ' M',
  absolutePath: 'C:\\docs\\docs\\Report.docx',
  documentId: 'docx-1',
};

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

describe('DocxDiffScreen (design 06)', () => {
  it('renders the selected document and its real change metadata', () => {
    vi.spyOn(globalThis, 'fetch');
    render(<DocxDiffScreen entry={entry} onBack={() => {}} inspectorOpen />);

    expect(screen.getByTestId('docx-diff-screen')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Document diff' })).toBeInTheDocument();
    expect(screen.getByText(/docs\/Report\.docx/)).toBeInTheDocument();
    expect(screen.getAllByText('Modified').length).toBeGreaterThan(0);
    expect(screen.getAllByText('DOCX · OOXML').length).toBeGreaterThan(0);
  });

  it('does not claim a rendered page image or a text diff', () => {
    vi.spyOn(globalThis, 'fetch');
    render(<DocxDiffScreen entry={entry} onBack={() => {}} inspectorOpen />);

    expect(screen.queryByTestId('markdown-diff')).not.toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByText(/Page 1 preview/i)).not.toBeInTheDocument();
    expect(screen.getByText(/read-only report/i)).toBeInTheDocument();
  });

  it('opens the document in Word through the existing POST /api/word/open path', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ success: true, status: 'Success', errorMessage: '' }),
    );
    render(<DocxDiffScreen entry={entry} onBack={() => {}} inspectorOpen />);

    fireEvent.click(screen.getByRole('button', { name: 'Open in Word' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe('http://localhost:5000/api/word/open');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ documentId: 'docx-1' });
  });

  it('does not offer an arbitrary-path opener for an unregistered document', () => {
    vi.spyOn(globalThis, 'fetch');
    render(
      <DocxDiffScreen
        entry={{ ...entry, documentId: null }}
        onBack={() => {}}
        inspectorOpen
      />,
    );

    expect(screen.queryByRole('button', { name: 'Open in Word' })).not.toBeInTheDocument();
    expect(screen.getByText(/not a registered document/i)).toBeInTheDocument();
  });
});
