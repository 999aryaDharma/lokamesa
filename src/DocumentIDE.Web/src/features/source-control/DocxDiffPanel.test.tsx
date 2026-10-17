import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import DocxDiffPanel, {
  type DocxChangeView,
  type DocxDiffView,
} from './DocxDiffPanel';

/**
 * SCM-05 frontend RED/GREEN contract.
 *
 * Spec 13 §9.2: the UI SHALL explain DOCX changes between Git versions in
 * document terms (paragraphs, tables, headings, formatting, metadata) while
 * staging stays file-level. §9.3: versions that cannot be compared as
 * documents degrade to a binary-change summary instead of a faked semantic
 * diff. Content is rendered as inert text (no HTML interpretation) and the
 * surface is strictly read-only.
 */

function textChange(overrides: Partial<DocxChangeView> = {}): DocxChangeView {
  return {
    kind: 'paragraph-text-changed',
    summary: 'Paragraph text changed: "Hello world" -> "Hello brave world"',
    before: 'Hello world',
    after: 'Hello brave world',
    property: null,
    oldParagraphIndex: 0,
    newParagraphIndex: 0,
    runIndex: null,
    tableIndex: null,
    rowIndex: null,
    cellIndex: null,
    ...overrides,
  };
}

function workingTreeDiff(overrides: Partial<DocxDiffView> = {}): DocxDiffView {
  return {
    target: 'working-tree',
    path: 'docs/report.docx',
    leftLabel: 'HEAD',
    rightLabel: 'working tree',
    leftExists: true,
    rightExists: true,
    changes: [textChange()],
    binarySummary: null,
    ...overrides,
  };
}

