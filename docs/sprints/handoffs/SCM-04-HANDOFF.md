# Sprint Handoff — SCM-04

**Status:** DONE
**Branch:** `feat/markdown-git-diff`
**Commits:** (see git log for `feat(scm): markdown-git-diff`)

## What changed

- **File-version materialization (Step 1)** — `GitDiffService.GetFileDiffAsync`
  materializes both compared versions **read-only**:
  - HEAD side: `git show HEAD:<path>` (index side: `git show :<path>`), invoked
    through the SCM-01 controlled process runner with structured argument
    lists, `--no-optional-locks`, and the true worktree root as working
    directory;
  - working-tree side: a direct file read (strict UTF-8);
  - **nothing is ever written**: no temp file, no index touch. A dedicated test
    proves the `.git/index` bytes, the worktree bytes and
    `git status --porcelain=v2` are bit-identical before/after the diff calls
    while a foreign `.git/index.lock` is held — the diff does not need the
    lock at all (spec 13 §18/§20).
- **Textual diff rendering (Step 2)** — new pure-Core
  `GitUnifiedDiffFormatter` (hand-rolled; **no new packages**):
  - Myers shortest-edit-script over the region between common prefix/suffix;
  - git/GNU unified conventions, all probe-verified against real git before
    being pinned in tests: 3 context lines, hunks merged when the unchanged gap
    is ≤ 2·context, count of 1 elided (`@@ -1 +1 @@`), empty side
    `@@ -0,0 +1,N @@` / `@@ -1,N +0,0 @@`, `/dev/null` for an absent version,
    `\ No newline at end of file` markers, a final line without newline never
    compares equal to the same line with newline;
  - an 8 M-cell trace-memory guard degrades pathologically large dissimilar
    inputs to a **correct coarse whole-region replacement** — never truncated,
    never fabricated.
- **Staged vs working-tree targets (Step 3)** — `GitDiffTarget`:
  - `Staged` = HEAD ↔ index (`git diff --cached` semantic; labels "HEAD"/"index"),
  - `WorkingTree` = HEAD ↔ working tree (`git diff HEAD` semantic; labels
    "HEAD"/"working tree"),
  - `Unstaged` = index ↔ working tree (`git diff` semantic; labels
    "index"/"working tree").
  Each pair is verified to show only its own change set (three-way staged /
  worktree fixture test).
- **Edge-case fixtures (Step 4)** — untracked file (complete addition, both
  working-tree and unstaged targets), deleted working-tree file (complete
  removal), binary content (NUL byte **or** disk-side strict-UTF-8 decode
  failure → typed `IsBinary`, no pretended text diff, spec 13 §9.3), no-trailing
  newline (marker after the affected side), Unicode path + content end-to-end,
  leading-dash filename (treated purely as a path, never an option), staged
  rename with/without `originalPath`.
- **Typed outcomes (spec 13 §20)** — `GitDiffStatus`:
  `Available` / `NoRepository` / `GitUnavailable` / `PathRejected` / `Failed`,
  each failure carrying the originating typed `GitProcessResult`. Unmerged
  index (`git show :path` → *"is in the index, but not at stage 0"*) maps to
  `Failed` with `ErrorKind = GitConflict` and an explicit message; the
  working-tree diff of a conflicted file stays `Available` and shows conflict
  markers (spec 13 §21).
