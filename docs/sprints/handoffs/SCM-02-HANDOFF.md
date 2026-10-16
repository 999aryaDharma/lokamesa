# Sprint Handoff — SCM-02

**Status:** DONE
**Branch:** `feat/git-repository-worktree-status-domain`
**Commits:** (see git log for `feat(scm): git-repository-worktree-status-domain`)

## What changed

- **Repository/worktree discovery (Step 1)** — `GitRepositoryService.DiscoverAsync(path)`
  derives all state through Git itself (`git rev-parse --show-toplevel --git-dir
  --git-common-dir` + `rev-parse --verify --quiet HEAD` + `symbolic-ref --short
  -q HEAD` + `rev-parse --abbrev-ref --symbolic-full-name @{upstream}`):
  - works from a worktree root, a nested directory, or an active-document file path;
  - a path outside any worktree yields typed `GitRepositoryStatus.NoRepository`
    (a valid non-Git Document IDE source), never an exception or silent fallback;
  - missing Git → `GitUnavailable`; unusable path → `Failed`; both carry the
    originating typed `GitProcessResult` for inspection;
  - linked worktrees resolve `WorktreeRoot` (linked path) and `RepositoryRoot`
    (common-dir owner / main repo) separately — verified by test.
- **Porcelain v2 parsing (Step 2)** — new pure-Core `GitPorcelainV2Parser`
  for `git status --porcelain=v2 -z --branch`:
  - covers type-1 ordinary, type-2 rename/copy (with `OriginalPath`), type-`u`
    unmerged, `?` untracked, `!` ignored records and all `# branch.*` headers
    (`branch.oid` incl. `(initial)`, `branch.head` incl. `(detached)`,
    `branch.upstream`, `branch.ab +N -M`);
  - NUL-terminated `-z` wire format only; paths are never quoted, spaces and
    Unicode survive verbatim; structurally malformed input throws
    `FormatException` (converted by the service into a typed `Failed` result).
- **Typed domain records (Step 3)** — `GitStatusEntry` keeps index side
  (`IndexChange`) and working-tree side (`WorkingTreeChange`) separate, so
  staged and unstaged state for the same file stays distinguishable
  (`MM` etc.); `GitFileState` provides the platform grouping vocabulary;
  `GitConflictKind` classifies the seven porcelain v2 unmerged combinations;
  `GitStatusSnapshot` carries branch/HEAD/upstream/ahead-behind. Nothing is
  written to SQLite — Git remains the only authority (spec 13 §4.2/§24).
- **Debounced watcher integration (Step 4)** — `GitRepositoryService` can be
  constructed with the FS-03 `FileChangeCoordinator` and then
  `StartWatching(worktreeRoot)` / `StopWatching()`:
  - the coordinator callback is a hint only: it filters by worktree and resets
    a debounce timer (default 300 ms, injectable); **Git never runs on the
    watcher callback path** (spec 13 §17);
  - one burst coalesces into exactly one refresh; `CurrentStatus` +
    `OnStatusChanged` publish every completed refresh (including typed
    failures); `RefreshAsync()` pulls one refresh explicitly and returns
    `null` when no worktree is watched instead of guessing.
- **Typed failures** — `GitProcessErrorKind`-mapped (`GitRepositoryNotFound`,
  `GitNotInstalled`, …), parse errors → `Failed` with an inspectable message;
  a repository that disappears between refreshes yields a typed
  `NoRepository` result (test-covered).

## Files changed

Production:

- `src/DocumentIDE.Core/SourceControl/GitRepositoryContext.cs` (new — discovery result + `GitRepositoryStatus`)
- `src/DocumentIDE.Core/SourceControl/GitStatusEntry.cs` (new — status domain records)
- `src/DocumentIDE.Core/SourceControl/GitPorcelainV2Parser.cs` (new — **extra file, disclosed below**)
- `src/DocumentIDE.Infrastructure/SourceControl/GitRepositoryService.cs` (new — discovery, status, debounced watching)

Tests:

- `tests/DocumentIDE.IntegrationTests/SourceControl/GitStatusTests.cs` (new, 26 tests)
- `tests/DocumentIDE.IntegrationTests/SourceControl/TestGitRepository.cs` (modified — additive `RunGitCapture` helper only; nothing else in the shared fixture changed)

