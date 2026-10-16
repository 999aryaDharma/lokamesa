/**
 * SCM-03 — read-only mirror of the privileged Source Control API contract
 * (`SourceControlController` records in DocumentIDE.Api; spec 13 §7/§18).
 *
 * These types carry Git state only: nothing in the pane mutates the
 * repository, and the branch fields are presentation context. String unions
 * match the enum names the API serializes via `ToString()`.
 */

export type SourceControlStatus = 'Available' | 'NoRepository' | 'GitUnavailable' | 'Failed';

export type GitFileState =
  | 'Unmodified'
  | 'Modified'
  | 'Added'
  | 'Deleted'
  | 'Renamed'
  | 'Copied'
  | 'TypeChanged'
  | 'Untracked'
  | 'Ignored'
  | 'Conflicted';

export type GitChangeKind =
  | 'Unmodified'
  | 'Modified'
  | 'Added'
  | 'Deleted'
  | 'Renamed'
  | 'Copied'
  | 'TypeChanged'
  | 'Unmerged';

export type GitConflictKind =
  | 'None'
  | 'BothModified'
  | 'BothAdded'
  | 'BothDeleted'
  | 'AddedByUs'
  | 'AddedByThem'
  | 'DeletedByUs'
  | 'DeletedByThem';

/** Typed Git failure surface (spec 13 §20); never raw secrets. */
export interface SourceControlFailure {
  status: string;
  errorKind: string;
  errorMessage: string | null;
  exitCode: number | null;
}

export interface SourceControlChangeEntry {
  /** Worktree-relative path exactly as Git reported it (forward slashes). */
  path: string;
  /** Previous path for rename/copy entries; otherwise null. */
  originalPath: string | null;
  state: GitFileState;
  indexChange: GitChangeKind;
  workingTreeChange: GitChangeKind;
  conflictKind: GitConflictKind;
  /** Raw porcelain status characters, preserved for inspectability. */
  statusCharacters: string;
  /** Contained absolute local path, or null when it cannot be safely resolved. */
  absolutePath: string | null;
  /** Id of an already-registered document, or null for unregistered paths. */
  documentId: string | null;
}

/** Changes grouped by Git state (spec 13 §8); a file with both an index and a
 * working-tree change appears in both staged and unstaged groups. */
export interface SourceControlSections {
  staged: SourceControlChangeEntry[];
  unstaged: SourceControlChangeEntry[];
  untracked: SourceControlChangeEntry[];
  conflicts: SourceControlChangeEntry[];
}

/** Changes-pane payload (spec 13 §7): grouped sections plus read-only branch
 * context and ahead/behind counters. `hasUpstream=false` means the counters
 * are unknown, not zero (spec 13 §14.2). */
export interface SourceControlStatusModel {
  status: SourceControlStatus;
  hasRepository: boolean;
  worktreeRoot: string | null;
  branchName: string | null;
  isDetached: boolean;
  headCommit: string | null;
  upstreamReference: string | null;
  hasUpstream: boolean;
  ahead: number | null;
  behind: number | null;
  sections: SourceControlSections;
  failure: SourceControlFailure | null;
}
