# Sprint Handoff — SCM-03

**Status:** DONE
**Branch:** `feat/source-control-changes-pane`
**Commits:** (see git log for `feat(scm): source-control-changes-pane`)

## What changed

- **Step 1 — Source-control API surface (backend).** New privileged controller
  `SourceControlController` (`api/source-control`, spec 13 §18) exposes exactly
  two read-only routes:
  - `GET /api/source-control/context?path=` — repository/worktree discovery
    from a workspace folder or active-document path via SCM-02
    `GitRepositoryService.DiscoverAsync` (branch, HEAD, detached flag, upstream
    reference, typed failure);
  - `GET /api/source-control/status?path=` — full changes payload: grouped
    sections + read-only branch context + ahead/behind counters.
  Every route inherits the `PrivilegedRequestGuard` localhost session-token
  rules; there is no unauthenticated path (test-pinned). Blank/missing `path`
  is a typed 400. **Every status call re-derives state from Git — nothing is
  cached** (`StatusRefreshReflectsCurrentGitStateWithoutCache`), so a pane
  refresh always shows current state.
- **Change grouping (spec 13 §8).** Sections are derived from the SCM-02
  snapshot vocabulary: staged = `HasStagedChange`, unstaged ("CHANGES") =
  `HasWorkingTreeChange`, untracked = `IsUntracked`, conflicts = `IsConflicted`.
  Conflicts are excluded from staged/unstaged; a file with an index **and** a
  working-tree change (`MM`) appears in **both** staged and unstaged groups,
  exactly as Git reports it (test-pinned both in backend and frontend).
- **Typed status/JSON contract.** New top-level records in the controller file
  (repo convention): `SourceControlStatusResponse`,
  `SourceControlContextResponse`, `SourceControlSectionsResponse`,
  `SourceControlChangeResponse`, `SourceControlFailureResponse`. Wire status
  vocabulary is the `GitRepositoryStatus` names (`Available`, `NoRepository`,
  `GitUnavailable`, `Failed`); enums serialize via `ToString()`. Each change
  entry keeps `path`, `originalPath`, `state`, `indexChange`,
  `workingTreeChange`, `conflictKind`, raw `statusCharacters`, plus two
  resolved fields described below.
- **Step 4 — document opening/inspection seam.** Each entry carries
  `absolutePath` (worktree-contained local path, `null` when it cannot be
  safely resolved) and `documentId` (resolved through the existing
  `IDocumentRepository.GetByPathAsync`). The frontend exposes
  `onOpenDocument(documentId)`; double-clicking a change with a resolved
  document invokes it. Unregistered paths keep `documentId=null` and are not
  opened (test-pinned).
- **Steps 2+3 — frontend pane (`SourceControlPanel.tsx` +
  `sourceControlModels.ts`).** Props-driven, host-agnostic component (same
  precedent as `WorktreeList`/`HistoryPanel`: no host mounting in this sprint):
  - renders `STAGED CHANGES`, `CHANGES`, `UNTRACKED`, `CONFLICTS` sections with
    counts, each entry showing its raw porcelain status badge (`.M`, `M.`,
    `??`, `UU`, …) and the Git-reported path verbatim (spaces/Unicode tested);
  - read-only branch label (branch name / `Detached` / `Unknown`) and
    ahead/behind text (`↑ N ↓ M`); without a configured upstream it shows
    **"No upstream"** instead of a fake 0/0 (spec 13 §14.2); no branch,
    checkout, switch, merge or rebase controls exist (test-pinned);
  - single click selects a change and reports it via
    `onSelectionChange(entry)`; double click opens via
    `onOpenDocument(documentId)`. Selection is reconciled on every model
    update: kept when the file is still changed (refresh without losing
    selection), cleared with `onSelectionChange(null)` when the file left all
    sections (test-pinned);
  - typed states rendered explicitly: loading, no-repository,
    Git-unavailable, `Failed` with `errorKind`/`errorMessage` in an alert, and
    a transport `errorMessage` alert that does not hide the last known status;
    clean working tree shows a clean-state message.
