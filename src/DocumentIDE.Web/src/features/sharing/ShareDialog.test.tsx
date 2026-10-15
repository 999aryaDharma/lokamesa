import { render, screen, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { ShareDialog } from './ShareDialog';

const documents = [
  { id: 'doc-a', displayName: 'Alpha.md' },
  { id: 'doc-b', displayName: 'Beta.docx' },
  { id: 'doc-c', displayName: 'Gamma.md' },
];

it('creates a share from the explicit selection only (no auto-publish)', () => {
  const onCreate = vi.fn();
  render(
    <ShareDialog
      availableDocuments={documents}
      initialSelectedIds={['doc-a']}
      onCreate={onCreate}
    />,
  );

  expect(screen.getByText('Alpha.md')).toBeInTheDocument();
  expect(screen.getByText('Beta.docx')).toBeInTheDocument();

  fireEvent.click(screen.getByRole('button', { name: /create share/i }));

  expect(onCreate).toHaveBeenCalledTimes(1);
  expect(onCreate).toHaveBeenCalledWith(
    expect.objectContaining({ memberDocumentIds: ['doc-a'] }),
  );
});

it('creating a share requires an explicit action', () => {
  const onCreate = vi.fn();
  render(
    <ShareDialog
      availableDocuments={documents}
      initialSelectedIds={['doc-a', 'doc-b']}
      onCreate={onCreate}
    />,
  );

  fireEvent.click(screen.getByRole('checkbox', { name: /gamma/i }));

  expect(onCreate).not.toHaveBeenCalled();
});

it('chooses Snapshot/Live mode and download permissions', () => {
  const onCreate = vi.fn();
  render(<ShareDialog availableDocuments={documents} onCreate={onCreate} />);

  fireEvent.click(screen.getByRole('checkbox', { name: /alpha/i }));
  fireEvent.click(screen.getByRole('radio', { name: /live/i }));
  fireEvent.click(screen.getByRole('checkbox', { name: /original download/i }));

  fireEvent.click(screen.getByRole('button', { name: /create share/i }));

  expect(onCreate).toHaveBeenCalledWith({
    mode: 'Live',
    memberDocumentIds: ['doc-a'],
    allowOriginalDownload: true,
    allowPdfDownload: false,
  });
});

it('defaults to Snapshot mode with downloads OFF', () => {
  const onCreate = vi.fn();
  render(
    <ShareDialog
      availableDocuments={documents}
      initialSelectedIds={['doc-b']}
      onCreate={onCreate}
    />,
  );

  expect(screen.getByRole('radio', { name: /snapshot/i })).toBeChecked();

  fireEvent.click(screen.getByRole('button', { name: /create share/i }));

  expect(onCreate).toHaveBeenCalledWith({
    mode: 'Snapshot',
    memberDocumentIds: ['doc-b'],
    allowOriginalDownload: false,
    allowPdfDownload: false,
  });
});