**Extra file beyond the planned list (disclosed):** `GitPorcelainV2Parser.cs` is
the pure-Core, IO-free parser for the porcelain v2 `-z` format. It is split out
because the sprint's own Step 2 demands deterministic parsing (Red→Green on its
own theory tests) and because SCM-03/04/05/14 consume the same format on their
way in. No behavior outside SCM-02 was added. No parallel-lane files
(`GitWorktreeService.cs`, `GitWorktreeDiscoveryTests.cs`) were created or modified.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitStatusTests"` → **PASS (26/26, 0 skipped; stable across 4 consecutive runs of the timing-sensitive debounce tests)**
- `dotnet test tests/DocumentIDE.IntegrationTests` → **562/564 passed** (2 failures are the pre-existing REL-03-sanctioned release-artifact checks: `ScriptTests.Release_Build_Embeds_Production_Web_Assets`, `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — `artifacts/` absent in a fresh worktree; identical to the SCM-01/REL-03 baseline, +26 new passing tests = exactly the SCM-02 additions)
- `dotnet test tests/DocumentIDE.SecurityTests` → **PASS (91/91)**
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **PASS (12/12; includes `SourceControlProcessBoundaryTests`)**
- `dotnet test tests/DocumentIDE.Core.Tests` → **PASS (459/459)** (Core was touched)
- `dotnet test DocumentIDE.sln` (repository verification) → **PASS overall: Architecture 12/12, Performance 3/3, Core 459/459, Security 91/91, Golden 39/39, Safety 24/24, Soak 6/6, Integration 562/564** (same 2 documented REL-03 failures; every other suite identical to the SCM-01 baseline)

TDD evidence (Red → Green → Refactor per step):

1. Step 1 RED: `Repository_Worktree_Discovery_Fixtures_Pass` failed with
   `CS0246`/`CS0103` (Core types missing) → GREEN: 5/5 discovery tests.
2. Step 2+3 RED: `CS0246` for `GitStatusEntry`/`GitStatusSnapshot`/`GitConflictKind`
   → GREEN: 22/22 (status + parser theory tests).
3. Step 4 RED: compile-time failures for the missing watching API
   (`CS1729` ctor, `CS1061` for `StartWatching`/`CurrentStatus`/`OnStatusChanged`/
   `StopWatching`/`RefreshAsync`, `CS1674` IDisposable) → GREEN: 26/26.
4. Refactor micro-cycle: pinned untracked/ignored `StatusCharacters` as
   `??`/`!!` (assertion RED → parser GREEN), doc comments tightened; full
   suites re-run after the change.

## Acceptance criteria

- [x] **Git status is derived from Git, not a parallel database** — all state
      comes from SCM-01 runner invocations (`DiscoverAsync`, `GetStatusAsync`),
      parsed into transient records; no SQLite writes anywhere in the new code
      (grep-verifiable: no repository/db types referenced). Evidence:
      `Status_*`, `Parser_*`, `FilesystemEvent_*` tests.
- [x] **Status distinguishes index changes from working-tree changes** —
      `GitStatusEntry.IndexChange` / `WorkingTreeChange` + `HasStagedChange` /
      `HasWorkingTreeChange`; pinned by
      `Status_Distinguishes_IndexChanges_From_WorkingTreeChanges_ForTheSameFile`
      (`MM`) and the full state-matrix test.
- [x] **Filesystem events trigger debounced status refresh** —
      `FilesystemEvent_Triggers_DebouncedStatusRefresh_OutsideTheWatcherCallback`
      (one burst → exactly one refresh, no Git synchronously in the callback),
      `StopWatching_Stops_DebouncedStatusRefresh`,
      `RefreshAsync_RefreshesWatchedWorktree_WithoutFilesystemEvent`,
      `RefreshAsync_WithoutWatchedWorktree_ReturnsNull_InsteadOfGuessing`.

Required tests from the sprint doc:

- [x] Repository/worktree discovery fixtures pass — first failing test
      (`Repository_Worktree_Discovery_Fixtures_Pass`); now covers repo root,
      nested dir, file path, linked worktree (repo root ≠ worktree root), and
      typed failures (`NonGitFolder_*`, `Discovery_MissingGitExecutable_*`,
      `Discovery_MissingDirectory_*`).
- [x] Porcelain v2 status parser covers staged, unstaged, untracked, and
      conflict states — end-to-end fixture tests (real `git status` output on
      a repo with merge conflict, staged/unstaged/untracked/rename states) plus
      parser theory tests for every record type and all seven unmerged
      combinations.
- [x] Non-Git folders return a typed no-repository state —
      `NonGitFolder_Discovery_Returns_TypedNoRepositoryState`,
      `Status_NonGitFolder_Returns_TypedNoRepositoryState`.

## Deviations / spec conflicts

- **Debounce composition point:** the debounce pipeline is integrated inside
  `GitRepositoryService` (optional `FileChangeCoordinator` constructor
  parameter), not through `Program.cs` DI wiring. `Program.cs` was deliberately
  left untouched to avoid conflicts with the parallel SCM-15 lane. SCM-03 must
  register the service and pass the coordinator when it starts consuming
  background refresh (noted below). No spec requirement was weakened: the
  behavior required by Step 4 is implemented and test-proven.
- **`--ignored` is not requested** in the status invocation (spec 13 §6 lists it
  only where ignored display is required); the parser still fully handles `!`
  records, so enabling it later is a one-argument change.
- **Unknown unmerged `XY` combinations** stay conflicted
  (`GitFileState.Conflicted`) with `GitConflictKind.None` and the raw
  `StatusCharacters` preserved — lenient inspectability instead of dropping or
  guessing; pinned by `Parser_UnknownUnmergedCombination_*`.
- **`StatusCharacters` convention:** porcelain v2 `?`/`!` records carry no `XY`
  pair; the entry exposes the `git status --short` convention `??`/`!!` for
  inspectability (pinned by test).
- **`TypeChanged`** was added to the state vocabulary beyond the sprint's
  minimum list because `T` is a real porcelain v2 state; it is fully covered.
- **Sprint doc header** still says `Status: BLOCKED`; per the task instruction
  only the `SPRINT-STATUS.md` row is updated (BLOCKED → DONE).

## Risks discovered

- **Consumer contract — mutation queue key (from SCM-01):** index-mutating
  invocations must pass the true worktree root (`GitRepositoryContext.WorktreeRoot`)
  as the runner working directory; passing a subdirectory would bypass
  per-worktree serialization. Discovery is the sanctioned way to obtain it.
- **Consumer contract — status paths:** entries are worktree-root-relative with
  forward slashes, exactly as Git reported them; `GitStatusSnapshot.WorktreeRoot`
  is the base for resolving them (verified from a nested directory and from a
  linked worktree created outside the main repo — Git normalizes to the called
  worktree root, not the repository root). SCM-04/05 must combine with the
  worktree root and treat paths as untrusted inputs (no shell; `--` before
  paths).
- **One writer per worktree at a time:** `RefreshAsync` may be called
  concurrently (timer + explicit). Last-writer-wins on `CurrentStatus`;
  serialization of refreshes was not required by this sprint. SCM-03/05 should
  call `RefreshAsync` rather than reading `CurrentStatus` mid-refresh if they
  need a guaranteed-fresh snapshot per pane action.
- **Watcher path filter is containment-only** (worktree boundary); the FS-03
  coordinator already filters temporary/lock files and document extensions, so
  no duplicate filtering was added in SCM-02.

## Next eligible sprint(s)

- SCM-03 (Source Control Changes Pane) — **unblocked** (SCM-02 + FND-07 DONE).
  Consumer notes: register `GitRepositoryService` in `Program.cs`; pass the
  `FileChangeCoordinator` so pane refresh is debounced; call
  `DiscoverAsync(workspaceOrActiveDocumentPath)` and only
  `StartWatching(context.WorktreeRoot)` when `HasRepository`; render grouping
  from `GitStatusEntry.HasStagedChange` / `HasWorkingTreeChange` / `IsUntracked`
  / `IsConflicted` (`State` for the file state); `OnStatusChanged` carries the
  typed `GitStatusResult` (show typed failure states, never a silent empty list).
- SCM-04 (Markdown Git Diff) — **unblocked** (SCM-02 + MD-01 DONE); consume
  `GitStatusEntry.Path` + snapshot `WorktreeRoot`/`RepositoryRoot`.
- SCM-05 (DOCX Document-Aware Git Diff) — **unblocked** (SCM-02 + IDX-02 +
  DOCX-01 DONE).
- SCM-15 was already eligible before this sprint (untouched parallel lane).
- SCM-11 (Git Remote State & Fetch) becomes eligible once SCM-07 lands; its
  remote-facing inputs (branch name, upstream, ahead/behind from the snapshot)
  are already parsed here.

## Security & file-safety gate

- [x] **No user file was mutated through an unreviewed/bypass path** — SCM-02
      performs read-only Git invocations only (`rev-parse`, `symbolic-ref`,
      `status`; `--no-optional-locks` keeps even optional index locks off);
      nothing writes/moves/deletes user files; no SQLite state duplicated.
- [x] **No arbitrary shell command can be injected through a filename/path** —
      all invocations go through the SCM-01 controlled runner (structured
      `ArgumentList`, no shell); SCM-02 adds no process spawning of its own
      (architecture test enforces spawning stays in `Infrastructure/SourceControl`);
      status parsing treats Git output as data (paths are string values, never
      executed); spaces/Unicode metacharacter fixtures pass unmangled.
- [x] **No repository escape is possible through path handling** — discovery
      returns Git-canonicalized roots; `StartWatching` canonicalizes and
      contains events to the watched root; `RefreshAsync` never guesses a
      repository and returns `null` without a watched worktree.
- [x] **No secret/credential output is exposed in UI logs** — no logging added;
      failure messages carry Git-provided text and are surfaced only as typed
      records; the SCM-01 environment policy (prompts disabled, closed stdin)
      still governs every invocation.
- [x] **No destructive action silently broadened its scope** — no destructive
      action exists in this sprint; status never mutates and watcher integration
      only *reads* Git state.
- [x] **Relevant higher-level safety rules remain stronger than convenience** —
      local filesystem remains source of truth; Git is not duplicated into the
      database; no Git runs inside the watcher callback (the exact rule the
      debounce exists to satisfy); typed failure states replace silent fallbacks.
