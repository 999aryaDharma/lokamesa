import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import DocumentNode from './DocumentNode';

describe('DocumentNode', () => {
  it('renders paper sheet object with readable filename at normal zoom', () => {
    render(
      <DocumentNode
        id="doc-1"
        title="Quarterly_Report_2026.docx"
        docType="docx"
      />
    );

    const filenameEl = screen.getByText('Quarterly_Report_2026.docx');
    expect(filenameEl).toBeInTheDocument();
    const sheet = screen.getByTestId('document-paper-sheet');
    expect(sheet).toBeInTheDocument();
    // UI-01: the file object carries its type as an accent colour on the sheet
    // (Finder icon-view convention), not as a text badge.
    expect(sheet).toHaveAttribute('data-kind', 'docx');
    expect(sheet.querySelector('[data-part="file-sheet"]')).not.toBeNull();
    expect(sheet.querySelector('[data-part="type-accent"]')).not.toBeNull();
  });

  it('renders markdown document with its markdown type accent', () => {
    render(
      <DocumentNode
        id="doc-2"
        title="README.md"
        docType="md"
        loadMarkdown={async (documentId) => ({ documentId, source: '# README' })}
      />
    );

    expect(screen.getByText('README.md')).toBeInTheDocument();
    const sheet = screen.getByTestId('document-paper-sheet');
    expect(sheet).toHaveAttribute('data-kind', 'md');
    expect(sheet.querySelector('[data-part="type-accent"]')).not.toBeNull();
  });

  it('renders visibly distinct missing document state', () => {
    render(
      <DocumentNode
        id="doc-3"
        title="MissingDoc.docx"
        docType="docx"
        isMissing={true}
        thumbnailState="missing"
      />
    );

    const paper = screen.getByTestId('document-paper-sheet');
    expect(paper).toHaveAttribute('data-missing', 'true');
    expect(screen.getByText(/File missing/i)).toBeInTheDocument();
  });

  it('falls back gracefully when thumbnail fails without breaking canvas', () => {
    const { container } = render(
      <DocumentNode
        id="doc-4"
        title="CorruptedImage.docx"
        docType="docx"
        thumbnailState="ready"
        thumbnailUrl="http://invalid-url.local/broken.png"
      />
    );

    const img = screen.getByTestId('document-thumbnail-img');
    expect(img).toBeInTheDocument();

    // Trigger image loading error
    fireEvent.error(img);

    // Should fall back gracefully to low-detail layout preview without breaking
    expect(screen.queryByTestId('document-thumbnail-img')).not.toBeInTheDocument();
    expect(screen.getByTestId('document-low-detail-fallback')).toBeInTheDocument();
    expect(container).toBeInTheDocument();
  });

  it('renders loading skeleton and stale preview indicators', () => {
    const { rerender } = render(
      <DocumentNode
        id="doc-5"
        title="LoadingDoc.docx"
        thumbnailState="loading"
      />
    );

    expect(screen.getByTestId('document-thumbnail-loading')).toBeInTheDocument();

    rerender(
      <DocumentNode
        id="doc-5"
        title="StaleDoc.docx"
        thumbnailState="stale"
        thumbnailUrl="http://valid-url.local/stale.png"
      />
    );

    expect(screen.getByTestId('document-stale-badge')).toBeInTheDocument();
  });

  it('handles click and double click events', () => {
    const onSelect = vi.fn();
    const onDoubleClick = vi.fn();

    render(
      <DocumentNode
        id="doc-6"
        title="ClickableDoc.docx"
        onSelect={onSelect}
        onDoubleClick={onDoubleClick}
      />
    );

    const paper = screen.getByTestId('document-paper-sheet');
    fireEvent.click(paper);
    expect(onSelect).toHaveBeenCalledWith('doc-6');

    fireEvent.doubleClick(paper);
    expect(onDoubleClick).toHaveBeenCalledWith('doc-6');
  });
});
