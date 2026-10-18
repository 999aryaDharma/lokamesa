import { render, screen, fireEvent } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import DiscardRestoreDialog from './DiscardRestoreDialog';
import type {
  SourceControlRestoreResult,
  SourceControlRestoreReviewResult,
} from './sourceControlModels';

/**
 * SCM-10 — safe Git discard & restore dialog.
 *
 * The destructive action is never hidden behind a generic refresh: the dialog
 * first shows what will be lost (diff, staged state, target version, recovery
 * availability), requires an explicit acknowledgement, and only then calls the
 * apply route with the review token. Every typed refusal stays visible
 * (spec 13 §15/§20).
 */

function reviewResult(
  overrides: Partial<SourceControlRestoreReviewResult> = {}
): SourceControlRestoreReviewResult {
  return {
    status: 'Available',
    isAvailable: true,
    errorCode: null,
    message: null,
    review: {
      mode: 'DiscardWorktree',
      path: 'docs/a.md',
      sourceCommit: null,
      targetSource: 'index',
      diff: '--- a/docs/a.md\n+++ b/docs/a.md\n-v1\n+edited content\n',
      isDiffTruncated: false,
      indexChange: 'Unmodified',
      workingTreeChange: 'Modified',
      statusCharacters: '.M',
      isStaged: false,
      isConflicted: false,
      isUntracked: false,
      hasWorkingTreeChange: true,
      recoveryAvailable: true,
      recoveryDetail: 'A recovery snapshot is written to Document IDE History before the restore.',
      currentSizeBytes: 14,
      isApplicable: true,
      blockedReason: null,
      reviewToken: 'review-token-1',
    },
    failure: null,
    ...overrides,
  };
}

function restoreResult(overrides: Partial<SourceControlRestoreResult> = {}): SourceControlRestoreResult {
  return {
    outcome: 'Succeeded',
    mode: 'DiscardWorktree',
    isSuccess: true,
    errorCode: null,
    message: null,
    operationId: 'scm-restore-abc',
    snapshotId: 'snapshot-1',
    recoveryRecorded: true,
    historyRecorded: true,
    reindexed: true,
    status: {
      status: 'Available',
      hasRepository: true,
      worktreeRoot: 'C:\\workspace',
      branchName: 'main',
      isDetached: false,
      headCommit: '1111111111111111111111111111111111111111',
      upstreamReference: null,
      hasUpstream: false,
      ahead: null,
      behind: null,
      sections: { staged: [], unstaged: [], untracked: [], conflicts: [] },
      failure: null,
    },
    failure: null,
    ...overrides,
  };
}

function jsonResponse(status: number, data: unknown): Response {
  return { ok: status < 400, status, json: async () => data } as Response;
}

type Handler = (url: string, init: RequestInit) => Response | Promise<Response>;

