import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import NodeIcon, { documentIconKind } from './NodeIcon';

describe('documentIconKind', () => {
  it('prefers an explicit docType', () => {
    expect(documentIconKind('notes.md', 'docx')).toBe('docx');
    expect(documentIconKind('Report.docx', 'md')).toBe('md');
  });

  it('falls back to the extension', () => {
    expect(documentIconKind('Report.docx')).toBe('docx');
    expect(documentIconKind('notes.md')).toBe('md');
    expect(documentIconKind('readme.markdown')).toBe('md');
  });

  it('returns unknown for an unrecognised file', () => {
    expect(documentIconKind('Budget.xlsx')).toBe('unknown');
  });
});

describe('NodeIcon', () => {
  it('renders a folder glyph for a folder kind', () => {
    const { container } = render(<NodeIcon kind="folder" />);
    expect(container.querySelector('[data-icon="folder"]')).not.toBeNull();
  });

  it('renders structurally distinct glyphs per file kind', () => {
    const { container: docx } = render(<NodeIcon kind="docx" />);
    const { container: md } = render(<NodeIcon kind="md" />);
    const { container: unknown } = render(<NodeIcon kind="unknown" />);

    expect(docx.querySelector('[data-icon="file"]')).not.toBeNull();
    expect(md.querySelector('[data-icon="file"]')).not.toBeNull();
    expect(unknown.querySelector('[data-icon="file"]')).not.toBeNull();
    // The kind is carried on the element so a folder can never masquerade as a file.
    expect(docx.querySelector('[data-kind="docx"]')).not.toBeNull();
    expect(md.querySelector('[data-kind="md"]')).not.toBeNull();
    expect(unknown.querySelector('[data-kind="unknown"]')).not.toBeNull();
    expect(docx.querySelector('[data-icon="folder"]')).toBeNull();
  });

  it('is decorative by default so it never competes with the adjacent text label', () => {
    const { container } = render(<NodeIcon kind="folder" />);
    expect(container.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });
});
