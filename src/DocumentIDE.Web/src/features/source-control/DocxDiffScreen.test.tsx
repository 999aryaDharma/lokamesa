import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import DocxDiffScreen from './DocxDiffScreen';
import type { DocxDiffApiResponse } from './docxDiffApi';
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

const worktreeRoot = 'C:\\docs';

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

/** Real-shaped SCM-19 payload: PascalCase enums and exact before/after text. */
function availablePayload(): DocxDiffApiResponse {
  return {
    status: 'Available',
    isSuccess: true,
    errorCode: null,
    message: null,
    report: {
      target: 'Unstaged',
      path: 'docs/Report.docx',
      worktreeRoot,
      leftLabel: 'index',
      rightLabel: 'working tree',
      leftExists: true,
      rightExists: true,
      changeCount: 3,
      hasChanges: true,
      changes: [
        {
          kind: 'ParagraphTextChanged',
          origin: 'Body',
          partName: 'word/document.xml',
          summary:
            'Paragraph text changed: "Either party may terminate with 30 days" -> "Either party may terminate with 60 days"',
          before: 'Either party may terminate this agreement with 30 days written notice.',
          after: 'Either party may terminate this agreement with 60 days written notice.',
          property: null,
          oldParagraphIndex: 12,
          newParagraphIndex: 12,
          runIndex: null,
          tableIndex: null,
          rowIndex: null,
          cellIndex: null,
        },
        {
          kind: 'TableCellChanged',
          origin: 'Table',
          partName: 'word/document.xml',
          summary: 'Table cell changed: "Alpha" -> "Beta"',
          before: 'Alpha',
          after: 'Beta',
          property: null,
          oldParagraphIndex: null,
          newParagraphIndex: null,
          runIndex: null,
          tableIndex: 1,
          rowIndex: 2,
          cellIndex: 3,
        },
        {
          kind: 'FormattingChanged',
          origin: 'Body',
          partName: 'word/document.xml',
          summary: 'Formatting changed for "Audit": Bold off -> on',
          before: 'off',
          after: 'on',
          property: 'Bold',
          oldParagraphIndex: 22,
          newParagraphIndex: 22,
          runIndex: 0,
          tableIndex: null,
          rowIndex: null,
          cellIndex: null,
        },
      ],
      binarySummary: null,
    },
    failure: null,
  };
}

