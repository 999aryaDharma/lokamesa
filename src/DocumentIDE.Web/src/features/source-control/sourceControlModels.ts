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
  | 'ConflictedWorktree'
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
  /** Unmerged paths that blocked a ConflictedWorktree outcome; null otherwise. */
  conflictedPaths?: string[] | null;
  status: SourceControlStatusModel | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-09 - typed outcome of one work-session/correlation request
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

/** SCM-11 — typed status of a worktree's remote/tracking state (spec 13 §14.2).
 * `NoUpstream` is a first-class, non-error state: the counters are unknown, not
 * zero. Counters can also be unknown while `hasUpstream` is true (see below). */
export type GitRemoteStatus =
  | 'Available'
  | 'NoUpstream'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'Failed';

/** SCM-11 — typed outcome of one explicit fetch (spec 13 §14.1/§20). */
export type GitFetchOutcome =
  | 'Succeeded'
  | 'NoUpstream'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'AuthenticationRequired'
  | 'HostUnreachable'
  | 'Rejected'
  | 'PermissionDenied'
  | 'Failed';

/**
 * SCM-11 — remote/tracking state of one worktree (spec 13 §14.1/§14.2).
 *
 * `hasUpstream:false` is the explicit `NoUpstream` state (no configured
 * upstream) and `ahead`/`behind` are null there — the pane renders the explicit
 * "no upstream" state instead of a fake 0/0.
 *
 * A configured upstream (`hasUpstream:true`) can *also* have null
 * `ahead`/`behind`: Git cannot compute them when the upstream's remote-tracking
 * ref is absent (the upstream branch was deleted remotely, or a `fetch --prune`
 * removed the ref). The pane must treat that as an explicit "tracking info
 * unavailable" state, not 0/0 — `hasUpstream` and counters-available are
 * independent (F1/F2).
 */
