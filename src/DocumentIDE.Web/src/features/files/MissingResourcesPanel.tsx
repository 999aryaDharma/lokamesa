/**
 * UI-05 B — the rendered host for `MissingResourceCard`.
 *
 * The workspace document payload already carries `availabilityStatus` (and the
 * record's last-known path). This panel renders one card per document whose
 * real status is not `available`; it never invents a missing row. The actions
 * are wired by `App` to the real routes:
 *   - Locate → the authenticated `LocateMovedFileDialog`;
 *   - Remove reference → `DELETE /api/documents/{id}/reference`, which removes
 *     only the database reference and never touches the file.
 */
import React from 'react';
import MissingResourceCard from './MissingResourceCard';
import {
  missingStatusFor,
  type MissingResourceDocument,
} from './missingResourceModel';

export type { MissingResourceDocument } from './missingResourceModel';

export interface MissingResourcesPanelProps {
  documents: ReadonlyArray<MissingResourceDocument>;
  onLocate: (documentId: string) => void;
  onRemove: (documentId: string) => void;
}

export const MissingResourcesPanel: React.FC<MissingResourcesPanelProps> = ({
  documents,
  onLocate,
  onRemove,
}) => {
  if (documents.length === 0) return null;

  return (
    <section
      aria-label="Missing resources"
      data-testid="missing-resources"
      className="absolute inset-x-0 top-0 z-30 max-h-[45%] overflow-y-auto border-b border-mac-warning/40 bg-mac-content/95 px-3 py-2 shadow-sm backdrop-blur-sm"
    >
      <header className="mb-2 flex items-center justify-between">
        <h2 className="text-[11px] font-semibold text-mac-text">Missing resources</h2>
        <span className="text-[10px] tabular-nums text-mac-text-3">
          {documents.length} {documents.length === 1 ? 'document' : 'documents'}
        </span>
      </header>
      <div className="flex flex-wrap gap-2">
        {documents.map((document) => (
          <MissingResourceCard
            key={document.id}
            kind="file"
            resourceId={document.id}
            displayName={document.filename}
            lastKnownPath={
              document.lastKnownPath || document.currentPath || document.filename
            }
            status={missingStatusFor(document.availabilityStatus ?? 'unavailable')}
            onLocate={onLocate}
            onRemove={onRemove}
          />
        ))}
      </div>
    </section>
  );
};

export default MissingResourcesPanel;
