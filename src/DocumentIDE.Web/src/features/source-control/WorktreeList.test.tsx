import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import WorktreeList, { type WorktreeListEntry } from './WorktreeList';

const mainWorktree: WorktreeListEntry = {
  path: 'C:\\projects\\app',
  headCommit: '1111111111111111111111111111111111111111',
  branchName: 'main',
  isDetached: false,
  isBare: false,
  isLocked: false,
  isPrunable: false,
  isMainWorktree: true,
};

const linkedWorktree: WorktreeListEntry = {
  path: 'C:\\projects\\app-wt-feature',
  headCommit: '2222222222222222222222222222222222222222',
  branchName: 'feature/spaces and unicode \u00e9\u00e0',
  isDetached: false,
  isBare: false,
  isLocked: false,
  isPrunable: false,
  isMainWorktree: false,
};

describe('WorktreeList Component', () => {
  it('lists the main and linked worktrees with read-only branch metadata', () => {
    render(
      <WorktreeList
        gitAvailable
        repositoryDetected
        worktrees={[mainWorktree, linkedWorktree]}
        activeWorktreePath={mainWorktree.path}
      />
    );

    expect(screen.getByText('C:\\projects\\app')).toBeInTheDocument();
    expect(screen.getByText('C:\\projects\\app-wt-feature')).toBeInTheDocument();
    expect(screen.getByText('main')).toBeInTheDocument();
    expect(screen.getByText('feature/spaces and unicode \u00e9\u00e0')).toBeInTheDocument();
    expect(screen.getByText(/main worktree/i)).toBeInTheDocument();

    // Read-only metadata: no branch-management controls may exist.
    expect(screen.queryByRole('button', { name: /branch/i })).not.toBeInTheDocument();
  });

  it('shows detached and locked metadata without a branch name', () => {
    const detached: WorktreeListEntry = {
      path: 'C:\\projects\\app-wt-release',
      headCommit: '3333333333333333333333333333333333333333',
      branchName: null,
      isDetached: true,
      isBare: false,
      isLocked: true,
      isPrunable: false,
      isMainWorktree: false,
    };

    render(
      <WorktreeList gitAvailable repositoryDetected worktrees={[detached]} />
    );

    expect(screen.getByText('Detached')).toBeInTheDocument();
    expect(screen.getByText('Locked')).toBeInTheDocument();
  });

  it('renders a Git-unavailable state when Git is not discoverable', () => {
    render(
      <WorktreeList gitAvailable={false} repositoryDetected={false} worktrees={[]} />
    );

    expect(screen.getByTestId('worktree-list-git-unavailable')).toBeInTheDocument();
  });

  it('renders a no-repository state for folders outside Git', () => {
    render(
      <WorktreeList gitAvailable repositoryDetected={false} worktrees={[]} />
    );

    expect(screen.getByTestId('worktree-list-no-repository')).toBeInTheDocument();
  });

  it('renders an empty state when Git reports no worktrees', () => {
    render(<WorktreeList gitAvailable repositoryDetected worktrees={[]} />);

    expect(screen.getByTestId('worktree-list-empty')).toBeInTheDocument();
  });

  it('invokes onOpenWorktree with the canonical path of the chosen worktree', () => {
    const onOpenWorktree = vi.fn();

    render(
      <WorktreeList
        gitAvailable
        repositoryDetected
        worktrees={[mainWorktree, linkedWorktree]}
        activeWorktreePath={mainWorktree.path}
        onOpenWorktree={onOpenWorktree}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /open/i }));

    expect(onOpenWorktree).toHaveBeenCalledTimes(1);
    expect(onOpenWorktree).toHaveBeenCalledWith(linkedWorktree.path);
  });

  it('marks the active worktree and does not offer to reopen it', () => {
    render(
      <WorktreeList
        gitAvailable
        repositoryDetected
        worktrees={[mainWorktree, linkedWorktree]}
        activeWorktreePath={linkedWorktree.path}
        onOpenWorktree={() => undefined}
      />
    );

    expect(screen.getByTestId(`worktree-active-${linkedWorktree.path}`)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /open/i })).toHaveLength(1);
  });

  it('disables open actions while a worktree is being opened', () => {
    render(
      <WorktreeList
        gitAvailable
        repositoryDetected
        worktrees={[mainWorktree, linkedWorktree]}
        activeWorktreePath={null}
        onOpenWorktree={() => undefined}
        busyPath={linkedWorktree.path}
      />
    );

    const buttons = screen.getAllByRole('button', { name: /opening|open/i });
    for (const button of buttons) {
      expect(button).toBeDisabled();
    }
  });

  it('shows the typed error message when listing or opening fails', () => {
    render(
      <WorktreeList
        gitAvailable
        repositoryDetected
        worktrees={[]}
        errorMessage="The worktree is no longer registered with Git."
      />
    );

    expect(screen.getByRole('alert')).toHaveTextContent(
      'The worktree is no longer registered with Git.'
    );
  });
});
