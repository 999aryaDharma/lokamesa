import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import WorktreeCreateDialog, {
  removalBlockedReason,
  type RemovableWorktree,
  type WorktreeCreateRequest,
} from './WorktreeCreateDialog';

const cleanWorktree: RemovableWorktree = {
  path: 'C:\\projects\\app-wt-feature',
  branchName: 'feature/x',
};

interface DialogOverrides {
  gitAvailable?: boolean;
  repositoryDetected?: boolean;
  onCreateWorktree?: (request: WorktreeCreateRequest) => void;
  removableWorktrees?: RemovableWorktree[];
  onRemoveWorktree?: (path: string) => void;
  onRefreshWorktrees?: () => void;
  busy?: boolean;
  errorMessage?: string | null;
}

function renderDialog(overrides: DialogOverrides = {}) {
  return render(
    <WorktreeCreateDialog
      gitAvailable={overrides.gitAvailable ?? true}
      repositoryDetected={overrides.repositoryDetected ?? true}
      onCreateWorktree={overrides.onCreateWorktree}
      removableWorktrees={overrides.removableWorktrees}
      onRemoveWorktree={overrides.onRemoveWorktree}
      onRefreshWorktrees={overrides.onRefreshWorktrees}
      busy={overrides.busy ?? false}
      errorMessage={overrides.errorMessage}
    />
  );
}