- **Path safety** — requested paths are validated as worktree-relative document
  paths (`GitStatusEntry.Path` convention: forward slashes, no rooted/drive
  form, no `.`/`..`/empty segments, no `:`) **before any Git or file access**,
  then containment is re-checked on the combined `Path.GetFullPath` result via
  `Path.GetRelativePath` (which also covers worktrees rooted at a drive root,
  e.g. `D:\`, where naive prefix comparison would false-reject every
  document). A table-driven test attacks with `../`, `..\`, `/etc/passwd`,
  `C:/…`, `docs//…`, `docs/plan.md:stream`, `.`, `..` — all rejected with
  `PathRejected` and zero repository access.
- **Frontend (planned file)** — `MarkdownGitDiff.tsx`, a purely presentational,
  strictly read-only review surface: renders the typed outcome (loading /
  typed failures with `role=alert` / Git-unavailable / no-repository / binary
  summary / empty / full diff), distinguishes the three version pairs, shows
  file header (`--- HEAD:path` … `+++ working tree:path`, `/dev/null` when a
  side is absent), per-hunk headers, per-line old/new gutters, `+`/`-`/context
  signs and the no-newline marker. File content and paths are rendered as
  **inert text** by React — no `dangerouslySetInnerHTML` anywhere — proven by
  an injection test (no `<img>`/`<script>` element survives; markup appears
  literally) and a read-only test (no buttons/inputs/links).

## Files changed

Planned files (as in the sprint document):

- `src/DocumentIDE.Infrastructure/SourceControl/GitDiffService.cs` (new)
- `src/DocumentIDE.Web/src/features/source-control/MarkdownGitDiff.tsx` (new)
- `tests/DocumentIDE.IntegrationTests/SourceControl/MarkdownGitDiffTests.cs` (new, 23 tests)

**Extra files beyond the planned list (disclosed):**

- `src/DocumentIDE.Core/SourceControl/GitDiffContracts.cs` (new) — the typed
  contracts (`GitDiffTarget`, `GitDiffStatus`, `GitDiffLineKind`, `GitDiffLine`,
  `GitDiffHunk`, `GitDiff`, `GitDiffResult`). It is a separate Core file for the
  same reason SCM-01/SCM-02 split domain contracts out of their services: the
  formatter and the frontend view model consume these types without taking an
  Infrastructure dependency, and the architecture tests keep Git process
  spawning in Infrastructure.
- `src/DocumentIDE.Core/SourceControl/GitUnifiedDiffFormatter.cs` (new) — the
  pure, IO-free Myers/unified formatter; it is separately unit-tested (7 pure
  formatter tests pin the git conventions without a repository) and keeps
  `GitDiffService` an orchestration layer.

No parallel-lane file was created or modified: `sourceControlModels.ts`,
`SourceControlPanel.tsx`, `SourceControlController.cs`,
`GitWorktreeMutationService.cs`, `WorktreeCreateDialog.tsx` untouched;
`Program.cs` untouched; `TestGitRepository.cs` untouched.

**CAN-05 colocated test mapping (disclosure):** the frontend contract test
lives colocated at
`src/DocumentIDE.Web/src/features/source-control/MarkdownGitDiff.test.tsx`
(new, 13 tests), following the CAN-05 ruling precedent already applied by
SCM-15 (`WorktreeList.test.tsx`) — the repo's vitest setup is rooted in
`src/DocumentIDE.Web` and `tests/frontend/**` is not wired into it.

Docs:

- `docs/sprints/handoffs/SCM-04-HANDOFF.md` (new)
- `docs/sprints/SPRINT-STATUS.md` (row SCM-04 only, BLOCKED → DONE)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~MarkdownGitDiffTests" --nologo` → **PASS (23/23, 0 skipped)**
- `dotnet test tests/DocumentIDE.IntegrationTests --nologo` → **601/603 passed** — the 2 failures are exactly the pre-existing REL-03-sanctioned release-artifact checks (`ScriptTests.Release_Build_Embeds_Production_Web_Assets`, `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`): they assert `artifacts/desktop/**` and `artifacts/installer/DocumentIDE-Setup.msi` exist, `artifacts/` does not exist in a fresh worktree, and this diff touches neither `scripts/` nor `artifacts/`. Identical to the documented SCM-01/SCM-02 baseline (SCM-02 handoff recorded the same two).
- `dotnet test tests/DocumentIDE.SecurityTests --nologo` → **PASS (91/91)**
- `dotnet test tests/DocumentIDE.ArchitectureTests --nologo` → **PASS (12/12)** — includes `SourceControlProcessBoundaryTests` (frontend-forbidden-substring scan + Core/Infrastructure process boundary) with the new `.tsx` in scope.
- `dotnet test tests/DocumentIDE.Core.Tests --nologo` → **PASS (459/459)** (Core was touched)
- `dotnet test DocumentIDE.sln --nologo` (repository verification) → **all suites green except the same 2 REL-03 release-artifact integration checks**: Architecture 12/12, Core 459/459, Security 91/91, Safety 24/24, Golden 39/39, Soak 6/6, Performance 3/3, Integration 601/603 (same 2 documented failures; solution exit code 1 mirrors the SCM-01/SCM-02 baseline).
- Frontend (`src/DocumentIDE.Web`): `npm install` → ok; `npx tsc -b` → **exit 0**; `npx oxlint` → **exit 0, no findings in new files**; `npx vitest run` → **PASS (43 files / 259 tests; 13 of them new)**, including the new `MarkdownGitDiff.test.tsx` 13/13.

TDD evidence (Red → Green → Refactor, with honesty notes):

1. **Backend Step 1 RED:** the required first test
   `WorkingTree_HeadToWorkingTreeDiff_MatchesFixture` was written first and
   failed with `CS0246`/`CS0103` (contracts/service missing). One minimal GREEN
   pass followed (contracts + formatter + service) → 1/1. *Honesty note:* this
   first GREEN pass was larger than the later increments because the required
   production file set had to compile at once; it did not include any test
   beyond the first one.
2. **Backend Steps 2–4 RED:** the suite was then expanded to 23 tests before
   the corresponding fixes; the run produced a genuine **16 passed / 7 failed**
   state. Failure causes were real: (a) missing-path detection only recognized
   `does not exist in 'HEAD'` and not `exists on disk, but not in 'HEAD'`
   (untracked/staged-new/renamed files returned `Failed` instead of
   `Available`) — implementation fix; (b) 4 formatter tests expected hunk text
   without the file-header lines and the Unicode test expected the wrong hunk
   window — test-expectation fixes after re-verifying the implementation output
   against the git conventions (the implementation was correct). Re-run → 23/23.
   *Honesty note:* because steps 2–4 behaviors were already implemented when
   their tests were expanded, those 16 previously-written tests act as
   regression pins; the 7 red tests above are the ones that genuinely drove
   changes.
3. **Frontend RED:** `MarkdownGitDiff.test.tsx` (13 tests) was written first and
   failed with *"Failed to resolve import ./MarkdownGitDiff"*; the component
   was then implemented → 13/13 GREEN; two test matcher expectations were
   corrected after the first GREEN run (sign-prefixed row text) and the full
   frontend suite re-run.
4. **Refactor:** `tsc -b` + `oxlint` clean; the containment helper was
   refactored from prefix comparison to `Path.GetRelativePath` containment so a
   worktree rooted at a drive root is no longer false-rejected (self-review
   finding; the escape-attack tests pin that escaping paths are still rejected,
   and the full suites were re-run). Results above.

## Acceptance criteria

- [x] **User can inspect Markdown changes before commit** — all three version
      pairs are materialized and rendered with structured hunks + full unified
      text: `WorkingTree_HeadToWorkingTreeDiff_MatchesFixture`,
      `Staged_HeadToIndexDiff_MatchesStagedFixture_AndUnstagedIsEmptyWhenInSync`,
      `UnstagedAndWorkingTreeTargets_ShowOnlyTheirOwnVersionPair`, plus 13
      component tests (incl. staged HEAD↔index rendering).
- [x] **Diff never writes to repository files** —
      `Diff_IsReadOnly_AndRemainsAvailableWhileAnIndexLockFileExists`: byte
      equality of `.git/index`, the worktree file and `git status` before/after;
      succeeds while `.git/index.lock` is held by a foreign process; service
      code performs only `git show`/`rev-parse --verify` + file reads.
- [x] **Staged and unstaged diff targets are correct** — three-way fixture test
      (`Staged_…` + `UnstagedAndWorkingTreeTargets_…`) pins that the staged
      pair (HEAD↔index), the unstaged pair (index↔working tree) and the full
      working-tree pair (HEAD↔worktree) each show only their own change set.

Required tests from the sprint document:

- [x] **HEAD↔working-tree diff matches fixture** — the first failing test
      (`WorkingTree_HeadToWorkingTreeDiff_MatchesFixture`).
- [x] **HEAD↔index diff matches staged fixture** —
      `Staged_HeadToIndexDiff_MatchesStagedFixture_AndUnstagedIsEmptyWhenInSync`.
- [x] **Unicode filenames/content are rendered safely** —
      `UnicodeAndSpecialCharacterPathAndContent_AreDiffedWithoutMangling`
      (backend, real repo with `docs/notes café 日本語 (v2) & more.md`) + the
      component Unicode and HTML-injection-as-text tests.

## Deviations / spec conflicts

- **Diff command mapping (spec 13 §9.4).** §9.4 maps product diff actions to
  `git diff --cached -- <paths>` / `git diff -- <paths>`. This sprint instead
  materializes the compared versions with `git show HEAD:<path>` /
  `git show :<path>` and renders the diff in-process with a tested, pure
  formatter. Rationale: the sprint's own Required Tests demand a deterministic
  fixture-pinned diff of exactly the HEAD/index/worktree versions, the
  read-only acceptance criterion is best proven without the index-lock
  interaction, and structured hunks (not raw text) are what SCM-14/17 consume.
  The formatter conventions were probe-verified against real `git diff` output.
  No product behavior is weakened; the version identity is pinned by object
  name at call time.
- **CRLF normalization.** Line comparison strips one trailing `\r` per line, so
  a pure line-ending change (CRLF↔LF with identical text) renders as no textual
  change. Deliberate review-noise reduction for Markdown; it never hides a
  content change. Raw git without `autocrlf` would show such a file as fully
  changed.
- **`originalPath` for renames/copies.** When the requested path does not exist
  in HEAD and the caller passes `GitStatusEntry.OriginalPath`, the HEAD side is
  materialized from the original path; the file header still uses the new
  path. Without `originalPath` a staged rename renders as a full addition —
  both behaviors pinned by test.
- **Unborn HEAD.** `rev-parse --verify --quiet HEAD` exit 1 with empty stderr is
  treated as "no HEAD version": every file diffs as an addition instead of
  failing.
- **`Program.cs` untouched** (per task constraints, following the SCM-02
  precedent). `GitDiffService` is not yet registered in DI/endpoints; SCM-03
  (controller wiring) / SCM-14 (conflict shell) must wire it.
- **Sprint doc header** still says `Status: BLOCKED`; only the
  `SPRINT-STATUS.md` row is updated (BLOCKED → DONE), per the task instruction
  and SCM-02 precedent.

## Risks discovered

- **Git stderr wording coupling.** Absent-side detection matches the pinned
  English signatures (`does not exist in '…'`, `exists on disk, but not in '…'`,
  `does not exist (neither on disk nor in the index)`, `… not at stage 0`) with
  `LC_ALL=C` pinned by the SCM-01 runner. If a future Git changes the wording,
  the side degrades to a typed `Failed` result (never a silent wrong diff).
- **Symlink/reparse documents are not special-cased.** The disk side follows a
  symlink and reads the target content, while `git show` returns the link blob.
  Read-only, so no write-through risk, but a symlinked document may diff
  surprisingly. V1 documents are regular files; no link handling was added.
- **Non-UTF-8 text.** The disk side is decoded strictly (invalid UTF-8 → typed
  binary), the git-materialized sides are decoded by the runner (leniently), so
  a non-UTF-8 blob whose working-tree file is valid UTF-8 could show U+FFFD
  replacement characters on the git side. No real-world Markdown case observed.
- **Trace-budget fallback** produces a coarse (yet correct and complete) diff
  for extremely large dissimilar regions (> 8 M Myers cells); never truncates.
- **Rename detection is caller-driven.** Consumers must pass
  `OriginalPath` from `GitStatusEntry` for staged renames; otherwise a rename
  shows as delete+add-style full addition of the new path.
- **Verification note (honesty):** one transient extra integration-test failure
  was observed during one verification run and did not reproduce in three
  subsequent full runs (each showed exactly the two documented REL-03
  release-artifact failures; the failure name was not captured, a load/timing-
  sensitive existing test is suspected). All SCM-04 tests were green in every
  run, including the filtered 23/23 and component 13/13.

## Next eligible sprint(s)

- SCM-06 (Git Stage & Unstage) remains blocked on SCM-03; SCM-14 (Git Conflict
  State & Resolution Shell) remains blocked on SCM-12 + SCM-05; SCM-17
  (Source Control Regression & Release Hardening) lists SCM-04 — its SCM-04
  dependency is now satisfied but the other listed sprints are not.
- **Consumer notes for SCM-14 (recorded as required):**
  - conflicted file, staged target → `GitDiffStatus.Failed`,
    `Failure.ErrorKind == GitProcessErrorKind.GitConflict`, message contains
    "unmerged" (test-pinned);
  - conflicted file, working-tree target → `Available` and the rendered text
    contains the `<<<<<<<`/`=======`/`>>>>>>>` conflict markers (test-pinned);
  - inputs: `GitStatusEntry.Path` (worktree-root-relative, forward slashes) +
    `GitRepositoryContext.WorktreeRoot`, plus optional
    `GitStatusEntry.OriginalPath` for staged renames;
  - the service is constructed with an `IGitProcessRunner` and is not yet in
    DI (SCM-03/14 wiring).
- **Consumer notes for SCM-03/17:** use `GitDiffResult.IsAvailable`, render
  `Diff.UnifiedText` or the structured `Diff.Hunks`/`GitDiffLine` records
  (old/new line numbers, `NoNewlineAtEndOfFile`); treat every non-`Available`
  status as a typed failure to surface, never as "no changes"; binary diffs
  have `IsBinary == true` and empty hunks (summary only).

## Security & file-safety gate

- [x] **No user file was mutated through an unreviewed/bypass path** — the
      service performs only read operations (`git show`, `rev-parse --verify`,
      file reads) and never creates temp files; the read-only test byte-compares
      index/worktree/status before/after and passes while a foreign
      `.git/index.lock` is held (no lock is even needed).
- [x] **No arbitrary shell command can be injected through a filename/path** —
      all invocations go through the SCM-01 runner (structured argument lists,
      no shell); paths are validated before use and Git object names are
      `HEAD:<path>`/`:<path>` with `:` rejected in inputs; the leading-dash
      filename test proves names are never parsed as options; the architecture
      process-boundary test passes with the new frontend file in scope.
- [x] **No repository escape is possible through path handling** — rooted,
      drive-qualified, `:`-containing, `.`/`..`/empty-segment paths are rejected
      with `PathRejected` before any access, and containment is re-checked on
      the combined absolute path; the escape-attack table test covers `../`,
      `..\`, absolute Unix/Windows paths, doubled separators and `:stream`
      forms.
- [x] **No secret/credential output is exposed in UI logs** — no new logging;
      typed failures carry short explanatory messages (git stderr text for
      `Failed`); the component shows only the typed message and diff content;
      SCM-01's prompt-disabled/closed-stdin environment policy still governs
      every Git invocation.
- [x] **No destructive action silently broadened its scope** — this sprint has
      no destructive action at all (not even stage/unstage); the frontend
      surface is read-only (test asserts no buttons/inputs/links).
- [x] **Relevant higher-level safety rules remain stronger than convenience** —
      local filesystem stays the source of truth (nothing is written anywhere,
      Git or disk); versions are pinned by object name at call time (no
      floating refs); exact string comparison only (no fuzzy/semantic
      matching); typed failures replace silent fallbacks; the DOCX deep-edit
      boundary is untouched.
