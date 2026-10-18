/**
 * UI-01 — faint page-1 preview on a file object (owner directive 2026-10-18).
 *
 * Shows a faint, clipped preview of the document's *real* content on the file
 * sheet face:
 *   - `.md`   → the source already served by
 *               `GET /api/documents/{documentId}/markdown`;
 *   - `.docx` → UI-02's read-only structure route once it lands; until then the
 *               existing low-detail layout stands (no backend change here);
 *   - otherwise / on failure → the low-detail layout.
 *
 * Honest limit: this is a *content-derived* preview, not a rendered page image.
 * A real page-1 raster needs the Word → temp PDF → page-image pipeline, which
 * exists in no shipped host (the same gap that blocks QuickLook/WORD-04). That
 * pipeline, when it lands, feeds the `thumbnailUrl` seam — which is already
 * honoured here — so the node needs no redesign then.
 *
 * The preview is `aria-hidden`: it is a visual hint of content, and the object's
 * accessible name stays the filename on the label.
 */
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { loadMarkdown as defaultLoadMarkdown, type MarkdownDocument } from '../../markdown/markdownApi';
import type { NodeIconKind } from '../../shell/NodeIcon';

export type FilePreviewThumbnailState = 'loading' | 'ready' | 'stale' | 'missing' | 'failed';

export interface FilePreviewProps {
  documentId: string;
  title?: string;
  /** Resolved document kind; `.docx`/unknown stay on the fallback for now. */
  docType: NodeIconKind;
  thumbnailUrl?: string;
  thumbnailContent?: string;
  thumbnailState?: FilePreviewThumbnailState;
  isMissing?: boolean;
  /** Injectable markdown source loader; defaults to the real markdown route. */
  loadMarkdown?: (documentId: string) => Promise<MarkdownDocument>;
  className?: string;
}

/** Number of source lines a sheet-face preview shows before it is clipped. */
const PREVIEW_LINE_LIMIT = 9;

/**
 * Low-detail paper layout fallback (used until real content is available).
 *
 * Three content lines (36/29/34 x 3, radius 1.5) in `$mac-chip` — the file
 * object's drawing from the design brief, standing in for content that cannot
 * be read yet.
 */
export function LowDetailPaperLayout() {
  return (
    <div
      data-testid="document-low-detail-fallback"
      className="pointer-events-none flex h-full w-full flex-col gap-[6px] overflow-hidden px-2 pt-1.5 opacity-70 select-none"
    >
      <div className="h-[3px] w-9 rounded-[1.5px] bg-mac-chip" />
      <div className="h-[3px] w-[29px] rounded-[1.5px] bg-mac-chip" />
      <div className="h-[3px] w-[34px] rounded-[1.5px] bg-mac-chip" />
    </div>
  );
}

/** Faint, clipped rendering of real document content on the sheet face. */
function ContentPreview({ source }: { source: string }) {
  const lines = useMemo(
    () =>
      source
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => line.length > 0)
        .slice(0, PREVIEW_LINE_LIMIT),
    [source],
  );

  if (lines.length === 0) return <LowDetailPaperLayout />;

  return (
    <div
      data-testid="file-preview-content"
      aria-hidden="true"
      className="pointer-events-none absolute inset-x-0 top-[9px] bottom-0 overflow-hidden px-1 text-[5px] leading-[7px] text-mac-text opacity-40 select-none"
    >
      {lines.map((line, index) => (
        <p key={index} className="m-0 truncate">
          {line}
        </p>
      ))}
    </div>
  );
}

export function FilePreview({
  documentId,
  title,
  docType,
  thumbnailUrl,
  thumbnailContent,
  thumbnailState = 'ready',
  isMissing = false,
  loadMarkdown,
  className = '',
}: FilePreviewProps) {
  const [content, setContent] = useState<string | null>(thumbnailContent ?? null);
  const [contentState, setContentState] = useState<'idle' | 'loading' | 'ready' | 'unavailable'>(
    thumbnailContent !== undefined ? 'ready' : 'idle',
  );
  const [imgFailed, setImgFailed] = useState(false);

  const effectiveMissing = isMissing || thumbnailState === 'missing';

  // Prefer supplied content (the seam a future page renderer will feed).
  useEffect(() => {
    if (thumbnailContent !== undefined) {
      setContent(thumbnailContent);
      setContentState('ready');
    }
  }, [thumbnailContent]);

  // A `.md` sheet derives its preview from the real source. A `.docx` sheet
  // stays on the fallback until UI-02's read-only structure route exists.
  useEffect(() => {
    if (thumbnailContent !== undefined) return;
    if (docType !== 'md' || !documentId || effectiveMissing) return;

    const loader = loadMarkdown ?? defaultLoadMarkdown;
    if (typeof fetch !== 'function' && !loadMarkdown) {
      setContentState('unavailable');
      return;
    }

    let cancelled = false;
    setContentState('loading');
    loader(documentId)
      .then((document) => {
        if (cancelled) return;
        setContent(document.source);
        setContentState(document.source.trim().length > 0 ? 'ready' : 'unavailable');
      })
      .catch(() => {
        if (!cancelled) setContentState('unavailable');
      });
    return () => {
      cancelled = true;
    };
  }, [documentId, docType, thumbnailContent, effectiveMissing, loadMarkdown]);

  const showLoading = thumbnailState === 'loading' || contentState === 'loading';

  let body: ReactNode;
  if (effectiveMissing) {
    body = (
      <div
        data-testid="document-missing-badge"
        className="flex h-full w-full flex-col items-center justify-center gap-1 rounded border border-mac-warning/60 bg-mac-warning/10 p-1 text-center text-[9px] font-medium text-mac-warning"
      >
        <span aria-hidden="true">⚠</span>
        <span>File missing</span>
      </div>
    );
  } else if (showLoading) {
    body = (
      <div
        data-testid="document-thumbnail-loading"
        className="flex h-full w-full animate-pulse flex-col gap-1.5 rounded bg-mac-chip/60 p-2"
      >
        <div className="h-2 w-3/4 rounded bg-mac-chip" />
        <div className="h-1.5 w-1/2 rounded bg-mac-chip" />
        <div className="mt-2 h-1 w-full rounded bg-mac-chip" />
        <div className="h-1 w-5/6 rounded bg-mac-chip" />
      </div>
    );
  } else if (thumbnailUrl && !imgFailed && thumbnailState !== 'failed') {
    body = (
      <>
        <img
          src={thumbnailUrl}
          alt={title ? `Page 1 preview of ${title}` : 'Page 1 preview'}
          data-testid="document-thumbnail-img"
          onError={() => setImgFailed(true)}
          className="h-full w-full rounded object-cover opacity-90"
        />
        {thumbnailState === 'stale' && (
          <span
            data-testid="document-stale-badge"
            title="Stale preview"
            className="absolute top-1 right-1 h-2 w-2 rounded-full bg-mac-warning ring-1 ring-white"
          />
        )}
      </>
    );
  } else if (contentState === 'ready' && content) {
    body = <ContentPreview source={content} />;
  } else {
    body = <LowDetailPaperLayout />;
  }

  return (
    <div
      data-testid="file-preview"
      data-preview-state={effectiveMissing ? 'missing' : contentState}
      className={['relative h-full w-full overflow-hidden', className].filter(Boolean).join(' ')}
    >
      {body}
    </div>
  );
}

export default FilePreview;
