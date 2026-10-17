# Sprint Handoff — SCM-09

**Status:** DONE
**Branch:** `feat/document-ide-history-git-correlation`
**Commits:** `b043a24` (implementation, tests, frontend) plus this handoff commit at the tip — base `7fa82d8` (tip of `integration/parallel-batches`); see `git log --oneline 7fa82d8..HEAD`.

## What changed

- **New optional `WorkSession` core model** (spec 13 §13/§24): `WorkSession`
  (`id`, `workspaceId`, `repositoryRoot`, `worktreeRoot`, `startedAt`,
  `endedAt`, `label`), `WorkSessionDocument` (one changed-document
  association per document) and `WorkSessionCommit` (`sessionId`, `commitId`,
  `shortId`, `summary`, `recordedAt`). A WorkSession **is not a Git commit**:
  the repository/worktree roots are read-only context recorded only when Git
  discovery happened to succeed, and stay `null` for a non-Git source.
- **Typed outcome vocabulary** (`WorkSessionOutcome` + `WorkSessionResult`):
  `Succeeded` / `SessionRequired` / `DocumentRequired` / `CommitRequired` /
  `SessionNotFound` / `StoreUnavailable`. The three `*Required` values are
  input rejections where **nothing ran**; `StoreUnavailable` is the
  correlation-is-optional outcome whose message states the Git commit is
  unaffected (spec 13 §24). Blank ids are trimmed and rejected typed — never a
  silent fallback.
- **`WorkSessionService` is deliberately Git-free.** It references no Git type
  and contains no Git code path, so **no correlation operation can create,
  amend or push a commit**. `StartAsync` records Document IDE History metadata;
  `RecordChangedDocumentAsync` (step 2) and `AttachCommitAsync` (step 3) derive
  the associations from ids the caller already has; `GetAsync`/`DeleteAsync`
  read/drop derived metadata. A store failure is caught and mapped to the typed
  `StoreUnavailable` result instead of propagating.
- **`WorkSessionRepository` (SQLite)** stores the derived metadata in the
  application database (`work_sessions` / `work_session_documents` /
  `work_session_commits`), never in the user's Git worktree. Writes are
  parameterized (a commit id or label cannot inject SQL). `INSERT … ON CONFLICT`
  keeps **one association per document** and **one correlation per commit**, so
  a repeated operation cannot inflate the correlation history. `RecordChangedDocumentAsync`
  / `AttachCommitAsync` / `DeleteAsync` return `false` when the session does not
  exist. Deleting a session cascades its child rows (`PRAGMA foreign_keys=ON`).
- **Migration `011_WorkSessions`** creates the three tables with no foreign key
  into `workspaces`/`documents` (a session may start before a workspace record
  exists and a changed document may not be registered yet); only a
  session→child cascade exists. Registered in `MigrationRunner`.
- **Privileged API** (`SourceControlController`, same localhost session-token
  guard as SCM-03/06/07; **no unauthenticated path**):
  - `POST api/source-control/work-sessions` — body `{path, workspaceId, label}`.
    Performs a **read-only** `GitRepositoryService.DiscoverAsync(path)` and
    passes repository/worktree roots only when Git reported a repository; a
    non-Git folder starts a session with null roots and Git is never
    initialized.
  - `GET api/source-control/work-sessions/{sessionId}`.
  - `POST api/source-control/work-sessions/{sessionId}/documents` — body
    `{documentId, path}`.
  - `POST api/source-control/work-sessions/{sessionId}/commit-correlations` —
    body `{commitId, shortId, summary}`.
  - `DELETE api/source-control/work-sessions/{sessionId}`.
  - HTTP mapping matches the established split: blank-id input rejections
    (`SessionRequired`/`DocumentRequired`/`CommitRequired`, and a blank `path`
    → `PathRequired`) are typed **400** `{errorCode, error}`; **every execution
    outcome** (including `SessionNotFound` and `StoreUnavailable`) is HTTP
    **200** with the typed `SourceControlCorrelationResponse{outcome,isSuccess,errorCode,message,session}`.
