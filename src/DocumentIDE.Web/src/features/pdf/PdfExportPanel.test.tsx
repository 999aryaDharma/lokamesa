/**
 * G8 — the shipped PDF UX container. These tests pin the product behaviors the
 * readiness gate cares about: the mounted surface reaches the live privileged
 * route, the request carries a real source path, typed engine refusals
 * (collision / unavailable source) are surfaced honestly, and no export is
 * ever attempted without an explicit user choice of output location.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, type Mock } from 'vitest';
import type { PdfSelectableDocument } from './PdfActions';
import {
  PdfRequestError,
  type BatchPdfExportRequestDto,
  type BatchPdfExportResponse,
  type PdfMergeRequestDto,
  type PdfMergeResponse,
  type SinglePdfExportRequestDto,
  type SinglePdfExportResponse,
} from './pdfApi';
import { PdfExportPanel, type PdfExportPanelApi } from './PdfExportPanel';

const docxA: PdfSelectableDocument = { id: 'a', name: 'Cover.docx', kind: 'docx' };
const docxB: PdfSelectableDocument = { id: 'b', name: 'BA Site A.docx', kind: 'docx' };

const paths: Record<string, string> = {
  a: 'C:/Docs/Cover.docx',
  b: 'C:/Docs/BA Site A.docx',
};

type SingleMock = Mock<
  (request: SinglePdfExportRequestDto) => Promise<SinglePdfExportResponse>
>;
type SeparateMock = Mock<
  (request: BatchPdfExportRequestDto) => Promise<BatchPdfExportResponse>
>;
type MergeMock = Mock<(request: PdfMergeRequestDto) => Promise<PdfMergeResponse>>;

interface MockedPdfApi {
  exportSingle: SingleMock;
  exportSeparate: SeparateMock;
  merge: MergeMock;
}

function makeApi(): PdfExportPanelApi & MockedPdfApi {
  return {
    exportSingle: vi.fn<(request: SinglePdfExportRequestDto) => Promise<SinglePdfExportResponse>>(),
    exportSeparate: vi.fn<(request: BatchPdfExportRequestDto) => Promise<BatchPdfExportResponse>>(),
    merge: vi.fn<(request: PdfMergeRequestDto) => Promise<PdfMergeResponse>>(),
  };
}

const resolveSourcePath = (id: string) => paths[id];

describe('PdfExportPanel', () => {
  it('single export sends the resolved source path and reports the written PDF', async () => {
    const api = makeApi();
    api.exportSingle.mockResolvedValue({
      status: 'Succeeded',
      outputPath: 'C:/Docs/Cover.pdf',
      sourceFingerprintBefore: 'A',
      sourceFingerprintAfter: 'A',
      errorMessage: null,
      isSuccess: true,
    });
    render(
      <PdfExportPanel selection={[docxA]} resolveSourcePath={resolveSourcePath} api={api} />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));

    await waitFor(() => expect(api.exportSingle).toHaveBeenCalledTimes(1));
    expect(api.exportSingle.mock.calls[0][0]).toMatchObject({
      sourceDocxPath: 'C:/Docs/Cover.docx',
    });
    expect(api.exportSingle.mock.calls[0][0].collisionBehavior).toBeUndefined();
    expect(await screen.findByText('C:/Docs/Cover.pdf')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'PDF export results' })).toHaveTextContent(
      'succeeded',
    );
  });

  it('surfaces an output collision as a typed state and retries only on an explicit decision', async () => {
    const api = makeApi();
    api.exportSingle
      .mockResolvedValueOnce({
        status: 'OutputCollision',
        outputPath: 'C:/Docs/Cover.pdf',
        sourceFingerprintBefore: 'A',
        sourceFingerprintAfter: 'A',
        errorMessage: 'Output PDF already exists: C:/Docs/Cover.pdf.',
        isSuccess: false,
      })
      .mockResolvedValueOnce({
        status: 'Succeeded',
        outputPath: 'C:/Docs/Cover.pdf',
        sourceFingerprintBefore: 'A',
        sourceFingerprintAfter: 'A',
        errorMessage: null,
        isSuccess: true,
      });
    render(
      <PdfExportPanel selection={[docxA]} resolveSourcePath={resolveSourcePath} api={api} />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));

    // The first attempt is a collision: never reported as success, and the
    // owner must choose explicitly (no silent overwrite / rename).
    expect(
      await screen.findByRole('group', { name: 'Output file already exists' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'PDF export results' })).toHaveTextContent(
      'skipped',
    );
    expect(api.exportSingle).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('radio', { name: 'Overwrite' }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply collision decision' }));

    await waitFor(() => expect(api.exportSingle).toHaveBeenCalledTimes(2));
    expect(api.exportSingle.mock.calls[1][0]).toMatchObject({
      sourceDocxPath: 'C:/Docs/Cover.docx',
      collisionBehavior: 'overwrite',
    });
    expect(screen.getByRole('list', { name: 'PDF export results' })).toHaveTextContent(
      'succeeded',
    );
  });

  it('refuses to export a document without a resolvable source path', async () => {
    const api = makeApi();
    render(
      <PdfExportPanel
        selection={[docxA]}
        resolveSourcePath={() => undefined}
        api={api}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No source path available for Cover.docx.',
    );
    expect(api.exportSingle).not.toHaveBeenCalled();
  });

  it('separate export uses the explicit output folder and reports per-file typed outcomes', async () => {
    const api = makeApi();
    api.exportSeparate.mockResolvedValue({
      totalCount: 2,
      succeededCount: 1,
      skippedCount: 1,
      failedCount: 0,
      cancelledCount: 0,
      isSuccess: true,
      summary: '1 PDFs generated / 1 skipped / 0 failed',
      files: [
        {
          sourceDocxPath: 'C:/Docs/Cover.docx',
          status: 'Succeeded',
          outputPath: 'C:/Docs/Cover.pdf',
          errorMessage: null,
        },
        {
          sourceDocxPath: 'C:/Docs/BA Site A.docx',
          status: 'OutputCollision',
          outputPath: 'C:/Docs/BA Site A.pdf',
          errorMessage: 'Output PDF already exists.',
        },
      ],
    });
    render(
      <PdfExportPanel
        selection={[docxA, docxB]}
        resolveSourcePath={resolveSourcePath}
        api={api}
      />,
    );

    // The output folder is an explicit, visible user choice pre-filled from the
    // selection — never invented silently.
    expect(screen.getByLabelText('Output folder')).toHaveValue('C:/Docs');

    fireEvent.click(screen.getByRole('button', { name: 'Export as separate PDFs' }));

    await waitFor(() => expect(api.exportSeparate).toHaveBeenCalledTimes(1));
    expect(api.exportSeparate.mock.calls[0][0]).toMatchObject({
      sourceDocxPaths: ['C:/Docs/Cover.docx', 'C:/Docs/BA Site A.docx'],
      outputDirectory: 'C:/Docs',
    });
    expect(api.exportSeparate.mock.calls[0][0].collisionBehavior).toBeUndefined();
    expect(await screen.findByRole('status')).toHaveTextContent(
      '1 PDFs generated / 1 skipped / 0 failed',
    );
    const results = screen.getByRole('list', { name: 'PDF export results' });
    expect(results).toHaveTextContent('BA Site A.docx');
    expect(results).toHaveTextContent('skipped');
  });

  it('merge sends the explicit user order and output path', async () => {
    const api = makeApi();
    api.merge.mockResolvedValue({
      status: 'Succeeded',
      outputPath: 'C:/Docs/Cover-merged.pdf',
      pageCount: 7,
      orderedSources: ['C:/Docs/Cover.docx', 'C:/Docs/BA Site A.docx'],
      errorMessage: null,
      isSuccess: true,
    });
    render(
      <PdfExportPanel
        selection={[docxA, docxB]}
        resolveSourcePath={resolveSourcePath}
        api={api}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Merge to PDF' }));
    fireEvent.click(screen.getByRole('button', { name: 'Merge in this order' }));

    await waitFor(() => expect(api.merge).toHaveBeenCalledTimes(1));
    expect(api.merge.mock.calls[0][0]).toMatchObject({
      orderedSourcePaths: ['C:/Docs/Cover.docx', 'C:/Docs/BA Site A.docx'],
    });
    expect(api.merge.mock.calls[0][0].outputPath).toBe('C:/Docs/Cover-merged.pdf');
    expect(await screen.findByText('7 pages written to C:/Docs/Cover-merged.pdf.')).toBeInTheDocument();
  });

  it('honours "apply choice to remaining collisions" by reusing the decision without re-prompting', async () => {
    const api = makeApi();
    api.exportSingle
      .mockResolvedValueOnce({
        status: 'OutputCollision',
        outputPath: 'C:/Docs/Cover.pdf',
        sourceFingerprintBefore: 'A',
        sourceFingerprintAfter: 'A',
        errorMessage: 'Output PDF already exists: C:/Docs/Cover.pdf.',
        isSuccess: false,
      })
      .mockResolvedValueOnce({
        status: 'OutputCollision',
        outputPath: 'C:/Docs/Cover.pdf',
        sourceFingerprintBefore: 'A',
        sourceFingerprintAfter: 'A',
        errorMessage: 'Output PDF already exists: C:/Docs/Cover.pdf.',
        isSuccess: false,
      })
      .mockResolvedValueOnce({
        status: 'Succeeded',
        outputPath: 'C:/Docs/Cover.pdf',
        sourceFingerprintBefore: 'A',
        sourceFingerprintAfter: 'A',
        errorMessage: null,
        isSuccess: true,
      });
    render(
      <PdfExportPanel selection={[docxA]} resolveSourcePath={resolveSourcePath} api={api} />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));
    await screen.findByRole('group', { name: 'Output file already exists' });

    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Apply choice to remaining collisions' }),
    );
    fireEvent.click(screen.getByRole('radio', { name: 'Overwrite' }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply collision decision' }));

    // The retained decision resolves the remaining collision of the run without
    // showing the fieldset a second time.
    await waitFor(() => expect(api.exportSingle).toHaveBeenCalledTimes(3));
    expect(
      screen.queryByRole('group', { name: 'Output file already exists' }),
    ).not.toBeInTheDocument();
    expect(api.exportSingle.mock.calls[2][0].collisionBehavior).toBe('overwrite');
    expect(screen.getByRole('list', { name: 'PDF export results' })).toHaveTextContent(
      'succeeded',
    );
  });

  it('names the offending document, not its raw id, when a merge source path is missing', async () => {
    const api = makeApi();
    render(
      <PdfExportPanel
        selection={[docxA, docxB]}
        resolveSourcePath={(id) => (id === 'a' ? paths.a : undefined)}
        api={api}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Merge to PDF' }));
    fireEvent.click(screen.getByRole('button', { name: 'Merge in this order' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No source path available for BA Site A.docx.',
    );
    expect(api.merge).not.toHaveBeenCalled();
  });

  it('clears the previous run\'s progress when a new export starts', async () => {
    const api = makeApi();
    api.exportSeparate.mockResolvedValue({
      totalCount: 2,
      succeededCount: 2,
      skippedCount: 0,
      failedCount: 0,
      cancelledCount: 0,
      isSuccess: true,
      summary: '2 PDFs generated / 0 skipped / 0 failed',
      files: [
        {
          sourceDocxPath: 'C:/Docs/Cover.docx',
          status: 'Succeeded',
          outputPath: 'C:/Docs/Cover.pdf',
          errorMessage: null,
        },
        {
          sourceDocxPath: 'C:/Docs/BA Site A.docx',
          status: 'Succeeded',
          outputPath: 'C:/Docs/BA Site A.pdf',
          errorMessage: null,
        },
      ],
    });
    api.exportSingle.mockResolvedValue({
      status: 'Succeeded',
      outputPath: 'C:/Docs/Cover.pdf',
      sourceFingerprintBefore: 'A',
      sourceFingerprintAfter: 'A',
      errorMessage: null,
      isSuccess: true,
    });
    const { rerender } = render(
      <PdfExportPanel
        selection={[docxA, docxB]}
        resolveSourcePath={resolveSourcePath}
        api={api}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Export as separate PDFs' }));
    await waitFor(() => expect(screen.getByRole('progressbar')).toBeInTheDocument());

    rerender(
      <PdfExportPanel selection={[docxA]} resolveSourcePath={resolveSourcePath} api={api} />,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));

    // The stale bar/summary from the separate run must not survive into the
    // new run.
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    await waitFor(() => expect(api.exportSingle).toHaveBeenCalledTimes(1));
  });

  it('reports a merge collision as skipped in both the results and progress', async () => {
    const api = makeApi();
    api.merge.mockResolvedValue({
      status: 'OutputCollision',
      outputPath: 'C:/Docs/Cover-merged.pdf',
      pageCount: 0,
      orderedSources: ['C:/Docs/Cover.docx', 'C:/Docs/BA Site A.docx'],
      errorMessage: 'Output PDF already exists: C:/Docs/Cover-merged.pdf.',
      isSuccess: false,
    });
    render(
      <PdfExportPanel
        selection={[docxA, docxB]}
        resolveSourcePath={resolveSourcePath}
        api={api}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Merge to PDF' }));
    fireEvent.click(screen.getByRole('button', { name: 'Merge in this order' }));

    await waitFor(() => expect(api.merge).toHaveBeenCalledTimes(1));
    // The row and the progress bar must agree about the same outcome.
    expect(screen.getByRole('list', { name: 'PDF export results' })).toHaveTextContent(
      'skipped',
    );
    expect(
      screen.getByRole('region', { name: 'Batch PDF export progress' }),
    ).toHaveTextContent('0 PDFs generated / 1 skipped / 0 failed');
  });

  it('surfaces an owner-readable message when the engine rejects the request', async () => {
    const api = makeApi();
    api.exportSingle.mockRejectedValue(new PdfRequestError(400, 'SourceRequired'));
    render(
      <PdfExportPanel selection={[docxA]} resolveSourcePath={resolveSourcePath} api={api} />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Select a DOCX document to export.');
    // The raw controller code stays visible as the typed detail.
    expect(alert).toHaveTextContent('SourceRequired');
  });
});
