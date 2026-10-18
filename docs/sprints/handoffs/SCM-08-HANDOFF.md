# Sprint Handoff — SCM-08

**Status:** DONE
**Branch:** `feat/git-commit-history-file-history`
**Commits:** `94dcc15`, `a5f104f`, `4e1c98b`, the handoff commit, plus the post-review fix-up commit at the tip — see `git log --oneline f2faa1b..HEAD` (base `f2faa1b`)

## What changed

- New `GitHistoryService` (Infrastructure) implements the two history reads and the
  version-inspection read against real Git (spec 13 §9.4/§12/§18/§19/§20/§23/§25):
  - `GetHistoryAsync` → `git log` over the worktree, newest first,
  - `GetFileHistoryAsync` → `git log -- <path>` (commits that touched one document),
  - `GetFileVersionAsync` → `git show <commit>:<path>` (the selected document at one commit).
  Every read goes through the SCM-01 `IGitProcessRunner` (structured `ArgumentList`, never
  a shell string) with the resolved true worktree root (`GitRepositoryService.DiscoverAsync`,
  SCM-02) as the working directory — a caller may pass a workspace folder **or** an
  active-document path.
- **Strictly read-only.** `log` and `show` are in the SCM-01 read-only command vocabulary,
  so they never take the per-worktree mutation queue and never touch the index, the working
  tree or `HEAD`. `HistoryAndVersionReadsAreStrictlyReadOnly` records every invocation and
  proves none is a mutating subcommand and that `HEAD`/`status` are unchanged; the version
  read adds `--no-optional-locks` (and captures bytes) so even the index lock is untouched.
  Git history is never rewritten and **no Document IDE history row is ever consulted**
  (spec 13 §4.2/§4.3/§12).
- **Structural Git framing, no text scraping** (spec 13 §23). The pinned invocation is
  `-c core.quotepath=false --no-pager log --no-color --format=%H%x00%h%x00%an%x00%ae%x00%cI%x00%s --name-only`
  (`--no-color` must follow `log`; leading it is rejected by Git with exit 129). The parser
  rule is structural: a line containing NUL starts a six-field commit record, and any other
  non-empty line is a changed-file name of the open record — so a subject can never blur a
  field boundary and a file name can never be mistaken for a commit. `%cI` keeps committer
  time as strict ISO-8601, never localized.
- **File history is literal and scoped** (spec 13 §12/§23). The path always travels after a
  `--` separator with `GIT_LITERAL_PATHSPECS=1` in the child environment. This is proven
  necessary: without it, a bracketed document such as `docs/notes[1].md` matches **both**
  itself and its glob-equivalent `docs/notes1.md` (`FileHistoryMatchesPathspecsLiterally`).
  No `--follow`: file history is Git's pathspec semantics, not rename tracking.
- **Pagination is Git's answer, not a guess** (spec 13 §25). Each page runs
  `--skip=<Skip> --max-count=<Limit+1>`; the extra commit's presence is `HasMore`, the page
  is trimmed back to `Limit`, and `NextSkip = Skip + Limit`. `GitHistoryQuery` bounds the
  window (`DefaultLimit = 50`, `MaxLimit = 200`); an out-of-range window is a typed HTTP 400
  rejection with **zero Git invocations**.
- **Typed, inspectable outcomes** (new Core contract `GitHistoryStatus`/`GitHistoryResult`/
  `GitFileVersionResult`): `Available` (possibly an empty page) / `NoRepository` /
  `GitUnavailable` / `InvalidRequest` / `PathRejected` / `NotFound` / `Failed`, never a
  silent fallback or an exception leaking out. `GitFailureClassificationsStayTypedAndInspectable`
  pins ten of the fourteen `GitProcessErrorKind` members through the passthrough — the ten with a
  read-only analogue (`None` is the success default, `GitNotInstalled`/`GitRepositoryNotFound` are
  pinned by the discovery theory, and `GitPullNotFastForward` has no read analogue) — and
  `GitDiscoveryClassificationsMapOntoTheirTypedHistoryOutcome` pins
  `GitNotInstalled → GitUnavailable` and `GitRepositoryNotFound → NoRepository`.
