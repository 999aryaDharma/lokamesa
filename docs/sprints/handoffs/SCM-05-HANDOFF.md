# Sprint Handoff — SCM-05

**Status:** DONE
**Branch:** `feat/docx-document-aware-git-diff`
**Commits:** (see git log for `feat(scm): docx-document-aware-git-diff`, base `2a5b926`)

## What changed

- **Read-only Git-version materialization (Step 1)** — new
  `DocxGitDiffProvider.GetDocumentDiffAsync(worktreeRoot, relativePath, target,
  originalPath?, cancellationToken)`:
  - materializes both compared versions as **read-only temp artifacts**
    (`{tempRoot}/scm05-{guid}/left.docx` + `right.docx`, `ReadOnly` attribute
    set) — left = older side, right = newer side of the requested
    `GitDiffTarget`:
    - `Staged` = HEAD (`git show HEAD:<path>`, or the pre-rename
      `originalPath`) ↔ index (`git show :<path>`),
    - `WorkingTree` = HEAD ↔ working-tree file read,
    - `Unstaged` = index ↔ working-tree file read;
  - all Git access goes through the SCM-01 `IGitProcessRunner` with structured
    argument lists, `--no-optional-locks` and the `rev-parse --verify --quiet
    HEAD` unborn-repo probe (SCM-04 pattern);
  - DOCX blobs are read as **raw bytes** (`GitProcessRequest.CaptureBinaryOutput`
    / `GitProcessResult.StandardOutputBytes`, see "Deviations"); artifacts are
    created and deleted in `finally`; a caller-provided temp root may remain as
    an empty directory (tests assert no leftover entries);
  - **nothing else is ever written**: no index touch, no worktree write. A
    dedicated test byte-compares `.git/index`, the worktree `.docx` and
    `git status --porcelain=v2` before/after and runs while a foreign
    `.git/index.lock` is held (the read path never needs the lock).
- **Parsed-structure comparison (Step 2)** — `DocxVersionSnapshotBuilder`
  (Infrastructure) builds a plain-data `DocxDocumentSnapshot` from each
  read-only artifact using the existing parsers: `DocxPackagePreflight` (IDX-01)
  for package validity, `DocxFontScan` (BAT-08) for paragraphs/runs, and
  `IDocxStructureParser` (IDX-02) `DocumentMetadata` for document metadata. New
  pure-Core `DocxDocumentComparer.Compare(left, right)` aligns paragraphs (and
  table cells row-major per table) with the SCM-04 Myers
  `GitUnifiedDiffFormatter.ComputeHunks` engine — **exact ordinal matching
  only**, no fuzzy/semantic matching.
- **Change classification (Step 3)** — typed `DocxChange` records with
  `DocxChangeKind`:
  - text: `ParagraphAdded` / `ParagraphRemoved` / `ParagraphTextChanged` /
    `HeadingChanged` (level or text),
  - table: `TableCellAdded` / `TableCellRemoved` / `TableCellChanged` with
    `TableIndex`/`RowIndex`/`CellIndex`,
  - formatting: `FormattingChanged` (property name + `Before`/`After` values),
    computed by aligning run-format descriptors (font family/size/bold/italic/
    underline) over the shared **character offsets** of paragraphs whose visible
    text is unchanged — a pure run split/merge with identical effective
    formatting produces **no change**;
  - metadata: `MetadataChanged` over the fixed IDX-02 field list (sections,
    tables, images, page count, primary font/size, paper, orientation, margins,
    header/footer presence).
- **Regression corpus + edge fixtures (Step 4)** — 7-case document-aware corpus
  theory (paragraph text changed, paragraph added, heading level changed,
  heading text changed, table cell changed, formatting changed, metadata paper
  changed) plus edge fixtures: clean committed document on all 3 targets,
  staged/unstaged pair correctness, unborn-repo addition, complete removal,
  binary fallback with deterministic size+SHA-256 summary, Git-unavailable,
  disappeared worktree, Unicode/space paths, 8 path-escape attacks,
  escaping `originalPath`, foreign `index.lock` (read-only proof), unmerged
  index conflict typing, staged rename via `originalPath` (see Verification for
  test names).
