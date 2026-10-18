import { describe, expect, it } from 'vitest';
import { buildUnifiedDiff } from './markdownDiff';

describe('buildUnifiedDiff (HEAD ↔ working tree)', () => {
  it('returns no hunks when the two versions are textually identical', () => {
    const diff = buildUnifiedDiff('# Notes\n\nsame\n', '# Notes\n\nsame\n', 'notes.md');

    expect(diff.leftExists).toBe(true);
    expect(diff.rightExists).toBe(true);
    expect(diff.hunks).toHaveLength(0);
  });

  it('renders a removed and an added line with real line numbers', () => {
    const diff = buildUnifiedDiff('a\nold\nz\n', 'a\nnew\nz\n', 'notes.md');

    expect(diff.leftExists).toBe(true);
    expect(diff.rightExists).toBe(true);
    expect(diff.hunks).toHaveLength(1);
    const lines = diff.hunks[0].lines;

    expect(lines.find((line) => line.kind === 'removed')).toMatchObject({
      content: 'old',
      oldLineNumber: 2,
      newLineNumber: null,
    });
    expect(lines.find((line) => line.kind === 'added')).toMatchObject({
      content: 'new',
      oldLineNumber: null,
      newLineNumber: 2,
    });
    expect(lines.filter((line) => line.kind === 'context')).toHaveLength(2);
  });

  it('models a new file as /dev/null on the left', () => {
    const diff = buildUnifiedDiff(null, 'first\nsecond\n', 'draft.md');

    expect(diff.leftExists).toBe(false);
    expect(diff.rightExists).toBe(true);
    expect(diff.hunks[0].header).toBe('@@ -0,0 +1,2 @@');
    expect(diff.hunks[0].lines.every((line) => line.kind === 'added')).toBe(true);
  });

  it('models a deleted file as /dev/null on the right', () => {
    const diff = buildUnifiedDiff('first\nsecond\n', null, 'gone.md');

    expect(diff.leftExists).toBe(true);
    expect(diff.rightExists).toBe(false);
    expect(diff.hunks[0].header).toBe('@@ -1,2 +0,0 @@');
    expect(diff.hunks[0].lines.every((line) => line.kind === 'removed')).toBe(true);
  });

  it('marks a side that has no trailing newline', () => {
    const diff = buildUnifiedDiff('old', 'new', 'notes.md');
    const flattened = diff.hunks[0].lines;

    expect(flattened.some((line) => line.noNewlineAtEndOfFile)).toBe(true);
  });

  it('normalizes CRLF without hiding a real content change', () => {
    const diff = buildUnifiedDiff('a\r\nold\r\n', 'a\nnew\n', 'notes.md');
    const lines = diff.hunks[0].lines;

    expect(lines.some((line) => line.kind === 'removed' && line.content === 'old')).toBe(true);
    expect(lines.some((line) => line.kind === 'added' && line.content === 'new')).toBe(true);
  });
});
