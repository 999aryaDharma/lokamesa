/**
 * G8 — contract tests for the privileged PDF finalization client.
 *
 * These pin the exact request/response wire shape of
 * `PdfController` (`src/DocumentIDE.Api/Controllers/PdfController.cs`):
 *   POST /api/pdf/export        { sourceDocxPath, outputDirectory?, outputFileName?, collisionBehavior? }
 *   POST /api/pdf/export-batch  { sourceDocxPaths, outputDirectory, collisionBehavior? }
 *   POST /api/pdf/merge         { orderedSourcePaths, outputPath, collisionBehavior? }
 * The client must never invent routes or response fields.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  exportSeparatePdfs,
  exportSinglePdf,
  mergeDocumentsToPdf,
  PdfRequestError,
} from './pdfApi';

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

interface ApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

function globals(): ApiGlobals {
  return window as unknown as ApiGlobals;
}

beforeEach(() => {
  globals().__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:1234';
  globals().__DOCUMENTIDE_SESSION_TOKEN__ = 'session-tok';
});

afterEach(() => {
  vi.restoreAllMocks();
  delete globals().__DOCUMENTIDE_API_URL__;
  delete globals().__DOCUMENTIDE_SESSION_TOKEN__;
});

describe('pdfApi', () => {
  it('single export posts the source path and collision behavior to /api/pdf/export', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({
        status: 'Succeeded',
        outputPath: 'C:/Docs/Cover.pdf',
        sourceFingerprintBefore: 'AAA',
        sourceFingerprintAfter: 'AAA',
        errorMessage: null,
        isSuccess: true,
      }),
    );

    const result = await exportSinglePdf({
      sourceDocxPath: 'C:/Docs/Cover.docx',
      collisionBehavior: 'overwrite',
    });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://127.0.0.1:1234/api/pdf/export');
    expect(init?.method).toBe('POST');
    expect(init?.headers).toMatchObject({
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': 'session-tok',
    });
    expect(JSON.parse(String(init?.body))).toEqual({
      sourceDocxPath: 'C:/Docs/Cover.docx',
      collisionBehavior: 'overwrite',
    });
    expect(result.status).toBe('Succeeded');
    expect(result.outputPath).toBe('C:/Docs/Cover.pdf');
    expect(result.isSuccess).toBe(true);
  });

  it('separate export posts ordered sources plus the explicit output directory', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({
        totalCount: 2,
        succeededCount: 1,
        skippedCount: 1,
        failedCount: 0,
        cancelledCount: 0,
        isSuccess: true,
        summary: '1 PDFs generated / 1 skipped / 0 failed',
        files: [
          {
            sourceDocxPath: 'C:/Docs/A.docx',
            status: 'Succeeded',
            outputPath: 'C:/Out/A.pdf',
            errorMessage: null,
          },
          {
            sourceDocxPath: 'C:/Docs/B.docx',
            status: 'OutputCollision',
            outputPath: 'C:/Out/B.pdf',
            errorMessage: 'Output PDF already exists.',
          },
        ],
      }),
    );

    const result = await exportSeparatePdfs({
      sourceDocxPaths: ['C:/Docs/A.docx', 'C:/Docs/B.docx'],
      outputDirectory: 'C:/Out',
    });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://127.0.0.1:1234/api/pdf/export-batch');
    expect(JSON.parse(String(init?.body))).toEqual({
      sourceDocxPaths: ['C:/Docs/A.docx', 'C:/Docs/B.docx'],
      outputDirectory: 'C:/Out',
    });
    expect(result.files.map((file) => file.status)).toEqual([
      'Succeeded',
      'OutputCollision',
    ]);
    expect(result.summary).toContain('1 skipped');
  });

  it('merge posts the explicit order and output path to /api/pdf/merge', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({
        status: 'Succeeded',
        outputPath: 'C:/Out/merged.pdf',
        pageCount: 7,
        orderedSources: ['C:/Docs/A.docx', 'C:/Docs/B.docx'],
        errorMessage: null,
        isSuccess: true,
      }),
    );

    const result = await mergeDocumentsToPdf({
      orderedSourcePaths: ['C:/Docs/A.docx', 'C:/Docs/B.docx'],
      outputPath: 'C:/Out/merged.pdf',
    });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://127.0.0.1:1234/api/pdf/merge');
    expect(JSON.parse(String(init?.body))).toEqual({
      orderedSourcePaths: ['C:/Docs/A.docx', 'C:/Docs/B.docx'],
      outputPath: 'C:/Out/merged.pdf',
    });
    expect(result.pageCount).toBe(7);
  });

  it('surfaces a human-readable message and keeps the raw controller code as the typed detail', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ errorCode: 'SourceRequired' }, false, 400),
    );

    const error = await exportSinglePdf({ sourceDocxPath: '  ' }).catch(
      (caught: unknown) => caught,
    );
    expect(error).toBeInstanceOf(PdfRequestError);
    expect((error as PdfRequestError).message).toBe(
      'Select a DOCX document to export. (SourceRequired)',
    );
    expect((error as PdfRequestError).errorCode).toBe('SourceRequired');
  });

  it('falls back to the raw code for an unmapped controller rejection', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ errorCode: 'SomethingNew' }, false, 409),
    );

    const error = await exportSinglePdf({ sourceDocxPath: 'C:/Docs/A.docx' }).catch(
      (caught: unknown) => caught,
    );
    expect((error as PdfRequestError).message).toBe('SomethingNew');
    expect((error as PdfRequestError).errorCode).toBe('SomethingNew');
  });
});
