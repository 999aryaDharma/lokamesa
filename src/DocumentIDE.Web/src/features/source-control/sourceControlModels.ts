/**
 * SCM-03/SCM-06/SCM-07 — mirror of the privileged Source Control API contract
 * (`SourceControlController` records in DocumentIDE.Api; spec 13 §7/§10/§11/§18).
 *
 * SCM-03 types carry Git state only; nothing here mutates the repository.
 * SCM-06 adds the typed stage/unstage result and SCM-07 the typed
 * Commit/Commit All result: a mutation response carries the status the API
 * re-read from Git, and Document IDE keeps no proprietary staging area of its
 * own. String unions match the enum names the API serializes via `ToString()`.
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

/** SCM-07 — which commit flow ran (spec 13 §11). Commit and Commit All are
 * deliberately separate flows: the plain action commits the current staged
 * state, the convenience action stages eligible changes first. */
export type GitCommitFlow = 'Commit' | 'CommitAll';

/** SCM-07 — typed outcome of one commit request (spec 13 §11/§20). */
export type GitCommitOutcome =
  | 'Succeeded'
  | 'MessageRequired'
  | 'MessageInvalid'
  | 'NothingStaged'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'MissingIdentity'
  | 'HookRejected'
  | 'IndexLocked'
  | 'Failed';

/** SCM-07 — summary metadata of the commit that was created (spec 13 §11.1). */
export interface SourceControlCommitInfo {
  /** Full commit object id. */
  commitId: string;
  /** Git's abbreviated commit id. */
  shortId: string;
  /** Commit subject as Git recorded it. */
  summary: string;
  /** Branch the commit landed on; null on a detached HEAD. */
  branchName: string | null;
}

/**
 * SCM-07 — typed Commit / Commit All result. On success `commit` carries the new
 * commit identity and `status` the status re-read from Git; for Commit All
 * `stagedPaths` lists the paths that flow staged. The commit path never pushes
 * anywhere, so no remote state appears here (spec 13 §11/§14.4).
 */
export interface SourceControlCommitResult {
  outcome: GitCommitOutcome;
  flow: GitCommitFlow;
  isSuccess: boolean;
  /** Machine-readable rejection code for message rejections. */
  errorCode: string | null;
  message: string | null;
  commit: SourceControlCommitInfo | null;
  /** Paths the Commit All flow staged; null for the plain Commit flow. */
  stagedPaths: string[] | null;
  status: SourceControlStatusModel | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-09 — typed outcome of one work-session/correlation request
 * (spec 13 §13/§20/§24). `SessionRequired`/`DocumentRequired`/`CommitRequired`
 * are input rejections where nothing ran; `SessionNotFound` and
 * `StoreUnavailable` are execution outcomes the pane renders instead of
 * treating a correlation problem as a broken commit.
 */
export type WorkSessionOutcome =
  | 'Succeeded'
  | 'SessionRequired'
  | 'DocumentRequired'
  | 'CommitRequired'
  | 'SessionNotFound'
  | 'StoreUnavailable';

/** SCM-09 — one document changed by a work session (Document IDE History). */
export interface WorkSessionDocumentModel {
  documentId: string;
  path: string | null;
}

/**
 * SCM-09 — one Git commit correlated with a work session. Derived correlation
 * metadata only: the id is exactly what Git reported at commit time and is
 * never required for the commit to remain valid (spec 13 §24).
 */
export interface WorkSessionCommitModel {
  commitId: string;
  shortId: string | null;
  summary: string | null;
}

/**
 * SCM-09 — the Document IDE History side of the correlation payload: the
 * session plus the documents it changed. It is deliberately separate from any
 * Git history so the pane renders a correlation, not a unified timeline.
 */
export interface WorkSessionModel {
  id: string;
  workspaceId: string | null;
  /** Repository root recorded read-only at session start; null when Git is absent. */
  repositoryRoot: string | null;
  /** Worktree root recorded read-only at session start; null when Git is absent. */
  worktreeRoot: string | null;
  startedAt: string;
  endedAt: string | null;
  label: string | null;
  changedDocuments: WorkSessionDocumentModel[];
  commitCorrelations: WorkSessionCommitModel[];
}

/** SCM-09 — typed work-session/correlation response (spec 13 §20/§24). */
export interface SourceControlCorrelationResult {
  outcome: WorkSessionOutcome;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  session: WorkSessionModel | null;
}
