/**
 * CAN-04: Folder Picker & File Release — FolderPicker popover
 *
 * Lightweight picker opened by single-clicking a folder object. Lists the
 * folder's supported files (`.docx`, `.md`), supports search + multi-select,
 * and exposes `Open selected` / `Open all` actions through callbacks.
 *
 * IMPORTANT: this component is presentational. It never touches the
 * filesystem — releasing documents only creates/activates workspace objects.
 */
import { useEffect, useMemo, useState } from 'react';

/** A supported document candidate inside a folder source. */
export interface FolderPickerFile {
  /** Stable document identity (FS-02). */
  documentId: string;
  /** Exact file name (basename) shown in the picker. */
  filename: string;
}

export interface FolderPickerProps {
  /** Whether the popover is open. */
  isOpen: boolean;
  /** Display name of the folder source. */
  folderName: string;
  /** Files found in the folder source (may include unsupported files). */
  files: FolderPickerFile[];
  /** Called with the selected DocumentIds when the user confirms "Open selected". */
  onOpenSelected: (documentIds: string[]) => void;
  /** Called when the user confirms "Open all". */
  onOpenAll: () => void;
  /** Called when the picker should close (close button / Escape). */
  onClose: () => void;
}

export type FolderFileType = 'docx' | 'md' | 'unknown';

/**
 * Infer the document type from a file name.
 * Mirrors the supported-file convention of FS-01 folder attachment:
 * `.docx` and `.md` only.
 */
export function inferFolderFileType(filename: string): FolderFileType {
  const lower = filename.toLowerCase();
  if (lower.endsWith('.docx')) return 'docx';
  if (lower.endsWith('.md')) return 'md';
  return 'unknown';
}

/** A file is listed/releasable only when its type is supported. */
export function isSupportedFolderFile(file: FolderPickerFile): boolean {
  return inferFolderFileType(file.filename) !== 'unknown';
}

const FolderPicker = ({
  isOpen,
  folderName,
  files,
  onOpenSelected,
  onOpenAll,
  onClose,
}: FolderPickerProps) => {
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState('');

  const supportedFiles = useMemo(() => files.filter(isSupportedFolderFile), [files]);

  // Search within folder picker (case-insensitive substring).
  const visibleFiles = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return supportedFiles;
    return supportedFiles.filter((file) => file.filename.toLowerCase().includes(needle));
  }, [supportedFiles, query]);

  // Prune stale selections when the folder contents change while open.
  useEffect(() => {
    setSelectedIds((prev) => {
      const available = new Set(supportedFiles.map((file) => file.documentId));
      const next = new Set<string>();
      let changed = false;
      prev.forEach((id) => {
        if (available.has(id)) {
          next.add(id);
        } else {
          changed = true;
        }
      });
      return changed ? next : prev;
    });
  }, [supportedFiles]);

  const toggleSelection = (documentId: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(documentId)) {
        next.delete(documentId);
      } else {
        next.add(documentId);
      }
      return next;
    });
  };

  // Selected DocumentIds in listing order — deterministic release set.
  const selectedDocumentIds = useMemo(
    () => supportedFiles.filter((file) => selectedIds.has(file.documentId)).map((file) => file.documentId),
    [supportedFiles, selectedIds],
  );

  if (!isOpen) return null;

  return (
    <div
      role="dialog"
      aria-label={`Folder picker: ${folderName}`}
      data-testid="folder-picker"
      className="w-72 rounded-lg border border-slate-200 bg-white shadow-xl flex flex-col overflow-hidden"
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
      }}
    >
      <div className="px-3 py-2 border-b border-slate-100 flex flex-col gap-2">
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm font-semibold text-slate-800 truncate block" title={folderName}>
            {folderName}
          </span>
          <button
            type="button"
            aria-label="Close folder picker"
            onClick={onClose}
            className="shrink-0 w-5 h-5 flex items-center justify-center rounded text-slate-400 hover:bg-slate-100 hover:text-slate-600 text-sm leading-none"
          >
            ×
          </button>
        </div>
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search files…"
          aria-label="Search files in folder"
          className="w-full rounded border border-slate-200 px-2 py-1 text-xs text-slate-700 placeholder:text-slate-400 focus:border-blue-400 focus:outline-none"
        />
      </div>

      <ul className="max-h-64 overflow-y-auto py-1">
        {visibleFiles.length === 0 ? (
          <li className="px-3 py-3 text-sm text-slate-400">
            {supportedFiles.length === 0 ? 'No supported files found.' : 'No files match your search.'}
          </li>
        ) : (
          visibleFiles.map((file) => (
            <li key={file.documentId}>
              <label className="flex items-center gap-2 px-3 py-1.5 text-sm text-slate-700 cursor-pointer hover:bg-slate-50">
                <input
                  type="checkbox"
                  checked={selectedIds.has(file.documentId)}
                  onChange={() => toggleSelection(file.documentId)}
                  aria-label={`Select ${file.filename}`}
                  className="accent-blue-600"
                />
                <span className="truncate" title={file.filename}>
                  {file.filename}
                </span>
              </label>
            </li>
          ))
        )}
      </ul>

      <div className="px-3 py-2 border-t border-slate-100 flex items-center justify-end gap-2">
        <button
          type="button"
          disabled={selectedDocumentIds.length === 0}
          onClick={() => onOpenSelected(selectedDocumentIds)}
          className="rounded bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Open selected
        </button>
        <button
          type="button"
          onClick={onOpenAll}
          className="rounded border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100"
        >
          Open all
        </button>
      </div>
    </div>
  );
};

export default FolderPicker;