- **Frontend** `CommitCorrelationPanel.tsx` (step 4) renders a **correlation,
  not a unified timeline**: Document IDE History (`correlation-document-ide-history`)
  and Git History (`correlation-git-history`) are two separately labelled
  sections, each with its own explicit empty state, plus a
  `correlation-git-context` line that reports the absent Git context instead of
  inventing one. A correlated commit always shows the full id exactly as Git
  reported it (and the short id when present); a changed document shows its
  recorded path and an `Open <name>` action through `onOpenDocument`.
  Exported typed API helpers (`startWorkSession`, `recordWorkSessionDocument`,
  `correlateWorkSessionCommit`, `fetchWorkSession`, `deleteWorkSession`) post
  through the privileged API with the session token and surface a typed 400
  rejection verbatim. The panel contains **no commit action**.

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Core/History/WorkSession.cs` — delivered as planned (new).
- `src/DocumentIDE.Infrastructure/Persistence/WorkSessionRepository.cs` —
  delivered as planned (new) at the documented path.
- `src/DocumentIDE.Web/src/features/source-control/CommitCorrelationPanel.tsx`
  — delivered as planned (new).
- `tests/DocumentIDE.IntegrationTests/SourceControl/WorkSessionCorrelationTests.cs`
  — delivered as planned (new; two classes: `WorkSessionCorrelationTests` 8 tests
  + `WorkSessionCorrelationApiTests` 5 tests = 13).

Additional files needed to satisfy the sprint (mapping recorded):

- `src/DocumentIDE.Core/History/WorkSessionService.cs` — **extra Core file**
  beyond the planned list: the Git-free service implementation, colocated with
  the contract in `WorkSession.cs` (the sprint plans only the model file). No
  lane file touched; no higher-level requirement weakened.
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/011_WorkSessions.cs`
  — the migration the repository needs (new), registered in
  `Migrations/MigrationRunner.cs` (one line appended to the ordered list).
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — extended in
  **bounded regions only**: 6 SCM-09 DTO records after the SCM-07 DTO block,
  one ctor/field for the injected `IWorkSessionService`, and one delimited
  `SCM-09` region at the end of the class holding the five routes plus
  `ToCorrelationResponse`/`ToWorkSessionResponse`. No existing member was
  reordered or reformatted; SCM-06/SCM-07 behaviour and vocabulary are intact.