- **Typed outcomes (spec 13 §9.2/§9.3)** — `DocxDiffStatus`:
  `Available` / `BinaryFallback` / `NoRepository` / `GitUnavailable` /
  `PathRejected` / `Failed`, every failure carrying the originating typed
  `GitProcessResult`. A version that cannot be parsed as a DOCX degrades to
  `BinaryFallback` with exact version sizes + SHA-256 digests instead of a
  faked semantic diff (spec 13 §9.3). Unmerged index path maps to `Failed` with
  `ErrorKind = GitProcessErrorKind.GitConflict`.
- **Path safety** — requested paths (incl. `originalPath`) are validated as
  worktree-relative document paths and containment-checked through the shared
  `GitDocumentPath` helper (extracted from SCM-04's `GitDiffService`) **before
  any Git or file access**; escaping/rooted/`..//:stream` forms are rejected
  with `PathRejected` and zero access.
- **Frontend (planned file)** — new `DocxDiffPanel.tsx`, a purely presentational,
  strictly read-only review surface: typed statuses (available / binary
  fallback / no-repository / git-unavailable / path-rejected / failed /
  loading), target label pairs (HEAD↔index, HEAD↔working tree,
  index↔working tree), per-change rows with exact `Before`/`After` values,
  table/run/paragraph coordinates, property names, `/dev/null` for a missing
  side, binary summary (sizes + hashes), `role=alert` typed failures. Content
  and paths are rendered as **inert text** by React (no
  `dangerouslySetInnerHTML`) and the surface has no buttons/inputs/links.

## Files changed

Planned files (as in the sprint document):

- `src/DocumentIDE.Infrastructure/SourceControl/DocxGitDiffProvider.cs` (new)
- `src/DocumentIDE.Web/src/features/source-control/DocxDiffPanel.tsx` (new)
- `tests/DocumentIDE.IntegrationTests/SourceControl/DocxGitDiffTests.cs` (new, 33 tests)

**Extra files beyond the planned list (disclosed):**

- `src/DocumentIDE.Core/SourceControl/DocxDiffContracts.cs` (new) — the typed
  Core contracts (`DocxDiffStatus`, `DocxChangeKind`, `DocxChange`,
  `DocxDiffReport`, `DocxDiffResult`, `DocxBinarySummary`, snapshot records) and
  the pure `DocxDocumentComparer`. Same split rationale as SCM-04's
  `GitDiffContracts.cs`: the comparer and the frontend view model consume these
  types without taking an Infrastructure dependency, and the architecture tests
  keep Git process spawning in Infrastructure.
- `src/DocumentIDE.Infrastructure/SourceControl/GitDocumentPath.cs` (new) —
  shared path canonicalization/validation/containment helper extracted from
  SCM-04's `GitDiffService` so the provider and the service enforce the same
  path safety once (extraction-only refactor; SCM-04 tests re-run green).
- `src/DocumentIDE.Infrastructure/SourceControl/GitErrorSignatures.cs` (new) —
  shared git-stderr signature matchers (`IsHeadPathMissing`,
  `IsIndexPathMissing`, `IsUnmergedIndexPath`); SCM-04 and SCM-05 call sites now
  use the single copy.

**SCM-01 files modified (justified, backward-compatible — see Deviations):**

- `src/DocumentIDE.Core/SourceControl/GitProcessRequest.cs` — additive
  `CaptureBinaryOutput` init-only property (default `false`).
- `src/DocumentIDE.Core/SourceControl/GitProcessResult.cs` — additive nullable
  `StandardOutputBytes` (null unless capture was requested).
- `src/DocumentIDE.Infrastructure/SourceControl/GitProcessRunner.cs` — raw-byte
  stdout capture path + lenient UTF-8 decode for diagnostics only.

**SCM-04 file modified (extraction-only refactor, behavior preserved):**

- `src/DocumentIDE.Infrastructure/SourceControl/GitDiffService.cs` — private
  path/error helpers replaced by shared `GitDocumentPath`/`GitErrorSignatures`
  calls; SCM-04 `MarkdownGitDiffTests` re-run 23/23.

**CAN-05 colocated test mapping (disclosure):** the frontend contract test
lives colocated at
`src/DocumentIDE.Web/src/features/source-control/DocxDiffPanel.test.tsx` (new,
13 tests), following the CAN-05 ruling precedent already applied by SCM-04
(`MarkdownGitDiff.test.tsx`) and SCM-15 — the repo's vitest setup is rooted in
`src/DocumentIDE.Web` and `tests/frontend/**` is not wired into it.

Constraints respected: `GitStageService.cs`, `GitStageResult.cs`,
`ChangeActions.tsx`, `GitStageTests.cs`, `SourceControlController.cs`,
`sourceControlModels.ts`, `SourceControlPanel.tsx`, `DI/Program.cs` and
`TestGitRepository.cs` are untouched (`git status` self-review check).

Docs:

- `docs/sprints/handoffs/SCM-05-HANDOFF.md` (new)
- `docs/sprints/SPRINT-STATUS.md` (row SCM-05 only, READY → DONE)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --no-build --nologo --filter "FullyQualifiedName~DocxGitDiffTests"` → **PASS (33/33, 0 skipped)**
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --no-build --nologo --filter "FullyQualifiedName~MarkdownGitDiffTests"` → **PASS (23/23)** (SCM-04 regression after the shared-helper refactor)
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --no-build --nologo` → **694/696 passed, 0 skipped**; the 2 failures are exactly the pre-existing REL-03 release-artifact checks
  `Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`
  and `Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets`
  (`artifacts/desktop/**` + `artifacts/installer/*.msi` do not exist in a fresh
  worktree; documented in the SCM-01/02/04 handoffs; this diff touches neither
  `scripts/` nor `artifacts/`).
- `dotnet test tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj --no-build --nologo` → **PASS (91/91)**
- `dotnet test tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj --no-build --nologo` → **PASS (12/12)** (includes the process-boundary frontend scan with the new `.tsx` in scope)
- `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj --no-build --nologo` → **PASS (459/459)** (Core was touched)
- `dotnet build DocumentIDE.sln --nologo` → **0 errors**; a `--no-incremental` rebuild produced **no warnings in any changed/new file**.
- Frontend (`src/DocumentIDE.Web`): `npx tsc -b` → **exit 0**;
  `npx oxlint` (whole app) → **exit 0** with only pre-existing warnings in
  unrelated files; `npx oxlint DocxDiffPanel.tsx DocxDiffPanel.test.tsx` →
  **exit 0, no findings**; `npx vitest run` → **PASS (46 files / 308 tests)**,
  including the new `DocxDiffPanel.test.tsx` 13/13.

TDD evidence (Red → Green → Refactor, with honesty notes):

1. **Backend RED:** the required first test
   `GitVersions_OfADocx_CanBeComparedWithoutMutation` was written first and
   failed with `CS0246` (contracts/provider types missing); the minimal GREEN
   pass made it compile and pass. *Honesty note:* like SCM-04's first pass, this
   initial GREEN had to include the contract file + provider skeleton to
   compile; it did not include any test beyond the first one.
2. **Frontend RED:** `DocxDiffPanel.test.tsx` (13 tests) was written first and
   failed to resolve the import `./DocxDiffPanel`; the component was then
   implemented → 13/13 GREEN.
3. **Incremental behavioral steps:** the Steps 2–4 suite (corpus theory,
   split/merge-bounded formatting, edge/typed-failure cases) was developed
   iteratively against the growing test list with RED runs before fixes; the
   final pinned state is 33/33. *Honesty note:* the per-step red histories
   beyond the two RED events above were not preserved in the session log; what
   is pinned is the initial RED evidence plus the final green suite.
4. **Refactor:** `GitDocumentPath` and `GitErrorSignatures` extracted and shared
   with SCM-04's `GitDiffService`; re-ran both filters (33/33, 23/23) and the
   full suites above. Extraction-only, behavior preserved.

## Acceptance criteria

- [x] **Git status remains authoritative for file state** — the provider is
      read-only end-to-end: only `rev-parse`/`git show` reads + working-tree
      file reads, no index/worktree write, no staging call;
      `Diff_IsReadOnly_AndRemainsAvailableWhileAnIndexLockFileExists`
      byte-compares `.git/index`, the worktree file and `git status` before/
      after with a foreign `index.lock` held.
- [x] **DOCX staging remains file-level** — no staging/partial-content API
      exists anywhere in this sprint's code; the diff surface is inspection
      only (`Diff_IsReadOnly_…` backend + the component read-only test
      asserting no buttons/inputs/links), and the SCM-06 staging files are
      untouched.
- [x] **Diff failures fall back to safe binary-change reporting** —
      `NonDocxContent_ProducesBinaryFallbackWithDeterministicDigestSummary`
      (corrupt/non-DOCX right-side blob) pins `DocxDiffStatus.BinaryFallback`
      with exact sizes + SHA-256 digests and the typed parse failure, never a
      faked semantic diff; the component test
      *"summarizes a binary fallback with sizes and hashes instead of faking
      changes"* pins the same contract in the UI.

Required tests from the sprint document:

- [x] **Git versions of a DOCX can be compared without mutation** — the first
      failing test `GitVersions_OfADocx_CanBeComparedWithoutMutation`, plus
      `MaterializedGitVersions_AreParsedAsReadOnlyArtifacts_AndCleanedUp` and
      `Diff_IsReadOnly_AndRemainsAvailableWhileAnIndexLockFileExists`.
- [x] **Text/table/formatting fixtures produce stable document-aware output** —
      `RegressionCorpus_ProducesStableDocumentAwareOutput` (7 cases: paragraph
      text changed / paragraph added / heading level changed / heading text
      changed / table cell changed / formatting changed / metadata paper
      changed) + 13 component tests (text, table coordinates, formatting and
      metadata property names, addition/removal, empty, binary, failures,
      injection-as-text, read-only).
- [x] **Split-run text changes remain understandable** —
      `SplitRunTextChange_ProducesOneUnderstandableParagraphChange` (one
      contiguous paragraph change despite run-boundary fragmentation) and
      `RunSplitMergeWithoutTextChange_ProducesNoChanges` (pure run split/merge
      yields no change).

## Deviations / spec conflicts

- **SCM-01 binary stdout capture (justified SCM-01 file changes).** DOCX blobs
  are binary; the runner's text-only stdout read is lossy (replacement
  characters), so the diff needs exact bytes. `CaptureBinaryOutput` is
  init-only and defaults to `false`, and `StandardOutputBytes` is null unless
  requested: every existing SCM-01/02/04 call path is byte-for-byte unchanged,
  proven by the untouched SCM-04 filter (23/23), Security (91/91),
  Architecture (12/12), Core (459/459) and the unchanged full-integration
  baseline (same 2 environmental failures).
- **SCM-04 `GitDiffService` refactor (justified SCM-04 file change).**
  `GitDocumentPath`/`GitErrorSignatures` extraction only; no behavior change,
  pinned by the re-run SCM-04 suite.
- **Temp artifacts are written under the temp root (read-only).** Spec 13 §9.2
  deep-edit boundary stays Microsoft Word; this sprint's only writes are
  `left.docx`/`right.docx` inside a fresh `scm05-{guid}` temp directory, marked
  read-only and deleted in `finally`. A caller-provided temp root may remain as
  an empty directory (deliberately not deleted); tests assert zero leftover
  entries and no user-file/`.git` write.
- **Metadata comparison is limited to the IDX-02 `DocumentMetadata` field
  list** (sections, tables, images, page count, primary font/size, paper,
  orientation, margins, header/footer). The comparer does not re-parse OOXML
  metadata beyond what the existing parser provides; no new parsing logic was
  introduced.
- **Formatting is classified only for paragraphs whose visible text is
  unchanged** (run-format alignment over character offsets). A paragraph with
  both text and formatting changes reports the text change; a pure run
  split/merge reports nothing. Deliberate review-noise reduction pinned by
  tests; it never hides a text change.
- **`Program.cs` untouched** (per task constraints, following SCM-02/04
  precedent). `DocxGitDiffProvider` is not yet registered in DI/endpoints;
  SCM-03/SCM-14 wiring must construct it with an `IGitProcessRunner`.
- **Sprint doc header** still says `Status: BLOCKED`; only the
  `SPRINT-STATUS.md` row is updated (READY → DONE), per the task instruction and
  SCM-04 precedent.

## Risks discovered

- **Git stderr wording coupling.** Absent-side detection matches the pinned
  English signatures via the shared `GitErrorSignatures` (SCM-01 runner pins
  `LC_ALL=C`). A future Git wording change degrades to a typed `Failed`
  result — never a silent wrong diff.
- **Binary stdout is buffered in memory** for the requested Git object (whole
  DOCX blob). Same order of magnitude as SCM-04's text read; no streaming was
  added. Very large documents (tens of MB) briefly hold two copies (bytes +
  temp artifact) — bounded by the file size, not by repository size.
- **Temp-artifact cleanup is best-effort.** An externally locked temp artifact
  cannot break a typed diff outcome (cleanup never throws), so a hostile
  lock could leave a read-only file inside the OS temp directory; no user file
  or repository state is affected.
- **Run-format fidelity inherits BAT-08's logical scan.** Formatting
  comparison covers the run properties that `DocxFontScan` exposes
  (font/size/bold/italic/underline); other OOXML run properties are not
  classified (documented field list above).
- **Rename detection is caller-driven.** Consumers must pass
  `GitStatusEntry.OriginalPath` for staged renames, otherwise the HEAD side is
  absent and the change renders as a complete addition
  (`RenamedFile_StagedDiff_UsesOriginalPathForTheHeadSide` pins the supported
  path).
- **Verification note (honesty):** one full-suite run under parallel load
  showed an extra transient failure in the pre-existing
  `PdfMergeMemoryTests.Merge_PeakMemory_DoesNotScaleLinearlyWithTotalSourceBytes`
  (a load/memory-sensitive test unrelated to this diff; passes 6/6 in
  isolation and did not recur in the subsequent full run, which showed exactly
  the 2 documented environmental failures). All SCM-05/SCM-04 tests were green
  in every run.

## Next eligible sprint(s)

- SCM-07 (Git Commit & Commit All) — its SCM-05 dependency is now satisfied;
  still blocked on SCM-06.
- SCM-14 (Git Conflict State & Resolution Shell) — SCM-05 dependency satisfied;
  still blocked on SCM-12.
- SCM-17 (Source Control Regression & Release Hardening) — SCM-04/SCM-05
  satisfied; other listed sprints (SCM-09/12/13/14/16, QA-02) are not.

**Consumer notes for SCM-07 (recorded as required):**

- the document-aware report is a **review surface** for a whole-file decision:
  staging/committing stays file-level; there is no partial-content API and the
  panel is read-only;
- construct `DocxGitDiffProvider(IGitProcessRunner, IDocxStructureParser?,
  string? tempRoot, IDocxPackagePreflight?)` and call
  `GetDocumentDiffAsync(worktreeRoot, relativePath, target, originalPath?,
  cancellationToken)`;
- treat every non-`Available` status as typed information to surface; a
  `BinaryFallback` is a *complete* description of the change for non-DOCX or
  unparseable content (sizes + SHA-256), never "no changes".

**Consumer notes for SCM-14 (recorded as required):**

- conflicted file, staged target → `DocxDiffStatus.Failed` with
  `Failure.ErrorKind == GitProcessErrorKind.GitConflict` and a message
  containing "unmerged" (`UnmergedFile_StagedDiff_YieldsTypedConflictFailure`,
  test-pinned); any target that reads the index side (`Staged`/`Unstaged`)
  takes this path — render the typed failure, never a fabricated diff;
- conflicted file, working-tree target → `Available` and parses the
  working-tree bytes (spec 13 §21: non-index versions stay inspectable;
  test-pinned); after resolution the same call returns
  `Available`/`BinaryFallback` normally;
- **no DOCX conflict-marker rendering is claimed** (unlike SCM-04's text
  Markdown case): binary DOCX conflicts keep one side's bytes; use Git status +
  the typed conflict failure for the shell UI.

**Consumer notes for SCM-17 (recorded as required):**

- regression suite: `tests/DocumentIDE.IntegrationTests/SourceControl/DocxGitDiffTests.cs`
  (33 tests) + colocated `DocxDiffPanel.test.tsx` (13 tests); corpus fixtures
  are generated programmatically in-test (no committed `.docx` binaries,
  following the existing DOCX/BAT golden precedent);
- the 7-corpus theory and the split-run pair are the stable output pins for
  document-aware classification;
- shared helpers `GitDocumentPath`/`GitErrorSignatures` are used by both
  SCM-04 and SCM-05 — route any future path/error-signature changes through
  them.

## Security & File-Safety Gate

- [x] **No user file was mutated through an unreviewed/bypass path** — the only
      writes are read-only temp artifacts under `{tempRoot}/scm05-{guid}/`,
      deleted in `finally`; `Diff_IsReadOnly_AndRemainsAvailableWhileAnIndexLockFileExists`
      byte-compares `.git/index`, the worktree `.docx` and `git status`
      before/after while a foreign `index.lock` is held; the provider performs
      only `git show`/`rev-parse` reads + a file read.
- [x] **No arbitrary shell command can be injected through a filename/path** —
      all Git invocations go through the SCM-01 runner (structured argument
      lists, no shell, `--no-optional-locks`); paths are validated before use
      and object names are `HEAD:<path>`/`:<path>` with `:` rejected in
      inputs; the 8-attack table test (incl. `../`, drive-rooted, `:stream`)
      and the escaping-`originalPath` test reject with `PathRejected` and zero
      Git/file access.
- [x] **No repository escape is possible through path handling** — shared
      `GitDocumentPath` validation + containment re-check on the combined
      absolute path, identical to SCM-04's attack-tested helper; both the
      requested path and `originalPath` are validated.
- [x] **No secret/credential output is exposed in UI logs** — no new logging;
      typed failures carry short explanatory messages (git stderr text for
      `Failed`); the panel renders only the typed message and diff content;
      SCM-01's prompt-disabled/closed-stdin environment policy still governs
      every Git invocation.
- [x] **No destructive action silently broadened its scope** — this sprint
      contains no destructive action at all; the frontend surface is read-only
      (test asserts no buttons/inputs/links), and no staging/commit code was
      added or modified.
- [x] **Relevant higher-level safety rules remain stronger than convenience** —
      local filesystem remains the source of truth (nothing is written outside
      the temp root; the worktree and index are never touched); exact ordinal
      matching only (no fuzzy/semantic matching); DOCX deep editing remains
      Microsoft Word's domain; typed failures replace silent fallbacks; the
      binary fallback is explicit and deterministic rather than a guessed
      semantic diff.
