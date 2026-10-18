import { useCallback, useEffect, useState } from 'react';
import type {
  SourceControlRestoreResult,
  SourceControlRestoreReviewInfo,
  SourceControlRestoreReviewResult,
} from './sourceControlModels';

/**
 * SCM-10 — safe Git discard & restore dialog (spec 13 §15/§18/§20).
 *
 * The destructive action is never hidden behind a generic refresh. Opening the
 * dialog only reads a review from the privileged API; the dialog shows what
 * will be lost (diff, staged state, target version, recovery availability) and
 * requires the user to acknowledge that review before the distinct
 * `Discard changes` / `Restore version` action becomes available. The apply call
 * echoes the review token, so a file that changed after the review is refused
 * typed instead of being overwritten.
 *
 * The component is self-contained and props-driven; it performs no Git work
 * itself (the backend owns the controlled process boundary).
 */

export type DiscardRestoreMode = 'discard' | 'restore';

interface SourceControlApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

function apiUrl(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & SourceControlApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

async function postJson<T>(route: string, body: unknown, fallback: string): Promise<T> {
  const response = await fetch(`${apiUrl()}${route}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-DocumentIDE-Session-Token': sessionToken(),
    },
    body: JSON.stringify(body),
  });

  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `${fallback} (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }

  return data as T;
}

function resultMessage(
  result: SourceControlRestoreReviewResult | SourceControlRestoreResult
): string {
  const outcome = 'outcome' in result ? result.outcome : result.status;
  if (result.failure) {
    const detail = result.failure.errorMessage ?? result.message ?? `Git reported ${outcome}.`;
    return `${result.failure.errorKind}: ${detail}`;
  }

  return result.message ?? `Git reported ${outcome}.`;
}

const buttonClassName =
  'px-3 py-1.5 text-sm font-medium text-slate-700 bg-white border border-slate-300 rounded-lg ' +
  'hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-slate-400 disabled:opacity-50 ' +
  'transition-colors';

const destructiveButtonClassName =
  'px-3 py-1.5 text-sm font-medium text-white bg-red-600 border border-red-600 rounded-lg ' +
  'hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-400 disabled:opacity-50 ' +
  'transition-colors';

export interface DiscardRestoreDialogProps {
  /** Workspace folder or active-document path used to discover the repository. */
  path: string;
  /** Worktree-relative path exactly as Git reported it. */
  changePath: string;
  /** `discard` (default) restores from the index; `restore` reads a commit. */
  mode?: DiscardRestoreMode;
  /** Commit to restore from; required when `mode` is `restore`. */
  commit?: string | null;
  /** True when Git is not installed/discoverable; the dialog then queries nothing. */
  gitUnavailable?: boolean;
  /** Receives the successful result (refreshed status included). */
  onCompleted?: (result: SourceControlRestoreResult) => void;
  /** Dismissed without any destructive request. */
  onCancel?: () => void;
}

function shortCommit(commit: string | null): string {
  return commit ? commit.slice(0, 7) : '—';
}

export default function DiscardRestoreDialog({
  path,
  changePath,
  mode = 'discard',
  commit = null,
  gitUnavailable = false,
  onCompleted,
  onCancel,
}: DiscardRestoreDialogProps) {
  const [review, setReview] = useState<SourceControlRestoreReviewInfo | null>(null);
  const [reviewError, setReviewError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [applying, setApplying] = useState(false);
  const [applyError, setApplyError] = useState<string | null>(null);
  const [result, setResult] = useState<SourceControlRestoreResult | null>(null);

  const isRestore = mode === 'restore';
  const actionLabel = isRestore ? 'Restore version' : 'Discard changes';
  const heading = isRestore ? 'Restore document version' : 'Discard working-tree changes';

  const loadReview = useCallback(async () => {
    if (gitUnavailable) {
      return;
    }

    if (isRestore && !commit) {
      setReview(null);
      setReviewError('A commit is required to restore a document version.');
      return;
    }

    setLoading(true);
    setReviewError(null);
    try {
      const reviewed = await postJson<SourceControlRestoreReviewResult>(
        '/api/source-control/discard/review',
        { path, changePath, commit: isRestore ? commit : null },
        'The review request failed'
      );

      if (!reviewed.isAvailable || reviewed.review === null) {
        setReview(null);
        setReviewError(resultMessage(reviewed));
        return;
      }

      setReview(reviewed.review);
    } catch (error) {
      setReview(null);
      setReviewError(error instanceof Error ? error.message : 'The review request failed.');
    } finally {
      setLoading(false);
    }
  }, [changePath, commit, gitUnavailable, isRestore, path]);

  useEffect(() => {
    setReview(null);
    setReviewError(null);
    setAcknowledged(false);
    setResult(null);
    setApplyError(null);
    void loadReview();
  }, [loadReview]);

  async function apply() {
    if (!review || !acknowledged) {
      return;
    }

    setApplying(true);
    setApplyError(null);
    try {
      const applied = await postJson<SourceControlRestoreResult>(
        isRestore ? '/api/source-control/restore' : '/api/source-control/discard',
        {
          path,
          changePath,
          commit: isRestore ? review.sourceCommit ?? commit : null,
          reviewToken: review.reviewToken,
        },
        'The restore request failed'
      );

      if (!applied.isSuccess) {
        setResult(null);
        setApplyError(resultMessage(applied));
        return;
      }

      setResult(applied);
      onCompleted?.(applied);
    } catch (error) {
      setResult(null);
      setApplyError(error instanceof Error ? error.message : 'The restore request failed.');
    } finally {
      setApplying(false);
    }
  }

  const canApply =
    review !== null && review.isApplicable && acknowledged && !applying && result === null;

  return (
    <section data-testid="discard-restore-dialog" className="space-y-3 p-3">
      <header className="space-y-1">
        <h3 data-testid="discard-restore-heading" className="text-sm font-semibold text-slate-800">
          {heading}
        </h3>
        <p data-testid="discard-restore-warning" className="text-xs text-red-700">
          {isRestore
            ? 'The working-tree content of this document will be replaced by the selected version. Local changes cannot be undone unless a recovery snapshot is available.'
            : 'Uncommitted working-tree changes to this document will be discarded. This cannot be undone unless a recovery snapshot is available.'}
        </p>
      </header>

      {gitUnavailable && (
        <div
          data-testid="discard-restore-unavailable"
          className="p-2 text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg"
        >
          Git is not available, so no document version can be reviewed or restored.
        </div>
      )}

      {loading && <p className="text-xs text-slate-500">Reviewing the change…</p>}

      {reviewError && (
        <div role="alert" className="p-2 text-xs text-red-700 bg-red-50 border border-red-200 rounded-lg">
          {reviewError}
        </div>
      )}

      {review && (
        <>
          <dl className="space-y-1 text-xs text-slate-700">
            <div>
              <dt className="inline text-slate-500">File: </dt>
              <dd className="inline font-mono" data-testid="discard-restore-path">
                {review.path}
              </dd>
            </div>
            <div>
              <dt className="inline text-slate-500">Target version: </dt>
              <dd className="inline" data-testid="discard-restore-target">
                {review.targetSource === 'commit'
                  ? `commit ${shortCommit(review.sourceCommit)}`
                  : 'index (staged version)'}
              </dd>
            </div>
            <div>
              <dt className="inline text-slate-500">Staged: </dt>
              <dd className="inline" data-testid="discard-restore-staged">
                {review.isStaged
                  ? 'yes — the staged version is kept; only the unstaged edit is discarded'
                  : 'no'}
              </dd>
            </div>
            <div>
              <dt className="inline text-slate-500">Recovery: </dt>
              <dd className="inline" data-testid="discard-restore-recovery">
                {review.recoveryAvailable
                  ? 'available — a recovery snapshot is created before the change'
                  : `not applicable — ${review.recoveryDetail}`}
              </dd>
            </div>
          </dl>

          {!review.isApplicable && (
            <div
              data-testid="discard-restore-blocked"
              role="alert"
              className="p-2 text-xs text-amber-800 bg-amber-50 border border-amber-200 rounded-lg"
            >
              {review.blockedReason ?? 'This file cannot be restored safely.'}
            </div>
          )}

          <div className="space-y-1">
            <p className="text-xs font-medium text-slate-600">
              {isRestore ? 'Difference from the selected version' : 'Changes that will be lost'}
            </p>
            <pre
              data-testid="discard-restore-diff"
              className="max-h-56 overflow-auto whitespace-pre-wrap font-mono text-xs text-slate-800 p-2 bg-slate-50 border border-slate-200 rounded-lg"
            >
              {review.diff || 'Git reports no textual diff for this change.'}
            </pre>
            {review.isDiffTruncated && (
              <p className="text-xs text-slate-500">The diff is long; only its first part is shown.</p>
            )}
          </div>

          <label className="flex items-start gap-2 text-xs text-slate-700">
            <input
              type="checkbox"
              data-testid="discard-review-ack"
              checked={acknowledged}
              disabled={!review.isApplicable || result !== null}
              onChange={(event) => setAcknowledged(event.target.checked)}
              className="mt-0.5"
            />
            <span>I reviewed the changes that will be lost and want to continue.</span>
          </label>
        </>
      )}

      {applyError && (
        <div role="alert" className="p-2 text-xs text-red-700 bg-red-50 border border-red-200 rounded-lg">
          {applyError}
        </div>
      )}

      {result && (
        <div
          data-testid="discard-restore-result"
          className="p-2 text-xs text-slate-700 bg-slate-50 border border-slate-200 rounded-lg space-y-1"
        >
          <div>
            {result.outcome} — {result.isSuccess ? 'Git re-read the status below.' : 'Nothing was changed.'}
          </div>
          <div>
            Recovery snapshot: {result.snapshotId ? 'recorded' : 'not created'} · Document IDE
            History: {result.historyRecorded ? 'recorded' : 'not recorded'} · Reindex:{' '}
            {result.reindexed ? 'done' : 'not run'}
          </div>
          {result.status && (
            <div data-testid="discard-restore-status">
              Working-tree changes remaining:{' '}
              {result.status.sections.unstaged.length +
                result.status.sections.untracked.length +
                result.status.sections.staged.length}
            </div>
          )}
        </div>
      )}

      <div className="flex items-center gap-2">
        <button
          type="button"
          data-testid="discard-confirm"
          className={destructiveButtonClassName}
          disabled={!canApply}
          onClick={() => void apply()}
        >
          {applying ? 'Applying…' : actionLabel}
        </button>
        <button
          type="button"
          className={buttonClassName}
          disabled={applying}
          onClick={() => onCancel?.()}
        >
          Cancel
        </button>
      </div>
    </section>
  );
}
