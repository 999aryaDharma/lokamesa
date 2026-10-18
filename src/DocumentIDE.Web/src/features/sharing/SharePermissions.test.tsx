import { render, screen, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { SharePermissions, ShareDownloadButtons } from './SharePermissions';

it('defaults both download permissions to OFF', () => {
  render(<SharePermissions />);

  expect(screen.getByRole('checkbox', { name: /original download/i })).not.toBeChecked();
  expect(screen.getByRole('checkbox', { name: /pdf download/i })).not.toBeChecked();
});

it('reflects owner-provided permissions', () => {
  render(
    <SharePermissions
      permissions={{ allowOriginalDownload: true, allowPdfDownload: false }}
    />,
  );

  expect(screen.getByRole('checkbox', { name: /original download/i })).toBeChecked();
  expect(screen.getByRole('checkbox', { name: /pdf download/i })).not.toBeChecked();
});

it('reports toggled permissions through onChange', () => {
  const onChange = vi.fn();
  const { rerender } = render(<SharePermissions onChange={onChange} />);

  fireEvent.click(screen.getByRole('checkbox', { name: /original download/i }));
  expect(onChange).toHaveBeenLastCalledWith({
    allowOriginalDownload: true,
    allowPdfDownload: false,
  });

  // Controlled usage: the owner applies the reported policy back as props.
  rerender(
    <SharePermissions
      permissions={{ allowOriginalDownload: true, allowPdfDownload: false }}
      onChange={onChange}
    />,
  );
  fireEvent.click(screen.getByRole('checkbox', { name: /pdf download/i }));
  expect(onChange).toHaveBeenLastCalledWith({
    allowOriginalDownload: true,
    allowPdfDownload: true,
  });
});

it('omits viewer download buttons when permissions are OFF', () => {
  render(
    <ShareDownloadButtons
      permissions={{ allowOriginalDownload: false, allowPdfDownload: false }}
      originalDownloadUrl="/share/t/documents/d/download-original"
      pdfDownloadUrl="/share/t/documents/d/download-pdf"
    />,
  );

  expect(screen.queryByRole('link', { name: /original/i })).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: /pdf/i })).not.toBeInTheDocument();
});

it('renders only the enabled viewer download buttons', () => {
  render(
    <ShareDownloadButtons
      permissions={{ allowOriginalDownload: true, allowPdfDownload: false }}
      originalDownloadUrl="/share/t/documents/d/download-original"
      pdfDownloadUrl="/share/t/documents/d/download-pdf"
    />,
  );

  expect(screen.getByRole('link', { name: /original/i })).toHaveAttribute(
    'href',
    '/share/t/documents/d/download-original',
  );
  expect(screen.queryByRole('link', { name: /pdf/i })).not.toBeInTheDocument();
});

it('omits a viewer button when its URL is missing even if enabled', () => {
  render(
    <ShareDownloadButtons
      permissions={{ allowOriginalDownload: true, allowPdfDownload: true }}
      originalDownloadUrl={null}
      pdfDownloadUrl="/share/t/documents/d/download-pdf"
    />,
  );

  expect(screen.queryByRole('link', { name: /original/i })).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: /pdf/i })).toBeInTheDocument();
});
