import React from 'react';

/**
 * UI-01 primitive — one small folder/file icon for every *list* surface.
 *
 * Owner directive (2026-10-18): a folder and a file must never be the same
 * look-alike box. On spatial surfaces (canvas, graph) they become objects; in
 * lists (list view, sidebar `FOLDER SOURCES`, source-control change rows,
 * inspector reference cards) they follow the Finder convention and get a small
 * type icon instead.
 *
 * `documentIconKind` mirrors the extension inference already used by
 * `DocumentNode` and `DocumentList`, so the icon always agrees with the type
 * label the row shows.
 */
export type NodeIconKind = 'folder' | 'docx' | 'md' | 'unknown';

export type NodeIconDocType = 'docx' | 'md' | 'unknown';

export function documentIconKind(title: string, docType?: NodeIconDocType): NodeIconKind {
  if (docType === 'docx') return 'docx';
  if (docType === 'md') return 'md';
  const lower = title.toLowerCase();
  if (lower.endsWith('.docx') || lower.endsWith('.doc')) return 'docx';
  if (lower.endsWith('.md') || lower.endsWith('.markdown')) return 'md';
  return 'unknown';
}

/** Type accent colours, shared with the canvas file object. */
export const NODE_ICON_COLORS: Record<NodeIconKind, string> = {
  folder: '#3E8EF7',
  docx: '#007AFF',
  md: '#34C759',
  unknown: '#8E8E93',
};

/**
 * Raw icon paths, for callers that must draw inside an SVG of their own (the
 * graph node draws its type glyph inside the node circle rather than embedding
 * an `<svg>`).
 */
export const NODE_ICON_PATHS: Record<'folder' | 'file', readonly string[]> = {
  folder: ['M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z'],
  file: ['M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z', 'M14 2v6h6'],
};

const FOLDER_PATH =
  'M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z';
const FILE_BODY_PATH = 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z';
const FILE_FOLD_PATH = 'M14 2v6h6';

export interface NodeIconProps {
  kind: NodeIconKind;
  /** Rendered size in px. 13 is the list/graph size from the brief. */
  size?: number;
  /** Overrides the colour (e.g. a missing file renders warning-coloured). */
  color?: string;
  className?: string;
}

export const NodeIcon: React.FC<NodeIconProps> = ({ kind, size = 13, color, className = '' }) => {
  const isFolder = kind === 'folder';
  const stroke = color ?? NODE_ICON_COLORS[kind];

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke={stroke}
      strokeWidth={isFolder ? 1.8 : 1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      data-icon={isFolder ? 'folder' : 'file'}
      data-kind={kind}
      className={['shrink-0', className].filter(Boolean).join(' ')}
    >
      {isFolder ? (
        <path d={FOLDER_PATH} />
      ) : (
        <>
          <path d={FILE_BODY_PATH} />
          <path d={FILE_FOLD_PATH} />
        </>
      )}
    </svg>
  );
};

export default NodeIcon;
