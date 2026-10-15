import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

export interface QuickLookPageData {
  pageIndex: number;
  totalPages: number;
  artifactId: string;
  pageText?: string | null;
  imageUrl?: string | null;
}

export interface QuickLookProps {
  documentId: string;
  fileName: string;
  totalPages: number;
  open: boolean;
  onClose: () => void;
  loadPage: (pageIndex: number) => Promise<QuickLookPageData>;
  searchTerms?: string[];
  editingInWord?: boolean;
  editingSummary?: string | null;
  initialPage?: number;
}

interface QuickLookKeyboardOptions {
  open: boolean;
  onOpen: () => void;
  onClose: () => void;
}

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    target.isContentEditable
  );
}

/**
 * Space opens Quick Look, Escape closes it. Space is ignored while typing in
 * an editable element so preview never hijacks text entry.
 */
export function useQuickLookKeyboard({ open, onOpen, onClose }: QuickLookKeyboardOptions) {
  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (open) {
          event.preventDefault();
          onClose();
        }
        return;
      }
      if (event.key === ' ' && !open && !isEditableTarget(event.target)) {
        event.preventDefault();
        onOpen();
      }
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
  }, [open, onOpen, onClose]);
}

function escapeRegExp(term: string): string {
  return term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Split page text into plain/highlighted segments for the active search terms. */
export function highlightSegments(
  pageText: string,
  terms: string[],
): Array<{ text: string; match: boolean; key: number }> {
  const active = terms.map((t) => t.trim()).filter((t) => t.length > 0);
  if (active.length === 0) return [{ text: pageText, match: false, key: 0 }];

  const pattern = new RegExp(`(${active.map(escapeRegExp).join('|')})`, 'gi');
  const parts = pageText.split(pattern);
  const lower = new Set(active.map((t) => t.toLowerCase()));
  return parts
    .filter((p) => p.length > 0)
    .map((part, i) => ({
      text: part,
      match: lower.has(part.toLowerCase()),
      key: i,
    }));
}

/**
 * WORD-04 — High-fidelity read-only Quick Look overlay.
 *
 * Lazy by construction: pages load through `loadPage` only when they become
 * visible (never eagerly for all files at startup). Search terms highlight
 * where page text is available; image artifacts render when provided.
 */
export function QuickLook({
  documentId,
  fileName,
  totalPages,
  open,
  onClose,
  loadPage,
  searchTerms = [],
  editingInWord = false,
  editingSummary = null,
  initialPage = 0,
}: QuickLookProps) {
  const [pageIndex, setPageIndex] = useState(initialPage);
  const [pages, setPages] = useState(new Map<number, QuickLookPageData>());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestRef = useRef(0);
  const pendingRef = useRef(new Set<number>());

  useEffect(() => {
    if (open) {
      setPageIndex(initialPage);
      setPages(new Map());
      setError(null);
    }
  }, [open, documentId, initialPage]);

  useEffect(() => {
    if (!open) return;
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
      }
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
  }, [open, onClose]);

  const load = useCallback(
    async (index: number) => {
      const requestId = ++requestRef.current;
      setLoading(true);
      setError(null);
      try {
        const data = await loadPage(index);
        if (requestRef.current !== requestId) return;
        setPages((prev) => new Map(prev).set(index, data));
      } catch (err) {
        if (requestRef.current !== requestId) return;
        const message =
          err && typeof err === 'object' && 'message' in err
            ? String((err as { message: unknown }).message)
            : 'Quick Look preview failed.';
        setError(message);
      } finally {
        if (requestRef.current === requestId) setLoading(false);
      }
    },
    [loadPage],
  );

  useEffect(() => {
    if (!open) return;
    if (pages.has(pageIndex) || pendingRef.current.has(pageIndex)) return;
    pendingRef.current.add(pageIndex);
    void load(pageIndex).finally(() => {
      pendingRef.current.delete(pageIndex);
    });
  }, [open, pageIndex, pages, load]);

  const page = pages.get(pageIndex);
  const segments = useMemo(
    () => (page?.pageText ? highlightSegments(page.pageText, searchTerms) : []),
    [page?.pageText, searchTerms],
  );

  if (!open) return null;

  const canPrev = pageIndex > 0;
  const canNext = pageIndex < totalPages - 1;

  return (
    <div
      data-testid="quick-look-dialog"
      role="dialog"
      aria-modal="true"
      aria-label={`Quick Look preview of ${fileName}`}
      className="quick-look-overlay"
    >
      <div className="quick-look-header">
        <span data-testid="quick-look-title">{fileName}</span>
        {editingInWord && (
          <span
            data-testid="quick-look-editing"
            title={editingSummary ?? 'Editing in Word'}
            className="quick-look-editing-badge"
          >
            Editing in Word
          </span>
        )}
        <button
          type="button"
          data-testid="quick-look-close"
          aria-label="Close preview"
          onClick={onClose}
        >
          Close
        </button>
      </div>

      <div data-testid="quick-look-body" className="quick-look-body">
        {error ? (
          <p data-testid="quick-look-error" role="alert">
            {error}
          </p>
        ) : page ? (
          <article data-testid={`quick-look-page-${pageIndex}`} className="quick-look-page">
            {page.imageUrl && (
              <img
                src={page.imageUrl}
                alt={`Page ${pageIndex + 1} of ${fileName}`}
                data-testid="quick-look-image"
              />
            )}
            {page.pageText &&
              (searchTerms.length > 0 ? (
                <p data-testid="quick-look-text">
                  {segments.map((seg) =>
                    seg.match ? (
                      <mark key={seg.key} data-testid="quick-look-match">
                        {seg.text}
                      </mark>
                    ) : (
                      <span key={seg.key}>{seg.text}</span>
                    ),
                  )}
                </p>
              ) : (
                <p data-testid="quick-look-text">{page.pageText}</p>
              ))}
          </article>
        ) : (
          loading && <p data-testid="quick-look-loading">Loading page…</p>
        )}
      </div>

      <div className="quick-look-footer">
        <button
          type="button"
          data-testid="quick-look-prev"
          disabled={!canPrev}
          onClick={() => canPrev && setPageIndex(pageIndex - 1)}
        >
          Previous
        </button>
        <span data-testid="quick-look-counter">
          Page {pageIndex + 1} of {totalPages}
        </span>
        <button
          type="button"
          data-testid="quick-look-next"
          disabled={!canNext}
          onClick={() => canNext && setPageIndex(pageIndex + 1)}
        >
          Next
        </button>
      </div>
    </div>
  );
}
