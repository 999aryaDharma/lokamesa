import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import MissingResourcesPanel from './MissingResourcesPanel';
import { missingDocumentsFrom, missingStatusFor } from './missingResourceModel';

describe('MissingResourcesPanel', () => {
  const missing = {
    id: 'doc-missing',
    filename: 'Report.docx',
    availabilityStatus: 'missing',
    currentPath: 'C:\\Docs\\Report.docx',
    lastKnownPath: 'C:\\Old\\Report.docx',
  };

  it('renders only documents whose real status is not available', () => {
    const documents = [
      { id: 'ok', filename: 'ok.md', availabilityStatus: 'available' },
      missing,
      { id: 'denied', filename: 'denied.md', availabilityStatus: 'permission_denied' },
    ];

    expect(missingDocumentsFrom(documents).map((document) => document.id)).toEqual([
      'doc-missing',
      'denied',
    ]);

    render(
      <MissingResourcesPanel
        documents={missingDocumentsFrom(documents)}
        onLocate={vi.fn()}
        onRemove={vi.fn()}
      />,
    );

    expect(screen.getByTestId('missing-resources')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Missing file: Report.docx' })).toHaveTextContent(
      'File missing',
    );
    expect(screen.getByRole('group', { name: 'Missing file: denied.md' })).toHaveTextContent(
      'Access denied',
    );
    expect(screen.queryByText('ok.md')).not.toBeInTheDocument();
  });

  it('uses the record last-known path, then the current path', () => {
    render(
      <MissingResourcesPanel
        documents={[
          missing,
          {
            id: 'no-last-known',
            filename: 'notes.md',
            availabilityStatus: 'locked',
            currentPath: 'C:\\Docs\\notes.md',
          },
        ]}
        onLocate={vi.fn()}
        onRemove={vi.fn()}
      />,
    );

    expect(screen.getByText('C:\\Old\\Report.docx')).toBeInTheDocument();
    expect(screen.getByText('C:\\Docs\\notes.md')).toBeInTheDocument();
  });

  it('routes locate and remove through their callbacks with the resource id', () => {
    const onLocate = vi.fn();
    const onRemove = vi.fn();
    render(
      <MissingResourcesPanel documents={[missing]} onLocate={onLocate} onRemove={onRemove} />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Locate' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove reference' }));

    expect(onLocate).toHaveBeenCalledWith('doc-missing');
    expect(onRemove).toHaveBeenCalledWith('doc-missing');
  });

  it('maps every persisted availability token onto a truthful card status', () => {
    expect(missingStatusFor('missing')).toBe('missing');
    expect(missingStatusFor('permission_denied')).toBe('permission_denied');
    expect(missingStatusFor('locked')).toBe('locked');
    expect(missingStatusFor('source_unavailable')).toBe('unavailable');
  });
});
