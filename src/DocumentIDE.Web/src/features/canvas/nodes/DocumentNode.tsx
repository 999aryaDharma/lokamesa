/**
 * CAN-03 / UI-01 — document *file object* on the canvas.
 *
 * Owner directive (2026-10-18): on the canvas a folder and a file must read as
 * two different objects, the way Finder's icon view does — never as two
 * look-alike boxes.
 *
 *   file object → a white sheet with a coloured type accent bar, a folded
 *                 corner, a faint page-1 content preview, and the exact
 *                 filename beneath it.
 *
 * The preview is *content-derived* real document content (`FilePreview`); there
 * is no page-image pipeline in this repo yet, so the sheet shows the source
 * text faintly rather than a rendered page.
 *
 * Persisted object geometry (`x/y/width/height`) is unchanged: the frame keeps
 * the size the canvas store and assisted placement already use, and the object
 * is drawn inside it. Nothing here mutates a file.
 */
import React, { memo, useCallback } from 'react';
import { NODE_ICON_COLORS } from '../../shell/NodeIcon';
import type { MarkdownDocument } from '../../markdown/markdownApi';
import FilePreview, { type FilePreviewThumbnailState } from './FilePreview';

export type ThumbnailState = FilePreviewThumbnailState;
export type DocType = 'docx' | 'md' | 'unknown';

export interface DocumentNodeProps {
  id: string;
  title: string;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  isSelected?: boolean;
  isMissing?: boolean;
  isFaded?: boolean;
  isMatch?: boolean;
  docType?: DocType;
  thumbnailState?: ThumbnailState;
  thumbnailUrl?: string;
  thumbnailContent?: string;
  statusText?: string;
  onSelect?: (id: string) => void;
  onDoubleClick?: (id: string) => void;
  /** CAN-07: pointer-down hook so the canvas can start a group drag. */
  onMouseDown?: (event: React.MouseEvent) => void;
  /** Injectable markdown loader for the faint preview (tests / host seam). */
  loadMarkdown?: (documentId: string) => Promise<MarkdownDocument>;
}

/**
 * Infer document type from title if not explicitly provided.
 */
function inferDocType(title: string, explicitType?: DocType): DocType {
  if (explicitType && explicitType !== 'unknown') {
    return explicitType;
  }
  const lower = title.toLowerCase();
  if (lower.endsWith('.docx') || lower.endsWith('.doc')) {
    return 'docx';
  }
  if (lower.endsWith('.md') || lower.endsWith('.markdown')) {
    return 'md';
  }
  return 'unknown';
}

/** Sheet-face metrics from the design brief. */
const SHEET_WIDTH = 52;
const SHEET_HEIGHT = 66;
const SHEET_ACCENT_HEIGHT = 7;

const DocumentNode = memo(function DocumentNode({
  id,
  title,
  x = 0,
  y = 0,
  width = 160,
  height = 220,
  isSelected = false,
  isMissing = false,
  isFaded = false,
  isMatch = false,
  docType: explicitDocType,
  thumbnailState = 'ready',
  thumbnailUrl,
  thumbnailContent,
  statusText,
  onSelect,
  onDoubleClick,
  onMouseDown,
  loadMarkdown,
}: DocumentNodeProps) {
  const docType = inferDocType(title, explicitDocType);
  const effectiveMissing = isMissing || thumbnailState === 'missing';

  const handleClick = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onSelect?.(id);
    },
    [onSelect, id],
  );

  const handleDoubleClick = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onDoubleClick?.(id);
    },
    [onDoubleClick, id],
  );

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={`Document: ${title}`}
      aria-selected={isSelected}
      data-testid="document-paper-sheet"
      data-part="file-object"
      data-kind={docType}
      data-missing={effectiveMissing ? 'true' : 'false'}
      data-object-id={id}
      style={{
        position: 'absolute',
        left: x,
        top: y,
        width,
        height,
        willChange: 'transform',
        opacity: isFaded ? 0.35 : 1,
      }}
      className={[
        'select-none cursor-pointer transition-colors',
        isMatch
          ? 'rounded-lg ring-2 ring-mac-accent/60'
          : isSelected
            ? 'rounded-lg ring-2 ring-mac-accent/40'
            : '',
      ]
        .filter(Boolean)
        .join(' ')}
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
      onMouseDown={onMouseDown}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          onSelect?.(id);
        }
      }}
    >
      {isMatch && (
        <span
          data-testid={`match-badge-${id}`}
          className="absolute top-1 right-1 z-10 rounded bg-mac-accent px-1 py-0.5 text-[9px] font-bold text-white shadow-xs"
        >
          MATCH
        </span>
      )}

      {/* The object: sheet face on top, filename label beneath */}
      <div
        className="absolute left-1/2 flex -translate-x-1/2 flex-col items-center"
        style={{ top: 30, width: 140 }}
      >
        <div
          data-part="file-sheet"
          className="relative overflow-hidden"
          style={{
            width: SHEET_WIDTH,
            height: SHEET_HEIGHT,
            borderRadius: 5,
            background: '#FFFFFF',
            border: '1px solid #D9D9DE',
            boxShadow: '0 2px 6px #0000001F',
          }}
        >
          {/* Type accent: the only colour that distinguishes file kinds. */}
          <div
            data-part="type-accent"
            aria-hidden="true"
            style={{
              height: SHEET_ACCENT_HEIGHT,
              background: effectiveMissing ? NODE_ICON_COLORS.unknown : NODE_ICON_COLORS[docType],
              borderTopLeftRadius: 4,
              borderTopRightRadius: 4,
            }}
          />

          {/* Folded corner */}
          <div
            data-part="file-fold"
            aria-hidden="true"
            className="pointer-events-none absolute top-0 right-0"
            style={{
              width: 14,
              height: 14,
              background: '#E6E6E9',
              borderTopRightRadius: 5,
            }}
          />

          {/* Faint page-1 content preview (real content when available) */}
          <div
            className="absolute inset-x-0 bottom-0"
            style={{ top: SHEET_ACCENT_HEIGHT }}
          >
            <FilePreview
              documentId={id}
              title={title}
              docType={docType}
              thumbnailUrl={thumbnailUrl}
              thumbnailContent={thumbnailContent}
              thumbnailState={thumbnailState}
              isMissing={effectiveMissing}
              loadMarkdown={loadMarkdown}
            />
          </div>
        </div>

        <span
          data-part="item-label"
          data-testid="document-filename"
          className="mt-1.5 w-full truncate text-center text-[12px] leading-4 font-medium text-mac-text"
          title={title}
        >
          {title}
        </span>

        {(statusText || effectiveMissing) && (
          <span
            data-part="item-status"
            className="mt-0.5 w-full truncate text-center text-[10px] leading-3 text-mac-text-3"
          >
            {effectiveMissing ? 'Missing' : statusText}
          </span>
        )}
      </div>
    </div>
  );
});

export default DocumentNode;
