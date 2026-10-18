# 13 — Source Control & Git Worktree Specification

**Status:** Approved feature specification / implementation-ready baseline  
**Area:** Source Control  
**Primary platform:** Windows desktop  
**Depends on:** Filesystem Sync, Markdown, DOCX, History, Desktop/WebView host

---

## 1. Purpose

Document IDE gains an optional Git-aware source-control layer for workspaces whose local files live inside Git repositories.

The feature is designed around one simple question:

> **What changed in my documents, what am I about to save as a version, and where did that version go?**

The feature intentionally follows Git's proven working-tree → staging → commit model, while adding Document IDE-specific understanding for DOCX and the application's existing operation/recovery history.

This feature is **not** a Git client replacement and is **not** intended to reproduce every Git command.

---

## 2. Product Boundary

Document IDE SHALL support these Git concepts:

- repository detection,
- current worktree context,
- working-tree changes,
- staged changes,
- untracked files,
- file diff/review,
- stage / unstage,
- commit,
- commit all as an explicit convenience action,
- commit history,
- file history,
- fetch,
- ahead/behind state,
- pull using a safe fast-forward-only default,
- push,
- conflict state reporting,
- discovery of Git worktrees,
- opening an existing worktree,
- creation/removal of worktrees using existing Git refs.

Document IDE SHALL NOT implement a product-level branch-management subsystem in V1.

Specifically out of scope:

- create branch,
- delete branch,
- rename branch,
- merge branches from a branch-management UI,
- rebase UI,
- branch comparison workspace,
- branch ranking or branch dashboards.

The current Git branch may be shown as **read-only context metadata** because Git status and worktree state expose it, but it is not a managed Document IDE entity.

---

## 3. Core Mental Model

Git distinguishes three important states:

```text
HEAD
  ↓
Index / Staging Area
  ↓
Working Tree
```

Document IDE mirrors this model in the Source Control pane.

Example:

```text
WORKING TREE
A.docx   modified
B.docx   modified
C.md     modified
```

After staging A and C:

```text
STAGED CHANGES
A.docx
C.md

CHANGES
B.docx
```

A normal Git commit records the staged state, not every currently modified file.

Therefore:

```text
Commit
→ commit staged changes only
```

The UI MAY provide:

```text
Commit All
```

as a convenience action equivalent to staging all eligible changes and then committing them.

The user must never be led to believe that saving a document automatically creates a Git commit.

---

## 4. Source of Truth Boundaries

Three authorities exist and must remain separate.

### 4.1 Local Filesystem

Authoritative for current document contents.

```text
C:\Projects\repo\docs\agreement.docx
```

remains the actual file being edited by Word or the Markdown editor.

### 4.2 Git Repository

Authoritative for Git state:

- HEAD,
- index/staging state,
- commit history,
- remote tracking state,
- worktree registration.

Document IDE SHALL derive Git status from Git rather than inventing a parallel Git-state database.

### 4.3 Document IDE History

Authoritative for application-level operation and recovery information:

- batch operation history,
- recovery snapshots,
- conflict-aware undo,
- document operation metadata.

Git history MUST NOT replace Document IDE History.

---

## 5. Repository Discovery

When a workspace or active document is associated with a local path, the backend SHOULD discover the enclosing Git working tree using Git itself rather than manually guessing `.git` boundaries.

The resulting repository context SHALL include at minimum:

```text
RepositoryContext
- repository_root
- worktree_root
- git_dir_reference
- current_head_commit
- current_branch_name_or_detached
- upstream_reference_if_present
```

A path outside a Git worktree remains a valid Document IDE source. It simply has no Git Source Control state.

Document IDE MAY support more than one Git repository within a workspace. For V1, the Source Control UI SHALL operate on the currently active repository context and MAY expose a repository selector when multiple repositories are detected.

Nested repositories and submodules are detected but are not required to expose full submodule management in V1.

---

## 6. Status Refresh

The Source Control status surface SHALL be derived from Git using a machine-readable status format.

Preferred invocation:

```text
 git --no-optional-locks status --porcelain=v2 -z --branch
```

