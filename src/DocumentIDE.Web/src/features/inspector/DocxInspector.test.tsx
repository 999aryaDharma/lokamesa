import { render, screen, fireEvent } from '@testing-library/react';
import { describe, test, expect, vi } from 'vitest';
import { DocxInspector, type DocxLensMetadataDto } from './DocxInspector';

describe('DocxInspector Component', () => {
  const sampleMetadata: DocxLensMetadataDto = {
    documentId: 'doc-laporan-januari',
    filename: 'Laporan Januari.docx',
    filePath: 'D:\\Project\\BA\\Laporan Januari.docx',
    modifiedAt: '2026-01-31T08:15:00Z',
    sizeBytes: 482_304,
    pageCount: 14,
    sectionCount: 2,
    tableCount: 4,
    imageCount: 7,
    paperSummary: 'A4',
    orientationSummary: 'Portrait',
    marginSummary: '4 / 4 / 3 / 3 cm',
    primaryFontSummary: 'Times New Roman',
    primaryFontSizeSummary: '12 pt',
    headings: [
      { level: 1, text: 'Pendahuluan' },
      { level: 2, text: 'Kondisi Site' },
      { level: 2, text: 'Hasil Pemeriksaan' },
      { level: 1, text: 'Kesimpulan' },
    ],
    headerPresent: true,
    footerPresent: true,
    status: 'ready',
  };

  test('Inspector displays fixture metadata', () => {
    render(<DocxInspector metadata={sampleMetadata} />);

    expect(screen.getByText('Laporan Januari.docx')).toBeInTheDocument();
    expect(screen.getByText('D:\\Project\\BA\\Laporan Januari.docx')).toBeInTheDocument();

    expect(screen.getByText('14 pages')).toBeInTheDocument();
    expect(screen.getByText('2 sections')).toBeInTheDocument();
    expect(screen.getByText('4 tables')).toBeInTheDocument();
    expect(screen.getByText('7 images')).toBeInTheDocument();

    expect(screen.getByText('Paper (summary)')).toBeInTheDocument();
    expect(screen.getByText('A4')).toBeInTheDocument();
    expect(screen.getByText('Orientation (summary)')).toBeInTheDocument();
    expect(screen.getByText('Portrait')).toBeInTheDocument();
    expect(screen.getByText('Margins (summary)')).toBeInTheDocument();
    expect(screen.getByText('4 / 4 / 3 / 3 cm')).toBeInTheDocument();
    expect(screen.getByText('Main font (summary)')).toBeInTheDocument();
    expect(screen.getByText('Times New Roman')).toBeInTheDocument();
    expect(screen.getByText('Main size (summary)')).toBeInTheDocument();
    expect(screen.getByText('12 pt')).toBeInTheDocument();

    expect(screen.getByText('Pendahuluan')).toBeInTheDocument();
    expect(screen.getByText('Kondisi Site')).toBeInTheDocument();
    expect(screen.getByText('Hasil Pemeriksaan')).toBeInTheDocument();
    expect(screen.getByText('Kesimpulan')).toBeInTheDocument();
    // Heading detection is a derived view, so the section is labeled as detected.
    expect(screen.getByText('Detected headings (4)')).toBeInTheDocument();

    expect(screen.getByTestId('docx-header-state')).toHaveTextContent('Present');
    expect(screen.getByTestId('docx-footer-state')).toHaveTextContent('Present');
  });

  test('Metadata click can invoke search filter where supported', () => {
    const handleApplyFilter = vi.fn();
    render(<DocxInspector metadata={sampleMetadata} onApplyMetadataFilter={handleApplyFilter} />);

    fireEvent.click(screen.getByTestId('docx-filter-paper'));
    expect(handleApplyFilter).toHaveBeenLastCalledWith({ field: 'paper', value: 'A4' });

    fireEvent.click(screen.getByTestId('docx-filter-margin'));
    expect(handleApplyFilter).toHaveBeenLastCalledWith({ field: 'margin', value: '4 / 4 / 3 / 3 cm' });

    fireEvent.click(screen.getByTestId('docx-filter-font'));
    expect(handleApplyFilter).toHaveBeenLastCalledWith({ field: 'font', value: 'Times New Roman' });

    // SRCH-03 font-size filters use numeric values, so the clickable value
    // must be normalized for the filter query token (e.g. font-size:12).
    fireEvent.click(screen.getByTestId('docx-filter-font-size'));
    expect(handleApplyFilter).toHaveBeenLastCalledWith({ field: 'font-size', value: '12' });

    fireEvent.click(screen.getByTestId('docx-filter-heading-0'));
    expect(handleApplyFilter).toHaveBeenLastCalledWith({ field: 'heading', value: 'Pendahuluan' });

    expect(handleApplyFilter).toHaveBeenCalledTimes(5);
  });

  test('Unsupported metadata values do not invoke a search filter', () => {
    const handleApplyFilter = vi.fn();
    render(<DocxInspector metadata={sampleMetadata} onApplyMetadataFilter={handleApplyFilter} />);

    // Orientation and the structural counts have no SRCH-03 metadata filter
    // field, so they must stay plain text rather than clickable filters.
    expect(screen.queryByTestId('docx-filter-orientation')).toBeNull();
    expect(screen.queryByTestId('docx-filter-page-count')).toBeNull();

    fireEvent.click(screen.getByText('Portrait'));
    expect(handleApplyFilter).not.toHaveBeenCalled();
  });

  test('Inspector displays file size and modified time when provided', () => {
    render(<DocxInspector metadata={sampleMetadata} />);

    expect(screen.getByText('471 KB')).toBeInTheDocument();
    expect(screen.getByText('2026-01-31 08:15 UTC')).toBeInTheDocument();
  });

  test('Inspector shows the typed failure state when metadata is unavailable', () => {
    render(
      <DocxInspector
        metadata={{
          filename: 'Rusak.docx',
          headings: [],
          headerPresent: false,
          footerPresent: false,
          status: 'error',
          errorCode: 'PackageValidationFailed',
          errorMessage: 'DOCX package validation failed: Invalid ZIP archive.',
        }}
      />,
    );

    expect(screen.getByText('Metadata unavailable')).toBeInTheDocument();
    expect(screen.getByText('PackageValidationFailed')).toBeInTheDocument();
    expect(screen.getByText('DOCX package validation failed: Invalid ZIP archive.')).toBeInTheDocument();
    expect(screen.queryByText('Paper (summary)')).toBeNull();
  });

  test('Inspector marks derived metadata as stale when the source changed', () => {
    render(<DocxInspector metadata={{ ...sampleMetadata, status: 'stale' }} />);

    expect(screen.getByText('Derived metadata may be stale')).toBeInTheDocument();
    expect(screen.getByText('A4')).toBeInTheDocument();
  });
});
