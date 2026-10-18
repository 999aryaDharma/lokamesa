import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import InspectorCard, { InspectorField, InspectorKeyValue } from './InspectorCard';

describe('InspectorCard', () => {
  it('exposes its title as the card accessible name', () => {
    render(
      <InspectorCard title="References (3)">
        <InspectorKeyValue label="Type" value="DOCX" />
      </InspectorCard>,
    );
    expect(screen.getByRole('region', { name: 'References (3)' })).toBeInTheDocument();
    expect(screen.getByText('Type')).toBeInTheDocument();
    expect(screen.getByText('DOCX')).toBeInTheDocument();
  });

  it('renders an untitled card as an unnamed region holding its children', () => {
    render(
      <InspectorCard>
        <p>Notes</p>
      </InspectorCard>,
    );
    expect(screen.getByText('Notes')).toBeInTheDocument();
  });

  it('renders a key/value field without inventing an interactive control', () => {
    render(<InspectorField label="Status" value="Active" tone="success" />);
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
  });
});