The parser SHALL NOT depend on localized human-readable Git output.

Status states SHALL include at least:

```text
unmodified
modified
added
deleted
renamed
copied
untracked
ignored
conflicted
```

The UI SHALL preserve enough raw status information to distinguish staged versus unstaged changes.

Filesystem watcher events are hints that Git status may have changed. They are not themselves the authoritative Git state.

A save event from Microsoft Word SHALL trigger a debounced Source Control refresh through the existing filesystem coordination layer.

---

## 7. Source Control Pane

The primary UI surface is:

```text
SOURCE CONTROL
```

Recommended structure:

```text
SOURCE CONTROL

feature/production-v1   ← read-only context
↑ 2   ↓ 0

STAGED CHANGES (2)
  M agreement.docx
  M implementation.md

CHANGES (3)
  M contract.docx
  M proposal.docx
  U notes.md

[Stage All]

Commit message…
[Commit] [Commit All]

[Fetch] [Pull] [Push]
```

The branch name in the example is illustrative only and SHALL remain non-editable in this feature.

The Source Control pane SHALL not require the user to understand Git internals to perform common document work.

---

## 8. Change Grouping

Changes SHALL be grouped by Git state rather than by Document IDE operation.

Primary groups:

```text
STAGED CHANGES
CHANGES
UNTRACKED
CONFLICTS
```

Optional metadata MAY show:

- changed by Word save,
- changed by Markdown editor,
- changed by batch operation,
- changed externally.

Such metadata is explanatory only and MUST NOT override Git state.

---

## 9. Diff Model

### 9.1 Markdown

Markdown files SHALL support ordinary textual diff.

The UI SHALL distinguish:

```text
HEAD ↔ working tree
HEAD ↔ index
index ↔ working tree
```

as appropriate to whether the user is inspecting staged or unstaged changes.

### 9.2 DOCX

Git remains the authority for whether a DOCX file changed and what version is staged.

Document IDE MAY provide a document-aware diff by materializing the relevant Git blob/version into a temporary read-only artifact and comparing it through the existing DOCX parser.

The DOCX diff SHOULD identify, where safely supported:

- changed visible text,
- added/removed paragraphs,
- changed table cells,
- changed headings,
- meaningful formatting changes,
- metadata changes.

The Git staging boundary remains **file-level**. Paragraph/cell-level partial staging is explicitly out of scope for V1.

### 9.3 Binary and unsupported files

If a file cannot be meaningfully diffed by Document IDE, the UI SHALL still report Git status and SHALL provide a binary-change summary rather than pretending a semantic diff exists.

---

## 9.4 Git Command Mapping

The implementation should map product actions to ordinary Git primitives rather than inventing alternate repository semantics.

| Product action | Git primitive / intent |
|---|---|
| Repository context | `git rev-parse` family |
| Status | `git status --porcelain=v2 -z --branch` |
| Stage | `git add -- <paths>` |
| Unstage | `git restore --staged -- <paths>` |
| Working-tree diff | `git diff -- <paths>` |
| Staged diff | `git diff --cached -- <paths>` |
| Commit | `git commit -m <message>` |
| History | `git log` |
| File history | `git log -- <path>` |
| Fetch | `git fetch` |
| Pull | `git pull --ff-only` |
| Push | `git push` |
| Worktree list | `git worktree list --porcelain` |
| Worktree create | `git worktree add <path> <existing-ref>` |
| Worktree remove | `git worktree remove <path>` |

This table is an implementation mapping, not permission to expose every underlying Git option.

Document IDE MUST NOT use `git commit -a` for the ordinary `Commit` action because that would bypass the explicit staged-change mental model.

## 10. Staging

Stage and unstage SHALL operate through Git.

Supported actions:

```text
Stage
Unstage
Stage All
Unstage All
```

A stage operation SHALL use the file content present at the time the Git add operation executes.

If the file changes after staging, Git status SHALL report the staged and working-tree states separately.

Document IDE MUST NOT maintain its own staging snapshot outside the Git index.

---

## 11. Commit Semantics

### 11.1 Commit

