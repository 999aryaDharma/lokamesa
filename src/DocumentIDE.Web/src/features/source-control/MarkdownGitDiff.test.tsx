import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import MarkdownGitDiff, {
  type GitDiffView,
  type GitDiffHunkView,
} from './MarkdownGitDiff';

/**
 * SCM-04 frontend RED/GREEN contract.
 *
 * Spec 13 §9.1: the UI SHALL distinguish HEAD↔working tree, HEAD↔index and
 * index↔working tree as appropriate. §9.3: a binary change is summarized, not
 * faked into a semantic diff. Content is rendered as inert text (no HTML
 * interpretation) and the surface is strictly read-only.
 */

function stagedFixture(): GitDiffView {
  const hunks: GitDiffHunkView[] = [
    {
      header: '@@ -1,2 +1,2 @@',
      lines: [
        {
          kind: 'context',
          content: 'one',
          oldLineNumber: 1,
          newLineNumber: 1,
          noNewlineAtEndOfFile: false,
        },
        {
          kind: 'removed',
          content: 'two',
          oldLineNumber: 2,
          newLineNumber: null,
          noNewlineAtEndOfFile: false,
        },
        {
          kind: 'added',
          content: 'two-staged',
          oldLineNumber: null,
          newLineNumber: 2,
          noNewlineAtEndOfFile: false,
        },
      ],
    },
  ];

  return {
    target: 'staged',
    path: 'docs/plan.md',
    leftLabel: 'HEAD',
    rightLabel: 'index',
    leftExists: true,
    rightExists: true,
    isBinary: false,
    hunks,
  };
}

