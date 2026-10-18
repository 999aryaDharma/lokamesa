/**
 * UI-05 B — pure view-model helpers for the missing-resource surface.
 *
 * Kept separate from the component so fast refresh and lint stay honest about
 * module exports, and so the mapping from the persisted `availabilityStatus`
 * token to the card's typed status is testable without rendering.
 */
import type { MissingResourceStatus } from './MissingResourceCard';

export interface MissingResourceDocument {
  id: string;
  filename: string;
  availabilityStatus?: string;
  currentPath?: string;
  lastKnownPath?: string;
}

/** Map the persisted availability token onto the card's typed status. */
export function missingStatusFor(availabilityStatus: string): MissingResourceStatus {
  switch (availabilityStatus) {
    case 'missing':
      return 'missing';
    case 'permission_denied':
      return 'permission_denied';
    case 'locked':
      return 'locked';
    default:
      return 'unavailable';
  }
}

/** Only documents the workspace actually reports as unavailable. */
export function missingDocumentsFrom(
  documents: ReadonlyArray<MissingResourceDocument>,
): MissingResourceDocument[] {
  return documents.filter(
    (document) =>
      Boolean(document.availabilityStatus) && document.availabilityStatus !== 'available',
  );
}
