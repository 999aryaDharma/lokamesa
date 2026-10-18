import React from 'react';

export interface RelationInspectorItemDto {
  relationId: string;
  sourceDocumentId: string;
  sourceDocumentName?: string | null;
  sourceDocumentPath?: string | null;
  targetDocumentId: string;
  targetDocumentName?: string | null;
  targetDocumentPath?: string | null;
  relationType: string;
  relationOrigin: string;
  isResolved: boolean;
  isAmbiguous?: boolean;
  confidence: number;
  metadataJson?: string | null;
  rawTarget?: string | null;
}

export interface DocumentInspectorRelationsDto {
  documentId: string;
  references: RelationInspectorItemDto[];
  backlinks: RelationInspectorItemDto[];
  inferredRelations: RelationInspectorItemDto[];
  unresolvedLinks: RelationInspectorItemDto[];
}

export interface RelationsInspectorProps {
  relations: DocumentInspectorRelationsDto | null;
  loading?: boolean;
  onNavigateDocumentId?: (documentId: string) => void;
}

export const RelationsInspector: React.FC<RelationsInspectorProps> = ({
  relations,
  loading = false,
  onNavigateDocumentId,
}) => {
  if (loading) {
    return <div className="relations-inspector-loading">Loading relations...</div>;
  }

  if (!relations) {
    return <div className="relations-inspector-empty">No document selected.</div>;
  }

  const handleNavigate = (docId: string) => {
    if (docId && onNavigateDocumentId) {
      onNavigateDocumentId(docId);
    }
  };

  return (
    <aside className="relations-inspector border-l p-4 space-y-6 text-sm">
      <h2 className="text-base font-semibold border-b pb-2">Document Relations</h2>

      {/* 1. References (Outbound explicit links) */}
      <section className="references-section">
        <h3 className="font-medium text-mac-text mb-2">
          References ({relations.references.length})
        </h3>
        {relations.references.length === 0 ? (
          <p className="text-xs text-mac-text-3 italic">No outbound references</p>
        ) : (
          <ul className="space-y-1">
            {relations.references.map((item) => (
              <li
                key={item.relationId}
                className="flex items-center justify-between p-1.5 rounded hover:bg-mac-chip cursor-pointer"
                onClick={() => handleNavigate(item.targetDocumentId)}
              >
                <span className="truncate font-medium">
                  {item.targetDocumentName || item.rawTarget || item.targetDocumentId}
                </span>
                <span className="text-xs px-1.5 py-0.5 rounded bg-gray-200 text-mac-text-2">
                  {item.relationType}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* 2. Referenced By (Inbound explicit links / Backlinks) */}
      <section className="backlinks-section">
        <h3 className="font-medium text-mac-text mb-2">
          Referenced By ({relations.backlinks.length})
        </h3>
        {relations.backlinks.length === 0 ? (
          <p className="text-xs text-mac-text-3 italic">No inbound backlinks</p>
        ) : (
          <ul className="space-y-1">
            {relations.backlinks.map((item) => (
              <li
                key={item.relationId}
                className="flex items-center justify-between p-1.5 rounded hover:bg-mac-chip cursor-pointer"
                onClick={() => handleNavigate(item.sourceDocumentId)}
              >
                <span className="truncate font-medium">
                  {item.sourceDocumentName || item.sourceDocumentId}
                </span>
                <span className="text-xs px-1.5 py-0.5 rounded bg-mac-accent-weak text-mac-accent">
                  {item.relationType}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* 3. Unresolved / Ambiguous Links */}
      {relations.unresolvedLinks.length > 0 && (
        <section className="unresolved-section border-t pt-4">
          <h3 className="font-medium text-mac-warning mb-2 flex items-center gap-1">
            <span>⚠ Unresolved / Ambiguous Links</span> ({relations.unresolvedLinks.length})
          </h3>
          <ul className="space-y-1">
            {relations.unresolvedLinks.map((item) => (
              <li
                key={item.relationId}
                className="p-1.5 rounded bg-mac-warning/10 text-amber-900 border border-mac-warning/40"
              >
                <div className="font-medium truncate">
                  {item.targetDocumentName || item.rawTarget || item.targetDocumentId}
                </div>
                <div className="text-xs text-mac-warning flex justify-between mt-0.5">
                  <span>{item.isAmbiguous ? 'Ambiguous match' : 'Missing target'}</span>
                  <span>{item.relationType}</span>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* 4. Separate section reserved for Inferred Relations (Semantic Suggestions) */}
      <section className="inferred-section border-t pt-4">
        <h3 className="font-medium text-purple-700 mb-2">
          Potentially Related ({relations.inferredRelations.length})
        </h3>
        {relations.inferredRelations.length === 0 ? (
          <p className="text-xs text-mac-text-3 italic">No semantic suggestions</p>
        ) : (
          <ul className="space-y-1">
            {relations.inferredRelations.map((item) => {
              const relatedId =
                item.sourceDocumentId === relations.documentId
                  ? item.targetDocumentId
                  : item.sourceDocumentId;
              const relatedName =
                item.sourceDocumentId === relations.documentId
                  ? item.targetDocumentName || item.targetDocumentId
                  : item.sourceDocumentName || item.sourceDocumentId;
              const pct = Math.round(item.confidence * 100);

              return (
                <li
                  key={item.relationId}
                  className="flex items-center justify-between p-1.5 rounded hover:bg-purple-50 cursor-pointer border border-dashed border-purple-200"
                  onClick={() => handleNavigate(relatedId)}
                >
                  <span className="truncate font-medium text-purple-900">{relatedName}</span>
                  <span className="text-xs font-semibold px-1.5 py-0.5 rounded bg-purple-100 text-purple-800">
                    {pct}%
                  </span>
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </aside>
  );
};
