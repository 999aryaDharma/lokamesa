/**
 * Workspace → canvas bootstrap (gap G6).
 *
 * The shipped app shell previously never populated the canvas store, so the
 * canvas was empty and folder sources could not be exercised. These helpers
 * transform the workspace document/folder-source DTOs into canvas objects
 * (metadata only — never a physical move) and a folder→files map for the
 * folder picker, so the app calls `setObjects`/`upsertObject` for real.
 */
import type { FolderPickerFile } from '../canvas/FolderPicker';
import { placeDocumentsNearOrigin, type PlaceableObject } from '../canvas/layout/AssistedPlacement';
import type { CanvasObject } from '../canvas/types';
import type { WorkspaceDocumentDto, WorkspaceFolderSourceDto } from './workspaceApi';

export const FOLDER_CARD_WIDTH = 160;
export const FOLDER_CARD_HEIGHT = 120;
export const DOCUMENT_CARD_WIDTH = 160;
export const DOCUMENT_CARD_HEIGHT = 220;

const FOLDER_ROW_ORIGIN = { x: 40, y: 40 };
const FOLDER_STRIDE_X = 220;
const DOCUMENT_FAN_ORIGIN = { x: 40, y: 240 };

/** Infer the canvas doc type from a document DTO (never from a path guess). */
export function documentDocType(doc: WorkspaceDocumentDto): 'docx' | 'md' | 'unknown' {
  const type = (doc.documentType ?? '').toLowerCase();
  if (type === 'docx' || type === 'word') return 'docx';
  if (type === 'md' || type === 'markdown') return 'md';
  const ext = (doc.extension ?? '').toLowerCase();
  if (ext === '.docx' || ext === 'docx' || ext === '.doc' || ext === 'doc') return 'docx';
  if (ext === '.md' || ext === 'md' || ext === '.markdown' || ext === 'markdown') return 'md';
  const name = doc.filename.toLowerCase();
  if (name.endsWith('.docx') || name.endsWith('.doc')) return 'docx';
  if (name.endsWith('.md') || name.endsWith('.markdown')) return 'md';
  return 'unknown';
}

/** Folder objects for each attached folder source, laid out in a row. */
export function buildFolderObjects(sources: WorkspaceFolderSourceDto[]): CanvasObject[] {
  return sources.map((source, index) => ({
    id: source.id,
    x: FOLDER_ROW_ORIGIN.x + index * FOLDER_STRIDE_X,
    y: FOLDER_ROW_ORIGIN.y,
    width: FOLDER_CARD_WIDTH,
    height: FOLDER_CARD_HEIGHT,
    title: source.displayName || basename(source.canonicalPath) || source.id,
    type: 'folder',
    statusText: source.availabilityStatus && source.availabilityStatus !== 'available' ? source.availabilityStatus : undefined,
  }));
}

/** Document objects placed compactly near the canvas origin (spec 03 §8). */
export function buildDocumentObjects(
  documents: WorkspaceDocumentDto[],
  folders: CanvasObject[],
): CanvasObject[] {
  const specs = documents.map((doc) => ({
    id: doc.id,
    title: doc.filename,
    width: DOCUMENT_CARD_WIDTH,
    height: DOCUMENT_CARD_HEIGHT,
  }));

  const existing: PlaceableObject[] = folders.map((folder) => ({
    ...folder,
    positionMode: 'pinned',
  }));

  const result = placeDocumentsNearOrigin(existing, specs, {
    origin: DOCUMENT_FAN_ORIGIN,
    columns: 4,
    gap: 24,
  });

  const placedById = new Map(result.placements.map((p) => [p.id, p]));

  return documents.map((doc, index) => {
    const placed = placedById.get(doc.id);
    const fallbackX = DOCUMENT_FAN_ORIGIN.x + (index % 4) * (DOCUMENT_CARD_WIDTH + 24);
    const fallbackY =
      DOCUMENT_FAN_ORIGIN.y + Math.floor(index / 4) * (DOCUMENT_CARD_HEIGHT + 24);
    return {
      id: doc.id,
      x: placed?.x ?? fallbackX,
      y: placed?.y ?? fallbackY,
      width: DOCUMENT_CARD_WIDTH,
      height: DOCUMENT_CARD_HEIGHT,
      title: doc.filename,
      type: 'document',
      docType: documentDocType(doc),
      isMissing: doc.availabilityStatus !== undefined && doc.availabilityStatus !== 'available',
      statusText:
        doc.availabilityStatus && doc.availabilityStatus !== 'available'
          ? doc.availabilityStatus
          : undefined,
    } satisfies CanvasObject;
  });
}

function normalizePath(value: string): string {
  return value.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
}

/** True when `documentPath` lives inside `folderPath` (separator-bounded). */
export function isWithinFolder(documentPath: string, folderPath: string): boolean {
  const doc = normalizePath(documentPath);
  const folder = normalizePath(folderPath);
  if (!doc || !folder) return false;
  if (doc === folder) return true;
  return doc.startsWith(`${folder}/`);
}

/** Supported files per folder object id for the folder picker (CAN-04). */
export function groupDocumentsByFolder(
  documents: WorkspaceDocumentDto[],
  sources: WorkspaceFolderSourceDto[],
): Record<string, FolderPickerFile[]> {
  // Deepest paths first so the most specific source wins a nested document.
  const ordered = [...sources].sort(
    (a, b) => normalizePath(b.canonicalPath).length - normalizePath(a.canonicalPath).length,
  );
  const result: Record<string, FolderPickerFile[]> = {};
  for (const source of sources) result[source.id] = [];

  for (const doc of documents) {
    const path = doc.currentPath ?? '';
    if (!path) continue;
    const owner = ordered.find((source) => isWithinFolder(path, source.canonicalPath));
    if (!owner) continue;
    result[owner.id].push({ documentId: doc.id, filename: doc.filename });
  }

  // Deterministic listing order for the picker.
  for (const id of Object.keys(result)) {
    result[id].sort((a, b) => a.filename.localeCompare(b.filename));
  }
  return result;
}

export interface WorkspaceBootstrapResult {
  objects: CanvasObject[];
  folderObjects: CanvasObject[];
  documentObjects: CanvasObject[];
  folderFiles: Record<string, FolderPickerFile[]>;
  folderSources: WorkspaceFolderSourceDto[];
  documents: WorkspaceDocumentDto[];
}

function basename(value: string): string {
  const parts = value.replace(/\\/g, '/').split('/').filter(Boolean);
  return parts.length > 0 ? parts[parts.length - 1] : '';
}

/**
 * Load and map a workspace into canvas-ready state. A failed load degrades to
 * an empty (but valid) result so the shell never crashes on an unavailable API.
 */
export async function bootstrapWorkspace(
  workspaceId: string,
  loaders: {
    documents: (id: string) => Promise<WorkspaceDocumentDto[]>;
    folderSources: (id: string) => Promise<WorkspaceFolderSourceDto[]>;
  },
): Promise<WorkspaceBootstrapResult> {
  const [documents, folderSources] = await Promise.all([
    loaders.documents(workspaceId).catch(() => [] as WorkspaceDocumentDto[]),
    loaders.folderSources(workspaceId).catch(() => [] as WorkspaceFolderSourceDto[]),
  ]);

  const folderObjects = buildFolderObjects(folderSources);
  const documentObjects = buildDocumentObjects(documents, folderObjects);

  return {
    objects: [...folderObjects, ...documentObjects],
    folderObjects,
    documentObjects,
    folderFiles: groupDocumentsByFolder(documents, folderSources),
    folderSources,
    documents,
  };
}