- **DI wiring.** `GitRepositoryService` registered in `Program.cs` (appended to
  the existing SCM-01 block) with the FS-03 `FileChangeCoordinator` passed via
  `GetService` when the host registers it — see deviations for the coordinator
  seam status.

## Files changed

Production:

- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` (new — API surface + DTOs)
- `src/DocumentIDE.Api/Program.cs` (modified — SCM-03 DI block appended after SCM-01 registrations)
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` (new — wire-contract mirror)
- `src/DocumentIDE.Web/src/features/source-control/SourceControlPanel.tsx` (new — changes pane)

Tests:

- `tests/DocumentIDE.IntegrationTests/SourceControl/SourceControlPanelTests.cs` (new, 15 tests)
- `src/DocumentIDE.Web/src/features/source-control/SourceControlPanel.test.tsx` (new, 20 tests)

**Planned-file mapping (rulings recorded):**

| Sprint planned path | Actual path (ruling) |
|---|---|
| `tests/DocumentIDE.IntegrationTests/SourceControl/SourceControlPanelTests.cs` | unchanged (no `DocumentIDE.UnitTests` project exists; suite lives in IntegrationTests) |
| frontend test (not in planned list) | `src/DocumentIDE.Web/src/features/source-control/SourceControlPanel.test.tsx` |

