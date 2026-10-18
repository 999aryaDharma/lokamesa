import { describe, expect, it, vi } from 'vitest';
import {
  bootstrapWorkspace,
  buildDocumentObjects,
  buildFolderObjects,
  documentDocType,
  groupDocumentsByFolder,
  isWithinFolder,
} from './workspaceBootstrap';

const folderA = { id: 'fs-a', workspaceId: 'ws', canonicalPath: 'C:\\Docs\\BA', displayName: 'BA' };
const folderB = { id: 'fs-b', workspaceId: 'ws', canonicalPath: 'C:\\Docs\\Indodax', displayName: 'Indodax' };

describe('documentDocType', () => {
  it('prefers the explicit documentType then the extension', () => {
    expect(documentDocType({ id: '1', filename: 'a.docx', documentType: 'docx' })).toBe('docx');
    expect(documentDocType({ id: '2', filename: 'b.md', extension: '.md' })).toBe('md');
    expect(documentDocType({ id: '3', filename: 'c.markdown' })).toBe('md');
    expect(documentDocType({ id: '4', filename: 'plain' })).toBe('unknown');
  });
});

describe('isWithinFolder', () => {
  it('is separator-bounded and case-insensitive', () => {
    expect(isWithinFolder('C:\\Docs\\BA\\x.md', 'C:\\Docs\\BA')).toBe(true);
    expect(isWithinFolder('C:\\Docs\\BA2\\x.md', 'C:\\Docs\\BA')).toBe(false);
  });
});

describe('buildFolderObjects', () => {
  it('creates one folder object per source in a row', () => {
    const objects = buildFolderObjects([folderA, folderB]);
    expect(objects).toHaveLength(2);
    expect(objects[0]).toMatchObject({ id: 'fs-a', type: 'folder', title: 'BA' });
    expect(objects[1].x).toBeGreaterThan(objects[0].x);
  });
});

describe('buildDocumentObjects', () => {
  it('creates document objects near the folder origin without overlap', () => {
    const folders = buildFolderObjects([folderA]);
    const objects = buildDocumentObjects(
      [
        { id: 'd-1', filename: 'a.md', currentPath: 'C:\\Docs\\BA\\a.md', documentType: 'markdown' },
        { id: 'd-2', filename: 'b.docx', currentPath: 'C:\\Docs\\BA\\b.docx', documentType: 'docx' },
      ],
      folders,
    );

    expect(objects).toHaveLength(2);
    expect(objects[0].docType).toBe('md');
    expect(objects[1].docType).toBe('docx');
    // Distinct positions (never stacked at one coordinate).
    expect(objects[0].x !== objects[1].x || objects[0].y !== objects[1].y).toBe(true);
  });
});

describe('groupDocumentsByFolder', () => {
  it('groups documents under the deepest matching source only', () => {
    const map = groupDocumentsByFolder(
      [
        { id: 'd-1', filename: 'a.md', currentPath: 'C:\\Docs\\BA\\a.md' },
        { id: 'd-2', filename: 'b.md', currentPath: 'C:\\Docs\\Indodax\\b.md' },
        { id: 'd-3', filename: 'c.md', currentPath: 'C:\\Other\\c.md' },
      ],
      [folderA, folderB],
    );

    expect(map['fs-a'].map((f) => f.documentId)).toEqual(['d-1']);
    expect(map['fs-b'].map((f) => f.documentId)).toEqual(['d-2']);
    expect(map['fs-a'].some((f) => f.documentId === 'd-3')).toBe(false);
  });
});

describe('bootstrapWorkspace', () => {
  it('loads documents + folder sources and returns canvas-ready objects', async () => {
    const result = await bootstrapWorkspace('ws', {
      documents: async () => [
        { id: 'd-1', filename: 'a.md', currentPath: 'C:\\Docs\\BA\\a.md', documentType: 'markdown' },
      ],
      folderSources: async () => [folderA],
    });

    expect(result.objects.some((o) => o.type === 'folder' && o.id === 'fs-a')).toBe(true);
    expect(result.objects.some((o) => o.type === 'document' && o.id === 'd-1')).toBe(true);
    expect(result.folderFiles['fs-a'][0].documentId).toBe('d-1');
  });

  it('degrades to an empty result when the API is unavailable', async () => {
    const loader = vi.fn().mockRejectedValue(new Error('offline'));
    const result = await bootstrapWorkspace('ws', { documents: loader, folderSources: loader });
    expect(result.objects).toEqual([]);
    expect(result.folderFiles).toEqual({});
  });
});
