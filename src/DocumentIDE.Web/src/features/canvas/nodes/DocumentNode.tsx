import React, { memo, useState, useCallback } from 'react';

export type ThumbnailState = 'loading' | 'ready' | 'stale' | 'missing' | 'failed';
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

/**
 * Low-detail paper layout fallback preview (simulated document page lines).
 * Designed for visual recognition without creating excessive visual noise or full text reading.
 */
function LowDetailPaperLayout({ docType }: { docType: DocType }) {
  return (
    <div
      data-testid="document-low-detail-fallback"
      className="w-full flex-1 bg-slate-50 border border-slate-100 rounded p-2.5 flex flex-col gap-1.5 overflow-hidden select-none pointer-events-none opacity-80"
    >
      {/* Title bar placeholder */}
      <div className="h-2 w-3/4 bg-slate-300 rounded" />
      {/* Heading accent */}
      <div className={`h-1.5 w-1/2 rounded ${docType === 'docx' ? 'bg-blue-300' : 'bg-emerald-300'}`} />
      
      {/* Simulated paragraph 1 */}
      <div className="flex flex-col gap-1 mt-1">
        <div className="h-1 w-full bg-slate-200 rounded" />
        <div className="h-1 w-5/6 bg-slate-200 rounded" />
        <div className="h-1 w-4/5 bg-slate-200 rounded" />
      </div>

      {/* Simulated paragraph 2 */}
      <div className="flex flex-col gap-1 mt-1">
        <div className="h-1 w-full bg-slate-200 rounded" />
        <div className="h-1 w-11/12 bg-slate-200 rounded" />
        <div className="h-1 w-2/3 bg-slate-200 rounded" />
      </div>
    </div>
  );
}

/**
 * CAN-03: Document Paper Object & Thumbnail Shell
 * Renders DOCX/Markdown documents as paper-sheet objects with exact filename and cached low-detail preview.
 */
const DocumentNode = memo(function DocumentNode({
  id,
  title,
  x = 0,
  y = 0,
  width = 160,
  height = 220,
  isSelected = false,
  isMissing = false,
  docType: explicitDocType,
  thumbnailState = 'ready',
  thumbnailUrl,
  statusText,
  onSelect,
  onDoubleClick,
  onMouseDown,
}: DocumentNodeProps) {
  const [imgFailed, setImgFailed] = useState(false);

  const docType = inferDocType(title, explicitDocType);
  const effectiveMissing = isMissing || thumbnailState === 'missing';
  const effectiveState: ThumbnailState = effectiveMissing
    ? 'missing'
    : imgFailed
    ? 'failed'
    : thumbnailState;

  const handleClick = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onSelect?.(id);
    },
    [onSelect, id]
  );

  const handleDoubleClick = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onDoubleClick?.(id);
    },
    [onDoubleClick, id]
  );

  const handleImageError = useCallback(() => {
    setImgFailed(true);
  }, []);

  // Determine badge styling based on docType
  const badgeText = docType === 'docx' ? 'DOCX' : docType === 'md' ? 'MD' : 'FILE';
  const badgeBg =
    docType === 'docx'
      ? 'bg-blue-50 text-blue-700 border-blue-200'
      : docType === 'md'
      ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
      : 'bg-slate-100 text-slate-600 border-slate-200';

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={`Document: ${title}`}
      aria-selected={isSelected}
      data-testid="document-paper-sheet"
      data-missing={effectiveMissing ? 'true' : 'false'}
      data-object-id={id}
      style={{
        position: 'absolute',
        left: x,
        top: y,
        width,
        height,
        willChange: 'transform',
      }}
      className={[
        'rounded border select-none cursor-pointer',
        'bg-white shadow flex flex-col p-2.5 gap-2 relative transition-all duration-100',
        effectiveMissing
          ? 'border-amber-400 bg-amber-50/40 ring-1 ring-amber-300 border-dashed'
          : isSelected
          ? 'border-blue-500 ring-2 ring-blue-200 shadow-md'
          : 'border-slate-200 hover:border-slate-300 hover:shadow-md',
      ].join(' ')}
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
      onMouseDown={onMouseDown}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          onSelect?.(id);
        }
      }}
    >
      {/* Paper fold accent (top right) */}
      <div
        className="absolute top-0 right-0 w-3.5 h-3.5 bg-slate-100 border-b border-l border-slate-300 rounded-bl-sm pointer-events-none opacity-60"
        aria-hidden="true"
      />

      {/* Header: Filename & Restrained Type Badge */}
      <div className="flex items-start justify-between gap-1 w-full pt-0.5">
        <span
          data-testid="document-filename"
          className="text-xs font-semibold text-slate-800 leading-snug break-words line-clamp-2 pr-2"
          title={title}
        >
          {title}
        </span>
        <span
          className={`text-[10px] font-mono font-bold px-1 py-0.5 rounded border shrink-0 ${badgeBg}`}
        >
          {badgeText}
        </span>
      </div>

      {/* Body: Thumbnail Shell Area */}
      <div className="flex-1 w-full relative flex items-center justify-center overflow-hidden">
        {effectiveMissing ? (
          /* Visibly distinct missing state */
          <div
            data-testid="document-missing-badge"
            className="flex flex-col items-center justify-center p-2 text-center text-amber-700 bg-amber-100/70 border border-amber-300 rounded w-full h-full gap-1"
          >
            <span className="text-base" aria-hidden="true">⚠️</span>
            <span className="text-[11px] font-medium leading-tight">File missing</span>
          </div>
        ) : effectiveState === 'loading' ? (
          /* Loading skeleton */
          <div
            data-testid="document-thumbnail-loading"
            className="w-full h-full bg-slate-100 rounded animate-pulse p-2 flex flex-col gap-1.5"
          >
            <div className="h-2 w-3/4 bg-slate-200 rounded" />
            <div className="h-1.5 w-1/2 bg-slate-200 rounded" />
            <div className="h-1 w-full bg-slate-200 rounded mt-2" />
            <div className="h-1 w-5/6 bg-slate-200 rounded" />
          </div>
        ) : thumbnailUrl && effectiveState !== 'failed' ? (
          /* Cached image thumbnail */
          <div className="relative w-full h-full flex items-center justify-center">
            <img
              src={thumbnailUrl}
              alt={`Thumbnail for ${title}`}
              data-testid="document-thumbnail-img"
              onError={handleImageError}
              className="max-w-full max-h-full object-contain rounded border border-slate-100"
            />
            {effectiveState === 'stale' && (
              <span
                data-testid="document-stale-badge"
                title="Stale preview"
                className="absolute top-1 right-1 w-2 h-2 bg-amber-400 rounded-full ring-1 ring-white"
              />
            )}
          </div>
        ) : (
          /* Low-detail fallback paper structure */
          <LowDetailPaperLayout docType={docType} />
        )}
      </div>

      {/* Footer / Status decoration (optional, restrained) */}
      {(statusText || effectiveMissing) && (
        <div className="text-[10px] text-slate-500 truncate w-full pt-0.5 border-t border-slate-100 flex items-center justify-between">
          <span>{effectiveMissing ? 'Missing' : statusText}</span>
        </div>
      )}
    </div>
  );
});

export default DocumentNode;
