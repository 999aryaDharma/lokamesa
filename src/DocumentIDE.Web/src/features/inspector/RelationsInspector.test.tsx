import { render, screen, fireEvent } from '@testing-library/react';
import { describe, test, expect, vi } from 'vitest';
import { RelationsInspector, type DocumentInspectorRelationsDto } from './RelationsInspector';

describe('RelationsInspector Component', () => {
  const sampleRelations: DocumentInspectorRelationsDto = {
    documentId: 'doc-target',
    references: [
      {
        relationId: 'rel-1',
        sourceDocumentId: 'doc-target',
        targetDocumentId: 'doc-outbound',
        targetDocumentName: 'OutboundDoc.md',
        relationType: 'MarkdownLink',
        relationOrigin: 'Explicit',
        isResolved: true,
        confidence: 1.0,
      },
    ],
    backlinks: [
      {
        relationId: 'rel-2',
        sourceDocumentId: 'doc-inbound',
        sourceDocumentName: 'InboundDoc.md',
        targetDocumentId: 'doc-target',
        relationType: 'Wikilink',
        relationOrigin: 'Explicit',
        isResolved: true,
        confidence: 1.0,
      },
    ],
    inferredRelations: [
      {
        relationId: 'rel-3',
        sourceDocumentId: 'doc-target',
        targetDocumentId: 'doc-semantic',
        targetDocumentName: 'SemanticDoc.md',
        relationType: 'SemanticSuggestion',
        relationOrigin: 'Inferred',
        isResolved: true,
        confidence: 0.85,
      },
    ],
    unresolvedLinks: [
      {
        relationId: 'rel-4',
        sourceDocumentId: 'doc-target',
        targetDocumentId: 'missing-doc',
        rawTarget: 'missing-file.md',
        relationType: 'Wikilink',
        relationOrigin: 'Explicit',
        isResolved: false,
        confidence: 1.0,
      },
    ],
  };

  test('renders references, backlinks, unresolved, and inferred sections separately', () => {
    render(<RelationsInspector relations={sampleRelations} />);

    expect(screen.getByText('References (1)')).toBeInTheDocument();
    expect(screen.getByText('OutboundDoc.md')).toBeInTheDocument();

    expect(screen.getByText('Referenced By (1)')).toBeInTheDocument();
    expect(screen.getByText('InboundDoc.md')).toBeInTheDocument();

    expect(screen.getByText('⚠ Unresolved / Ambiguous Links')).toBeInTheDocument();
    expect(screen.getByText('missing-file.md')).toBeInTheDocument();

    expect(screen.getByText('Potentially Related (1)')).toBeInTheDocument();
    expect(screen.getByText('SemanticDoc.md')).toBeInTheDocument();
    expect(screen.getByText('85%')).toBeInTheDocument();
  });

  test('navigates by DocumentId on click', () => {
    const handleNavigate = vi.fn();
    render(<RelationsInspector relations={sampleRelations} onNavigateDocumentId={handleNavigate} />);

    const backlinkItem = screen.getByText('InboundDoc.md');
    fireEvent.click(backlinkItem);
    expect(handleNavigate).toHaveBeenCalledWith('doc-inbound');

    const inferredItem = screen.getByText('SemanticDoc.md');
    fireEvent.click(inferredItem);
    expect(handleNavigate).toHaveBeenCalledWith('doc-semantic');
  });

  test('handles empty or loading state safely without crashing', () => {
    const { rerender } = render(<RelationsInspector relations={null} loading={true} />);
    expect(screen.getByText('Loading relations...')).toBeInTheDocument();

    rerender(<RelationsInspector relations={null} loading={false} />);
    expect(screen.getByText('No document selected.')).toBeInTheDocument();
  });
});