describe('WorktreeCreateDialog Component', () => {
  it('creates a worktree from the submitted existing ref and returns to idle', () => {
    const onCreateWorktree = vi.fn();

    renderDialog({ onCreateWorktree });

    fireEvent.change(screen.getByLabelText(/target path/i), {
      target: { value: '  C:\\projects\\app-wt-feature  ' },
    });
    fireEvent.change(screen.getByLabelText(/existing ref/i), {
      target: { value: 'feature/x' },
    });
    fireEvent.click(screen.getByRole('button', { name: /create worktree/i }));

    expect(onCreateWorktree).toHaveBeenCalledTimes(1);
    expect(onCreateWorktree).toHaveBeenCalledWith({
      targetPath: 'C:\\projects\\app-wt-feature',
      ref: 'feature/x',
    });
  });

  it('does not offer any branch-creation control', () => {
    renderDialog();

    expect(screen.queryByRole('button', { name: /branch/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.getByText(/no branch is ever created/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/existing ref/i)).toBeInTheDocument();
  });

  it('disables create until both the target path and an existing ref are provided', () => {
    renderDialog();

    const createButton = screen.getByRole('button', { name: /create worktree/i });
    expect(createButton).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/target path/i), {
      target: { value: 'C:\\projects\\app-wt-feature' },
    });
    expect(createButton).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/existing ref/i), {
      target: { value: 'feature/x' },
    });
    expect(createButton).toBeEnabled();

    fireEvent.change(screen.getByLabelText(/existing ref/i), {
      target: { value: '   ' },
    });
    expect(createButton).toBeDisabled();
  });

  it('renders a Git-unavailable state when Git is not discoverable', () => {
    renderDialog({ gitAvailable: false });

    expect(screen.getByTestId('worktree-dialog-git-unavailable')).toBeInTheDocument();
    expect(screen.queryByLabelText(/target path/i)).not.toBeInTheDocument();
  });

  it('renders a no-repository state for folders outside Git', () => {
    renderDialog({ repositoryDetected: false });

    expect(screen.getByTestId('worktree-dialog-no-repository')).toBeInTheDocument();
    expect(screen.queryByLabelText(/target path/i)).not.toBeInTheDocument();
  });

  it('requires an explicit confirmation before removing a worktree', () => {
    const onRemoveWorktree = vi.fn();

    renderDialog({ removableWorktrees: [cleanWorktree], onRemoveWorktree });

    expect(screen.getByText(cleanWorktree.path)).toBeInTheDocument();
    expect(screen.getByText('feature/x')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: `Remove worktree ${cleanWorktree.path}` }));

    expect(onRemoveWorktree).not.toHaveBeenCalled();
    expect(screen.getByTestId(`worktree-remove-confirm-${cleanWorktree.path}`)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /confirm remove/i }));

    expect(onRemoveWorktree).toHaveBeenCalledTimes(1);
    expect(onRemoveWorktree).toHaveBeenCalledWith(cleanWorktree.path);
    expect(
      screen.queryByTestId(`worktree-remove-confirm-${cleanWorktree.path}`)
    ).not.toBeInTheDocument();
  });

  it('cancels a pending removal without invoking the callback', () => {
    const onRemoveWorktree = vi.fn();

    renderDialog({ removableWorktrees: [cleanWorktree], onRemoveWorktree });

    fireEvent.click(screen.getByRole('button', { name: `Remove worktree ${cleanWorktree.path}` }));
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));

    expect(
      screen.queryByTestId(`worktree-remove-confirm-${cleanWorktree.path}`)
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: `Remove worktree ${cleanWorktree.path}` })
    ).toBeInTheDocument();
    expect(onRemoveWorktree).not.toHaveBeenCalled();
  });

  it('blocks removal of a dirty worktree with a typed reason and no remove action', () => {
    const dirty: RemovableWorktree = { ...cleanWorktree, isDirty: true };

    renderDialog({ removableWorktrees: [dirty], onRemoveWorktree: () => undefined });

    const blocked = screen.getByTestId(`worktree-remove-blocked-${dirty.path}`);
    expect(blocked).toHaveTextContent(/uncommitted changes/i);
    expect(
      screen.queryByRole('button', { name: `Remove worktree ${dirty.path}` })
    ).not.toBeInTheDocument();
  });

  it('blocks removal of main, locked, prunable, and unknown-cleanliness worktrees', () => {
    const blockedCases: Array<{ worktree: RemovableWorktree; reason: RegExp }> = [
      {
        worktree: { ...cleanWorktree, isMainWorktree: true },
        reason: /main worktree/i,
      },
      {
        worktree: { ...cleanWorktree, isLocked: true },
        reason: /locked/i,
      },
      {
        worktree: { ...cleanWorktree, isPrunable: true },
        reason: /directory is missing/i,
      },
      {
        worktree: { ...cleanWorktree, isCleanlinessUnknown: true },
        reason: /could not be determined/i,
      },
    ];

    for (const { worktree, reason } of blockedCases) {
      const { unmount } = renderDialog({ removableWorktrees: [worktree] });

      expect(screen.getByTestId(`worktree-remove-blocked-${worktree.path}`)).toHaveTextContent(
        reason
      );
      expect(
        screen.queryByRole('button', { name: `Remove worktree ${worktree.path}` })
      ).not.toBeInTheDocument();

      unmount();
    }
  });

  it('labels a detached worktree without inventing a branch name', () => {
    renderDialog({ removableWorktrees: [{ path: 'C:\\projects\\app-wt-tag', branchName: null }] });

    expect(screen.getByText('C:\\projects\\app-wt-tag')).toBeInTheDocument();
    expect(screen.getByText('Detached')).toBeInTheDocument();
  });

  it('disables every action while a worktree operation is in flight', () => {
    renderDialog({
      removableWorktrees: [cleanWorktree],
      onRemoveWorktree: () => undefined,
      onRefreshWorktrees: () => undefined,
      busy: true,
    });

    expect(screen.getByLabelText(/target path/i)).toBeDisabled();
    expect(screen.getByLabelText(/existing ref/i)).toBeDisabled();
    expect(screen.getByRole('button', { name: /working/i })).toBeDisabled();
    expect(
      screen.getByRole('button', { name: `Remove worktree ${cleanWorktree.path}` })
    ).toBeDisabled();
    expect(screen.getByRole('button', { name: /refresh worktrees/i })).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: `Remove worktree ${cleanWorktree.path}` }));
    expect(
      screen.queryByTestId(`worktree-remove-confirm-${cleanWorktree.path}`)
    ).not.toBeInTheDocument();
  });

  it('shows the typed error message when a worktree operation fails', () => {
    renderDialog({
      errorMessage: 'A branch named feature/x is already checked out in another worktree.',
    });

    expect(screen.getByRole('alert')).toHaveTextContent(
      'A branch named feature/x is already checked out in another worktree.'
    );
  });

  it('invokes the refresh callback for safe metadata pruning', () => {
    const onRefreshWorktrees = vi.fn();

    renderDialog({
      removableWorktrees: [{ ...cleanWorktree, isPrunable: true }],
      onRefreshWorktrees,
    });

    fireEvent.click(screen.getByRole('button', { name: /refresh worktrees/i }));

    expect(onRefreshWorktrees).toHaveBeenCalledTimes(1);
  });

  it('hides the refresh action when no handler is provided', () => {
    renderDialog({ removableWorktrees: [cleanWorktree] });

    expect(screen.queryByRole('button', { name: /refresh worktrees/i })).not.toBeInTheDocument();
  });
});

describe('removalBlockedReason', () => {
  it('returns null for a clean, removable worktree', () => {
    expect(removalBlockedReason(cleanWorktree)).toBeNull();
  });

  it('mirrors the backend preflight precedence: main, locked, missing, dirty, unknown', () => {
    expect(
      removalBlockedReason({
        path: cleanWorktree.path,
        branchName: 'main',
        isMainWorktree: true,
        isLocked: true,
        isPrunable: true,
        isDirty: true,
        isCleanlinessUnknown: true,
      })
    ).toMatch(/main worktree/i);

    expect(
      removalBlockedReason({
        path: cleanWorktree.path,
        branchName: 'feature/x',
        isLocked: true,
        isPrunable: true,
        isDirty: true,
      })
    ).toMatch(/locked/i);

    expect(
      removalBlockedReason({
        path: cleanWorktree.path,
        branchName: 'feature/x',
        isPrunable: true,
        isDirty: true,
      })
    ).toMatch(/directory is missing/i);

    expect(
      removalBlockedReason({
        path: cleanWorktree.path,
        branchName: 'feature/x',
        isDirty: true,
        isCleanlinessUnknown: true,
      })
    ).toMatch(/uncommitted changes/i);
  });
});