`Commit` SHALL invoke Git using the current staged state.

The commit message is required.

The UI SHALL not launch an editor for the commit message in the normal path.

Git hooks configured by the repository remain part of normal Git semantics. Hook failures SHALL be surfaced as commit failures; the application SHALL not silently bypass them.

### 11.2 Commit All

`Commit All` SHALL:

1. refresh status,
2. show the set of eligible working-tree changes,
3. stage all eligible changes,
4. re-read status,
5. create the commit,
6. show the resulting commit identifier.

The action SHALL never silently include ignored files or files outside the current repository/worktree.

### 11.3 Empty commit

The UI SHALL not create an empty commit unless a future explicit feature adds that capability.

---

## 12. Commit and File History

The History surface SHALL show real Git commits.

A repository history item SHOULD include:

```text
commit_id
short_id
author
committer_time
message
file_count
```

File history SHALL allow the user to inspect commits affecting a selected file.

The UI MAY offer:

```text
View commit
View file at commit
Compare with previous version
Restore file from commit
```

Restore is destructive and follows the safety rules in Section 15.

Document IDE History and Git History SHALL be visually distinguishable.

Recommended labels:

```text
Document IDE History
Git History
```

---

## 13. Work Sessions and Correlation

A future-compatible `WorkSession` MAY group a user's activity across multiple files and operations.

A WorkSession is not a Git commit.

Recommended relationship:

```text
IDE Operation
    ↓
Working Tree Change
    ↓
Staging
    ↓
Git Commit
```

Document IDE MAY associate a Git commit with a WorkSession after successful commit creation.

The association is derived metadata and MUST NOT alter Git history.

---

## 14. Remote Operations

### 14.1 Fetch

`Fetch` SHALL update remote-tracking information without modifying the working-tree document contents.

### 14.2 Ahead / Behind

The UI SHOULD expose configured upstream state where available:

```text
↑ 2   ↓ 1
```

Meaning:

- `↑` local commits not yet on upstream,
- `↓` upstream commits not yet in local history.

When no upstream exists, the UI SHALL report that explicitly rather than showing `0/0`.

### 14.3 Pull

V1 SHALL use a conservative pull policy:

```text
git pull --ff-only
```

or the equivalent two-step:

```text
git fetch
→ verify fast-forward is possible
→ fast-forward only
```

Document IDE SHALL NOT silently create merge commits or perform a rebase.

If the histories diverge, the operation stops and the UI reports:

```text
Local and remote history have diverged.

No automatic merge/rebase was performed.
```

### 14.4 Push

`Push` SHALL be explicit.

Document IDE SHALL NOT automatically push after commit.

The application SHOULD use the user's configured Git credential helpers and SHALL fail clearly when authentication requires unsupported interactive input.

---

## 15. Safe Destructive Operations

### 15.1 Discard Changes

Discarding working-tree changes is destructive.

Before execution, the UI SHALL expose:

- affected files,
- current diff,
- whether each file is staged,
- recovery availability.

Document IDE SHOULD create an application-level recovery record when practical before discarding a document change, especially for DOCX and Markdown files managed by the application.

### 15.2 Restore from Git

Restoring a file from Git SHALL:

1. check current file state,
2. show the target Git version,
3. warn that local changes will be replaced,
4. create recovery coverage where available,
5. execute the restore,
6. re-index the document,
7. refresh Git status,
8. write Document IDE History metadata.

The restore operation SHALL not silently restore an entire repository when the user selected a single document.

---

## 16. Worktree Support

Git worktree is supported as a filesystem/worktree-context feature, not as a branch-management feature.

### 16.1 Discovery

The application SHALL be able to list registered worktrees for the current repository.

Each item SHOULD include:

```text
path
HEAD commit
branch name or detached state
locked/prunable state where available
```

### 16.2 Open Existing Worktree

Opening a worktree means making that filesystem location the active Document IDE source/worktree context. If the worktree is not already attached to the current workspace, the user must explicitly choose the existing workspace/source attachment flow; opening a worktree MUST NOT silently detach other sources or mutate canvas/file organization.

