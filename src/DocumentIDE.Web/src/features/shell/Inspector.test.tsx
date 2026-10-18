import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { CanvasObject } from '../canvas/types';
import Inspector from './Inspector';

const folder: CanvasObject = {
  id: 'folder-1',
  type: 'folder',
  title: 'Specs',
  x: 0,
  y: 0,
  width: 140,
  height: 122,
};

const docx: CanvasObject = {
  id: 'docx-1',
  type: 'document',
  title: 'Q3 Report.docx',
  docType: 'docx',
  x: 0,
  y: 0,
  width: 140,
  height: 122,
  statusText: 'Ready',
};

const missing: CanvasObject = {
  id: 'docx-2',
  type: 'document',
  title: 'Old Deck.docx',
  docType: 'docx',
  x: 0,
  y: 0,
  width: 140,
  height: 122,
  isMissing: true,
};

describe('Inspector', () => {
  it('shows the selection inspector for the active canvas selection', () => {
    render(
      <Inspector activeView="canvas" objects={[folder, docx]} selection={[docx]} />,
    );
    expect(screen.getByRole('region', { name: 'Selection' })).toBeInTheDocument();
    expect(screen.getByText('Q3 Report.docx')).toBeInTheDocument();
    expect(screen.getByText('DOCX')).toBeInTheDocument();
    expect(screen.getByText('Ready')).toBeInTheDocument();
  });

  it('summarises the workspace when nothing is selected', () => {
    render(<Inspector activeView="canvas" objects={[folder, docx, missing]} selection={[]} />);
    expect(screen.getByRole('region', { name: 'Workspace state' })).toBeInTheDocument();
    // 1 folder · 1 ready document · 1 missing document
    expect(screen.getByText('Folders')).toBeInTheDocument();
    expect(screen.getByText('Ready')).toBeInTheDocument();
    expect(screen.getByText('Missing')).toBeInTheDocument();
  });

  it('shows the scope + edge legend inspectors for the graph view', () => {
    render(
      <Inspector
        activeView="graph"
        objects={[docx]}
        selection={[]}
        graphNodeCount={6}
        graphEdgeCount={6}
      />,
    );
    expect(screen.getByRole('region', { name: 'Scope' })).toBeInTheDocument();
    expect(screen.getByText('Nodes')).toBeInTheDocument();
    expect(screen.getByText('Edges')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Edge legend' })).toBeInTheDocument();
    expect(screen.getByText('Explicit relation')).toBeInTheDocument();
    expect(screen.getByText('Inferred (semantic)')).toBeInTheDocument();
  });

  it('uses the selection inspector in the list view too', () => {
    render(<Inspector activeView="list" objects={[folder, docx]} selection={[docx]} />);
    expect(screen.getByRole('region', { name: 'Selection' })).toBeInTheDocument();
  });

  it('renders the dock with a title header and the item count, without inventing data', () => {
    render(<Inspector activeView="canvas" objects={[folder, docx, missing]} selection={[]} />);
    expect(screen.getByRole('complementary', { name: 'Inspector' })).toBeInTheDocument();
    expect(screen.getByText('3 items')).toBeInTheDocument();
  });
});