function stubFetch(handler: Handler) {
  const fetchMock = vi.fn(handler);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('DiscardRestoreDialog', () => {
  beforeEach(() => {
    Object.assign(window, {
      __DOCUMENTIDE_API_URL__: 'http://localhost:5000',
      __DOCUMENTIDE_SESSION_TOKEN__: 'secret',
    });
  });

  it('shows what will be lost and keeps the destructive action disabled until acknowledged', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, reviewResult()));

    render(<DiscardRestoreDialog path={'C:\\workspace'} changePath="docs/a.md" />);

    const diff = await screen.findByTestId('discard-restore-diff');
    expect(diff).toHaveTextContent('edited content');
    expect(screen.getByTestId('discard-restore-path')).toHaveTextContent('docs/a.md');
    expect(screen.getByTestId('discard-restore-recovery')).toHaveTextContent(/recovery snapshot/i);
    expect(screen.getByTestId('discard-restore-warning')).toHaveTextContent(/cannot be undone/i);

    const confirm = screen.getByTestId('discard-confirm');
    expect(confirm).toBeDisabled();

    // There is no generic refresh action that could discard anything.
    expect(screen.queryByRole('button', { name: /refresh/i })).toBeNull();

    fireEvent.click(screen.getByTestId('discard-review-ack'));
    expect(confirm).toBeEnabled();

    // Reviewing alone never applies the destructive action.
    expect(fetchMock.mock.calls.every(([url]) => String(url).includes('/discard/review'))).toBe(
      true
    );
  });

  it('applies the discard with the review token and reports the refreshed status', async () => {
    const onCompleted = vi.fn();
    const fetchMock = stubFetch(async (url) => {
      if (String(url).includes('/discard/review')) {
        return jsonResponse(200, reviewResult());
      }

      return jsonResponse(200, restoreResult());
    });

    render(
      <DiscardRestoreDialog
        path={'C:\\workspace'}
        changePath="docs/a.md"
        onCompleted={onCompleted}
      />
    );

    fireEvent.click(await screen.findByTestId('discard-review-ack'));
    fireEvent.click(screen.getByTestId('discard-confirm'));

    const result = await screen.findByTestId('discard-restore-result');
    expect(result).toHaveTextContent('Succeeded');
    expect(result).toHaveTextContent(/recovery snapshot/i);

    const applyCall = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith('/api/source-control/discard')
    );
    expect(applyCall).toBeDefined();
    expect(JSON.parse(String(applyCall![1].body))).toMatchObject({
      path: 'C:\\workspace',
      changePath: 'docs/a.md',
      reviewToken: 'review-token-1',
    });
    expect(applyCall![1].headers).toMatchObject({ 'X-DocumentIDE-Session-Token': 'secret' });
    expect(onCompleted).toHaveBeenCalledTimes(1);
  });

  it('calls the restore route with the selected commit', async () => {
    const fetchMock = stubFetch(async (url) => {
      if (String(url).includes('/discard/review')) {
        return jsonResponse(
          200,
          reviewResult({
            review: {
              ...reviewResult().review!,
              mode: 'RestoreVersion',
              sourceCommit: '2222222222222222222222222222222222222222',
              targetSource: 'commit',
            },
          })
        );
      }

      return jsonResponse(200, restoreResult({ mode: 'RestoreVersion' }));
    });

    render(
      <DiscardRestoreDialog
        path={'C:\\workspace'}
        changePath="docs/a.md"
        mode="restore"
        commit="2222222222222222222222222222222222222222"
      />
    );

    expect(await screen.findByTestId('discard-restore-target')).toHaveTextContent('2222222');
    fireEvent.click(screen.getByTestId('discard-review-ack'));
    fireEvent.click(screen.getByTestId('discard-confirm'));

    await screen.findByTestId('discard-restore-result');

    const applyCall = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith('/api/source-control/restore')
    );
    expect(applyCall).toBeDefined();
    expect(JSON.parse(String(applyCall![1].body))).toMatchObject({
      changePath: 'docs/a.md',
      commit: '2222222222222222222222222222222222222222',
      reviewToken: 'review-token-1',
    });
  });

  it('never lets a conflicted or untracked review be applied', async () => {
    stubFetch(async () =>
      jsonResponse(
        200,
        reviewResult({
          review: {
            ...reviewResult().review!,
            isConflicted: true,
            isApplicable: false,
            blockedReason: 'The file is an unmerged conflict participant; resolve the conflict first.',
          },
        })
      )
    );

    render(<DiscardRestoreDialog path={'C:\\workspace'} changePath="docs/a.md" />);

    expect(await screen.findByTestId('discard-restore-blocked')).toHaveTextContent(/conflict/i);
    expect(screen.getByTestId('discard-review-ack')).toBeDisabled();
    expect(screen.getByTestId('discard-confirm')).toBeDisabled();
  });

  it('surfaces a typed apply refusal as an alert and keeps the file safe', async () => {
    stubFetch(async (url) => {
      if (String(url).includes('/discard/review')) {
        return jsonResponse(200, reviewResult());
      }

      return jsonResponse(400, {
        errorCode: 'ReviewStale',
        error: 'The file changed after it was reviewed; nothing was restored.',
      });
    });

    render(<DiscardRestoreDialog path={'C:\\workspace'} changePath="docs/a.md" />);

    fireEvent.click(await screen.findByTestId('discard-review-ack'));
    fireEvent.click(screen.getByTestId('discard-confirm'));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('ReviewStale');
    expect(screen.queryByTestId('discard-restore-result')).toBeNull();
  });

  it('does not query the API when Git is not available', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, reviewResult()));

    render(
      <DiscardRestoreDialog path={'C:\\workspace'} changePath="docs/a.md" gitUnavailable />
    );

    expect(await screen.findByTestId('discard-restore-unavailable')).toHaveTextContent('Git');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('cancels without any destructive request', async () => {
    const onCancel = vi.fn();
    const fetchMock = stubFetch(async () => jsonResponse(200, reviewResult()));

    render(
      <DiscardRestoreDialog
        path={'C:\\workspace'}
        changePath="docs/a.md"
        onCancel={onCancel}
      />
    );

    await screen.findByTestId('discard-restore-diff');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/api/source-control/discard'))).toBe(
      false
    );
  });
});