function binaryPayload(): DocxDiffApiResponse {
  return {
    status: 'BinaryFallback',
    isSuccess: false,
    errorCode: 'BinaryFallback',
    message: 'The materialized version is not a valid DOCX package.',
    report: {
      target: 'Unstaged',
      path: 'docs/Report.docx',
      worktreeRoot,
      leftLabel: 'index',
      rightLabel: 'working tree',
      leftExists: true,
      rightExists: true,
      changeCount: 0,
      hasChanges: false,
      changes: [],
      binarySummary: {
        leftExists: true,
        rightExists: true,
        leftSizeBytes: 120,
        rightSizeBytes: 340,
        leftSha256: 'a'.repeat(64),
        rightSha256: 'b'.repeat(64),
        contentIdentical: false,
      },
    },
    failure: {
      status: 'Failed',
      errorKind: 'Unknown',
      errorMessage: 'The materialized version is not a valid DOCX package.',
      exitCode: null,
    },
  };
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
  it('renders the selected document and its real change metadata', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(availablePayload()));
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );

    expect(screen.getByTestId('docx-diff-screen')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Document diff' })).toBeInTheDocument();
    expect(screen.getByText(/docs\/Report\.docx/)).toBeInTheDocument();
    expect(screen.getAllByText('Modified').length).toBeGreaterThan(0);
    expect(screen.getAllByText('DOCX · OOXML').length).toBeGreaterThan(0);
    // The report resolves, so the screen is in a real (not typed-failure) state.
    expect(await screen.findByTestId('docx-diff')).toBeInTheDocument();
  });

  it('requests the true worktree root and renders the real document-aware report', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(jsonResponse(availablePayload()));
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );

    const panel = await screen.findByTestId('docx-diff');
    expect(panel).toBeInTheDocument();

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe('http://localhost:5000/api/source-control/docx-diff');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({
      path: worktreeRoot,
      documentPath: 'docs/Report.docx',
      target: 'Unstaged',
      originalPath: null,
    });

    // Paragraph before/after comes from the engine, not from client inference.
    expect(screen.getByTestId('docx-diff-kind-0')).toHaveTextContent('Paragraph text changed');
    expect(screen.getByTestId('docx-diff-before-0')).toHaveTextContent('30 days written notice');
    expect(screen.getByTestId('docx-diff-after-0')).toHaveTextContent('60 days written notice');
    expect(screen.getByTestId('docx-diff-location-0')).toHaveTextContent('paragraph 12');

    // Table + formatting summaries keep their engine location vocabulary.
    expect(screen.getByTestId('docx-diff-kind-1')).toHaveTextContent('Table cell changed');
    expect(screen.getByTestId('docx-diff-location-1')).toHaveTextContent(
      'table 1, row 2, cell 3',
    );
    expect(screen.getByTestId('docx-diff-kind-2')).toHaveTextContent('Formatting changed');
    expect(screen.getByTestId('docx-diff-location-2')).toHaveTextContent('Bold');

    expect(screen.getByTestId('docx-diff-target')).toHaveTextContent('index ↔ working tree');

    const summary = screen.getByTestId('docx-diff-class-summary');
    expect(summary).toHaveTextContent('1 text change');
    expect(summary).toHaveTextContent('1 table change');
    expect(summary).toHaveTextContent('1 formatting change');
    expect(summary).toHaveTextContent('0 metadata changes');
  });

  it('renders a binary fallback as its typed size/SHA state, never as change rows', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(binaryPayload()));
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );

    const binary = await screen.findByTestId('docx-diff-binary');
    expect(binary).toBeInTheDocument();
    const summary = screen.getByTestId('docx-diff-binary-summary');
    expect(summary).toHaveTextContent('120 bytes');
    expect(summary).toHaveTextContent('340 bytes');
    expect(summary).toHaveTextContent('a'.repeat(64));
    expect(summary).toHaveTextContent('b'.repeat(64));
    expect(screen.getByTestId('docx-diff-binary-reason')).toHaveTextContent(
      'not a valid DOCX package',
    );
    expect(screen.queryByTestId('docx-diff-change-0')).not.toBeInTheDocument();
    expect(screen.queryByTestId('docx-diff-class-summary')).not.toBeInTheDocument();
  });

  it('renders a failed read as its typed state, never as fabricated rows', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({
        status: 'Failed',
        isSuccess: false,
        errorCode: 'Failed',
        message: 'The document could not be compared: git show HEAD:docs/Report.docx failed.',
        report: null,
        failure: {
          status: 'Failed',
          errorKind: 'Unknown',
          errorMessage: 'The document could not be compared.',
          exitCode: 128,
        },
      } satisfies DocxDiffApiResponse),
    );
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('could not be compared');
    expect(screen.queryByTestId('docx-diff')).not.toBeInTheDocument();
    expect(screen.queryByTestId('docx-diff-change-0')).not.toBeInTheDocument();
    expect(screen.queryByTestId('docx-diff-binary')).not.toBeInTheDocument();
  });

  it('renders a transport failure as a typed error without change rows', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ errorCode: 'HostUnavailable', error: 'The local API is unreachable.' }, false, 503),
    );
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('HostUnavailable');
    expect(alert).toHaveTextContent('The local API is unreachable.');
    expect(screen.queryByTestId('docx-diff-change-0')).not.toBeInTheDocument();
  });

  it('shows the loading state while the report is in flight', async () => {
    let resolveFetch: (value: Response) => void = () => {};
    vi.spyOn(globalThis, 'fetch').mockImplementation(
      () =>
        new Promise<Response>((resolve) => {
          resolveFetch = resolve;
        }),
    );
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );

    expect(screen.getByTestId('docx-diff-loading')).toBeInTheDocument();
    resolveFetch(jsonResponse(availablePayload()));
    expect(await screen.findByTestId('docx-diff')).toBeInTheDocument();
  });

  it('does not call the route without a real worktree root, and says so', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={null} onBack={() => {}} inspectorOpen />,
    );

    expect(screen.getByTestId('docx-diff-worktree-unknown')).toHaveTextContent(
      /worktree root/i,
    );
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByTestId('docx-diff-change-0')).not.toBeInTheDocument();
  });

  it('does not claim a rendered page image or a text diff', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(availablePayload()));
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );
    await screen.findByTestId('docx-diff');

    expect(screen.queryByTestId('markdown-diff')).not.toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByText(/Page 1 preview/i)).not.toBeInTheDocument();
    expect(screen.getByText(/read-only report/i)).toBeInTheDocument();
  });

  it('opens the document in Word through the existing POST /api/word/open path', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/word/open')) {
        return jsonResponse({ success: true, status: 'Success', errorMessage: '' });
      }
      return jsonResponse(availablePayload());
    });
    render(
      <DocxDiffScreen entry={entry} worktreeRoot={worktreeRoot} onBack={() => {}} inspectorOpen />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Open in Word' }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/word/open'))).toBe(
        true,
      ),
    );
    const call = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/word/open'),
    )!;
    expect(call[1]?.method).toBe('POST');
    expect(JSON.parse(String(call[1]?.body))).toEqual({ documentId: 'docx-1' });
  });

  it('does not offer an arbitrary-path opener for an unregistered document', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(availablePayload()));
    render(
      <DocxDiffScreen
        entry={{ ...entry, documentId: null }}
        worktreeRoot={worktreeRoot}
        onBack={() => {}}
        inspectorOpen
      />,
    );
    await screen.findByTestId('docx-diff');

    expect(screen.queryByRole('button', { name: 'Open in Word' })).not.toBeInTheDocument();
    expect(screen.getByText(/not a registered document/i)).toBeInTheDocument();
  });
});