export interface SourceControlRemoteModel {
  status: GitRemoteStatus;
  hasRepository: boolean;
  worktreeRoot: string | null;
  branchName: string | null;
  isDetached: boolean;
  headCommit: string | null;
  /** Remote of the configured upstream (e.g. `origin`); null without one or when the upstream is a local branch. */
  remoteName: string | null;
  /** Configured upstream reference (e.g. `origin/main`); null without one. */
  upstreamReference: string | null;
  hasUpstream: boolean;
  /** Local commits not yet upstream; null without an upstream or when Git cannot compute the count. */
  ahead: number | null;
  /** Upstream commits not yet local; null without an upstream or when Git cannot compute the count. */
  behind: number | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-11 — typed fetch result. On success `remote` carries the tracking state
 * the API re-read from Git after the fetch; remote failures stay typed in
 * `outcome`/`failure` (spec 13 §14.1/§20).
 */
export interface SourceControlFetchResult {
  outcome: GitFetchOutcome;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  remote: SourceControlRemoteModel | null;
  failure: SourceControlFailure | null;
}

/** SCM-13 — typed outcome of one explicit push (spec 13 §14.4/§20). */
export type GitPushOutcome =
  | 'Succeeded'
  | 'NoUpstream'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'AuthenticationRequired'
  | 'Rejected'
  | 'PermissionDenied'
  | 'HostUnreachable'
  | 'Failed';

/**
 * SCM-13 — typed push result. On success `remote` carries the tracking state
 * the API re-read from Git after the push; a branch with no configured upstream
 * is the first-class `NoUpstream` outcome (no Git ran, no guessed default
 * remote), and every remote failure stays typed in `outcome`/`failure`
 * (spec 13 §14.2/§14.4/§20). A rejected push leaves local commits unchanged.
 */
export interface SourceControlPushResult {
  outcome: GitPushOutcome;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  remote: SourceControlRemoteModel | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-08 - typed status of one read-only Git history request (spec 13
 * §12/§20). `Available` means Git answered (the page may still be empty);
 * everything else is an inspectable typed state.
 */
export type GitHistoryStatus =
  | 'Available'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'InvalidRequest'
  | 'PathRejected'
  | 'NotFound'
  | 'Failed';

/** SCM-08 — one commit as Git reports it (spec 13 §12). */
export interface SourceControlHistoryCommit {
  /** Full commit object id. */
  commitId: string;
  /** Git's abbreviated commit id. */
  shortId: string;
  authorName: string;
  authorEmail: string;
  /** Git's strict ISO-8601 committer time; never localized. */
  committerTime: string;
  /** Commit subject. */
  summary: string;
  /** Number of files Git reports for this commit (pathspec-limited for file history). */
  fileCount: number;
}

/**
 * SCM-08 — one page of commits plus the window it came from, so the pane can
 * lazily append the next page (spec 13 §25).
 */
export interface SourceControlHistoryPage {
  commits: SourceControlHistoryCommit[];
  skip: number;
  limit: number;
  hasMore: boolean;
  /** Skip to pass for the next page; null when the history is exhausted. */
  nextSkip: number | null;
}

/**
 * SCM-08 — typed history payload. `source` is always `'Git'`: Git history is a
 * separate authority from Document IDE History (spec 13 §4.3/§12).
 */
export interface SourceControlHistoryResult {
  status: GitHistoryStatus;
  source: string;
  errorCode: string | null;
  message: string | null;
  isAvailable: boolean;
  page: SourceControlHistoryPage | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-08 — one document version read back from a commit (spec 13 §12).
 * `content` is null for a binary blob, and also for a blob over the served
 * bound: such a version is flagged `isTruncated` and is never read, so the pane
 * shows its true `sizeBytes` without a text preview.
 */
export interface SourceControlFileVersionInfo {
  commitId: string;
  path: string;
  content: string | null;
  isBinary: boolean;
  isTruncated: boolean;
  sizeBytes: number;
}

/** SCM-08 — typed document-version payload (spec 13 §12/§20). */
export interface SourceControlFileVersionResult {
  status: GitHistoryStatus;
  source: string;
  errorCode: string | null;
  message: string | null;
  isAvailable: boolean;
  version: SourceControlFileVersionInfo | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-10 — which destructive operation a review/apply pair describes
 * (spec 13 §15.1/§15.2). `DiscardWorktree` restores the file from the index;
 * `RestoreVersion` restores it from a selected commit. Neither touches the
 * index, HEAD or any other file.
 */
export type GitRestoreMode = 'DiscardWorktree' | 'RestoreVersion';

/** SCM-10 — typed outcome of one discard/restore request (spec 13 §15/§20). */
export type GitRestoreOutcome =
  | 'Succeeded'
  | 'ReviewRequired'
  | 'ReviewStale'
  | 'RejectedPath'
  | 'ConflictedPath'
  | 'UntrackedPath'
  | 'NoChanges'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'RecoveryBlocked'
  | 'Failed';

/** SCM-10 — typed outcome of one review read (spec 13 §15.1). */
export type GitRestoreReviewStatus =
  | 'Available'
  | 'RejectedPath'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'Failed';

/** SCM-10 — the reviewed confirmation payload the dialog renders. */
export interface SourceControlRestoreReviewInfo {
  mode: GitRestoreMode;
  path: string;
  sourceCommit: string | null;
  targetSource: string;
  diff: string;
  isDiffTruncated: boolean;
  indexChange: GitChangeKind | null;
  workingTreeChange: GitChangeKind | null;
  statusCharacters: string;
  isStaged: boolean;
  isConflicted: boolean;
  isUntracked: boolean;
  hasWorkingTreeChange: boolean;
  recoveryAvailable: boolean;
  recoveryDetail: string;
  currentSizeBytes: number | null;
  isApplicable: boolean;
  blockedReason: string | null;
  /** Opaque token the apply call must echo back; bound to the reviewed state. */
  reviewToken: string;
}

/** SCM-10 — typed review response (spec 13 §15.1/§20). */
export interface SourceControlRestoreReviewResult {
  status: GitRestoreReviewStatus;
  isAvailable: boolean;
  errorCode: string | null;
  message: string | null;
  review: SourceControlRestoreReviewInfo | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-10 — typed discard/restore result. On success `status` is the status the
 * API re-read from Git, `snapshotId` the HIST-01 recovery snapshot created
 * before the mutation, and `historyRecorded`/`reindexed` report the Document
 * IDE History record and reindex step.
 */
export interface SourceControlRestoreResult {
  outcome: GitRestoreOutcome;
  mode: GitRestoreMode;
  isSuccess: boolean;
  errorCode: string | null;
  message: string | null;
  operationId: string | null;
  snapshotId: string | null;
  recoveryRecorded: boolean;
  historyRecorded: boolean;
  reindexed: boolean;
  status: SourceControlStatusModel | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-12 — typed outcome of one conservative Pull (spec 13 §14.3/§20).
 * `Diverged`, `DirtyWorktree` and `ConflictedWorktree` are blocking states that
 * changed nothing; every non-success outcome is rendered typed, never blank.
 */
export type GitPullOutcome =
  | 'Succeeded'
  | 'NoUpstream'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'DirtyWorktree'
  | 'ConflictedWorktree'
  | 'Diverged'
  | 'IndexLocked'
  | 'AuthenticationRequired'
  | 'PermissionDenied'
  | 'HostUnreachable'
  | 'WorktreeMissing'
  | 'Failed';

/**
 * SCM-12 — typed Pull result. On success `head` is the fast-forwarded commit,
 * `changedPaths` the files the pull brought in, and `status`/`remote` the state
 * re-read from Git. A blocking result ran nothing: no merge commit, no rebase.
 */
export interface SourceControlPullResult {
  outcome: GitPullOutcome;
  isSuccess: boolean;
  isBlocking: boolean;
  errorCode: string | null;
  message: string | null;
  operationId: string | null;
  previousHead: string | null;
  head: string | null;
  changedPaths: string[];
  reindexedDocuments: number;
  statusRefreshed: boolean;
  historyRecorded: boolean;
  remote: SourceControlRemoteModel | null;
  status: SourceControlStatusModel | null;
  failure: SourceControlFailure | null;
}

/**
 * SCM-14 — typed unmerged-state outcome (spec 13 §20/§21). `Available` means
 * conflicts are present, `NoConflicts` means Git was read successfully and
 * reports none, and every other value is a typed inability to observe Git
 * safely. None of them is ever rendered as a silent clean state.
 */
export type GitConflictStatus =
  | 'Available'
  | 'NoConflicts'
  | 'NoRepository'
  | 'GitUnavailable'
  | 'WorktreeMissing'
  | 'IndexLocked'
  | 'Unreadable'
  | 'Failed';

/**
 * SCM-14 — how an affected document may be opened for manual resolution.
 * `OpenInWord` is the existing guarded Word open route, `OpenDocument` the
 * existing document-open seam, `None` means the path is not a registered
 * document (the shell must not offer an arbitrary-path opener).
 */
export type GitConflictOpenAction = 'None' | 'OpenInWord' | 'OpenDocument';

/**
 * SCM-14 — one real unmerged entry (spec 13 §21). `path` is worktree-relative
 * exactly as Git reported it; `documentId` is the only open target. There is
 * deliberately no absolute-path field.
 */
export interface SourceControlConflictEntry {
  path: string;
  conflictKind: GitConflictKind;
  statusCharacters: string;
  isDocx: boolean;
  openAction: GitConflictOpenAction;
  documentId: string | null;
}

/** SCM-14 — one unsafe action refused while the repository is unmerged. */
export interface SourceControlBlockedAction {
  action: string;
  errorCode: string;
  message: string;
  affectedPaths: string[];
}

/**
 * SCM-14 — typed conflict summary consumed by the ConflictPanel. The panel is
 * props-driven and performs no fetch/Git work of its own.
 */
export interface SourceControlConflictModel {
  status: GitConflictStatus;
  hasRepository: boolean;
  hasConflicts: boolean;
  worktreeRoot: string | null;
  branchName: string | null;
  isDetached: boolean;
  headCommit: string | null;
  conflicts: SourceControlConflictEntry[];
  blockedActions: SourceControlBlockedAction[];
  message: string | null;
  failure: SourceControlFailure | null;
}