No Git branch switch is performed merely by opening a worktree.

### 16.3 Create Worktree

V1 MAY create a worktree from:

- an existing local branch,
- an existing tag/ref,
- an explicit commit.

The UI SHALL NOT provide branch creation as a side effect of worktree creation.

If a new branch is required, that remains a Git operation outside the supported product scope.

### 16.4 Remove Worktree

Removal is destructive.

Default rule:

- refuse removal when the worktree contains uncommitted changes,
- show the current change list,
- require explicit acknowledgement before removing a clean worktree.

Forced removal is out of scope for V1.

### 16.5 Prune

The UI MAY expose a safe `Refresh Worktrees` action that runs Git worktree metadata refresh/prune semantics where supported. It MUST NOT delete a valid existing worktree merely because its path is temporarily unavailable.

---

## 17. Filesystem and Word Integration

Git status refresh integrates with the existing filesystem watcher.

Flow:

```text
Word Save
   ↓
Filesystem event
   ↓
FileChangeCoordinator
   ↓
Document re-index
   ↓
Debounced Git status refresh
   ↓
Source Control pane update
```

Git commands SHALL NOT be executed directly from the filesystem watcher callback.

Word COM serialization rules remain unchanged. Source Control never invokes Word COM simply to compute ordinary Git status.

---

## 18. API Surface

Suggested privileged local API groups:

```text
GET  /api/source-control/context
GET  /api/source-control/status
GET  /api/source-control/diff
POST /api/source-control/stage
POST /api/source-control/unstage
POST /api/source-control/stage-all
POST /api/source-control/unstage-all
POST /api/source-control/commit
POST /api/source-control/commit-all
GET  /api/source-control/history
GET  /api/source-control/file-history
GET  /api/source-control/remote
POST /api/source-control/fetch
POST /api/source-control/pull
POST /api/source-control/push
GET  /api/source-control/worktrees
POST /api/source-control/worktrees
DELETE /api/source-control/worktrees/{id}
```

There SHALL be no branch-management endpoints in this feature.

All routes are privileged local API routes and inherit the localhost authentication rules from the master specification.

## 18. Process Execution Boundary

Frontend code MUST NOT invoke `git.exe` directly.

Preferred boundary:

```text
React / WebView2
        ↓
Local Application API
        ↓
Git Application Services
        ↓
Controlled git.exe process
```

Git processes SHALL use structured argument passing.

Do NOT construct shell command strings from user input.

The process runner SHALL support:

- working directory selection,
- cancellation,
- stdout/stderr capture,
- exit code inspection,
- timeout policy,
- environment control,
- process-tree cleanup where necessary.

For background/status operations, use Git's no-optional-lock behavior to avoid needless index-lock contention.

For remote commands, set `GIT_TERMINAL_PROMPT=0` (or equivalent process-level behavior) so a hidden process cannot hang waiting for terminal credentials. Repository-configured credential helpers remain supported.

---

## 19. Concurrency

Git operations SHALL be serialized per worktree where they mutate shared repository state.

At minimum, the following MUST NOT run concurrently against the same worktree/index:

- stage/unstage,
- commit,
- restore,
- operations that write the index,
- worktree registration mutation.

Read-only history/status operations MAY run concurrently when Git permits it safely, but the implementation should avoid races around index writes.

Recommended abstraction:

```text
GitWorktreeLock
  └── per worktree/repository mutation queue
```

This queue is distinct from the Word COM STA queue.

---

## 20. Error Classification

Git failures SHOULD be classified into typed application outcomes.

Examples:

```text
git_not_installed
git_repository_not_found
git_worktree_invalid
git_lock_contention
git_auth_required
git_permission_denied
git_commit_hook_failed
git_push_rejected
git_pull_not_fast_forward
git_conflict
git_untracked_conflict
git_dirty_worktree
```

The UI SHALL show actionable error context without exposing raw secrets or unnecessary filesystem internals.

---

## 21. Conflict Handling

V1 is conflict-aware, not conflict-merging.

When Git reports an unmerged state:

```text
CONFLICTS
  agreement.docx
  implementation.md
```

