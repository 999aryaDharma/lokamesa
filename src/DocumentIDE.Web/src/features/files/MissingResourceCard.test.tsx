import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import MissingResourceCard from './MissingResourceCard';

describe('MissingResourceCard', () => {
  const fileProps = {
    kind: 'file' as const,
    resourceId: 'doc-1',
    displayName: 'PRD.md',
    lastKnownPath: 'C:\\Docs\\PRD.md',
    status: 'missing' as const,
    onLocate: vi.fn(),
    onRemove: vi.fn(),
  };

  it('renders missing file placeholder with locate and remove reference actions', () => {
    render(<MissingResourceCard {...fileProps} />);
    expect(screen.getByText('PRD.md')).toBeInTheDocument();
    expect(screen.getByText(/file missing/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^locate$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /remove reference/i })).toBeInTheDocument();
  });

  it('renders missing folder with locate folder and remove source actions', () => {
    render(
      <MissingResourceCard
        {...fileProps}
        kind="folder"
        displayName="BA Agustus"
        status="unavailable"
      />,
    );
    expect(screen.getByText('BA Agustus')).toBeInTheDocument();
    expect(screen.getByText(/source unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /locate folder/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /remove source/i })).toBeInTheDocument();
  });

  it('labels permission-denied distinctly from missing', () => {
    render(<MissingResourceCard {...fileProps} status="permission_denied" />);
    expect(screen.getByText(/access denied/i)).toBeInTheDocument();
    expect(screen.queryByText(/file missing/i)).not.toBeInTheDocument();
  });

  it('invokes locate and remove callbacks with the resource id', () => {
    const onLocate = vi.fn();
    const onRemove = vi.fn();
    render(<MissingResourceCard {...fileProps} onLocate={onLocate} onRemove={onRemove} />);
    fireEvent.click(screen.getByRole('button', { name: /^locate$/i }));
    fireEvent.click(screen.getByRole('button', { name: /remove reference/i }));
    expect(onLocate).toHaveBeenCalledWith('doc-1');
    expect(onRemove).toHaveBeenCalledWith('doc-1');
  });
});
