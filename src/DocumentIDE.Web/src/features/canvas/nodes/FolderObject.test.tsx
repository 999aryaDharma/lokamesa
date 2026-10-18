/**
 * UI-01 — interactive folder object (owner directive 2026-10-18).
 *
 * The folder is an object, not a static glyph: closed by default, it opens on
 * click and fans out the documents it really contains (the same path
 * containment the CAN-04 picker already uses), and a fanned page can be
 * released with the existing drag-out semantic (metadata only).
 */
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import FolderObject from './FolderObject';

const contained = [
  { documentId: 'doc-in-1', filename: 'a.md' },
  { documentId: 'doc-in-2', filename: 'b.md' },
];

/** Page previews read the real markdown route; unit tests inject a stub so no
 *  request leaves the component under test. */
const stubLoad = async (documentId: string) => ({
  documentId,
  source: `# ${documentId}\n\ncontent line`,
});

function stubReducedMotion(matches: boolean) {
  const fn = (query: string) => ({
    matches: matches && query.includes('prefers-reduced-motion'),
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  });
  vi.stubGlobal('matchMedia', fn);
  (window as Window & { matchMedia: typeof window.matchMedia }).matchMedia =
    fn as unknown as typeof window.matchMedia;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('FolderObject', () => {
  it('is closed by default and opens on click, fanning out the contained documents', () => {
    render(<FolderObject id="folder-1" title="Specs" containedDocuments={contained} loadMarkdown={stubLoad} />);

    const folder = screen.getByTestId('folder-object');
    expect(folder).toHaveAttribute('data-open', 'false');
    expect(screen.queryByTestId('folder-page-doc-in-1')).not.toBeInTheDocument();

    fireEvent.click(folder);
    expect(folder).toHaveAttribute('data-open', 'true');
    expect(screen.getByTestId('folder-page-doc-in-1')).toBeInTheDocument();
    expect(screen.getByTestId('folder-page-doc-in-2')).toBeInTheDocument();

    fireEvent.click(folder);
    expect(folder).toHaveAttribute('data-open', 'false');
    expect(screen.queryByTestId('folder-page-doc-in-1')).not.toBeInTheDocument();
  });

  it('fans out exactly what it is given and nothing else', () => {
    render(
      <FolderObject
        id="folder-1"
        title="Specs"
        containedDocuments={[{ documentId: 'doc-in-1', filename: 'a.md' }]}
        loadMarkdown={stubLoad}
      />,
    );
    fireEvent.click(screen.getByTestId('folder-object'));

    expect(screen.getAllByTestId(/^folder-page-/)).toHaveLength(1);
    expect(screen.queryByTestId('folder-page-doc-outside')).not.toBeInTheDocument();
  });

  it('opens to an empty state when the folder holds no documents', () => {
    render(<FolderObject id="folder-2" title="Empty" containedDocuments={[]} />);
    const folder = screen.getByTestId('folder-object');
    fireEvent.click(folder);

    expect(folder).toHaveAttribute('data-open', 'true');
    expect(screen.queryAllByTestId(/^folder-page-/)).toHaveLength(0);
    expect(screen.getByTestId('folder-open-empty')).toBeInTheDocument();
  });

  it('still opens and closes with prefers-reduced-motion', () => {
    stubReducedMotion(true);
    render(<FolderObject id="folder-1" title="Specs" containedDocuments={contained} loadMarkdown={stubLoad} />);

    const folder = screen.getByTestId('folder-object');
    fireEvent.click(folder);
    expect(folder).toHaveAttribute('data-open', 'true');
    expect(screen.getByTestId('folder-page-doc-in-1')).toBeInTheDocument();

    fireEvent.click(folder);
    expect(folder).toHaveAttribute('data-open', 'false');
  });

  it('releases a fanned page on drag-out using the metadata-only semantic', () => {
    const onReleaseDocument = vi.fn();
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    render(
      <FolderObject
        id="folder-1"
        title="Specs"
        containedDocuments={contained}
        loadMarkdown={stubLoad}
        onReleaseDocument={onReleaseDocument}
      />,
    );

    fireEvent.click(screen.getByTestId('folder-object'));
    const page = screen.getByTestId('folder-page-doc-in-1');

    fireEvent.mouseDown(page, { button: 0, clientX: 100, clientY: 100 });
    fireEvent.mouseMove(window, { clientX: 320, clientY: 60 });
    fireEvent.mouseUp(window);

    expect(onReleaseDocument).toHaveBeenCalledWith('doc-in-1');
    // Canvas drag never physically moves a file: no request of any kind is made.
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('does not release a page that was only clicked', () => {
    const onReleaseDocument = vi.fn();
    render(
      <FolderObject
        id="folder-1"
        title="Specs"
        containedDocuments={contained}
        loadMarkdown={stubLoad}
        onReleaseDocument={onReleaseDocument}
      />,
    );

    fireEvent.click(screen.getByTestId('folder-object'));
    const page = screen.getByTestId('folder-page-doc-in-1');
    fireEvent.mouseDown(page, { button: 0, clientX: 100, clientY: 100 });
    fireEvent.mouseUp(window);

    expect(onReleaseDocument).not.toHaveBeenCalled();
  });

  it('keeps the existing accessible name and the double-click open-all path', () => {
    const onDoubleClick = vi.fn();
    render(
      <FolderObject
        id="folder-1"
        title="Specs"
        containedDocuments={contained}
        loadMarkdown={stubLoad}
        onDoubleClick={onDoubleClick}
      />,
    );

    const folder = screen.getByRole('button', { name: 'Folder: Specs' });
    fireEvent.doubleClick(folder);
    expect(onDoubleClick).toHaveBeenCalledWith('folder-1');
  });

  it('keeps the existing single-click → picker wiring reachable', () => {
    const onOpenPicker = vi.fn();
    render(
      <FolderObject
        id="folder-1"
        title="Specs"
        containedDocuments={contained}
        loadMarkdown={stubLoad}
        onOpenPicker={onOpenPicker}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Open documents in Specs' }));
    expect(onOpenPicker).toHaveBeenCalledWith('folder-1');
  });

  it('is structurally a folder, never a file sheet', () => {
    render(<FolderObject id="folder-1" title="Specs" containedDocuments={[]} />);
    const folder = screen.getByTestId('folder-object');
    expect(folder).toHaveAttribute('data-kind', 'folder');
    expect(folder.querySelector('[data-part="folder-body"]')).not.toBeNull();
    expect(folder.querySelector('[data-part="folder-front"]')).not.toBeNull();
    expect(folder.querySelector('[data-part="file-sheet"]')).toBeNull();
  });

  it('keeps the picker affordance outside the folder button (no nested interactive control)', () => {
    render(
      <FolderObject
        id="folder-1"
        title="Specs"
        containedDocuments={[]}
        onOpenPicker={() => {}}
      />,
    );

    const folder = screen.getByTestId('folder-object');
    // A real <button> inside a role="button" subtree is a nested interactive
    // control: AT announces two buttons and only one of them is operable.
    expect(folder.querySelector('button')).toBeNull();

    const picker = screen.getByTestId('folder-picker-folder-1');
    expect(picker.tagName).toBe('BUTTON');
    expect(folder.contains(picker)).toBe(false);
    expect(folder.parentElement?.contains(picker)).toBe(true);
  });

  it('exposes selection with aria-pressed, never aria-selected (valid on role=button)', () => {
    const { rerender } = render(
      <FolderObject id="folder-1" title="Specs" containedDocuments={[]} />,
    );
    const folder = screen.getByTestId('folder-object');
    expect(folder).not.toHaveAttribute('aria-selected');
    expect(folder).toHaveAttribute('aria-pressed', 'false');

    rerender(<FolderObject id="folder-1" title="Specs" containedDocuments={[]} isSelected />);
    expect(screen.getByTestId('folder-object')).toHaveAttribute('aria-pressed', 'true');
  });
});