- **Unborn HEAD is a normal state, not a failure** (spec 13 §20). A fresh worktree with no
  commits makes `git log` fail; the service then runs the same exit-code probe SCM-05 uses
  (`rev-parse --verify --quiet HEAD`: exit 1 with empty stderr), and
  `GitErrorSignatures.IsUnbornHeadProbe` (SCM-08 addition) turns it into an `Available` **empty**
  page with the read's window echoed (`UnbornHeadRepositoryReportsAnEmptyHistoryPage`,
  `UnbornHeadFileHistoryIsAnEmptyPageNotAFailure`). The decision no longer scrapes Git's English
  stderr wording, so a Git message change cannot turn a normal state into a typed `Failed`.
- **Version inspection is bounded before the read** (spec 13 §9.3/§12/§25).
  `GetFileVersionAsync` validates the commit id first — only hex-only text of length 4..64 is
  accepted (`IsCommitIdentifier`), so `HEAD`, `main`, `--help`, `abc` and `zzzzzzzz` are typed
  `InvalidRequest`/400 rejections and never reach Git. Two `[Theory]` cases
  (`DocumentVersionRejectsNonCommitIdentifiersWithoutRunningGit`) plus
  `DocumentVersionAbsentAtTheSelectedCommitIsTypedNotFound` pin it. The service then probes the
  committed object's size with `git cat-file -s <commit>:<path>` and only runs the byte-capturing
  `git show` (`CaptureBinaryOutput = true`, so a DOCX never passes through a lossy text decode)
  when the size fits `MaxVersionBytes` (256 KiB, public const). A larger blob returns
  `IsTruncated = true`, `Content = null` and its true `SizeBytes`, and **no blob bytes are read at
  all** — a multi-gigabyte commit can neither exhaust memory nor trip the `byte[]` limit
  (`LargeDocumentVersionIsTruncatedWithoutReadingTheBlob`). Within the bound, a NUL byte — or a
  NUL-free blob that is not valid UTF-8 or is dense with control bytes — marks the blob binary
  (`Content = null`), so the pane never receives a garbage text decode.
- API (`SourceControlController`, same privileged localhost guard as SCM-03/SCM-06/SCM-07):
  `GET api/source-control/history`, `GET file-history`, `GET file-version`.
  Input rejection (`InvalidRequest`/`PathRejected`) ⇒ HTTP 400 `{errorCode, error}` with
  nothing run; every Git-reported state (`NotFound`/`NoRepository`/`GitUnavailable`/`Failed`)
  stays HTTP **200** typed so the pane renders it. Responses carry `source: "Git"` so Git
  history is a visibly separate authority from Document IDE History (spec 13 §4.3/§12).
  Missing `path` is rejected before the service runs.
- Frontend: new `GitHistoryPanel.tsx` keeps Git history **visually separate** — its heading
  is "Git History" with a persistent `Source: Git …` note. A scope toggle switches between
  the repository and the selected document (`This document`, enabled only when a document
  path exists), a `Load more` button appends the next window using the limit Git actually
  served (so there are no gaps), and an `Inspect version <shortId>` action renders the
  read-only version preview (text, truncation, or the binary notice). A typed non-success is
  surfaced in a `role="alert"` region, never rendered as an empty history. The component is
  not yet wired into `App.tsx` (Lane C owns that).

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Infrastructure/SourceControl/GitHistoryService.cs` — delivered as planned (new; history, file history, pagination, version read, `MaxVersionBytes`, `IsCommitIdentifier`, `BuildVersion`, `LooksBinary`).
- `src/DocumentIDE.Web/src/features/source-control/GitHistoryPanel.tsx` — delivered as planned (new).
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitHistoryTests.cs` — delivered as planned (new; two classes: `GitHistoryTests` **50** tests + `GitHistoryApiTests` **14** test cases = **64**; the fix-up round added four service tests).

Additional files needed to satisfy the sprint (mapping recorded):

- `src/DocumentIDE.Core/SourceControl/GitHistoryResult.cs` — **new** typed outcome contract
  (`GitHistoryStatus`, `GitHistoryQuery`, `GitHistoryEntry`, `GitHistoryPage`,
  `GitHistoryResult`, `GitFileVersion`, `GitFileVersionResult`), consumed by
  Infrastructure + API. Mirrors the SCM-02 `GitStatusResult` / SCM-06 `GitStageResult` /
  SCM-07 `GitCommitResult` Core placement.
