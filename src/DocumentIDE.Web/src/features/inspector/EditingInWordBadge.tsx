/**
 * WORD-02 — Word Editing/Lock Detection: "Editing in Word" badge.
 *
 * Presents the detector's editing verdict in the inspector. The verdict is
 * heuristic (owner-lock file, exclusive-access probe, automation document
 * list) so the badge labels confidence and never claims certainty from a
 * single signal.
 */
import React from 'react';

export type WordEditingConfidence = 'none' | 'low' | 'high';

export interface EditingInWordBadgeProps {
  isEditing: boolean;
  confidence?: WordEditingConfidence;
  /** Human-readable detector summary, shown as a tooltip. */
  summary?: string | null;
  onOpenInWord?: () => void;
}

export const EditingInWordBadge: React.FC<EditingInWordBadgeProps> = ({
  isEditing,
  confidence = 'low',
  summary = null,
  onOpenInWord,
}) => {
  if (!isEditing) {
    return null;
  }

  const confidenceLabel = confidence === 'high' ? 'likely' : 'possibly';

  return (
    <p
      role="status"
      data-testid="editing-in-word-badge"
      title={summary ?? undefined}
      className="editing-in-word-badge rounded-md bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 px-2 py-1 text-xs text-amber-800 dark:text-amber-300"
    >
      <span className="font-semibold">Editing in Word</span>
      <span data-testid="editing-in-word-confidence"> ({confidenceLabel})</span>
      {onOpenInWord && (
        <button
          type="button"
          data-testid="editing-in-word-open"
          onClick={onOpenInWord}
          className="ml-2 underline cursor-pointer"
        >
          Open Word
        </button>
      )}
    </p>
  );
};

export default EditingInWordBadge;
