import { useState } from 'react';
import { SharePermissions } from './SharePermissions';
import type { ShareModeChoice } from './ShareManager';

export interface ShareDialogDocument {
  id: string;
  displayName: string;
}

export interface ShareCreatePayload {
  mode: ShareModeChoice;
  memberDocumentIds: string[];
  allowOriginalDownload: boolean;
  allowPdfDownload: boolean;
}

export interface ShareDialogProps {
  availableDocuments: ShareDialogDocument[];
  initialSelectedIds?: string[];
  onCreate?: (payload: ShareCreatePayload) => void;
  onCancel?: () => void;
}

/**
 * SHR-11 — owner share creation dialog.
 *
 * The member set is frozen from the owner's explicit checkbox selection
 * (spec §5/§29: explicit membership; SHR-05 live content never auto-adds).
 * Nothing is published until the owner clicks Create share. Defaults to
 * Snapshot with both downloads OFF (SHR-07).
 */
export function ShareDialog({
  availableDocuments,
  initialSelectedIds,
  onCreate,
  onCancel,
}: ShareDialogProps) {
  const [selectedIds, setSelectedIds] = useState<string[]>(() =>
    (initialSelectedIds ?? []).filter((id) =>
      availableDocuments.some((doc) => doc.id === id),
    ),
  );
  const [mode, setMode] = useState<ShareModeChoice>('Snapshot');
  const [permissions, setPermissions] = useState({
    allowOriginalDownload: false,
    allowPdfDownload: false,
  });

  const toggleDocument = (id: string) => {
    setSelectedIds((current) =>
      current.includes(id)
        ? current.filter((selected) => selected !== id)
        : [...current, id],
    );
  };

  return (
    <section aria-label="Create share" role="dialog">
      <fieldset>
        <legend>Members</legend>
        {availableDocuments.map((doc) => (
          <label key={doc.id}>
            <input
              checked={selectedIds.includes(doc.id)}
              onChange={() => toggleDocument(doc.id)}
              type="checkbox"
            />
            {doc.displayName}
          </label>
        ))}
      </fieldset>
      <fieldset>
        <legend>Mode</legend>
        <label>
          <input
            checked={mode === 'Snapshot'}
            name="share-mode"
            onChange={() => setMode('Snapshot')}
            type="radio"
          />
          Snapshot
        </label>
        <label>
          <input
            checked={mode === 'Live'}
            name="share-mode"
            onChange={() => setMode('Live')}
            type="radio"
          />
          Live
        </label>
      </fieldset>
      <SharePermissions permissions={permissions} onChange={setPermissions} />
      <button
        onClick={() =>
          onCreate?.({
            mode,
            memberDocumentIds: [...selectedIds],
            allowOriginalDownload: permissions.allowOriginalDownload,
            allowPdfDownload: permissions.allowPdfDownload,
          })
        }
        type="button"
      >
        Create share
      </button>
      <button onClick={() => onCancel?.()} type="button">
        Cancel
      </button>
    </section>
  );
}
