import { render, screen, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import {
  PdfActions,
  getAvailablePdfActions,
  getPdfCommandPaletteEntries,
  resolveDefaultExportMode,
} from './PdfActions';

const docxA = { id: 'a', name: 'Cover.docx', kind: 'docx' as const };
const docxB = { id: 'b', name: 'BA Site A.docx', kind: 'docx' as const };

describe('PdfActions context rules', () => {
  it('one selected DOCX defaults to single export', () => {
    expect(resolveDefaultExportMode([docxA])).toBe('single');
    const onExportSingle = vi.fn();
    render(<PdfActions selection={[docxA]} onExportSingle={onExportSingle} />);
    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }));
    expect(onExportSingle).toHaveBeenCalledWith('a');
  });

  it('multiple docs offer separate/merge choices', () => {
    expect(getAvailablePdfActions([docxA, docxB])).toEqual([
      'export-separate',
      'merge-pdf',
    ]);
    const onExportSeparate = vi.fn();
    const onMerge = vi.fn();
    render(
      <PdfActions
        selection={[docxA, docxB]}
        onExportSeparate={onExportSeparate}
        onMerge={onMerge}
      />,
    );
    fireEvent.click(
      screen.getByRole('button', { name: 'Export as separate PDFs' }),
    );
    expect(onExportSeparate).toHaveBeenCalledWith(['a', 'b']);
    // Merge flow opens the ordering UI; confirming emits the explicit order.
    fireEvent.click(screen.getByRole('button', { name: 'Merge to PDF' }));
    expect(screen.getByRole('dialog', { name: 'Merge document order' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Merge in this order' }));
    expect(onMerge).toHaveBeenCalledWith(['a', 'b']);
  });

  it('failures identify affected source', () => {
    render(
      <PdfActions
        selection={[docxA, docxB]}
        results={[
          { documentId: 'a', sourceName: 'Cover.docx', status: 'succeeded' },
          {
            documentId: 'b',
            sourceName: 'BA Site A.docx',
            status: 'failed',
            errorCode: 'WordBusy',
            message: 'Word could not render BA Site A.docx.',
          },
        ]}
      />,
    );
    const results = screen.getByRole('list', { name: 'PDF export results' });
    expect(results).toHaveTextContent('BA Site A.docx');
    expect(results).toHaveTextContent('Word could not render BA Site A.docx.');
  });

  it('collision UI requires an explicit decision and never auto-overwrites', () => {
    const onCollisionResolve = vi.fn();
    render(
      <PdfActions
        selection={[docxA]}
        collisions={[
          { documentId: 'a', sourceName: 'Cover.docx', targetPath: 'out/Cover.pdf' },
        ]}
        onCollisionResolve={onCollisionResolve}
      />,
    );
    for (const name of ['Overwrite', 'Rename', 'Skip', 'Cancel']) {
      expect(screen.getByRole('radio', { name })).toBeInTheDocument();
    }
    expect(
      screen.getByRole('checkbox', { name: 'Apply choice to remaining collisions' }),
    ).toBeInTheDocument();
    // No callback fires without an explicit user decision.
    expect(onCollisionResolve).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('radio', { name: 'Rename' }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply collision decision' }));
    expect(onCollisionResolve).toHaveBeenCalledWith('Rename', false);
  });

  it('command-palette entries cover single, separate, and merge', () => {
    const single = getPdfCommandPaletteEntries([docxA]);
    expect(single.map((e) => e.id)).toEqual(['pdf.export-single']);
    const multi = getPdfCommandPaletteEntries([docxA, docxB]);
    expect(multi.map((e) => e.id)).toEqual([
      'pdf.export-separate',
      'pdf.merge',
    ]);
    expect(getPdfCommandPaletteEntries([])).toEqual([]);
  });

  it('non-DOCX selection offers no export (PDF stays output-only)', () => {
    expect(
      getAvailablePdfActions([{ id: 'p', name: 'out.pdf', kind: 'pdf' }]),
    ).toEqual([]);
    render(
      <PdfActions selection={[{ id: 'p', name: 'out.pdf', kind: 'pdf' }]} />,
    );
    expect(screen.getByText('Select one or more DOCX documents to export.')).toBeInTheDocument();
  });
});