- `src/DocumentIDE.Api/Program.cs` — **append-only** DI at the very end of the
  existing SCM block (after `GitCommitService`): `IWorkSessionRepository`
  (scoped) and `IWorkSessionService` (explicit factory, so no dependency on
  optional-parameter resolution). One `using DocumentIDE.Core.History;` added.
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` —
  SCM-09 model types (`WorkSessionOutcome`, `WorkSessionDocumentModel`,
  `WorkSessionCommitModel`, `WorkSessionModel`, `SourceControlCorrelationResult`).
- `src/DocumentIDE.Web/src/features/source-control/CommitCorrelationPanel.test.tsx`
  — 10 colocated component/helper tests (CAN-05 batch-B ruling; `tests/frontend/**`
  in the sprint doc is stale — that path does not exist in this repo).

Lane safety: `docs/sprints/SPRINT-STATUS.md`, `docs/quality/v1-readiness-report.md`,
`scripts/verify-release.ps1`, `docs/sprints/sprint-manifest.json`,
`docs/sprints/01-dependency-graph.md`, `docs/sprints/02-execution-waves.md` and
`docs/sprints/00-sprint-index.md` were **not touched** (left to the controller).

## TDD evidence

Backend cycles were RED-first; the mandated first behaviour was

> Successful commit can be linked to a WorkSession.

which failed first (`CS0246: WorkSessionService not found`) and then went GREEN
with the minimal service.

- Cycle 1 (`SuccessfulCommitCanBeLinkedToAWorkSession`): RED `CS0246` → GREEN
  with `WorkSession` + `WorkSessionService` (start/record/attach/get + blank-id
  validation) and the repository/migration. **Honest note:** the service was
  implemented in a single pass for this step, so the service edge cases below
  are coverage cycles over Cycle-1 behaviour (the same precedent SCM-07 used for
  its Cycles 7–8), not independent production-code increments.
- Cycle 2 (drop derived metadata): this cycle was made **genuinely RED** by
  temporarily removing the repository `DeleteAsync` member, confirming
  `CS1061: 'WorkSessionRepository' does not contain a definition for 'DeleteAsync'`,
  then re-adding it → GREEN.
- Cycle 3 (coverage): `BatchOperationHistoryRemainsUsableWithoutGit`,
  `CorrelationStoreFailureIsTypedAndNeverInvalidatesTheCommit`,
  `BlankIdsAreRejectedTypedWithoutWritingAnything`,
  `UnknownSessionIsTypedSessionNotFound`,
  `RecordingTheSameDocumentAndCommitTwiceKeepsOneAssociationEach`,
  `PathsWithSpacesUnicodeAndSpecialCharactersSurviveTheRoundTrip` → 8 service
  tests.
- Cycle 4 (API): RED confirmed by `dotnet build tests/DocumentIDE.IntegrationTests`
  (`CS0246` for `SourceControlWorkSessionRequest`, `SourceControlCorrelationResponse`,
  `SourceControlWorkSessionDocumentRequest/Response`,
  `SourceControlWorkSessionCommitRequest/Response`) → GREEN with the controller
  DTOs, routes, mapping and DI → 13 tests total.
  - One test-authoring fix during this cycle: a whitespace route value
    (`work-sessions/%20/...`) was rejected by the `[ApiController]` implicit
    `[Required]` non-nullable-reference-type rule with a `ProblemDetails` 400
    **before** the action ran. The route parameters were made nullable so the
    controller's own typed `SessionRequired` 400 is returned, which is the
    contract the sprint requires ("400 only where nothing ran"). The
    assertion was tightened back to the original after diagnosing the body.
- Refactor stage: strengthened `GitCommitRemainsValidWhenTheCorrelationDataIsDeleted`
  to assert the child rows are dropped (not orphaned) after a delete; still GREEN.
- Frontend Cycle 5: RED `CommitCorrelationPanel.test.tsx` failed on the missing
  `./CommitCorrelationPanel` import → GREEN with the model types + panel/handlers
  → 10 tests.

## Verification

- `dotnet build DocumentIDE.sln` → **0 errors**, 128 warnings (all pre-existing).
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~WorkSessionCorrelation"` → **13 passed / 0 failed / 0 skipped** (the sprint-filter evidence).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **800 passed / 2 failed / 0 skipped** (802 total). Both failures are the documented, REL-03-sanctioned release-artifact checks (`ScriptTests.Release_Build_Embeds_Production_Web_Assets` and `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`): `artifacts/` is gitignored, absent in a fresh worktree, and this diff touches neither `scripts/` nor packaging. The same two failures are recorded in the SCM-01…SCM-07 handoffs.
- `dotnet test tests/DocumentIDE.Core.Tests` → **459 passed / 0 failed / 0 skipped** (Core touched).
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **12 passed / 0 failed / 0 skipped**.
- `dotnet test tests/DocumentIDE.SecurityTests` → **91 passed / 0 failed / 0 skipped**.
- `npx vitest run` (`src/DocumentIDE.Web`) → **54 files / 384 tests passed, 0 failed**.
- `npx tsc -b` (`src/DocumentIDE.Web`) → exit 0.
- `npx oxlint` (`src/DocumentIDE.Web`) → exit 0, 0 errors. The new panel adds the
  same `react(only-export-components)` note `CommitBox.tsx` already carries, for
  co-locating the exported API helpers with the component — the established
  convention for this feature's helpers. No new warning class was introduced.
- `git diff` self-reviewed; bounded controller/`Program.cs` regions verified.

## Acceptance criteria

- [x] **Git commit IDs are stored only as derived correlation metadata** —
  `SuccessfulCommitCanBeLinkedToAWorkSession` creates the commit through the
  real SCM-07 flow first, then attaches `info.CommitId`; the stored value is
  read back through `GetAsync`. `GitCommitRemainsValidWhenTheCorrelationDataIsDeleted`
  proves the commit object, message, HEAD and working tree all survive dropping
  the correlation. `CorrelationStoreFailureIsTypedAndNeverInvalidatesTheCommit`
  proves a store failure is typed and the commit still valid. Nothing is written
  into Git.
- [x] **Document IDE History remains independent (of Git)** —
  `BatchOperationHistoryRemainsUsableWithoutGit` proves batch history is written
  and read back in a folder Git reports as `NoRepository`, and that the
  correlation surface starts a session with null roots there.
  `CorrelationWorksForANonGitFolderWithoutInitialisingGit` proves the same
  through the API and that Git is not initialized (no `.git`). The frontend
  renders Document IDE History and Git History as separate lists and keeps the
  IDE side usable with no Git context.
- [x] **No application operation creates an automatic commit** —
  `WorkSessionService` contains no Git code path (Git-free by construction), so
  it cannot invoke `git commit`. `WorkSessionRoutesCorrelateASuccessfulCommitWithoutCreatingAnotherCommit`
  asserts `commitsBefore + 1` after the whole correlation flow, where the single
  new commit is the explicit SCM-07 commit. There is no commit trigger on any
  correlation route.

Required tests from the sprint doc:

- [x] Successful commit can be linked to a WorkSession — `SuccessfulCommitCanBeLinkedToAWorkSession` (the mandated RED-first test) + `WorkSessionRoutesCorrelateASuccessfulCommitWithoutCreatingAnotherCommit` (API).
- [x] Git commit remains valid if IDE correlation data is deleted — `GitCommitRemainsValidWhenTheCorrelationDataIsDeleted` + `UnknownSessionIsTypedAndDeletingCorrelationLeavesTheCommitValid` (API).
- [x] Batch operation history remains usable without Git — `BatchOperationHistoryRemainsUsableWithoutGit` + `CorrelationWorksForANonGitFolderWithoutInitialisingGit` (API).

Implementation steps:

- [x] Step 1 Add optional WorkSession entity — `WorkSession.cs` + `011_WorkSessions` + `WorkSessionRepository`.
- [x] Step 2 Record changed-document association — `RecordChangedDocumentAsync` (service/repository/route) + one-association-per-document upsert test.
- [x] Step 3 Attach successful commit IDs — `AttachCommitAsync` (service/repository/route) + one-correlation-per-commit upsert test.
- [x] Step 4 Render correlation in history views — `CommitCorrelationPanel.tsx` (separate labelled lists, empty states, typed failures) + 10 tests.

## Deviations / spec conflicts

- One **extra Core file** beyond the planned list (`WorkSessionService.cs`) and
  one unplanned migration file (`011_WorkSessions.cs`, required by the planned
  repository). Both are dependencies of the planned deliverables, follow the
  SCM-02/06/07 placement precedent, touch no lane file and weaken no
  higher-level requirement.
- Frontend test path follows the CAN-05 batch-B ruling (colocated
  `CommitCorrelationPanel.test.tsx`); the sprint's `tests/frontend/**` path
  does not exist in this repository.
- The correlation surface lives in the existing `SourceControlController` under
  `api/source-control/work-sessions…` rather than a new controller, following
  the SCM-06/07 convention of one privileged Source Control controller.
- Sprint doc "SCM-09 (Discard)" consumer note in the **SCM-07 handoff** is
  mislabelled: SCM-09 is *correlation*; safe discard is a different sprint. No
  discard/restore/push/pull/fetch behaviour was implemented here.
- Cycle-1 service edge cases are coverage cycles over one implementation pass
  (recorded above) rather than separate production increments; no behaviour is
  claimed that the RED-first Cycle-1 test did not already pin.

## Risks discovered

- **Correlation has no session lifetime/close semantics in this sprint.**
  `WorkSession.EndedAt` exists in the model and column but nothing sets it:
  "end a session" belongs to a later sprint. A consumer must not assume a
  session is ever automatically closed.
- **The store is the application SQLite DB.** If that database is unavailable
  the outcome is typed `StoreUnavailable` (never a silent drop), but there is no
  retry/queue: a correlation attempted while the store is down is lost until the
  caller retries. This is acceptable because correlation is derived metadata
  (spec 13 §24) and never affects the commit.
- **Multiple sessions per workspace are allowed** and there is no uniqueness
  constraint, so `GetWorkSession` is always by id — a future history-view sprint
  must decide how it lists sessions (the migration has an `idx_work_sessions_started`
  index for that).
- **`Path` on a changed document is display metadata only** and is not validated
  for containment; it is never resolved against the filesystem or passed to
  Git by this surface, so it cannot escape the worktree. Any future consumer
  that turns it into a filesystem path must apply the SCM-06 containment rules.
- **No Git-history read surface here.** Correlated ids are shown as reported
  metadata; rendering an actual Git log/diff for a correlated commit is SCM-08's
  surface and is deliberately not implemented.

Consumer notes for SCM-08/SCM-10/SCM-11/SCM-13:

- **SCM-08 (History/Log):** `WorkSessionCommit.CommitId` is the full id exactly
  as Git reported it — reuse the SCM-07 read pattern
  (`git log -1 --format=%H%x00%h%x00%s`) if you need to re-read a correlated
  commit; the correlation store is not an authority on Git state.
- **SCM-13 / any further action surface:** keep the HTTP split — 400 only for
  input rejection where nothing ran, 200 typed body for every execution outcome.
  Branch on the body, not the status alone. Do not attach commit creation or
  push to a correlation call.
- Any consumer that lists sessions should treat the correlation rows as
  disposable: they may be dropped and rebuilt without affecting the repository.

## Next eligible sprint(s)

- Governed by the roadmap's own `Depends On` rows. This sprint unblocks nothing
  by itself: it deliberately added no history-read, discard/restore, branch or
  remote surface. The controller should flip the SCM-09 row to DONE in
  `docs/sprints/SPRINT-STATUS.md` (row currently at line 128, `READY`).

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — the
  correlation surface writes only to the application SQLite database and never
  to the user's worktree; `WorkSessionService` has no Git code path and no file
  write; every route carries the privileged localhost session guard
  (`CorrelationRoutesRejectMissingInvalidTokenAndHostileOrigin`).
- [x] **no arbitrary shell command can be injected through a filename/path** —
  there is no shell anywhere: the repository issues parameterized SQL only
  (`AddWithValue`), the Git discovery call reuses the SCM-01 structured
  `ArgumentList` runner, and no path/session/commit value is concatenated into a
  command. `PathsWithSpacesUnicodeAndSpecialCharactersSurviveTheRoundTrip`
  proves a path with spaces, Unicode and `& ( ) #` round-trips verbatim.
- [x] **no repository escape is possible through path handling** — Git discovery
  is read-only (`GitRepositoryService.DiscoverAsync`) and the resulting roots
  are recorded as context, never used to resolve or write a filesystem path; a
  non-Git folder yields null roots rather than an invented repository.
- [x] **no secret/credential output is exposed in UI logs** — responses carry a
  typed outcome, message and ids only; the store failure message includes only
  the exception type name, never a connection string or path; nothing contacts a
  remote.
- [x] **no destructive action silently broadened its scope** — the only
  destructive route is `DELETE work-sessions/{id}`, which drops exactly one
  session's derived metadata (cascade to its child rows only) and never touches
  Git; a blank/unknown id is a typed rejection, not a wildcard.
- [x] **relevant higher-level safety rules remain stronger than convenience** —
  the local filesystem and Git remain the source of truth (correlation is
  explicitly derived and disposable); typed outcomes replace silent fallbacks;
  no semantic/fuzzy matching is involved; no commit is ever created
  automatically. The source-mutation snapshot/temp/atomic-replace rule is **N/A**
  in one sentence: this sprint performs no in-place mutation of user file
  content — it only reads Git context and stores Document IDE metadata in the
  application database.
