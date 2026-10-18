/**
 * UI-03 — exact HEAD ↔ working-tree line diff for a Markdown document.
 *
 * SCM-04 ships the diff *renderer* (`MarkdownGitDiff`) and the read-only
 * document-version route, but the controller does not expose the spec 13 §18
 * `GET /api/source-control/diff` route yet. Rather than fabricate a diff or
 * claim an endpoint that does not exist, the screen composes the two real
 * read-only sources it already has:
 *
 *   - `GET /api/source-control/file-version?commit=HEAD` → the HEAD side;
 *   - `GET /api/documents/{id}/markdown`               → the working-tree side.
 *
 * This module is a pure, deterministic line formatter over exactly those two
 * strings. It performs exact line comparison only (no fuzzy/semantic matching)
 * and is deliberately coarse around a changed region: common prefix/suffix are
 * context, the middle is a removal block followed by an addition block. That is
 * a valid unified diff and never hides or invents content.
 */

export type UnifiedDiffLineKind = 'context' | 'added' | 'removed';

export interface UnifiedDiffLine {
  kind: UnifiedDiffLineKind;
  content: string;
  oldLineNumber: number | null;
  newLineNumber: number | null;
  noNewlineAtEndOfFile: boolean;
}

export interface UnifiedDiffHunk {
  header: string;
  lines: UnifiedDiffLine[];
}

/** Structurally identical to `MarkdownGitDiff`'s view model. */
export interface UnifiedDiffView {
  target: 'working-tree';
  path: string;
  leftLabel: string;
  rightLabel: string;
  leftExists: boolean;
  rightExists: boolean;
  isBinary: boolean;
  hunks: UnifiedDiffHunk[];
}

interface SplitText {
  lines: string[];
  endsWithNewline: boolean;
}

function splitText(text: string): SplitText {
  if (text === '') {
    return { lines: [], endsWithNewline: true };
  }
  const normalized = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
  const endsWithNewline = normalized.endsWith('\n');
  const lines = normalized.split('\n');
  if (endsWithNewline) {
    lines.pop();
  }
  return { lines, endsWithNewline };
}

function range(start: number, count: number): string {
  return count === 1 ? `${start}` : `${start},${count}`;
}

/**
 * Builds one unified hunk over the changed region. `leftText === null` means
 * the path does not exist on that side (`/dev/null`); the same for the right.
 */
export function buildUnifiedDiff(
  leftText: string | null,
  rightText: string | null,
  path: string,
  leftLabel = 'HEAD',
  rightLabel = 'working tree',
): UnifiedDiffView {
  const left = leftText === null ? { lines: [], endsWithNewline: true } : splitText(leftText);
  const right = rightText === null ? { lines: [], endsWithNewline: true } : splitText(rightText);

  const base: UnifiedDiffView = {
    target: 'working-tree',
    path,
    leftLabel,
    rightLabel,
    leftExists: leftText !== null,
    rightExists: rightText !== null,
    isBinary: false,
    hunks: [],
  };

  const oldLines = left.lines;
  const newLines = right.lines;

  let prefix = 0;
  while (
    prefix < oldLines.length &&
    prefix < newLines.length &&
    oldLines[prefix] === newLines[prefix]
  ) {
    prefix += 1;
  }

  let suffix = 0;
  while (
    suffix < oldLines.length - prefix &&
    suffix < newLines.length - prefix &&
    oldLines[oldLines.length - 1 - suffix] === newLines[newLines.length - 1 - suffix]
  ) {
    suffix += 1;
  }

  const removed = oldLines.slice(prefix, oldLines.length - suffix);
  const added = newLines.slice(prefix, newLines.length - suffix);

  if (removed.length === 0 && added.length === 0) {
    return base;
  }

  // This hunk spans the whole file, so its start is the first line (Git uses
  // `-0,0` / `+0,0` for an absent side).
  const oldCount = prefix + removed.length + suffix;
  const newCount = prefix + added.length + suffix;
  const oldStart = oldCount === 0 ? 0 : 1;
  const newStart = newCount === 0 ? 0 : 1;

  const lines: UnifiedDiffLine[] = [];
  let oldLine = 1;
  let newLine = 1;

  for (let index = 0; index < prefix; index += 1) {
    lines.push({
      kind: 'context',
      content: oldLines[index],
      oldLineNumber: oldLine,
      newLineNumber: newLine,
      noNewlineAtEndOfFile: false,
    });
    oldLine += 1;
    newLine += 1;
  }

  for (const content of removed) {
    lines.push({
      kind: 'removed',
      content,
      oldLineNumber: oldLine,
      newLineNumber: null,
      noNewlineAtEndOfFile: false,
    });
    oldLine += 1;
  }

  for (const content of added) {
    lines.push({
      kind: 'added',
      content,
      oldLineNumber: null,
      newLineNumber: newLine,
      noNewlineAtEndOfFile: false,
    });
    newLine += 1;
  }

  for (let index = suffix; index > 0; index -= 1) {
    const content = oldLines[oldLines.length - index];
    lines.push({
      kind: 'context',
      content,
      oldLineNumber: oldLine,
      newLineNumber: newLine,
      noNewlineAtEndOfFile: false,
    });
    oldLine += 1;
    newLine += 1;
  }

  // Mark the true end of a side that has no trailing newline. The renderer
  // prints Git's "\ No newline at end of file" marker for these rows.
  if (leftText !== null && !left.endsWithNewline) {
    const lastOld = [...lines].reverse().find((line) => line.oldLineNumber !== null);
    if (lastOld) lastOld.noNewlineAtEndOfFile = true;
  }
  if (rightText !== null && !right.endsWithNewline) {
    const lastNew = [...lines].reverse().find((line) => line.newLineNumber !== null);
    if (lastNew) lastNew.noNewlineAtEndOfFile = true;
  }

  return {
    ...base,
    hunks: [
      {
        header: `@@ -${range(oldStart, oldCount)} +${range(newStart, newCount)} @@`,
        lines,
      },
    ],
  };
}
