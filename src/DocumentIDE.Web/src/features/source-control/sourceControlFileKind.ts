/** UI-03 — the render strategy for a changed path. */
export type SourceControlFileKind = 'docx' | 'md' | 'other';

/**
 * Decides whether a row gets a text diff (`md`), the document-aware summary
 * (`docx`), or no preview. Extension-only: the path is exactly what Git
 * reported, never a guessed local path.
 */
export function fileKind(path: string): SourceControlFileKind {
  const lower = path.toLowerCase();
  if (lower.endsWith('.docx') || lower.endsWith('.doc')) return 'docx';
  if (lower.endsWith('.md') || lower.endsWith('.markdown')) return 'md';
  return 'other';
}
