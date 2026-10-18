import { render, screen, within, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import ConflictPanel from './ConflictPanel';
import type {
  GitConflictStatus,
  SourceControlConflictEntry,
  SourceControlConflictModel,
} from './sourceControlModels';

/**
 * SCM-14 — Git conflict state & resolution shell (spec 13 §20/§21).
 *
 * The panel is props-driven: rendering it never fetches or runs Git. It must
 * make the unmerged state visible, name the actions that are blocked, and offer
 * only the existing document-open seam for registered documents. DOCX conflicts
 * are never merged automatically and no arbitrary-path opener exists.
 */

function conflict(
  path: string,
  overrides: Partial<SourceControlConflictEntry> = {}
): SourceControlConflictEntry {
  return {
    path,
    conflictKind: 'BothModified',
    statusCharacters: 'UU',
    isDocx: path.toLowerCase().endsWith('.docx'),
    openAction: path.toLowerCase().endsWith('.docx') ? 'OpenInWord' : 'OpenDocument',
    documentId: `doc-${path}`,
    ...overrides,
  };
}

function conflictState(
  overrides: Partial<SourceControlConflictModel> = {}
): SourceControlConflictModel {
  const conflicts = overrides.conflicts ?? [
    conflict('docs/agreement.docx'),
    conflict('docs/plan.md'),
  ];

  return {
    status: 'Available',
    hasRepository: true,
    hasConflicts: conflicts.length > 0,
    worktreeRoot: 'C:\\workspace',
    branchName: 'main',
    isDetached: false,
    headCommit: '1111111111111111111111111111111111111111',
    conflicts,
    blockedActions: [
      {
        action: 'Commit',
        errorCode: 'ConflictedWorktree',
        message: 'The Commit action is blocked; resolve the conflicts first.',
        affectedPaths: conflicts.map((entry) => entry.path),
      },
      {
        action: 'Pull',
        errorCode: 'ConflictedWorktree',
        message: 'The Pull action is blocked; resolve the conflicts first.',
        affectedPaths: conflicts.map((entry) => entry.path),
      },
    ],
    message: 'The repository has unresolved conflicts.',
    failure: null,
    ...overrides,
  };
}

describe('ConflictPanel', () => {
  it('renders the conflict summary and every blocked action', () => {
    const { container } = render(<ConflictPanel state={conflictState()} />);

    expect(screen.getByTestId('conflict-panel')).toBeInTheDocument();
    expect(screen.getByTestId('conflict-count')).toHaveTextContent('Conflicts (2)');

    const entries = screen.getAllByTestId(/^conflict-entry-/);
    expect(entries).toHaveLength(2);
    expect(within(entries[0]).getByText('docs/agreement.docx')).toBeInTheDocument();
    expect(within(entries[1]).getByText('docs/plan.md')).toBeInTheDocument();
    expect(screen.getAllByText('Both modified').length).toBeGreaterThan(0);

    const blocked = screen.getByTestId('conflict-blocked-actions');
    expect(within(blocked).getByTestId('conflict-blocked-Commit')).toBeInTheDocument();
    expect(within(blocked).getByTestId('conflict-blocked-Pull')).toBeInTheDocument();

    // The safety policy is stated, not implied by disabled buttons.
    expect(container).toHaveTextContent(/never merges conflicted content automatically/i);
    expect(container).toHaveTextContent(/resolved in Word/i);
  });

  it('opens registered documents only through the existing document-open seam', () => {
    const onOpenDocument = vi.fn();
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    render(<ConflictPanel state={conflictState()} onOpenDocument={onOpenDocument} />);

    fireEvent.click(screen.getByTestId('conflict-open-docs/agreement.docx'));
    expect(onOpenDocument).toHaveBeenCalledWith('doc-docs/agreement.docx');

    fireEvent.click(screen.getByTestId('conflict-open-docs/plan.md'));
    expect(onOpenDocument).toHaveBeenCalledWith('doc-docs/plan.md');

    // The panel itself never calls an API or builds a path-based opener.
    expect(fetchMock).not.toHaveBeenCalled();
    expect(onOpenDocument).toHaveBeenCalledTimes(2);
  });

  it('offers no opener for an unregistered conflict and shows no absolute path', () => {
    const onOpenDocument = vi.fn();
    const state = conflictState({
      conflicts: [
        conflict('docs/unregistered.md', { openAction: 'None', documentId: null }),
      ],
    });

    const { container } = render(<ConflictPanel state={state} onOpenDocument={onOpenDocument} />);

    expect(screen.queryByTestId('conflict-open-docs/unregistered.md')).toBeNull();
    expect(container).toHaveTextContent(/not a registered document/i);
    expect(container).not.toHaveTextContent('C:\\workspace');
    expect(onOpenDocument).not.toHaveBeenCalled();
  });

  it('marks DOCX conflicts as manual Word work and never auto-merged', () => {
    render(<ConflictPanel state={conflictState()} />);

    const row = screen.getByTestId('conflict-entry-docs/agreement.docx');
    expect(within(row).getByText(/DOCX —/)).toBeInTheDocument();
    expect(within(row).getByText(/never auto-merged/i)).toBeInTheDocument();
    expect(within(row).getByTestId('conflict-open-docs/agreement.docx')).toHaveTextContent(
      /open for manual resolution/i
    );
  });

  it('renders the explicit typed states instead of a misleading clean list', () => {
    const cases: Array<{ status: GitConflictStatus; label: RegExp }> = [
      { status: 'NoConflicts', label: /no unresolved conflicts/i },
      { status: 'NoRepository', label: /not inside a git repository/i },
      { status: 'GitUnavailable', label: /git is not available/i },
      { status: 'WorktreeMissing', label: /no longer exists/i },
      { status: 'IndexLocked', label: /index is locked/i },
      { status: 'Unreadable', label: /could not be read/i },
      { status: 'Failed', label: /could not be determined/i },
    ];

    for (const { status, label } of cases) {
      const { unmount } = render(
        <ConflictPanel
          state={conflictState({
            status,
            hasConflicts: false,
            conflicts: [],
            blockedActions: [],
            message: null,
          })}
        />
      );

      expect(screen.getByTestId(`conflict-status-${status}`)).toHaveTextContent(label);
      expect(screen.queryByTestId('conflict-blocked-actions')).toBeNull();
      expect(screen.queryAllByTestId(/^conflict-entry-/)).toHaveLength(0);
      unmount();
    }
  });

  it('renders transport errors and loading without fetching', () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const { rerender } = render(<ConflictPanel state={null} isLoading />);
    expect(screen.getByTestId('conflict-loading')).toHaveTextContent(/loading/i);

    rerender(<ConflictPanel state={null} errorMessage="The local API is unreachable." />);
    expect(screen.getByRole('alert')).toHaveTextContent(/local API is unreachable/i);

    rerender(<ConflictPanel state={null} />);
    expect(screen.getByTestId('conflict-no-state')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