- `src/DocumentIDE.Infrastructure/SourceControl/GitErrorSignatures.cs` — **one appended
  member** (`IsUnbornHeadProbe`); no existing signature touched.
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — extended in a bounded
  region: 5 DTOs, 3 `[HttpGet]` routes, `ToHistoryResponse`/`ToFileVersionResponse`/
  `ToHistoryCommit` mapping helpers, plus the `GitHistoryService` ctor parameter/field. No
  existing member reformatted; additions live in an SCM-08-marked block at the end of the
  class (kept clear of the SCM-09/SCM-11 sibling edit surface).
- `src/DocumentIDE.Api/Program.cs` — **append-only** DI singleton registration of
  `GitHistoryService` at the very end of the existing SCM block (after `GitCommitService`).
  No other line in the file was touched.
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` — `GitHistoryStatus`
  union, `SourceControlHistoryCommit`/`Page`/`Result`, `SourceControlFileVersionInfo`/`Result`.
- `src/DocumentIDE.Web/src/features/source-control/GitHistoryPanel.test.tsx` - 9 colocated
  component tests (CAN-05 batch-B ruling; the plan's `tests/frontend/**` path is stale —
  every prior feature component colocates its test).

`TestGitRepository.cs` was **not** modified: the fixture already supports the commit/author
needs of this sprint.

Lane safety (verified with `git diff --name-only f2faa1b..HEAD` → exactly the nine files
above): Lane B (`BatchPreviewController.cs`, Search/Share/Pdf controllers, `WebView/WebAppHost.cs`)
and Lane C (every `src/features/**` file except the two SCM-08 files above; `App.tsx`) were
**not touched**. `docs/sprints/SPRINT-STATUS.md`, `docs/quality/**`, `scripts/verify-release.ps1`,
`docs/sprints/sprint-manifest.json`, `01-dependency-graph.md`, `02-execution-waves.md` and
`00-sprint-index.md` were **not touched** (left to the controller). The board row SCM-08 → DONE
is reported for the controller to flip.

## TDD evidence

Backend cycles were RED-first; the mandated first behavior was

> Known fixture history renders correctly.

- Cycle 1 (commit list): RED confirmed by `dotnet build` (`CS0246` for the missing
  `GitHistoryResult`/`GitHistoryService` type) → GREEN minimal `GetHistoryAsync` +
  `--format` parser (`KnownFixtureHistoryRendersCorrectly`); then refactored.
- Cycle 2 (file history): RED — 6 failures — `FileHistoryFiltersToCommitsAffectingTheFile`,
  `FileHistoryRejectsEscapingOrAbsolutePathsWithoutRunningGit`,
  `FileHistoryWithoutAPathIsRejectedTypedWithoutRunningGit` → GREEN `git log -- <path>` with
  literal pathspec + containment rejection (8 tests).
- Cycle 3 (pagination): RED — 4 failures — `LargeHistoryLoadsIncrementally`,
  `HistoryQueryWindowIsRejectedTypedWithoutRunningGit` (4-case theory) → GREEN
  `--skip`/`--max-count` window + `HasMore`/`NextSkip` (13 tests).
- Cycle 4 (version inspection): RED confirmed by `dotnet build` (`CS0246` `GitFileVersion`/
  `GitFileVersionResult`, `CS1061` `GetFileVersionAsync`, `CS0117` `MaxVersionBytes`) → GREEN
  `GitFileVersion` + the four version tests (27 tests); refactor removed a redundant
  `DecodeText` helper.
- Cycle 5 (unborn HEAD): RED — 2 failures (`Expected: Available / Actual: Failed`) —
  `UnbornHeadRepositoryReportsAnEmptyHistoryPage`, `UnbornHeadFileHistoryIsAnEmptyPageNotAFailure`
  → GREEN via `GitErrorSignatures.IsUnbornHeadProbe` (29 tests).
- Cycle 6 (read-only discipline + failure vocabulary + hostile paths): RED introduced the
  read-only/classification/pathspec assertions → GREEN with 10-kind theory,
  discovery-classification theory, worktree-disappearance, literal/Unicode/space pathspecs
  (46 tests).
- Cycle 7 (API): RED confirmed by `dotnet build` (`CS0246` `SourceControlHistoryResponse` /
  `SourceControlFileVersionResponse` not found) → GREEN DTOs + routes + mapping + DI +
  `HistoryRoutesRejectMissingInvalidTokenAndHostileOrigin` (60 tests total; the API test class
  lives in the same `GitHistoryTests.cs` file, per the plan's single-artifact listing).
- Frontend: RED `GitHistoryPanel.test.tsx` failed on the missing `./GitHistoryPanel` import →
  GREEN `sourceControlModels.ts` types + `GitHistoryPanel.tsx` (8 tests). One test-authoring
  fix during this cycle: the "Load more" case initially reused a stale limit constant, so the
  append window was wrong — the fixture now uses distinct commit ids and the panel re-uses the
  limit Git served; no production change resulted.

Honest notes (recorded, not hidden):

- Cycle 1 and Cycle 6 introduced the pathspec/window arguments inside the shared
  `ReadHistoryAsync` at the same time as the first test that exercised them; most Cycle-6
  tests were **coverage-pinning** and passed on their first run (the read-only, classification
  and hostile-path behaviors came from the Cycle-1/2/3/4 code). Only the worktree-disappearance
  test genuinely failed first: on Windows Git marks loose objects read-only, so the fixture
  had to clear read-only attributes before deleting the worktree.

## Verification

- `dotnet build DocumentIDE.sln` → **Build succeeded, 0 errors** (warning output is the pre-existing
  style/analyzer/NU1603/NU1902/NU1903 set; no new warning from the SCM-08 files).
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitHistory"` →
  **64 passed / 0 failed / 0 skipped** (50 service + 14 API; the sprint-filter evidence, fix-up
  round 1 re-run).
- `dotnet test tests/DocumentIDE.IntegrationTests` (full) → **851 passed / 2 failed / 0 skipped**
  (853 total, fix-up round 1 re-run). Both failures are the documented REL-03-sanctioned
  release-artifact checks (`ScriptTests.Release_Build_Embeds_Production_Web_Assets` — missing
  `artifacts/desktop/DocumentIDE.Desktop.exe`;
  `Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`
  — missing `artifacts/installer/DocumentIDE-Setup.msi`): `artifacts/` is gitignored, absent in
  a fresh worktree, and this diff touches neither `scripts/` nor packaging. The same two
  failures are recorded in the SCM-01…SCM-07 handoffs. The known nondeterministic
  `BatchRename`/load-sensitive environmental flake did **not** reproduce in this run.
- `dotnet test tests/DocumentIDE.Core.Tests` → **459 passed / 0 failed / 0 skipped** (Core touched
  via the new `GitHistoryResult.cs`).
- `dotnet test tests/DocumentIDE.SecurityTests` → **91 passed / 0 failed / 0 skipped**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **12 passed / 0 failed / 0 skipped**.
- `npx vitest run` (`src/DocumentIDE.Web`) → **54 files / 383 tests passed, 0 failed**
  (SCM-08 `GitHistoryPanel.test.tsx` → 9 tests).
- `npx tsc -b` (`src/DocumentIDE.Web`) → exit 0.
- `npx oxlint` (`src/DocumentIDE.Web`) → exit 0, **23 warnings / 0 errors**. Two of the
  warnings are the `react(only-export-components)` note for co-locating the exported
  `fetchHistory`/`fetchFileVersion` helpers with the component — the established convention
  shared by `CommitBox.tsx`, `PdfActions.tsx`, `ShareManager.tsx`, `MetadataFilterChips.tsx`.
  No new warning class was introduced.

## Fix-up round 1 (post-review)

An independent review approved SCM-08 with notes and found one merge-blocking MAJOR defect. The
fix is a single follow-up commit on the same branch (`feat/git-commit-history-file-history`) on
top of `7fd9e9e`; no rebase, no new branch.

### MAJOR — the "bounded" version read materialised the whole blob

**Finding.** `GitHistoryService.GetFileVersionAsync` ran `git show <commit>:<path>` with
`CaptureBinaryOutput = true`, so `GitProcessRunner.ReadAllBytesAsync` copied the **entire** blob
into a `MemoryStream` and then `ToArray()` before `BuildVersion` clipped it to `MaxVersionBytes`
(256 KiB). `IsTruncated`/`SizeBytes` therefore bounded the *returned value*, not the *read*: a
committed multi-gigabyte blob was fully materialised (twice: buffer + `ToArray()`), and past the
`byte[]` limit `ToArray()` threw `OutOfMemoryException`, which `RunCoreAsync` does not catch (it
catches only `Win32Exception`). That broke `IGitProcessRunner`'s contract of never throwing for
operational Git failures.

**Fix.**
- Probe the object size first with `git cat-file -s <revision>:<path>` — read-only, already in
  SCM-01's `ReadOnlySubcommands`, so it never takes the mutation queue.
- When the size exceeds `MaxVersionBytes`, return `Available` with `IsTruncated = true`,
  `Content = null`, `IsBinary = false` and the true size; the `show` read is **never executed**.
- Only a blob within the bound is read (byte-captured, as before).
- `GitFileVersion.SizeBytes` (and the API DTO) is now `long`, so a >2 GiB blob is reported
  truthfully instead of overflowing an `int` size parse.
- A successful probe with an unparseable size is a typed `Failed`; nothing is read.
- `BuildVersion` still clips defensively and now backs off to the last complete UTF-8 code point
  (no trailing U+FFFD). With the probe gating every read that clip is unreachable through normal
  Git reads; it is kept as a belt-and-braces invariant.
- The panel renders `IsTruncated && Content === null` as "Document is too large to preview
  (N bytes)." instead of an empty `<pre>`.

**RED evidence (before the fix; the exact run output).**

- Backend, `--filter` on the six touched cases → `Failed: 5, Passed: 1, Total: 6`:
  - `LargeDocumentVersionIsTruncatedWithoutReadingTheBlob` —
    `Assert.Null() Failure: Value is not null / Expected: null / Actual: "aaaa…"` (the clipped
    content still arrived).
  - `SmallDocumentVersionIsReadInFullAfterTheSizeProbe` —
    `Assert.Contains() Failure: Filter not matched in collection / Collection:
    [["rev-parse", …], …, ["--no-optional-locks", "show", "<sha>:docs/small.md"]]` (no
    `cat-file -s` probe ran).
  - `ControlCharacterBlobWithoutNulByteIsReportedAsBinary` and
    `InvalidUtf8BlobWithoutNulByteIsReportedAsBinary` — `Assert.True() Failure / Expected: True /
    Actual: False` (the NUL-only heuristic called them text).
  - `UnbornHeadRepositoryReportsAnEmptyHistoryPage` — `the unborn-HEAD decision must come from a
    HEAD probe run after the failed log` (the stderr matcher was used).
  - `NonAsciiUtf8DocumentStaysText` passed on the old code (guard against over-classification).
- Frontend, `npx vitest run src/features/source-control/GitHistoryPanel.test.tsx` →
  `Tests 2 failed | 7 passed (9)`:
  - scope note: `Expected element to have text content: /renames are not followed/i /
    Received: File history for docs/a.md`.
  - over-the-bound version: `Expected … /too large to preview/i / Received: docs/large.md at
    1111111262208 bytes — truncated preview (the file is larger)`.

**GREEN after the fix.** The same backend filter → `6 passed`; the whole
`FullyQualifiedName~GitHistory` filter → **64 passed**; the frontend file → **9 passed**; the
full suites and build are the numbers in "Verification" above. The read-only invariant
(`HistoryAndVersionReadsAreStrictlyReadOnly`) still passes: `cat-file -s` and `show` are both in
SCM-01's read-only vocabulary and no read takes the mutation queue.

### Minors fixed in the same round

1. **NUL-free binary detection.** `LooksBinary` now also rejects a sniff window that is not valid
   UTF-8 and one where control characters exceed 10% (TAB/LF/FF/CR excluded) — pinned by
   `ControlCharacterBlobWithoutNulByteIsReportedAsBinary`,
   `InvalidUtf8BlobWithoutNulByteIsReportedAsBinary` and the false-positive guard
   `NonAsciiUtf8DocumentStaysText`.
2. **Rename disclosure.** The panel's document-scope note now says "renames are not followed, so
   history before a move may not appear", asserted by the scope test.
3. **Unborn HEAD** now uses the SCM-05 exit-code probe (`rev-parse --verify --quiet HEAD` → exit 1
   with empty stderr) via `GitErrorSignatures.IsUnbornHeadProbe`, run after the failed `log`;
   the stderr-substring matcher was removed. Pinned by the probe-ordering assertion in
   `UnbornHeadRepositoryReportsAnEmptyHistoryPage`.
4. **UTF-8 truncation back-off** implemented in `CompleteUtf8PrefixLength` (defensive, see above).

### Deliberately not changed (recorded, out of scope)

- **`repositoryPath` on the history routes is caller-named** (`?path=C:\other-repo` reads that
  repository's history). This is identical to the pre-existing SCM-03 `context`/`status` routes,
  is **not** a traversal escape (the *document* path is containment-checked), and is **not** a
  regression. Recorded as an accepted, pre-existing consistency point for a future sprint; route
  validation was not changed here.
- **`NextSkip` overflow near `int.MaxValue`** — not reachable through the bounded page window in
  this sprint; known and deliberate.
- **`--no-optional-locks` is present on the version reads but not on every `log`/`rev-parse`
  read** — cosmetic consistency nit only; those commands never take the index lock either.
- **`IsTruncated` + `Content = null` is also the binary shape** — now documented as the
  "too large to preview" state; `IsBinary` stays `false` because no bytes were sniffed. A richer
  discriminated version state is a contract change for a later sprint.

## Acceptance criteria

- [x] **Git History is visually separate from Document IDE History** — the panel heading is
  "Git History" with a persistent `Source: Git — this history comes from the repository and is
  separate from Document IDE History.` note (`git-history-heading`/`git-history-source-note`,
  asserted by the frontend tests), and every API payload carries `source: "Git"`
  (`HistoryEndpointReturnsCommitsWithMetadataAndSourceMarker`). The service reads no Document
  IDE history row.
- [x] **User can inspect commit metadata** — `KnownFixtureHistoryRendersCorrectly` pins the
  full commit id, short id, author name/email, strict-ISO committer time and subject on a known
  fixture; `HistoryEndpointReturnsCommitsWithMetadataAndSourceMarker` pins the same over HTTP; the
  panel renders author, time and file count per commit.
- [x] **File history can target a selected document** — `FileHistoryFiltersToCommitsAffectingTheFile`,
  `FileHistoryEndpointFiltersToTheSelectedDocument` and `FileHistoryMatchesPathspecsLiterally`
  prove the document scope filters to exactly that file; the panel's `This document` toggle drives
  it and shows the `git-history-scope-note`.

Required tests from the sprint doc:

- [x] Known fixture history renders correctly — `KnownFixtureHistoryRendersCorrectly` (the mandatory RED-first test).
- [x] File history filters to commits affecting the file — `FileHistoryFiltersToCommitsAffectingTheFile`.
- [x] Large histories load incrementally — `LargeHistoryLoadsIncrementally` + `HistoryEndpointLoadsLargeHistoriesIncrementally`.

Implementation steps:

- [x] Step 1 commit list query — `GetHistoryAsync` + `--format` parser.
- [x] Step 2 file history query — `GetFileHistoryAsync` (`git log -- <path>`, literal pathspec).
- [x] Step 3 pagination/lazy loading — `GitHistoryQuery` + `--skip`/`--max-count` + `Load more`.
- [x] Step 4 wire commit selection into document version inspection — `GetFileVersionAsync` +
  `Inspect version` → `git-history-version-preview`.

Failure / edge cases from the sprint doc:

- [x] Git is not installed / not discoverable — `HistoryWhenGitIsUnavailableIsTypedGitUnavailable`,
  `GitDiscoveryClassificationsMapOntoTheirTypedHistoryOutcome`.
- [x] Repository/worktree disappears during an operation — `WorktreeDisappearingBeforeTheReadIsATypedFailure`.
- [x] File path contains spaces, Unicode or special characters — `FileHistoryMatchesPathspecsLiterally`.
- [x] Git operation already running elsewhere / index lock unavailable — the version read is
  `--no-optional-locks` and no read takes the mutation queue (`HistoryAndVersionReadsAreStrictlyReadOnly`);
  no history/version path ever needs the index lock.
- [x] Word changed a DOCX between operations — the version read projects the **committed** blob,
  never the working tree (`SelectedCommitExposesTheDocumentVersionAtThatCommit`,
  `BinaryDocumentVersionIsReportedWithoutTextContent`); working-tree churn cannot affect it.
- [x] Hook / permission / authentication / conflict / remote rejection — these are commit-side
  outcomes (SCM-07) and are **N/A** for read-only `log`/`show`; any Git failure still surfaces typed
  via `GitFailureClassificationsStayTypedAndInspectable` and `HistoryRoutesReportTypedNoRepositoryAndNotFoundStates`.

## Deviations / spec conflicts

- One extra Core file beyond the planned list (`GitHistoryResult.cs`); it is the typed public
  contract consumed by Infrastructure/API and follows the SCM-02/SCM-06/SCM-07 Core placement
  precedent. No lane file touched; no higher-level requirement weakened.
- `GitErrorSignatures.cs` (SCM-01/06 shared helper) gained one member (`IsUnbornHeadProbe`). It is an
  append-only addition in the same shape as the existing `IsUnmergedIndexPath`; no existing
  signature changed.
- The API test class `GitHistoryApiTests` lives in the same `GitHistoryTests.cs` file. The plan
  lists a single test artifact for this sprint, and SCM-07 established the single-file convention
  (`GitCommitTests` + `GitCommitApiTests`).
- The plan's frontend test path (`tests/frontend/**`, if any) is stale; the test is colocated as
  `GitHistoryPanel.test.tsx` per the CAN-05 batch-B ruling used by every prior feature component.
- File history does **not** use `--follow`; rename tracking is deliberately out of scope (Git's
  `log -- <path>` semantics, which is what "commits affecting the file" means here).
- No production code change resulted from the frontend test-authoring fix in the panel cycle.

## Risks discovered

- **Bounded version read (consumer-relevant):** `GetFileVersionAsync` serves at most
  `MaxVersionBytes` (256 KiB) of content and enforces the bound **before** reading: it probes the
  object size (`git cat-file -s`) and a larger blob returns `IsTruncated = true` with
  `Content = null` and the true `SizeBytes` (a `long`, so a multi-gigabyte blob is reported
  exactly). A consumer must treat `IsTruncated` as "too large to preview", not as empty content.
- **Binary detection is a heuristic**, now with three signals over the first 8000 bytes: a NUL
  byte (Git's own signal), a window that is not valid UTF-8, and a window dense with control
  characters (>10% excluding TAB/LF/FF/CR). A text file that legitimately contains a NUL byte in
  that window is reported as binary (`Content = null`) — never rendered as text, which is the
  safer failure direction.
- **`FileCount` for file history is pathspec-limited**, i.e. Git's own count of that commit's files
  matching the requested path. A consumer must not read it as "files in the commit" when the read
  was file-scoped.
- **Commit id shape check** accepts hex length 4..64 (`IsCommitIdentifier`). A SHA-256 repo
  (64 hex) and a SHA-1 repo (40 hex) are both served; a value that is hex but not a real object id
  is left for Git to report as a typed `Failed` (it can never become an option or a revision
  expression).
- **`%cI` committer time is a string**, deliberately never localized server-side; the panel calls
  `toLocaleString()` only for display and falls back to the raw value if parsing fails.
- **Unborn-HEAD handling is an exit-code probe** (`rev-parse --verify --quiet HEAD`: exit 1 with
  empty stderr), the same probe SCM-05 uses. It does not depend on Git's locale (the runner pins
  `LC_ALL=C` in `GitProcessRunnerOptions.ProtectedEnvironment`, which a request cannot override),
  but it is not invariant to a change in the probe's own exit-code contract. The remaining
  stderr-signature matches (`IsHeadPathMissing` and friends) are wording-based: a Git message
  change would degrade those cases to a typed `Failed`, never to a silent success.
- **No rename following** in file history: a document moved/renamed shows history only under its
  current path. `--follow` was intentionally omitted (spec 13 §12 semantics + determinism), and
  the panel's scope note now discloses that limitation to the user.
- **The panel is not wired into `App.tsx`**; it is a self-contained lane-C-owned insertion point.
  Until wired, it exposes no navigation surface — this matches the sprint's "no branch-management
  UI" constraint.

Consumer notes for SCM-09/SCM-11/SCM-13:

- **SCM-09 (Discard):** reuse the SCM-08 read-only discipline as the *contrast* — Discard is a
  mutation and must NOT be modeled on this service. Copy instead the input-validation/containment
  pattern (`GitDocumentPath.TryNormalizeRelativePath` + `TryResolveContainedPath` **before** any Git
  call) and the typed-outcome vocabulary. `GitHistoryService` itself mutates nothing and takes no
  queue slot; a discard flow must take the SCM-01 per-worktree mutation queue via the resolved true
  worktree root.
- **SCM-11 (Remote/sync, if it follows the roadmap):** SCM-08 adds **no** remote/push/pull surface.
  Do not attach remote reads to the history routes.
- **SCM-13:** the `GitHistoryStatus` vocabulary and its HTTP mapping are the contract to preserve —
  HTTP 400 only for `InvalidRequest`/`PathRejected` (nothing ran), HTTP 200 typed for every
  Git-reported state. This split is now consistent across SCM-06/07/08; clients must branch on the
  body, not the status. The `source: "Git"` marker keeps Git history separate from Document IDE
  History in the payload.
- Any consumer that mutates the repository must pass the workspace/active-document path and let the
  service resolve the true worktree root: that root is the SCM-01 queue key.

## Next eligible sprint(s)

- SCM-09 (Discard) and SCM-11 (Remote/sync) become eligible once their own `Depends On` rows are
  DONE. This sprint deliberately opened no branch-management, push, pull or remote surface. SCM-13
  remains governed by its own `Depends On` row.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — every SCM-08 Git invocation
  is `log` or `show`, both read-only; `HistoryAndVersionReadsAreStrictlyReadOnly` records every
  invocation and asserts none is a mutating subcommand, and the working tree, index and `HEAD` are
  unchanged after a read. The version read adds `--no-optional-locks` and captures bytes so even the
  index lock is untouched. Every route carries the privileged localhost session guard
  (`HistoryRoutesRejectMissingInvalidTokenAndHostileOrigin`).
- [x] **no arbitrary shell command can be injected through a filename/path** — no shell exists
  (structured `ArgumentList` only); the document path always travels after a `--` separator and is
  passed as a discrete argument (`FileHistoryMatchesPathspecsLiterally` proves a hostile-looking path
  is one literal argument), the commit id is hex-validated before Git runs, and the query window is
  range-validated before Git runs.
- [x] **no repository escape is possible through path handling** — discovery validates containment
  through the SCM-02 `GitRepositoryService`, and every read validates the document path through
  `GitDocumentPath.TryNormalizeRelativePath` + `TryResolveContainedPath` **before any Git or
  filesystem access**; absolute, rooted, unnormalized and escaping paths are typed `PathRejected`.
- [x] **no secret/credential output is exposed in UI logs** — responses carry typed statuses, Git's
  structured stdout/stderr for failures and paths only (no environment dumps); the reads involve no
  credentials and never contact a remote. The SCM-01 prompt-disabled environment policy applies
  unchanged.
- [x] **no destructive action silently broadened its scope** — SCM-08 contains no destructive action
  at all: no commit, no stage, no discard, no branch/remote operation. File history stays scoped to
  the one document (`GIT_LITERAL_PATHSPECS=1`, no `--follow`), and a file-history request without a
  document is a typed rejection rather than a silent widening to the whole repository.
- [x] **relevant higher-level safety rules remain stronger than convenience** — Git and the local
  filesystem remain the source of truth (history is derived on every read, never cached; no Document
  IDE history row consulted); no semantic/fuzzy matching anywhere (literal pathspecs, hex-only commit
  ids, NUL-structural parsing); typed failures instead of silent fallbacks; version content is bounded
  and binary blobs are never rendered as text. The file-safety
  snapshot/temp/atomic-replace rule is **N/A** in one sentence: this sprint performs no in-place
  mutation of user file content — it only reads committed Git objects and is a pure projection of the
  repository.
