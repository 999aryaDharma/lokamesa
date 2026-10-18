# Sprint Handoff — SCM-19

**Status:** DONE
**Branch:** `feat/docx-diff-api-surface`
**Commits:** `2c4785b` (code + tests); this follow-up commit adds the handoff and a 400
`path`-message wording fix (no behaviour change)

## What changed

- **Registered `DocxGitDiffProvider` in both composition paths.** The SCM-05 provider
  (`DocxGitDiffProvider`, previously wired nowhere) is now a singleton in
  `src/DocumentIDE.Api/Program.cs` (the dev API host) **and** in
  `SourceControlServiceRegistration.AddSourceControlServices` (the shipped desktop host's
  `WebAppHost` path). Both registrations use the DI `IGitProcessRunner`, so a replaced/mocked
  runner flows into the provider in tests and no second Git boundary was introduced.
- **Appended one privileged route:** `POST api/source-control/docx-diff` taking
  `{ path, documentPath, target, originalPath? }`. It calls
  `DocxGitDiffProvider.GetDocumentDiffAsync(path, documentPath, target, originalPath, ct)`
  and returns the engine's typed report. The provider is injected per action
  (`[FromServices]`), so the controller constructor and every existing member are untouched.
- **Typed DTOs** in the existing SCM style: `SourceControlDocxDiffRequest`,
  `SourceControlDocxDiffResponse` (`status` / `isSuccess` / `errorCode` / `message` +
  `report` + `failure`), plus `SourceControlDocxDiffReportResponse`,
  `SourceControlDocxChangeResponse` and `SourceControlDocxBinarySummaryResponse`.
  `report` exposes `changeCount` / `hasChanges` summary counts, the real change list
  (kind, origin, part, exact before/after content, property and all index fields) and the
  binary summary.
- **Read-only by construction:** the route never takes `GitWorktreeMutationQueue`, never
  touches the index, never writes the worktree, never opens the document (no Word/COM), and
  never renders a page image. The engine's only Git calls are `rev-parse --verify --quiet HEAD`
  and `--no-optional-locks show <rev>:<path>`.
- **Status convention:** `400` only for input rejected before any Git or filesystem access
  (null body, blank `path`, blank `documentPath`, missing/unknown `target`). Every inspectable
  outcome — `NoRepository`, `GitUnavailable`, `PathRejected`, `BinaryFallback`, `Failed` — is
  `200` with a typed body.
- **No engine change.** `DocxGitDiffProvider`, its limits, classification (`DocxDiffStatus`,
  `DocxChangeKind`), `DocxDocumentComparer` and `DocxDiffContracts` are byte-identical.

## Files changed

- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` (append-only: one route, target
  parser, report mappers, one 400 helper, and the SCM-19 DTO records at end of file)
- `src/DocumentIDE.Api/Program.cs` (append-only: one provider registration block)
- `src/DocumentIDE.Infrastructure/SourceControl/SourceControlServiceRegistration.cs`
  (append-only: one `TryAddSingleton` provider registration)
- `tests/DocumentIDE.IntegrationTests/SourceControl/DocxDiffApiTests.cs` (new, 10 tests;
  reuses `TestGitRepository` and the in-project `DocxTestFixtureBuilder` package builder)
- `docs/sprints/handoffs/SCM-19-HANDOFF.md` (this file)

## Verification

- `dotnet build DocumentIDE.sln -v q --nologo` → **PASS** (0 errors; warnings are pre-existing
  xUnit analyzer warnings elsewhere in the suite)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~DocxDiff"`
  → **PASS 10/10** (the new API surface)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~DocxGitDiff|FullyQualifiedName~DocxDiff"`
  → **PASS 43/43** (33 pre-existing SCM-05 engine tests + the 10 new surface tests; the literal
  `~DocxDiff` filter alone does not match the engine class `DocxGitDiffTests`, so this broader
  filter is reported to prove the engine suite is still green)
