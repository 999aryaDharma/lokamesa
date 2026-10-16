import { render, screen, within, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import SourceControlPanel from './SourceControlPanel';
import type {
  SourceControlChangeEntry,
  SourceControlSections,
  SourceControlStatusModel,
} from './sourceControlModels';

function change(path: string, overrides: Partial<SourceControlChangeEntry> = {}): SourceControlChangeEntry {
  return {
    path,
    originalPath: null,
    state: 'Modified',
    indexChange: 'Unmodified',
    workingTreeChange: 'Modified',
    conflictKind: 'None',
    statusCharacters: '.M',
    absolutePath: `C:\\workspace\\${path}`,
    documentId: `doc-${path}`,
    ...overrides,
  };
}

function sections(overrides: Partial<SourceControlSections> = {}): SourceControlSections {
  return { staged: [], unstaged: [], untracked: [], conflicts: [], ...overrides };
}

function statusModel(overrides: Partial<SourceControlStatusModel> = {}): SourceControlStatusModel {
  return {
    status: 'Available',
    hasRepository: true,
    worktreeRoot: 'C:\\workspace',
    branchName: 'feature/production-v1',
    isDetached: false,
    headCommit: '1111111111111111111111111111111111111111',
    upstreamReference: 'origin/feature/production-v1',
    hasUpstream: true,
    ahead: 2,
    behind: 0,
    sections: sections(),
    failure: null,
    ...overrides,
  };
}

describe('SourceControlPanel', () => {
  it('shows three modified documents as three changes', () => {
    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({
            unstaged: [
              change('agreement.docx'),
              change('contract.docx'),
              change('proposal.docx'),
            ],
          }),
        })}
      />
    );

    const unstaged = screen.getByTestId('source-control-section-unstaged');
    expect(within(unstaged).getAllByRole('listitem')).toHaveLength(3);
    expect(within(unstaged).getByText('agreement.docx')).toBeInTheDocument();
    expect(within(unstaged).getByText('contract.docx')).toBeInTheDocument();
    expect(within(unstaged).getByText('proposal.docx')).toBeInTheDocument();
  });

  it('keeps staged and unstaged groups separate', () => {
    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({
            staged: [
              change('agreement.docx', { state: 'Modified', indexChange: 'Modified', workingTreeChange: 'Unmodified', statusCharacters: 'M.' }),
              change('implementation.md', { state: 'Modified', indexChange: 'Modified', workingTreeChange: 'Unmodified', statusCharacters: 'M.' }),
            ],
            unstaged: [change('contract.docx')],
          }),
        })}
      />
    );

    const staged = screen.getByTestId('source-control-section-staged');
    const unstaged = screen.getByTestId('source-control-section-unstaged');
    expect(staged).toHaveTextContent(/staged changes \(2\)/i);
    expect(unstaged).toHaveTextContent(/changes \(1\)/i);
    expect(within(staged).getByText('agreement.docx')).toBeInTheDocument();
    expect(within(unstaged).queryByText('agreement.docx')).not.toBeInTheDocument();
    expect(within(staged).queryByText('contract.docx')).not.toBeInTheDocument();
    expect(within(unstaged).getByText('contract.docx')).toBeInTheDocument();
  });

  it('shows a file with both staged and working-tree changes in both groups', () => {
    const both = change('shared.docx', {
      indexChange: 'Modified',
      workingTreeChange: 'Modified',
      statusCharacters: 'MM',
    });

    render(
      <SourceControlPanel
        status={statusModel({ sections: sections({ staged: [both], unstaged: [both] }) })}
      />
    );

    expect(within(screen.getByTestId('source-control-section-staged')).getByText('shared.docx')).toBeInTheDocument();
    expect(within(screen.getByTestId('source-control-section-unstaged')).getByText('shared.docx')).toBeInTheDocument();
  });

  it('lists untracked files only in the untracked section, with raw status characters', () => {
    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({
            untracked: [
              change('notes.md', {
                state: 'Untracked',
                indexChange: 'Unmodified',
                workingTreeChange: 'Unmodified',
                statusCharacters: '??',
              }),
            ],
          }),
        })}
      />
    );

    const untracked = screen.getByTestId('source-control-section-untracked');
    expect(untracked).toHaveTextContent(/untracked \(1\)/i);
    expect(within(untracked).getByText('notes.md')).toBeInTheDocument();
    expect(within(untracked).getByText('??')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-section-staged')).not.toHaveTextContent('notes.md');
    expect(screen.getByTestId('source-control-section-unstaged')).not.toHaveTextContent('notes.md');
    expect(screen.getByTestId('source-control-section-conflicts')).not.toHaveTextContent('notes.md');
  });

  it('lists conflicted files only in the conflict section, never as normal changes', () => {
    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({
            conflicts: [
              change('planned.md', {
                state: 'Conflicted',
                indexChange: 'Unmerged',
                workingTreeChange: 'Unmerged',
                conflictKind: 'BothModified',
                statusCharacters: 'UU',
              }),
            ],
          }),
        })}
      />
    );

    const conflicts = screen.getByTestId('source-control-section-conflicts');
    expect(conflicts).toHaveTextContent(/conflicts \(1\)/i);
    expect(within(conflicts).getByText('planned.md')).toBeInTheDocument();
    expect(screen.getByTestId('source-control-section-staged')).not.toHaveTextContent('planned.md');
    expect(screen.getByTestId('source-control-section-unstaged')).not.toHaveTextContent('planned.md');
  });

  it('renders paths with spaces and unicode exactly as Git reported them', () => {
    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({
            unstaged: [change('docs/plan d\u2019\u00e9t\u00e9 \u2014 final.md'), change('src/\u00fcber file.docx')],
          }),
        })}
      />
    );

    expect(screen.getByText('docs/plan d\u2019\u00e9t\u00e9 \u2014 final.md')).toBeInTheDocument();
    expect(screen.getByText('src/\u00fcber file.docx')).toBeInTheDocument();
  });

  it('marks the selected file and reports it through onSelectionChange', () => {
    const onSelectionChange = vi.fn();

    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({ unstaged: [change('contract.docx'), change('proposal.docx')] }),
        })}
        onSelectionChange={onSelectionChange}
      />
    );

    const row = screen.getByTestId('source-control-entry-contract.docx');
    expect(row).toHaveAttribute('aria-selected', 'false');

    fireEvent.click(row);

    expect(row).toHaveAttribute('aria-selected', 'true');
    expect(onSelectionChange).toHaveBeenCalledTimes(1);
    expect(onSelectionChange).toHaveBeenCalledWith(expect.objectContaining({ path: 'contract.docx' }));
  });

  it('updates the pane on refresh without losing the selected file', () => {
    const onSelectionChange = vi.fn();
    const initial = statusModel({
      sections: sections({ unstaged: [change('contract.docx')] }),
    });
    const refreshed = statusModel({
      ahead: 3,
      sections: sections({ unstaged: [change('contract.docx'), change('proposal.docx')] }),
    });

    const { rerender } = render(
      <SourceControlPanel status={initial} onSelectionChange={onSelectionChange} />
    );

    fireEvent.click(screen.getByTestId('source-control-entry-contract.docx'));
    expect(screen.getByTestId('source-control-entry-contract.docx')).toHaveAttribute('aria-selected', 'true');

    rerender(<SourceControlPanel status={refreshed} onSelectionChange={onSelectionChange} />);

    expect(screen.getByTestId('source-control-entry-contract.docx')).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByTestId('source-control-entry-proposal.docx')).toHaveAttribute('aria-selected', 'false');
  });

  it('clears the selection when the selected file is no longer changed', () => {
    const onSelectionChange = vi.fn();
    const initial = statusModel({
      sections: sections({ unstaged: [change('contract.docx'), change('proposal.docx')] }),
    });
    const refreshed = statusModel({
      sections: sections({ unstaged: [change('proposal.docx')] }),
    });

    const { rerender } = render(
      <SourceControlPanel status={initial} onSelectionChange={onSelectionChange} />
    );

    fireEvent.click(screen.getByTestId('source-control-entry-contract.docx'));

    rerender(<SourceControlPanel status={refreshed} onSelectionChange={onSelectionChange} />);

    expect(screen.queryByTestId('source-control-entry-contract.docx')).not.toBeInTheDocument();
    expect(screen.getByTestId('source-control-entry-proposal.docx')).toHaveAttribute('aria-selected', 'false');
    expect(onSelectionChange).toHaveBeenLastCalledWith(null);
  });

  it('opens the registered document when a selected change is double-clicked', () => {
    const onOpenDocument = vi.fn();

    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({ unstaged: [change('contract.docx', { documentId: 'doc-42' })] }),
        })}
        onOpenDocument={onOpenDocument}
      />
    );

    fireEvent.doubleClick(screen.getByTestId('source-control-entry-contract.docx'));

    expect(onOpenDocument).toHaveBeenCalledTimes(1);
    expect(onOpenDocument).toHaveBeenCalledWith('doc-42');
  });

  it('does not attempt to open a change that is not a registered document', () => {
    const onOpenDocument = vi.fn();

    render(
      <SourceControlPanel
        status={statusModel({
          sections: sections({ untracked: [change('scratch.md', { documentId: null })] }),
        })}
        onOpenDocument={onOpenDocument}
      />
    );

    fireEvent.doubleClick(screen.getByTestId('source-control-entry-scratch.md'));

    expect(onOpenDocument).not.toHaveBeenCalled();
  });

  it('shows branch and ahead/behind context as read-only text with no branch controls', () => {
    render(
      <SourceControlPanel
        status={statusModel({ branchName: 'main', ahead: 2, behind: 0 })}
      />
    );

    expect(screen.getByTestId('source-control-branch')).toHaveTextContent('main');
    expect(screen.getByTestId('source-control-ahead-behind')).toHaveTextContent('\u2191 2 \u2193 0');

    // Acceptance criteria: no branch-management controls in the pane.
    expect(screen.queryByRole('button', { name: /branch|checkout|switch|merge|rebase/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: /branch/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: /branch|tag|remote/i })).not.toBeInTheDocument();
  });

  it('shows detached HEAD state without pretending to know a branch', () => {
    render(
      <SourceControlPanel
        status={statusModel({ branchName: null, isDetached: true, upstreamReference: null, hasUpstream: false, ahead: null, behind: null })}
      />
    );

    expect(screen.getByTestId('source-control-branch')).toHaveTextContent(/detached/i);
  });

  it('shows an explicit no-upstream placeholder instead of false 0/0 counters', () => {
    render(
      <SourceControlPanel
        status={statusModel({ upstreamReference: null, hasUpstream: false, ahead: null, behind: null })}
      />
    );

    const counter = screen.getByTestId('source-control-ahead-behind');
    expect(counter).toHaveTextContent(/no upstream/i);
    expect(counter).not.toHaveTextContent('0');
  });

  it('renders a no-repository state for folders outside Git', () => {
    render(
      <SourceControlPanel
        status={statusModel({ status: 'NoRepository', hasRepository: false, worktreeRoot: null, branchName: null, upstreamReference: null, hasUpstream: false, ahead: null, behind: null })}
      />
    );

    expect(screen.getByTestId('source-control-no-repository')).toHaveTextContent(/not inside a git repository/i);
  });

  it('renders a Git-unavailable state when Git is not discoverable', () => {
    render(
      <SourceControlPanel
        status={statusModel({ status: 'GitUnavailable', hasRepository: false, worktreeRoot: null, branchName: null, upstreamReference: null, hasUpstream: false, ahead: null, behind: null })}
      />
    );

    expect(screen.getByTestId('source-control-git-unavailable')).toBeInTheDocument();
  });

  it('surfaces typed failures with their classification instead of a silent fallback', () => {
    render(
      <SourceControlPanel
        status={statusModel({
          status: 'Failed',
          hasRepository: false,
          sections: sections(),
          failure: {
            status: 'Failed',
            errorKind: 'GitLockContention',
            errorMessage: 'Another Git process seems to be running in this repository.',
            exitCode: 128,
          },
        })}
      />
    );

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('GitLockContention');
    expect(alert).toHaveTextContent('Another Git process seems to be running in this repository.');
  });

  it('shows a clean state when the working tree has no changes', () => {
    render(<SourceControlPanel status={statusModel()} />);

    expect(screen.getByTestId('source-control-clean')).toHaveTextContent(/working tree clean|no changes/i);
    expect(screen.getByTestId('source-control-branch')).toHaveTextContent('feature/production-v1');
  });

  it('shows a loading state before the first status arrives', () => {
    render(<SourceControlPanel status={null} isLoading />);

    expect(screen.getByTestId('source-control-loading')).toBeInTheDocument();
  });

  it('shows a transport error without hiding the last known status', () => {
    render(
      <SourceControlPanel
        status={statusModel({ sections: sections({ unstaged: [change('contract.docx')] }) })}
        errorMessage="Could not reach the local API."
      />
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Could not reach the local API.');
    expect(screen.getByText('contract.docx')).toBeInTheDocument();
  });
});
