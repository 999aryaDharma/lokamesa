/**
 * MD-01 — Source-first Markdown editor (real editing surface).
 *
 * Replaces the demo stub: loads raw `.md` source from the document API for a
 * real stable DocumentId, detects changes, exposes explicit Save (Ctrl+S) and
 * optional autosave, and reports save state/errors. The `.md` file on disk
 * remains the source of truth (spec 08 §2) — the editor never invents sidecar
 * metadata and never injects unsanitized HTML (preview renders inert text; the
 * escaped source is shown, never executed — spec 08 §6).
 */
import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { loadMarkdown, saveMarkdown } from './markdownApi';

export type MarkdownEditorMode = 'edit' | 'preview' | 'split';

export interface MarkdownEditorProps {
  /** Stable FS-02 document id. When omitted the editor is a scratch surface. */
  documentId?: string;
  /** Pre-seeded source (used before/without a document load, e.g. tests). */
  initialSource?: string;
  /** Optional autosave (spec 08 §4 — autosave is optional but explicit). */
  autoSave?: boolean;
  /** Autosave debounce window in milliseconds. */
  autoSaveDelayMs?: number;
  onSaved?: (documentId: string) => void;
  onClose?: () => void;
}

export const MarkdownEditor: React.FC<MarkdownEditorProps> = ({
  documentId,
  initialSource = '',
  autoSave = false,
  autoSaveDelayMs = 1500,
  onSaved,
  onClose,
}) => {
  const [source, setSource] = useState<string>(initialSource);
  const [savedSource, setSavedSource] = useState<string>(initialSource);
  const [mode, setMode] = useState<MarkdownEditorMode>('edit');
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const loadToken = useRef(0);

  const isDirty = source !== savedSource;

  // Load source when the target document changes.
  useEffect(() => {
    if (!documentId) {
      setSource(initialSource);
      setSavedSource(initialSource);
      return;
    }
    const token = ++loadToken.current;
    setIsLoading(true);
    setError(null);
    loadMarkdown(documentId)
      .then((doc) => {
        if (loadToken.current !== token) return;
        setSource(doc.source);
        setSavedSource(doc.source);
      })
      .catch((err: unknown) => {
        if (loadToken.current !== token) return;
        setError(err instanceof Error ? err.message : 'Failed to load Markdown document.');
      })
      .finally(() => {
        if (loadToken.current === token) setIsLoading(false);
      });
  }, [documentId, initialSource]);

  const handleSave = useCallback(async () => {
    if (!documentId || !isDirty || isSaving) return;
    setIsSaving(true);
    setError(null);
    try {
      await saveMarkdown(documentId, source);
      setSavedSource(source);
      onSaved?.(documentId);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to save Markdown document.');
    } finally {
      setIsSaving(false);
    }
  }, [documentId, isDirty, isSaving, source, onSaved]);

  // Ctrl+S / Cmd+S manual save.
  useEffect(() => {
    const keyHandler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) {
        e.preventDefault();
        void handleSave();
      }
    };
    window.addEventListener('keydown', keyHandler);
    return () => window.removeEventListener('keydown', keyHandler);
  }, [handleSave]);

  // Optional autosave (explicitly enabled by the shell/settings).
  useEffect(() => {
    if (!autoSave || !documentId || !isDirty) return;
    const timer = setTimeout(() => {
      void handleSave();
    }, autoSaveDelayMs);
    return () => clearTimeout(timer);
  }, [autoSave, autoSaveDelayMs, documentId, isDirty, source, handleSave]);

  const handleChange = useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setSource(e.target.value);
  }, []);

  const status = useMemo(() => {
    if (isLoading) return 'Loading…';
    if (isSaving) return 'Saving…';
    if (error) return 'Save failed';
    if (isDirty) return 'Unsaved changes';
    return 'Saved';
  }, [isLoading, isSaving, error, isDirty]);

  return (
    <section className="markdown-editor flex flex-col h-full w-full" aria-label="Markdown editor">
      <header className="flex items-center gap-2 border-b border-mac-hairline bg-mac-content px-3 py-2">
        <span className="text-xs font-semibold text-mac-text truncate" data-testid="markdown-title">
          {documentId ?? 'Markdown'}
        </span>
        <div role="group" aria-label="Editor mode" className="inline-flex rounded border border-mac-hairline overflow-hidden">
          <button
            type="button"
            aria-pressed={mode === 'edit'}
            onClick={() => setMode('edit')}
            className={`px-2 py-0.5 text-xs ${mode === 'edit' ? 'bg-mac-chip' : 'bg-mac-content'}`}
          >
            Edit
          </button>
          <button
            type="button"
            aria-pressed={mode === 'preview'}
            onClick={() => setMode('preview')}
            className={`px-2 py-0.5 text-xs border-l border-mac-hairline ${mode === 'preview' ? 'bg-mac-chip' : 'bg-mac-content'}`}
          >
            Preview
          </button>
          <button
            type="button"
            aria-pressed={mode === 'split'}
            onClick={() => setMode('split')}
            className={`px-2 py-0.5 text-xs border-l border-mac-hairline ${mode === 'split' ? 'bg-mac-chip' : 'bg-mac-content'}`}
          >
            Split
          </button>
        </div>

        <span className="text-[11px] text-mac-text-3" role="status" data-testid="markdown-status">
          {status}
        </span>
        {isDirty && (
          <span
            data-testid="markdown-dirty"
            className="h-2 w-2 rounded-full bg-amber-400"
            aria-label="Unsaved changes"
          />
        )}

        <div className="ml-auto flex items-center gap-2">
          <button
            type="button"
            onClick={() => void handleSave()}
            disabled={!isDirty || isSaving || !documentId}
            className="px-2 py-1 text-xs rounded bg-mac-accent text-white disabled:opacity-40"
          >
            Save (Ctrl+S)
          </button>
          {onClose && (
            <button type="button" onClick={onClose} className="px-2 py-1 text-xs rounded border border-mac-hairline-strong">
              Close
            </button>
          )}
        </div>
      </header>

      {error && (
        <p role="alert" className="px-3 py-1 text-xs text-mac-danger bg-mac-danger/10 border-b border-mac-danger/40">
          {error}
        </p>
      )}

      <div className="flex-1 min-h-0 flex overflow-hidden">
        {mode !== 'preview' && (
          <textarea
            aria-label="Markdown source"
            value={source}
            onChange={handleChange}
            spellCheck={false}
            className="flex-1 min-w-0 h-full w-full p-3 font-mono text-sm resize-none focus:outline-none"
          />
        )}
        {mode !== 'edit' && (
          <pre
            data-testid="markdown-preview"
            aria-label="Markdown preview"
            className={`flex-1 min-w-0 h-full w-full p-3 text-sm overflow-auto bg-mac-canvas ${
              mode === 'split' ? 'border-l border-mac-hairline' : ''
            }`}
          >
            {source}
          </pre>
        )}
      </div>
    </section>
  );
};

export default MarkdownEditor;