- `dotnet test tests/DocumentIDE.IntegrationTests` → **1024 passed, 2 failed, 0 skipped**
  (3m54s). The two failures are the known environment-only release checks and are unrelated to
  this sprint:
  - `ScriptTests.Release_Build_Embeds_Production_Web_Assets` — `artifacts\desktop\DocumentIDE.Desktop.exe` is absent in a fresh worktree.
  - `Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — `artifacts\installer\DocumentIDE-Setup.msi` is absent in a fresh worktree.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **PASS 12/12**
- `dotnet test tests/DocumentIDE.Core.Tests` → **PASS 459/459**
- No flakes observed: the ReadOnly test initially failed because the fixture's own `git status`
  refreshed racy-clean index stat data; the fixture now snapshots status before the index bytes
  (documented inline) and the suite has been run twice green since.

### TDD record

1. **Red:** `DocxDiffApiTests.cs` was written first; `dotnet build` of the integration test project
   failed with 77 `CS0246` errors for the missing DTOs/route surface (the expected first failure).
2. **Green:** DTOs, route and both DI registrations added; build clean.
3. First green run: 9/10. The read-only index assertion caught the fixture's own racy-clean index
   rewrite; the test snapshot order was fixed (not the product), and the test then passed.
4. Refactor/verify: full suites re-run after the comment-only polish; all green except the two
   environment failures above.

## Acceptance criteria

- [x] `POST api/source-control/docx-diff` returns a real document-aware report from a real
      repository — `WorkingTreeDiffOfTwoCommittedDocxVersions_ReturnsRealStructuralReport`
      (real Git repo, two real DOCX packages; asserts `ParagraphTextChanged` before/after,
      `ParagraphAdded`, `HeadingChanged` level 1→2, `TableCellChanged` table 0/row 0/cell 1,
      `changeCount == changes.Count`).
- [x] The route is reachable from **both** the API host and the desktop host —
      `DocxDiffProvider_IsRegisteredInBothCompositionRoots` resolves the provider from the real
      `WebApplicationFactory<Program>` host *and* from a bare `ServiceCollection` composed with
      `AddSourceControlServices()`.
- [x] No diff-engine behaviour changed; no DOCX is opened, parsed into Word, or written — the
      engine files are untouched, and `WorkingTreeDiff_IsReadOnly_...` proves queue concurrency
      stays 0, only `rev-parse`/`show` run, and the document bytes, index bytes, HEAD, status and
      repository file set are identical before/after.
- [x] The route set stays append-only — `git diff` against the parent adds 275 lines to the three
      source files and deletes none (verified with `git diff --unified=0 -- src/`).
- [x] Non-DOCX path refused typed — `NonDocxPath_IsRefusedAsTypedBinaryFallback_NotDiffedAsText`
      (`.md` → `BinaryFallback`, empty change list, real size/SHA-256 binary summary).
- [x] Path escape rejected before Git runs — `UnsafePaths_AreRejectedTypedBeforeGitRuns`
      (absolute, rooted, `..`, drive-qualified, escaping `originalPath`) with a recording
      `IGitProcessRunner` asserting zero invocations.
- [x] Document absent at the compared version stays typed —
      `DocumentAbsentAtComparedVersion_ReturnsTypedRemovalReport`.
- [x] Not-a-repository and Git-missing are typed `200` —
      `NotARepository_IsTypedHttp200`, `GitUnavailable_IsTypedHttp200`.
- [x] Missing/invalid token `401`; hostile Origin/Host `403` —
      `DocxDiffRouteRejectsMissingInvalidTokenAndHostileOriginOrHost`.
- [x] Security & file-safety gate: no file mutated; structured Git arguments only (engine,
      untouched); no repository escape (typed `PathRejected`, containment re-checked by the
      engine); no secrets in the payload (existing typed failure surface); no destructive action.

## Deviations / spec conflicts

1. **`path` is the true worktree root**, not a workspace/active-document path that gets resolved
   through `GitRepositoryService.DiscoverAsync`. The engine needs the worktree root and validates
   the relative document path *before any Git or filesystem access*; a discovery call would have
   run Git before a rejected path, contradicting the required zero-invocation proof. The DTO
   documents the contract (`WorktreeRoot` from `GET context`/`GET status`). Not a spec conflict —
   spec 13 §5 permits discovery, it does not require it for this route — but it is an explicit
   surface decision for the later UI commit.
2. **`isSuccess` is true only for `Available`.** A `BinaryFallback` is a legitimate typed engine
   outcome (spec 13 §9.3) and carries its full `report` and the typed `failure` reason, but it is
   not a successful document-aware comparison, so `isSuccess=false`, `errorCode="BinaryFallback"`.
   This matches the sprint's "refused typed" framing.
3. **DTO vocabulary keeps the engine names** (`Available`, `WorkingTree`, `ParagraphTextChanged`)
   exactly like every other SCM DTO (`Succeeded`, `NotARepository`, …). Mapping to the existing
   `DocxDiffPanel` view model (`available`, `working-tree`, `paragraph-text-changed`) belongs to
   the follow-up UI commit, which was explicitly out of scope.

## Risks discovered

- **Nested `path`:** if a caller passes a subdirectory or a document path instead of the true
  worktree root, the engine will compare the wrong working-tree file (Git `show` still resolves
  against the repo root, but the disk side is joined onto `path`). The route documents the
  contract; the UI commit must pass `WorktreeRoot`.
- **Request-thread parse cost:** the route awaits the Git reads, but the DOCX preflight/parse runs
  synchronously inside the request. It never blocks a UI thread (this is the server), but a very
  large document occupies one request for the parse duration; there is no per-call concurrency
  cap on read-only diffs. No incorrectness, and the engine's own resource budgets still apply.
- **Binary fallback is the only non-DOCX outcome:** a `.md`/other file is not rejected by
  extension; it degrades to the engine's binary summary. That is the engine's tested
  classification (out of scope to change) and is asserted.
- **Both composition paths now duplicate the provider registration** (Program.cs and the
  extension), consistent with the KNOWN DUPLICATION already recorded in
  `SourceControlServiceRegistration`; consolidating Program.cs remains the existing follow-up.

## Next eligible sprint(s)

- UI follow-up commit (not part of SCM-19): wire `DocxDiffScreen`/`DocxDiffPanel` to
  `POST api/source-control/docx-diff` using the reported `WorktreeRoot`, mapping the engine
  vocabulary to the panel's view model. Delete the hardcoded "not available in this build" state.
- `HIST-04` (screen-10 data gap) remains the other recorded UI data gap.
