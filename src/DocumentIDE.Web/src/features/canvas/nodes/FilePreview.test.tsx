/**
 * UI-01 — faint page-1 preview on a file object (owner directive 2026-10-18).
 *
 * A file sheet must show a faint preview of the document's *real* content:
 *   - `.md`  → the source served by the existing
 *              `GET /api/documents/{documentId}/markdown` route;
 *   - `.docx`→ UI-02's read-only structure route; until it lands the existing
 *              `LowDetailPaperLayout` fallback stays (no backend change here).
 *
 * The preview is content-derived, not a rendered page image; it must be faint
 * so the filename label stays legible.
 */
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import FilePreview from './FilePreview';

const markdownLoader = (source: string) => async () => ({ documentId: 'doc-1', source });

describe('FilePreview', () => {
  it('renders a faint preview derived from the real markdown source', async () => {
    render(
      <FilePreview
        documentId="doc-1"
        title="notes.md"
        docType="md"
        loadMarkdown={markdownLoader('# Architecture\n\nThe workspace keeps [[Atlas index]] as the entry point.')}
      />,
    );

    const preview = await screen.findByTestId('file-preview-content');
    expect(preview).toHaveTextContent('Architecture');
    expect(preview).toHaveTextContent('Atlas index');
    // Faint, and clipped to the sheet face.
    expect(preview.className).toContain('opacity-40');
    expect(preview.className).toContain('overflow-hidden');
  });

  it('keeps the low-detail fallback when the real content is unavailable', async () => {
    render(
      <FilePreview
        documentId="doc-2"
        title="notes.md"
        docType="md"
        loadMarkdown={async () => {
          throw new Error('route unavailable');
        }}
      />,
    );

    expect(await screen.findByTestId('document-low-detail-fallback')).toBeInTheDocument();
    expect(screen.queryByTestId('file-preview-content')).not.toBeInTheDocument();
  });

  it('uses the low-detail fallback for a DOCX until UI-02 serves extracted content', () => {
    render(<FilePreview documentId="doc-3" title="Report.docx" docType="docx" />);
    expect(screen.getByTestId('document-low-detail-fallback')).toBeInTheDocument();
    expect(screen.queryByTestId('file-preview-content')).not.toBeInTheDocument();
  });

  it('renders supplied thumbnail content without fetching anything', () => {
    const loadMarkdown = vi.fn();
    render(
      <FilePreview
        documentId="doc-4"
        title="notes.md"
        docType="md"
        thumbnailContent="Real cached content"
        loadMarkdown={loadMarkdown}
      />,
    );
    expect(screen.getByTestId('file-preview-content')).toHaveTextContent('Real cached content');
    expect(loadMarkdown).not.toHaveBeenCalled();
  });

  it('honours the explicit thumbnail states (loading / missing / failed)', async () => {
    const { rerender } = render(
      <FilePreview documentId="doc-5" title="Report.docx" docType="docx" thumbnailState="loading" />,
    );
    expect(screen.getByTestId('document-thumbnail-loading')).toBeInTheDocument();

    rerender(
      <FilePreview documentId="doc-5" title="Report.docx" docType="docx" thumbnailState="missing" />,
    );
    expect(screen.getByTestId('document-missing-badge')).toBeInTheDocument();

    rerender(
      <FilePreview
        documentId="doc-5"
        title="Report.docx"
        docType="docx"
        thumbnailState="ready"
        thumbnailUrl="http://invalid-url.local/broken.png"
      />,
    );
    const img = screen.getByTestId('document-thumbnail-img');
    fireEvent.error(img);
    expect(await screen.findByTestId('document-low-detail-fallback')).toBeInTheDocument();
  });

  it('flags a cached image as stale', () => {
    render(
      <FilePreview
        documentId="doc-6"
        title="Report.docx"
        docType="docx"
        thumbnailState="stale"
        thumbnailUrl="http://valid-url.local/stale.png"
      />,
    );
    expect(screen.getByTestId('document-stale-badge')).toBeInTheDocument();
  });
});
