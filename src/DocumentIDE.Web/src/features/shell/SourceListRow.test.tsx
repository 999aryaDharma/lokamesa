import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import SourceListRow from './SourceListRow';

describe('SourceListRow', () => {
  it('renders an interactive row as a button named after its label', () => {
    render(<SourceListRow label="Search" icon="⌕" onClick={() => {}} selected={false} />);
    expect(screen.getByRole('button', { name: 'Search' })).toBeInTheDocument();
  });

  it('exposes the selected state with aria-pressed', () => {
    const { rerender } = render(
      <SourceListRow label="Shares" icon="⌘" onClick={() => {}} selected={false} />,
    );
    expect(screen.getByRole('button', { name: 'Shares' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );

    rerender(<SourceListRow label="Shares" icon="⌘" onClick={() => {}} selected />);
    expect(screen.getByRole('button', { name: 'Shares' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('renders the trailing count badge without polluting the accessible name', () => {
    render(<SourceListRow label="Source control" icon="⌥" badge={3} onClick={() => {}} />);
    expect(screen.getByRole('button', { name: 'Source control' })).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('supports non-interactive presentational rows', () => {
    render(<SourceListRow label="Documents" icon="▤" onClick={undefined} />);
    expect(screen.queryByRole('button', { name: 'Documents' })).not.toBeInTheDocument();
    expect(screen.getByText('Documents')).toBeInTheDocument();
  });

  it('fires onClick for an interactive row', () => {
    const onClick = vi.fn();
    render(<SourceListRow label="History" icon="↺" onClick={onClick} />);
    fireEvent.click(screen.getByRole('button', { name: 'History' }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
