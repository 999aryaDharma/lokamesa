import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, test, expect, vi } from 'vitest';
import { QuickLook, useQuickLookKeyboard, type QuickLookPageData } from './QuickLook';

function pageData(pageIndex: number, totalPages = 3): QuickLookPageData {
  return {
    pageIndex,
    totalPages,
    artifactId: `artifact-${pageIndex}`,
    pageText: `page ${pageIndex} content alpha beta`,
    imageUrl: null,
  };
}

function Harness({
  onClose = () => {},
  loadPage,
  open = true,
  searchTerms = [],
  editingInWord = false,
}: {
  onClose?: () => void;
  loadPage: (page: number) => Promise<QuickLookPageData>;
  open?: boolean;
  searchTerms?: string[];
  editingInWord?: boolean;
}) {
  return (
    <QuickLook
      documentId="doc-1"
      fileName="Report.docx"
      totalPages={3}
      open={open}
      onClose={onClose}
      loadPage={loadPage}
      searchTerms={searchTerms}
      editingInWord={editingInWord}
      editingSummary={editingInWord ? 'Likely editing in Word (2 of 3 signals positive).' : null}
    />
  );
}

function KeyboardHarness({
  open,
  onOpen,
  onClose,
}: {
  open: boolean;
  onOpen: () => void;
  onClose: () => void;
}) {
  useQuickLookKeyboard({ open, onOpen, onClose });
  return <div />;
}

describe('QuickLook', () => {
  test('renders nothing and loads nothing when closed (no eager render)', () => {
    const loadPage = vi.fn(async (p: number) => pageData(p));
    const { container } = render(<Harness open={false} loadPage={loadPage} />);
    expect(container).toBeEmptyDOMElement();
    expect(loadPage).not.toHaveBeenCalled();
  });

  test('Space opens and Escape closes via keyboard hook', () => {
    const onOpen = vi.fn();
    const onClose = vi.fn();
    const { rerender } = render(
      <KeyboardHarness open={false} onOpen={onOpen} onClose={onClose} />,
    );

    fireEvent.keyDown(document, { key: ' ' });
    expect(onOpen).toHaveBeenCalledTimes(1);

    // Escape is a no-op while closed, and closes while open.
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();

    rerender(<KeyboardHarness open={true} onOpen={onOpen} onClose={onClose} />);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  test('Escape closes the open overlay', async () => {
    const onClose = vi.fn();
    render(<Harness onClose={onClose} loadPage={async (p) => pageData(p)} />);
    await screen.findByTestId('quick-look-dialog');

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  test('lazily loads only the visible page, then the next page on navigation', async () => {
    const loadPage = vi.fn(async (p: number) => pageData(p));
    render(<Harness loadPage={loadPage} />);

    await screen.findByTestId('quick-look-page-0');
    expect(loadPage).toHaveBeenCalledTimes(1);
    expect(loadPage).toHaveBeenCalledWith(0);

    fireEvent.click(screen.getByTestId('quick-look-next'));
    await screen.findByTestId('quick-look-page-1');
    expect(loadPage).toHaveBeenCalledWith(1);
    expect(loadPage).toHaveBeenCalledTimes(2);
  });

  test('highlights active search terms in page text', async () => {
    render(<Harness loadPage={async (p) => pageData(p)} searchTerms={['alpha']} />);
    await screen.findByTestId('quick-look-page-0');

    const marks = screen.getAllByTestId('quick-look-match');
    expect(marks.length).toBeGreaterThan(0);
    expect(marks[0]).toHaveTextContent('alpha');
  });

  test('surfaces editing-in-word state without blocking preview', async () => {
    render(
      <Harness loadPage={async (p) => pageData(p)} editingInWord={true} />,
    );
    await screen.findByTestId('quick-look-page-0');

    const badge = screen.getByTestId('quick-look-editing');
    expect(badge).toHaveTextContent('Editing in Word');
    expect(badge).toHaveAttribute(
      'title',
      'Likely editing in Word (2 of 3 signals positive).',
    );
  });

  test('shows a typed error when the render backend reports Word unavailable', async () => {
    const loadPage = vi.fn(async () => {
      throw { code: 'WordUnavailable', message: 'Microsoft Word is required for this operation.' };
    });
    render(<Harness loadPage={loadPage} />);

    await waitFor(() => {
      expect(screen.getByTestId('quick-look-error')).toHaveTextContent(
        'Microsoft Word is required for this operation.',
      );
    });
  });
});
