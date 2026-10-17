/**
 * SCM-03/SCM-06 — mirror of the privileged Source Control API contract
 * (`SourceControlController` records in DocumentIDE.Api; spec 13 §7/§10/§18).
 *
 * SCM-03 types carry Git state only; nothing here mutates the repository.
 * SCM-06 adds the typed stage/unstage result: the mutation response carries
 * the status the API re-read from Git, and Document IDE keeps no proprietary
 * staging area of its own. String unions match the enum names the API
 * serializes via `ToString()`.
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

/** SCM-06 — typed outcome of one stage/unstage request (spec 13 §10/§20). */
export type GitStageOutcome =
  | 'Succeeded'
  | 'RejectedPath'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'Failed';

/**
 * SCM-06 — typed stage/unstage result. On success `status` carries the status
 * re-read from Git after the mutation (the real index is the only staging
 * authority); failures keep their typed classification in `failure`.
 */
export interface SourceControlStageResult {
  outcome: GitStageOutcome;
  isSuccess: boolean;
  /** Machine-readable rejection code, or null outside `RejectedPath` outcomes. */
  errorCode: string | null;
  message: string | null;
  status: SourceControlStatusModel | null;
  failure: SourceControlFailure | null;
}
