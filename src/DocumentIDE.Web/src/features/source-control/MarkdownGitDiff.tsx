/**
 * SCM-04 — read-only review surface for one file's Markdown Git diff
 * (spec 13 §9.1/§9.3).
 *
 * This is a pure presentation component: it renders the typed diff outcome
 * that the SCM-04 service produced (staged HEAD↔index, working-tree
 * HEAD↔working tree, or unstaged index↔working tree). It never invokes Git,
 * never writes to the repository and never interprets file content as HTML:
 * React escapes every line, so a file containing `<script>` or `<img>`
 * markup is shown as inert text.
 *
 * Version content may legitimately be absent on one side (`/dev/null`, e.g.
 * a new or deleted file) and a binary change is summarized per §9.3 instead
 * of being faked into a line diff.
 */

/** Spec 13 §9.1 — the three version pairs the UI must distinguish. */
export type GitDiffTargetView = 'staged' | 'working-tree' | 'unstaged';

/** Typed outcome of one diff request (mirrors the Core service result). */
export type GitDiffStatusView =
  | 'available'
  | 'no-repository'
  | 'git-unavailable'
  | 'path-rejected'
  | 'failed';

export type GitDiffLineKindView = 'context' | 'added' | 'removed';

export interface GitDiffLineView {
  kind: GitDiffLineKindView;
  content: string;
  oldLineNumber: number | null;
  newLineNumber: number | null;
  noNewlineAtEndOfFile: boolean;
}

export interface GitDiffHunkView {
  header: string;
  lines: GitDiffLineView[];
}

export interface GitDiffView {
  target: GitDiffTargetView;
  path: string;
  leftLabel: string;
  rightLabel: string;
  leftExists: boolean;
  rightExists: boolean;
  isBinary: boolean;
  hunks: GitDiffHunkView[];
}

interface MarkdownGitDiffProps {
  status: GitDiffStatusView;
  diff?: GitDiffView | null;
  /** Typed failure message (already safe to display; never raw Git output dump). */
  failureMessage?: string | null;
  isLoading?: boolean;
}

const NO_NEWLINE_MARKER = '\\ No newline at end of file';

const lineKindClass: Record<GitDiffLineKindView, string> = {
  context: 'text-slate-700',
  added: 'bg-emerald-50 text-emerald-900',
  removed: 'bg-red-50 text-red-900',
};

function panelClass(extra: string): string {
  return `w-full max-w-3xl p-4 text-sm bg-white border border-slate-200 rounded-xl shadow-sm ${extra}`;
}

function lineSign(kind: GitDiffLineKindView): string {
  if (kind === 'added') {
    return '+';
  }
  if (kind === 'removed') {
    return '-';
  }
  return ' ';
}

export default function MarkdownGitDiff({
  status,
  diff,
  failureMessage,
  isLoading = false,
}: MarkdownGitDiffProps) {
  if (isLoading) {
    return (
      <div data-testid="markdown-diff-loading" className={panelClass('text-slate-500')}>
        Loading diff...
      </div>
    );
  }

  if (status === 'git-unavailable') {
    return (
      <div
        data-testid="markdown-diff-git-unavailable"
        className={panelClass('text-slate-600')}
      >
        Git is not available on this machine. Install Git to review changes.
      </div>
    );
  }

  if (status === 'no-repository') {
    return (
      <div data-testid="markdown-diff-no-repository" className={panelClass('text-slate-600')}>
        This folder is not inside a Git repository.
      </div>
    );
  }

  if (status !== 'available') {
    return (
      <div
        role="alert"
        className={panelClass('text-red-700 bg-red-50 border-red-200')}
      >
        {failureMessage ??
          (status === 'path-rejected'
            ? 'The requested path was rejected because it is not a worktree-relative document path.'
            : 'The diff could not be produced.')}
      </div>
    );
  }

  if (!diff) {
    return (
      <div role="alert" className={panelClass('text-red-700 bg-red-50 border-red-200')}>
        {failureMessage ?? 'No diff data was provided.'}
      </div>
    );
  }

  if (diff.isBinary) {
    return (
      <div data-testid="markdown-diff-binary" className={panelClass('text-slate-600 space-y-1')}>
        <p className="font-medium text-slate-700">Binary change</p>
        <p>
          {diff.path} differs between {diff.leftLabel} and {diff.rightLabel}; the content
          is not text, so no line diff is shown.
        </p>
      </div>
    );
  }

  if (diff.hunks.length === 0) {
    return (
      <div data-testid="markdown-diff-empty" className={panelClass('text-slate-500')}>
        No changes between {diff.leftLabel} and {diff.rightLabel} for {diff.path}.
      </div>
    );
  }

  let lineIndex = 0;

  return (
    <div data-testid="markdown-diff" className="w-full max-w-3xl space-y-2">
      <div
        data-testid="markdown-diff-target"
        className="text-sm font-medium text-slate-700"
      >
        {diff.leftLabel} ↔ {diff.rightLabel}
      </div>

      <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden">
        <div
          data-testid="markdown-diff-file-header"
          className="px-3 py-2 bg-slate-50 border-b border-slate-200 font-mono text-xs text-slate-700"
        >
          <div>{`--- ${diff.leftExists ? `${diff.leftLabel}:${diff.path}` : '/dev/null'}`}</div>
          <div>{`+++ ${diff.rightExists ? `${diff.rightLabel}:${diff.path}` : '/dev/null'}`}</div>
        </div>

        <div className="font-mono text-xs leading-5">
          {diff.hunks.map((hunk, hunkIndex) => (
            <div key={hunkIndex}>
              <div
                data-testid={`markdown-diff-hunk-${hunkIndex}`}
                className="px-3 py-1 bg-sky-50 text-sky-800 border-y border-sky-100"
              >
                {hunk.header}
              </div>
              {hunk.lines.map((line) => {
                const index = lineIndex;
                lineIndex += 1;

                return (
                  <div
                    key={index}
                    data-testid={`markdown-diff-line-${index}`}
                    className={`flex items-stretch ${lineKindClass[line.kind]}`}
                  >
                    <span className="w-10 shrink-0 px-2 text-right text-slate-400 select-none">
                      {line.oldLineNumber ?? ''}
                    </span>
                    <span className="w-10 shrink-0 px-2 text-right text-slate-400 select-none">
                      {line.newLineNumber ?? ''}
                    </span>
                    <span className="pl-2 pr-3 whitespace-pre-wrap break-words">
                      {lineSign(line.kind)}
                      {line.content}
                    </span>
                    {line.noNewlineAtEndOfFile && (
                      <span className="pr-3 text-slate-400 italic">{NO_NEWLINE_MARKER}</span>
                    )}
                  </div>
                );
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