describe('DocxDiffPanel Component', () => {
  it('renders document-aware changes with target, file header and exact before/after', () => {
    render(<DocxDiffPanel status="available" diff={workingTreeDiff()} />);

    expect(screen.getByTestId('docx-diff')).toBeInTheDocument();
    expect(screen.getByTestId('docx-diff-target')).toHaveTextContent('HEAD ↔ working tree');

    const fileHeader = screen.getByTestId('docx-diff-file-header');
    expect(fileHeader).toHaveTextContent('HEAD:docs/report.docx');
    expect(fileHeader).toHaveTextContent('working tree:docs/report.docx');

    expect(screen.getByTestId('docx-diff-kind-0')).toHaveTextContent('Paragraph text changed');
    expect(screen.getByTestId('docx-diff-before-0')).toHaveTextContent('Hello world');
    expect(screen.getByTestId('docx-diff-after-0')).toHaveTextContent('Hello brave world');
    expect(screen.getByTestId('docx-diff-text-0')).toHaveTextContent('Hello brave world');
  });

  it('distinguishes the staged and unstaged label pairs', () => {
    const { unmount } = render(
      <DocxDiffPanel
        status="available"
        diff={workingTreeDiff({ target: 'staged', leftLabel: 'HEAD', rightLabel: 'index' })}
      />
    );
    expect(screen.getByTestId('docx-diff-target')).toHaveTextContent('HEAD ↔ index');
    unmount();

    render(
      <DocxDiffPanel
        status="available"
        diff={workingTreeDiff({ target: 'unstaged', leftLabel: 'index', rightLabel: 'working tree' })}
      />
    );
    expect(screen.getByTestId('docx-diff-target')).toHaveTextContent('index ↔ working tree');
  });

  it('renders table cell changes with their row and cell coordinates', () => {
    render(
      <DocxDiffPanel
        status="available"
        diff={workingTreeDiff({
          changes: [
            textChange({
              kind: 'table-cell-changed',
              summary: 'Table cell changed: "Alpha" -> "Beta"',
              before: 'Alpha',
              after: 'Beta',
              oldParagraphIndex: null,
              newParagraphIndex: null,
              tableIndex: 0,
              rowIndex: 1,
              cellIndex: 1,
            }),
          ],
        })}
      />
    );

    expect(screen.getByTestId('docx-diff-kind-0')).toHaveTextContent('Table cell changed');
    expect(screen.getByTestId('docx-diff-location-0')).toHaveTextContent('table 0, row 1, cell 1');
    expect(screen.getByTestId('docx-diff-before-0')).toHaveTextContent('Alpha');
    expect(screen.getByTestId('docx-diff-after-0')).toHaveTextContent('Beta');
  });

  it('renders formatting and metadata changes with their property names', () => {
    render(
      <DocxDiffPanel
        status="available"
        diff={workingTreeDiff({
          changes: [
            textChange({
              kind: 'formatting-changed',
              summary: 'Formatting changed for "Styled text": Bold inherit -> on',
              before: 'inherit',
              after: 'on',
              property: 'Bold',
            }),
            textChange({
              kind: 'metadata-changed',
              summary: "Metadata 'PaperSummary' changed from 'none' to 'Letter'",
              before: 'none',
              after: 'Letter',
              property: 'PaperSummary',
              oldParagraphIndex: null,
              newParagraphIndex: null,
            }),
          ],
        })}
      />
    );

    expect(screen.getByTestId('docx-diff-kind-0')).toHaveTextContent('Formatting changed');
    expect(screen.getByTestId('docx-diff-location-0')).toHaveTextContent('Bold');
    expect(screen.getByTestId('docx-diff-kind-1')).toHaveTextContent('Metadata changed');
    expect(screen.getByTestId('docx-diff-location-1')).toHaveTextContent('PaperSummary');
  });

  it('renders a complete addition with /dev/null on the missing side', () => {
    render(
      <DocxDiffPanel
        status="available"
        diff={workingTreeDiff({
          path: 'docs/new.docx',
          leftExists: false,
          changes: [textChange({ kind: 'paragraph-added', before: null, after: 'New paragraph' })],
        })}
      />
    );

    const fileHeader = screen.getByTestId('docx-diff-file-header');
    expect(fileHeader).toHaveTextContent('/dev/null');
    expect(fileHeader).toHaveTextContent('working tree:docs/new.docx');
    expect(screen.getByTestId('docx-diff-kind-0')).toHaveTextContent('Paragraph added');
    expect(screen.queryByTestId('docx-diff-before-0')).not.toBeInTheDocument();
    expect(screen.getByTestId('docx-diff-after-0')).toHaveTextContent('New paragraph');
  });

  it('renders a complete removal with /dev/null on the missing side', () => {
    render(
      <DocxDiffPanel
        status="available"
        diff={workingTreeDiff({
          path: 'docs/gone.docx',
          rightExists: false,
          changes: [textChange({ kind: 'paragraph-removed', before: 'Removed paragraph', after: null })],
        })}
      />
    );

    expect(screen.getByTestId('docx-diff-file-header')).toHaveTextContent('/dev/null');
    expect(screen.getByTestId('docx-diff-kind-0')).toHaveTextContent('Paragraph removed');
    expect(screen.getByTestId('docx-diff-before-0')).toHaveTextContent('Removed paragraph');
    expect(screen.queryByTestId('docx-diff-after-0')).not.toBeInTheDocument();
  });

  it('renders an empty state when the two versions are equal', () => {
    render(<DocxDiffPanel status="available" diff={workingTreeDiff({ changes: [] })} />);

    expect(screen.getByTestId('docx-diff-empty')).toBeInTheDocument();
    expect(screen.getByTestId('docx-diff-empty')).toHaveTextContent(/no document changes/i);
    expect(screen.queryByTestId('docx-diff-change-0')).not.toBeInTheDocument();
  });

  it('summarizes a binary fallback with sizes and hashes instead of faking changes', () => {
    render(
      <DocxDiffPanel
        status="binary-fallback"
        failureMessage="The materialized version is not a valid DOCX package."
        diff={workingTreeDiff({
          changes: [],
          binarySummary: {
            leftExists: true,
            rightExists: true,
            leftSizeBytes: 120,
            rightSizeBytes: 340,
            leftSha256: 'a'.repeat(64),
            rightSha256: 'b'.repeat(64),
            contentIdentical: false,
          },
        })}
      />
    );

    expect(screen.getByTestId('docx-diff-binary')).toBeInTheDocument();
    expect(screen.getByTestId('docx-diff-binary')).toHaveTextContent(/binary change/i);
    expect(screen.getByTestId('docx-diff-binary-summary')).toHaveTextContent('HEAD');
    expect(screen.getByTestId('docx-diff-binary-summary')).toHaveTextContent('120 bytes');
    expect(screen.getByTestId('docx-diff-binary-summary')).toHaveTextContent('340 bytes');
    expect(screen.getByTestId('docx-diff-binary-summary')).toHaveTextContent('a'.repeat(64));
    expect(screen.getByTestId('docx-diff-binary-summary')).toHaveTextContent('b'.repeat(64));
    expect(screen.queryByTestId('docx-diff-change-0')).not.toBeInTheDocument();
  });

  it('reports byte-identical versions in a binary fallback summary', () => {
    render(
      <DocxDiffPanel
        status="binary-fallback"
        diff={workingTreeDiff({
          changes: [],
          binarySummary: {
            leftExists: true,
            rightExists: true,
            leftSizeBytes: 64,
            rightSizeBytes: 64,
            leftSha256: 'c'.repeat(64),
            rightSha256: 'c'.repeat(64),
            contentIdentical: true,
          },
        })}
      />
    );

    expect(screen.getByTestId('docx-diff-binary-summary')).toHaveTextContent(/byte-identical/i);
  });

  it('shows typed failures with role=alert and never a fabricated report', () => {
    const { unmount } = render(
      <DocxDiffPanel
        status="failed"
        failureMessage="The materialized version could not be parsed as a DOCX document."
      />
    );
    expect(screen.getByRole('alert')).toHaveTextContent(
      'The materialized version could not be parsed as a DOCX document.'
    );
    expect(screen.queryByTestId('docx-diff')).not.toBeInTheDocument();
    unmount();

    render(<DocxDiffPanel status="path-rejected" />);
    expect(screen.getByRole('alert')).toHaveTextContent('not a worktree-relative document path');
  });

  it('renders Git-unavailable, no-repository and loading states', () => {
    const { unmount } = render(<DocxDiffPanel status="git-unavailable" />);
    expect(screen.getByTestId('docx-diff-git-unavailable')).toBeInTheDocument();
    unmount();

    const { unmount: unmountNoRepo } = render(<DocxDiffPanel status="no-repository" />);
    expect(screen.getByTestId('docx-diff-no-repository')).toBeInTheDocument();
    unmountNoRepo();

    render(<DocxDiffPanel status="available" diff={null} isLoading />);
    expect(screen.getByTestId('docx-diff-loading')).toBeInTheDocument();
    expect(screen.queryByTestId('docx-diff')).not.toBeInTheDocument();
  });

  it('treats injected HTML in change content and path as inert text', () => {
    const { container } = render(
      <DocxDiffPanel
        status="available"
        diff={workingTreeDiff({
          path: 'docs/<script>alert(1)</script>.docx',
          changes: [
            textChange({
              before: '<img src=x onerror=alert(1)>',
              after: '<script>alert(2)</script>',
            }),
          ],
        })}
      />
    );

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('script')).toBeNull();
    expect(screen.getByTestId('docx-diff-before-0')).toHaveTextContent(
      '<img src=x onerror=alert(1)>'
    );
    expect(screen.getByTestId('docx-diff-after-0')).toHaveTextContent(
      '<script>alert(2)</script>'
    );
    expect(screen.getByTestId('docx-diff-file-header')).toHaveTextContent(
      'docs/<script>alert(1)</script>.docx'
    );
  });

  it('is strictly read-only: no buttons, inputs or links in a rendered diff', () => {
    const { container } = render(<DocxDiffPanel status="available" diff={workingTreeDiff()} />);

    expect(container.querySelectorAll('button')).toHaveLength(0);
    expect(container.querySelectorAll('input')).toHaveLength(0);
    expect(container.querySelectorAll('a')).toHaveLength(0);
  });
});