describe('MarkdownGitDiff Component', () => {
  it('renders a staged HEAD-to-index diff with header, hunks, gutters and signs', () => {
    render(<MarkdownGitDiff status="available" diff={stagedFixture()} />);

    expect(screen.getByTestId('markdown-diff')).toBeInTheDocument();
    expect(screen.getByTestId('markdown-diff-target')).toHaveTextContent('HEAD ↔ index');

    const fileHeader = screen.getByTestId('markdown-diff-file-header');
    expect(fileHeader).toHaveTextContent('--- HEAD:docs/plan.md');
    expect(fileHeader).toHaveTextContent('+++ index:docs/plan.md');

    expect(screen.getByTestId('markdown-diff-hunk-0')).toHaveTextContent('@@ -1,2 +1,2 @@');

    const removed = screen.getByTestId('markdown-diff-line-1');
    expect(removed).toHaveTextContent('-two');
    expect(removed).toHaveTextContent('2');

    const added = screen.getByTestId('markdown-diff-line-2');
    expect(added).toHaveTextContent('+two-staged');

    const context = screen.getByTestId('markdown-diff-line-0');
    expect(context).toHaveTextContent('one');
  });

  it('distinguishes the index-to-working-tree unstaged target', () => {
    const diff: GitDiffView = {
      ...stagedFixture(),
      target: 'unstaged',
      leftLabel: 'index',
      rightLabel: 'working tree',
    };

    render(<MarkdownGitDiff status="available" diff={diff} />);

    expect(screen.getByTestId('markdown-diff-target')).toHaveTextContent(
      'index ↔ working tree'
    );
  });

  it('renders /dev/null for a side that does not exist (complete addition)', () => {
    const diff: GitDiffView = {
      target: 'working-tree',
      path: 'docs/new.md',
      leftLabel: 'HEAD',
      rightLabel: 'working tree',
      leftExists: false,
      rightExists: true,
      isBinary: false,
      hunks: [
        {
          header: '@@ -0,0 +1,1 @@',
          lines: [
            {
              kind: 'added',
              content: '# New',
              oldLineNumber: null,
              newLineNumber: 1,
              noNewlineAtEndOfFile: false,
            },
          ],
        },
      ],
    };

    render(<MarkdownGitDiff status="available" diff={diff} />);

    const fileHeader = screen.getByTestId('markdown-diff-file-header');
    expect(fileHeader).toHaveTextContent('--- /dev/null');
    expect(fileHeader).toHaveTextContent('+++ working tree:docs/new.md');
    expect(screen.getByTestId('markdown-diff-line-0')).toHaveTextContent('+# New');
  });

  it('summarizes a binary change instead of pretending a text diff', () => {
    const diff: GitDiffView = {
      target: 'working-tree',
      path: 'docs/asset.bin',
      leftLabel: 'HEAD',
      rightLabel: 'working tree',
      leftExists: true,
      rightExists: true,
      isBinary: true,
      hunks: [],
    };

    render(<MarkdownGitDiff status="available" diff={diff} />);

    expect(screen.getByTestId('markdown-diff-binary')).toBeInTheDocument();
    expect(screen.queryByTestId('markdown-diff-hunk-0')).not.toBeInTheDocument();
    expect(screen.getByTestId('markdown-diff-binary')).toHaveTextContent(/binary/i);
  });

  it('renders an empty state when the two versions are equal', () => {
    const diff: GitDiffView = {
      target: 'unstaged',
      path: 'docs/plan.md',
      leftLabel: 'index',
      rightLabel: 'working tree',
      leftExists: true,
      rightExists: true,
      isBinary: false,
      hunks: [],
    };

    render(<MarkdownGitDiff status="available" diff={diff} />);

    expect(screen.getByTestId('markdown-diff-empty')).toBeInTheDocument();
    expect(screen.getByTestId('markdown-diff-empty')).toHaveTextContent(/no changes/i);
  });

  it('marks a final line that has no trailing newline', () => {
    const diff: GitDiffView = {
      ...stagedFixture(),
      hunks: [
        {
          header: '@@ -1 +1 @@',
          lines: [
            {
              kind: 'removed',
              content: 'a',
              oldLineNumber: 1,
              newLineNumber: null,
              noNewlineAtEndOfFile: true,
            },
            {
              kind: 'added',
              content: 'a',
              oldLineNumber: null,
              newLineNumber: 1,
              noNewlineAtEndOfFile: false,
            },
          ],
        },
      ],
    };

    render(<MarkdownGitDiff status="available" diff={diff} />);

    expect(screen.getAllByText('\\ No newline at end of file')).toHaveLength(1);
    expect(screen.getByTestId('markdown-diff-line-0')).toHaveTextContent(
      '\\ No newline at end of file'
    );
    expect(screen.getByTestId('markdown-diff-line-1')).not.toHaveTextContent(
      '\\ No newline at end of file'
    );
  });

  it('renders Unicode filenames and content without mangling', () => {
    const path = 'docs/notes caf\u00e9 \u65e5\u672c\u8a9e (v2) & more.md';
    const diff: GitDiffView = {
      target: 'working-tree',
      path,
      leftLabel: 'HEAD',
      rightLabel: 'working tree',
      leftExists: true,
      rightExists: true,
      isBinary: false,
      hunks: [
        {
          header: '@@ -2,3 +2,4 @@',
          lines: [
            {
              kind: 'context',
              content: '- caf\u00e9',
              oldLineNumber: 3,
              newLineNumber: 3,
              noNewlineAtEndOfFile: false,
            },
            {
              kind: 'added',
              content: '- \u65e5\u672c\u8a9e\u306e\u30e1\u30e2',
              oldLineNumber: null,
              newLineNumber: 4,
              noNewlineAtEndOfFile: false,
            },
          ],
        },
      ],
    };

    render(<MarkdownGitDiff status="available" diff={diff} />);

    expect(screen.getByTestId('markdown-diff-file-header')).toHaveTextContent(path);
    expect(screen.getByTestId('markdown-diff-line-0')).toHaveTextContent('- caf\u00e9');
    expect(screen.getByTestId('markdown-diff-line-1')).toHaveTextContent(
      '- \u65e5\u672c\u8a9e\u306e\u30e1\u30e2'
    );
  });

  it('treats injected HTML in content and path as inert text', () => {
    const { container } = render(
      <MarkdownGitDiff
        status="available"
        diff={{
          target: 'working-tree',
          path: 'docs/<script>alert(1)</script>.md',
          leftLabel: 'HEAD',
          rightLabel: 'working tree',
          leftExists: true,
          rightExists: true,
          isBinary: false,
          hunks: [
            {
              header: '@@ -1 +1 @@',
              lines: [
                {
                  kind: 'added',
                  content: '<img src=x onerror=alert(1)>',
                  oldLineNumber: null,
                  newLineNumber: 1,
                  noNewlineAtEndOfFile: false,
                },
              ],
            },
          ],
        }}
      />
    );

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('script')).toBeNull();
    expect(screen.getByTestId('markdown-diff-line-0')).toHaveTextContent(
      '<img src=x onerror=alert(1)>'
    );
    expect(screen.getByTestId('markdown-diff-file-header')).toHaveTextContent(
      'docs/<script>alert(1)</script>.md'
    );
  });

  it('shows a typed failure message with role=alert', () => {
    render(
      <MarkdownGitDiff
        status="failed"
        failureMessage="The file could not be read while the index lock was held."
      />
    );

    expect(screen.getByRole('alert')).toHaveTextContent(
      'The file could not be read while the index lock was held.'
    );
    expect(screen.queryByTestId('markdown-diff')).not.toBeInTheDocument();
  });

  it('renders a rejection for unsafe paths without attempting a diff', () => {
    render(
      <MarkdownGitDiff
        status="path-rejected"
        failureMessage="The path '../outside.md' is not a worktree-relative document path."
      />
    );

    expect(screen.getByRole('alert')).toHaveTextContent('not a worktree-relative document path');
    expect(screen.queryByTestId('markdown-diff-file-header')).not.toBeInTheDocument();
  });

  it('renders Git-unavailable and no-repository states', () => {
    const { unmount } = render(<MarkdownGitDiff status="git-unavailable" />);
    expect(screen.getByTestId('markdown-diff-git-unavailable')).toBeInTheDocument();
    unmount();

    render(<MarkdownGitDiff status="no-repository" />);
    expect(screen.getByTestId('markdown-diff-no-repository')).toBeInTheDocument();
  });

  it('renders a loading state while the diff is materialized', () => {
    render(<MarkdownGitDiff status="available" diff={null} isLoading />);

    expect(screen.getByTestId('markdown-diff-loading')).toBeInTheDocument();
    expect(screen.queryByTestId('markdown-diff')).not.toBeInTheDocument();
  });

  it('is strictly read-only: no buttons, inputs or links in a rendered diff', () => {
    const { container } = render(<MarkdownGitDiff status="available" diff={stagedFixture()} />);

    expect(container.querySelectorAll('button')).toHaveLength(0);
    expect(container.querySelectorAll('input')).toHaveLength(0);
    expect(container.querySelectorAll('a')).toHaveLength(0);
  });
});
