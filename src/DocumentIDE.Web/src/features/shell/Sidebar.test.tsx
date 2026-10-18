import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import Sidebar from './Sidebar';

function renderSidebar(overrides: Partial<React.ComponentProps<typeof Sidebar>> = {}) {
  const props: React.ComponentProps<typeof Sidebar> = {
    activeView: 'canvas',
    onViewChange: () => {},
    folderSources: [
      { id: 'folder-1', title: 'Docs', documentCount: 4 },
      { id: 'folder-2', title: 'Archive', documentCount: 0 },
    ],
    onAddFolder: () => {},
    addFolderOpen: false,
    onToggleSearch: () => {},
    searchOpen: false,
    onToggleShares: () => {},
    sharesOpen: false,
    activeSurface: 'workspace',
    onNavigate: () => {},
    ...overrides,
  };
  return render(<Sidebar {...props} />);
}

describe('Sidebar', () => {
  it('renders the three source-list sections', () => {
    renderSidebar();
    expect(screen.getByRole('heading', { name: 'WORKSPACE' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'FOLDER SOURCES' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'LIBRARY' })).toBeInTheDocument();
  });

  it('drives the view switch from the WORKSPACE rows', () => {
    const onViewChange = vi.fn();
    renderSidebar({ activeView: 'list', onViewChange });

    expect(screen.getByRole('button', { name: 'List view' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    fireEvent.click(screen.getByRole('button', { name: 'Graph view' }));
    expect(onViewChange).toHaveBeenCalledWith('graph');
  });

  it('renders the folder sources it is given, using the same data the shell loads', () => {
    renderSidebar();
    expect(screen.getByText('Docs')).toBeInTheDocument();
    expect(screen.getByText('Archive')).toBeInTheDocument();
  });

  it('keeps Add folder reachable from the FOLDER SOURCES section', () => {
    const onAddFolder = vi.fn();
    renderSidebar({ onAddFolder });
    fireEvent.click(screen.getByRole('button', { name: 'Add folder' }));
    expect(onAddFolder).toHaveBeenCalledTimes(1);
  });

  it('keeps the Search and Shares surfaces reachable from the LIBRARY section', () => {
    const onToggleSearch = vi.fn();
    const onToggleShares = vi.fn();
    renderSidebar({ onToggleSearch, onToggleShares });

    fireEvent.click(screen.getByRole('button', { name: 'Search' }));
    fireEvent.click(screen.getByRole('button', { name: 'Shares' }));

    expect(onToggleSearch).toHaveBeenCalledTimes(1);
    expect(onToggleShares).toHaveBeenCalledTimes(1);
  });

  it('navigates the SCM surfaces instead of rendering inert labels', () => {
    const onNavigate = vi.fn();
    renderSidebar({ activeSurface: 'workspace', onNavigate });

    // Every SCM row is a real button: no inert label is allowed to remain.
    fireEvent.click(screen.getByRole('button', { name: 'Source control' }));
    fireEvent.click(screen.getByRole('button', { name: 'Worktrees' }));
    fireEvent.click(screen.getByRole('button', { name: 'History' }));

    expect(onNavigate).toHaveBeenNthCalledWith(1, 'source-control');
    expect(onNavigate).toHaveBeenNthCalledWith(2, 'worktrees');
    expect(onNavigate).toHaveBeenNthCalledWith(3, 'git-history');
  });

  it('marks only the active SCM row as pressed', () => {
    renderSidebar({ activeSurface: 'worktrees' });

    expect(screen.getByRole('button', { name: 'Worktrees' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Source control' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });
});