The frontend test is colocated with the implementation per the CAN-05/CAN-04
batch-B ruling (frontend tests live next to the code under
`src/DocumentIDE.Web/src/features/**`). `TestGitRepository.cs` was **not**
modified (its SCM-02 `RunGitCapture` helper was reused as-is). No SCM-04,
SCM-06, or SCM-16 file was created or modified.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControlPanelTests"` → **PASS (15/15, 0 skipped)**
- `dotnet test tests/DocumentIDE.IntegrationTests` → **593 passed / 2 failed / 595 total**; both failures are the documented pre-existing REL-03-sanctioned release-artifact checks (`ScriptTests.Release_Build_Embeds_Production_Web_Assets`, `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — `artifacts/` absent in a fresh worktree; unchanged from the SCM-02/REL-03 baseline)
- `dotnet test tests/DocumentIDE.SecurityTests` → **PASS (91/91)**
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **PASS (12/12; includes `SourceControlProcessBoundaryTests`)**
- `dotnet test tests/DocumentIDE.Core.Tests` → **PASS (459/459)** (evidence only; Core untouched)
- `dotnet test tests/DocumentIDE.SafetyTests` → **PASS (24/24)**
- `cd src/DocumentIDE.Web && npx vitest run` → **PASS (43 files, 266/266)**
- `cd src/DocumentIDE.Web && npx tsc -b` → **exit 0**
- `cd src/DocumentIDE.Web && npx oxlint` → **exit 0** (no warnings attributable to the new files)

TDD evidence (Red → Green → Refactor):

1. **Backend cycle 1 RED:** first failing test
   `ThreeModifiedDocumentsAppearAsThreeChanges` failed with `CS0246`
   (`SourceControlStatusResponse` not found) before the controller existed →
   GREEN: 1/1 after controller + DI. (First runtime failure — files reported
   untracked instead of modified — was a fixture flaw: the baseline commit had
   to include the files before editing; fixed in the test helper, not in
   production code.)
2. **Backend cycle 2 (disclosed):** the remaining 14 backend tests were added
   after cycle 1 and **passed on their first run** — their behaviors were
   already satisfied by the generalized cycle-1 implementation, so no RED was
   observed for this batch. See deviations.
3. **Frontend cycle A RED:** `Failed to resolve import "./SourceControlPanel"`
   → GREEN: 1/1 (data-driven section rendering).
4. **Frontend cycle B RED:** full pane suite failed **14/20** (missing branch
   context, ahead/behind placeholder, failure states, selection retention,
   open seam, clean state) → GREEN: 20/20 after implementing those behaviors.

## Acceptance criteria

- [x] **The pane shows exactly the current Git state** — backend re-derives
      from Git on every call with no cache
      (`StatusRefreshReflectsCurrentGitStateWithoutCache`); grouping exactly
      matches SCM-02 predicates (`StagedAndUnstagedGroupsRemainSeparate`,
      `FileWithBothStagedAndWorkingTreeChangesAppearsInBothGroups`,
      `UntrackedFilesAppearOnlyInTheUntrackedSection`,
      `ConflictedFilesAppearOnlyInTheConflictSection`); frontend renders the
      payload verbatim (20 frontend tests incl. spaces/Unicode and raw status
      characters).
- [x] **Current branch/worktree information is read-only** —
      `ContextReportsReadOnlyBranchAndDetachedState` (branch + detached
      reported, nothing mutates); frontend branch/ahead-behind are plain text
      (`shows branch and ahead/behind context as read-only text with no branch
      controls`).
- [x] **No branch-management controls are present** — backend
      `BranchManagementRoutesDoNotExist` (POST branch/checkout, DELETE branch →
      404) and frontend assertions that no branch/checkout/switch/merge/rebase
      button, branch combobox, or branch/tag/remote textbox exists.

Required tests from the sprint doc:

- [x] *Three modified documents appear as three changes* — first failing test
      in both halves (`ThreeModifiedDocumentsAppearAsThreeChanges`,
      `shows three modified documents as three changes`).
- [x] *Staged and unstaged groups remain separate* —
      `StagedAndUnstagedGroupsRemainSeparate`,
      `keeps staged and unstaged groups separate` (+ `MM` both-groups tests).
- [x] *Status refresh updates the pane without losing selection* — backend
      refresh test (no cache) + frontend
      `updates the pane on refresh without losing the selected file` and
      `clears the selection when the selected file is no longer changed`.

## Deviations / spec conflicts

- **TDD process disclosure (backend cycle 2):** 14 of the 15 backend tests
  were written after the cycle-1 implementation and passed without an observed
  RED. Cycle 1 itself followed Red → Green, and the frontend halves followed
  Red → Green twice; no implementation behavior was weakened, but strict
  per-behavior Red→Green was not observed for this batch.
- **`Program.cs` is not in the sprint's planned file list** but is required DI
  wiring: the SCM-02 handoff explicitly instructs SCM-03 to register
  `GitRepositoryService`. The edit is 11 lines appended to the existing
  SCM-01/SCM-02 registration block; nothing else in `Program.cs` changed.
- **Coordinator seam is structural, not active.** `FileChangeCoordinator` is
  not registered in the API host (its dependencies are scoped), so
  `sp.GetService<FileChangeCoordinator>()` resolves to `null` and no
  server-side watching is started. The pane is pull-based (every call
  re-derives status); no transport exists yet in this sprint to push state to
  the browser. The SCM-02 debounced-refresh capability remains intact behind
  `StartWatching`/`RefreshAsync` for a host that registers a singleton-safe
  coordinator and a push channel. Any future registration must keep the
  factory contract in mind: the coordinator consumed by this singleton must
  itself be root-safe/singleton-safe.
- **Frontend test path** not in the sprint's planned list — colocated per the
  CAN-05 batch-B ruling (recorded above).
- **Raw status characters are shown verbatim** (`.M`, `M.`), including the
  index/working-tree dots — deliberate inspectability per spec 13 §6; a
  friendlier badge is a later UX decision, not a semantic change.
- **Sprint-doc header still says `Status: BLOCKED`;** per task instruction only
  the `SPRINT-STATUS.md` row is updated (READY → DONE).

## Risks discovered

- **Selection is keyed by path only.** An `MM` file appears twice but shares
  one selection state — intentional (same file), but SCM-06 must pass the
  originating section along with the path when a per-group action (stage vs
  unstage) is attached to a row.
- **No serialization between status reads and future mutations.** SCM-06
  mutations must go through the SCM-01 mutation queue per worktree root;
  concurrent Git runs can currently surface typed `GitLockContention` (by
  design, not silent).
- **`documentId` resolution is one `GetByPathAsync` per change entry** (N+1
  lookups on large change sets). Acceptable at current scale; if a very large
  repository causes visible latency, batch the lookup instead of weakening the
  seam.
- **Path containment uses `OrdinalIgnoreCase`** (Windows target). Correct for
  this product; a future case-sensitive filesystem host should revisit.
- **Pane is not mounted in a host UI yet** (same as `WorktreeList`): fetching,
  polling, and refresh triggers are the host wiring sprint's job; the
  `onSelectionChange`/`onOpenDocument` props are the integration surface.

## Consumer notes for SCM-06 / SCM-07

- SCM-06 extends `SourceControlController.cs` (its planned files list it).
  Reuse `TryResolveWithinWorktree` for containment before any path reaches
  Git; the `absolutePath` in status responses is already containment-checked,
  `null` means "do not operate on it".
- Grouping semantics available to action components: staged = entry with
  `indexChange != "Unmodified"` (and not untracked/ignored/conflicted),
  unstaged = `workingTreeChange != "Unmodified"`; `statusCharacters` preserves
  the raw `XY` pair.
- After a mutation, call `GET /api/source-control/status` again — it always
  re-derives (no cache). The pane reconciles selection automatically: kept if
  the path is still changed, otherwise cleared via `onSelectionChange(null)`.
- Frontend row contract: `data-testid="source-control-entry-${path}"`,
  `aria-selected`, `source-control-section-{staged|unstaged|untracked|conflicts}`.
  SCM-06's `ChangeActions.tsx` should keep the existing branch-control-free
  assertions green.
- Wire strings are enum names (`Modified`, `Untracked`, `BothModified`, …);
  `sourceControlModels.ts` mirrors them — extend it rather than duplicating
  types.
- Typed failures: `failure.errorKind` uses the SCM-01 `GitProcessErrorKind`
  names (e.g. `GitLockContention`, `GitNotInstalled`); surface them verbatim.

## Next eligible sprint(s)

- **SCM-06 (Git Stage & Unstage)** — dependencies SCM-02 + SCM-03 are now
  DONE; its SPRINT-STATUS row still reads BLOCKED and needs the usual
  unblock/dependency flip before starting (per the sprint hard rule).
- SCM-04 / SCM-05 remain READY and untouched by this sprint.
- SCM-16 remains READY.

## Security & file-safety gate

- [x] **No user file was mutated through an unreviewed/bypass path** — the
      controller performs read-only SCM-01 invocations only (`rev-parse`,
      `symbolic-ref`, `status`; `--no-optional-locks`); no writes, moves, or
      deletions anywhere in the new code.
- [x] **No arbitrary shell command can be injected through a filename/path** —
      all Git runs go through the SCM-01 controlled runner with structured
      argument passing (no shell); paths are only ever query values or
      Git-reported data; spaces/Unicode/metacharacter paths are test-covered.
- [x] **No repository escape is possible through path handling** —
      `TryResolveWithinWorktree` canonicalizes and containment-checks every
      Git-reported path against the worktree root; escaping paths yield `null`
      (`absolutePath`/`documentId` are never exposed for them). Blank/absent
      `path` is a typed 400.
- [x] **No secret/credential output is exposed in UI logs** — no logging was
      added; failure payloads carry only Git-provided text and typed kinds; the
      SCM-01 environment policy (prompts disabled, closed stdin) still governs
      every invocation.
- [x] **No destructive action silently broadened its scope** — no mutating
      action exists in this sprint; branch-management routes are absent
      (test-pinned 404s) and the pane contains no action controls.
- [x] **Relevant higher-level safety rules remain stronger than convenience** —
      local filesystem stays the source of truth; Git stays the sole authority
      for change state (nothing persisted); the pane is read-only; branch
      lifecycle stays out of the feature.