Document IDE SHALL:

- block unsafe actions that could overwrite conflict state,
- provide a conflict summary,
- allow opening affected files for manual resolution,
- allow inspection of available Git versions where possible.

Markdown conflict assistance may be richer in a future version.

DOCX automatic merge is explicitly out of scope for V1.

---

## 22. Multi-Repository Workspace

A workspace may eventually contain multiple repositories.

V1 support SHOULD remain bounded:

- identify repository context from the active source/document,
- allow switching repository context when multiple roots are visible,
- keep each Git operation scoped to exactly one repository/worktree.

Do not implement a cross-repository commit transaction.

---

## 23. Security and File-Safety Rules

The existing master-spec safety baseline remains authoritative.

Additional Git-specific rules:

- never execute Git through a shell command string,
- never pass uncontrolled user text as an option; use `--` before file paths,
- never operate outside the resolved worktree root for file-scoped Git actions,
- never expose `.git` internals through public ShareHost routes,
- never place Document IDE caches/SQLite state inside the user's Git worktree by default,
- never auto-stage or auto-commit on save,
- never auto-push,
- never silently merge or rebase during Pull,
- never force-push from the Document IDE V1 surface,
- never discard conflict state silently.

Git hooks are user/repository-controlled code. Explicit commit/push actions may invoke configured hooks through normal Git behavior, but the application MUST provide timeout/error handling and MUST surface hook failures.

---

## 24. Persistence Model

Git repository state remains external and authoritative.

Do not persist a duplicate copy of:

- index,
- HEAD,
- branches,
- commits,
- remote refs.

Document IDE may persist derived metadata such as:

```text
WorkSession
- id
- workspace_id
- repository_root
- worktree_root
- started_at
- ended_at
- label
```

and optional commit correlations:

```text
WorkSessionCommit
- session_id
- commit_id
- created_at
```

These rows are disposable derived metadata and may be rebuilt or dropped without making the Git repository unusable.

---

## 25. Performance Targets

For a typical repository/worktree containing fewer than approximately 1,000 tracked document files:

- status refresh should feel interactive,
- Source Control pane should update shortly after a save,
- repeated Git refreshes should be debounced,
- large history views should be paginated/lazy-loaded,
- DOCX diff parsing should run off the UI thread.

Do not run a full repository status scan synchronously inside a UI click handler.

Untracked-file scanning can become expensive in larger worktrees. The implementation should use Git's machine-readable status and avoid unnecessary high-frequency rescans.

---

## 26. Non-Goals

Explicitly not part of this feature:

- branch creation/deletion/rename UI,
- merge/rebase UI,
- cherry-pick UI,
- interactive rebase,
- Git submodule management,
- Git LFS administration,
- remote account management,
- force push,
- patch-level staging for DOCX,
- automatic DOCX merge/conflict resolution,
- GitHub/GitLab/Bitbucket issue/PR integration,
- replacing Document IDE History with Git.

---

## 27. Acceptance Criteria

The feature is acceptable when:

- a user changes three documents and sees exactly those changes in the Source Control pane,
- staged and unstaged changes remain separately visible,
- Commit records only staged changes,
- Commit All can intentionally commit all eligible changes,
- Markdown diffs are reviewable,
- DOCX changes can be reviewed at a document-aware level where supported,
- Git history is readable,
- Document IDE History remains separate and functional,
- fetch/pull/push are explicit actions,
- pull never silently merges/rebases in V1,
- Git worktrees can be discovered and opened,
- worktrees can be created from existing refs without a branch-management UI,
- dirty worktrees cannot be removed through the normal V1 flow,
- all Git operations execute through a controlled backend process boundary,
- filesystem source-of-truth and existing recovery rules remain intact.

---

## 28. Recommended External References

Normative Git semantics referenced by this specification:

- Git status: https://git-scm.com/docs/git-status
- Git add / staging area: https://git-scm.com/docs/git-add
- Git commit: https://git-scm.com/docs/git-commit
- Git worktree: https://git-scm.com/docs/git-worktree
- Git reference: https://git-scm.com/docs
