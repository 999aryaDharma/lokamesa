/**
 * UI-01 — canvas-level folder/file object tests (owner directive 2026-10-18).
 *
 * Proves that:
 *   - a folder object and a file object are structurally distinct objects, so
 *     they can never silently regress into the same look-alike card;
 *   - the documents a folder fans out are exactly the ones that path-belong to
 *     that folder, derived from the existing `groupDocumentsByFolder`
 *     containment rule (an outside document must not appear);
 *   - releasing a fanned page keeps the CAN-04 semantic: metadata only, with no
 *     filesystem mutation path invoked.
 */
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import DocumentCanvas from './DocumentCanvas';
import { useCanvasStore } from './canvasStore';
import type { CanvasObject } from './types';
import DocumentNode from './nodes/DocumentNode';
import FolderObject from './nodes/FolderObject';
import { groupDocumentsByFolder } from '../workspace/workspaceBootstrap';
import type {
  WorkspaceDocumentDto,
  WorkspaceFolderSourceDto,
} from '../workspace/workspaceApi';

const folderId = 'folder-1';

const documents: WorkspaceDocumentDto[] = [
  {
    id: 'doc-in-1',
    filename: 'a.md',
    currentPath: 'C:/Docs/a.md',
    availabilityStatus: 'available',
  },
  {
    id: 'doc-in-2',
    filename: 'b.md',
    currentPath: 'C:/Docs/sub/b.md',
    availabilityStatus: 'available',
  },
  {
    id: 'doc-outside',
    filename: 'c.md',
    currentPath: 'C:/Other/c.md',
    availabilityStatus: 'available',
  },
];

const sources: WorkspaceFolderSourceDto[] = [
  {
    id: folderId,
    workspaceId: 'default',
    canonicalPath: 'C:/Docs',
    displayName: 'Docs',
    availabilityStatus: 'available',
  },
];

const folderFiles = groupDocumentsByFolder(documents, sources);

const folderObject: CanvasObject = {
  id: folderId,
  x: 0,
  y: 0,
  width: 140,
  height: 122,
  title: 'Docs',
  type: 'folder',
};

const documentObjects: CanvasObject[] = documents.map((doc, index) => ({
  id: doc.id,
  x: 200 + index * 160,
  y: 0,
  width: 140,
  height: 122,
  title: doc.filename,
  type: 'document',
  docType: 'md',
}));

beforeEach(() => {
  useCanvasStore.getState().reset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('canvas folder objects', () => {
  it('fans out exactly the documents that path-belong to the folder', () => {
    useCanvasStore.getState().setObjects([folderObject, ...documentObjects]);
    render(<DocumentCanvas workspaceId="ws-1" folderFiles={folderFiles} />);

    fireEvent.click(screen.getByRole('button', { name: 'Folder: Docs' }));

    // The containment rule is separator-bounded: C:/Docs/sub/b.md is inside,
    // C:/Other/c.md is not.
    expect(screen.getByTestId('folder-page-doc-in-1')).toBeInTheDocument();
    expect(screen.getByTestId('folder-page-doc-in-2')).toBeInTheDocument();
    expect(screen.queryByTestId('folder-page-doc-outside')).not.toBeInTheDocument();
  });

  it('releases a fanned page through the existing metadata-only CAN-04 path', () => {
    const onReleaseDocuments = vi.fn();
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    useCanvasStore.getState().setObjects([folderObject, ...documentObjects]);
    render(
      <DocumentCanvas
        workspaceId="ws-1"
        folderFiles={folderFiles}
        onReleaseDocuments={onReleaseDocuments}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Folder: Docs' }));
    const page = screen.getByTestId('folder-page-doc-in-2');
    fireEvent.mouseDown(page, { button: 0, clientX: 100, clientY: 100 });
    fireEvent.mouseMove(window, { clientX: 340, clientY: 40 });
    fireEvent.mouseUp(window);

    expect(onReleaseDocuments).toHaveBeenCalledWith(folderId, ['doc-in-2']);
    // No filesystem mutation path is invoked — the drag is pure metadata. The
    // only requests present are the read-only preview GETs of the `.md` pages.
    const mutating = fetchSpy.mock.calls.filter(([, init]) => {
      const method = (init as RequestInit | undefined)?.method;
      return typeof method === 'string' && method.toUpperCase() !== 'GET';
    });
    expect(mutating).toHaveLength(0);
  });

  it('keeps the existing single-click → picker affordance reachable', () => {
    const onReleaseDocuments = vi.fn();
    useCanvasStore.getState().setObjects([folderObject, ...documentObjects]);
    render(
      <DocumentCanvas
        workspaceId="ws-1"
        folderFiles={folderFiles}
        onReleaseDocuments={onReleaseDocuments}
      />,
    );

    // The picker affordance opens the CAN-04 folder picker; "Open all" from
    // there still uses the release semantic (folders never physically move).
    fireEvent.click(screen.getByRole('button', { name: 'Open documents in Docs' }));
    expect(screen.getByTestId('folder-picker')).toBeInTheDocument();
  });
});

describe('folder and file objects are structurally distinct', () => {
  it('renders a folder object and a file object with different structure', () => {
    render(
      <>
        <FolderObject id="f1" title="Specs" containedDocuments={[]} />
        <DocumentNode id="d1" title="Specs.docx" docType="docx" />
      </>,
    );

    const folder = screen.getByTestId('folder-object');
    const file = screen.getByTestId('document-paper-sheet');

    expect(folder).toHaveAttribute('data-kind', 'folder');
    expect(file).toHaveAttribute('data-kind', 'docx');

    expect(folder.querySelector('[data-part="folder-body"]')).not.toBeNull();
    expect(folder.querySelector('[data-part="file-sheet"]')).toBeNull();
    expect(file.querySelector('[data-part="file-sheet"]')).not.toBeNull();
    expect(file.querySelector('[data-part="folder-body"]')).toBeNull();

    // The file carries its type accent; the folder never does.
    expect(file.querySelector('[data-part="type-accent"]')).not.toBeNull();
    expect(folder.querySelector('[data-part="type-accent"]')).toBeNull();
  });
});
