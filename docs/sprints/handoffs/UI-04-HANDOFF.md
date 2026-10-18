# Sprint Handoff — UI-04 (final V1 UI pass)

**Status:** DONE with two explicit stops (context pack, missing resources)
**Branch:** `feat/ui-04-final-surfaces` (worktree `ui-04-final`, detached HEAD at start)
**Scope:** screen 06 real DOCX diff, a11y selection fixes, dead CSS removal. No backend change.

## Outcome per requested step

| Step | Outcome |
| --- | --- |
| 1. Screen 06 — real DOCX diff | **Shipped** |
| 2. Context pack dialog | **Stopped and reported** (needs a product decision + a write route) |
| 3. Missing resources | **Stopped and reported** (needs a write route + a broken shipped transport) |
| 4. Cleanups | **Shipped** (SearchPanel roles, SourceControlPanel `aria-current`, `App.css` deleted) |

---

## 1. Screen 06 — real DOCX document-aware diff (shipped)

`DocxDiffScreen` no longer renders a hardcoded `status="failed"` panel. It calls the
SCM-19 route and maps the engine report onto the shipped `DocxDiffPanel` view model.

- **New `docxDiffApi.ts`** (typed client + pure mapper):
  - `fetchDocxDiff(worktreeRoot, documentPath, target, originalPath)` →
    `POST /api/source-control/docx-diff` with the host session token header.
  - `docxDiffTargetFor(entry)` chooses only a pair the Git entry actually has:
    - `state === 'Untracked'` → `WorkingTree` (HEAD absent, working tree present);
    - `conflictKind !== 'None'` or `indexChange !== 'Unmodified'` → `Staged`
      (an unmerged path surfaces the provider's typed conflict there);
    - otherwise → `Unstaged`.
  - `toDocxDiffViewState(response)` maps `Status` → `DocxDiffStatusView`,
    `Kind` (PascalCase enum names) → `DocxChangeKindView`, and mirrors
    before/after, paragraph/run/table/row/cell indexes and the binary
    size/SHA summary. An unknown status, unknown change class, unknown target
    or a missing report body becomes a **typed failure** — never rows, never an
    empty diff.
- **`DocxDiffScreen`** now has `worktreeRoot: string | null` (the true worktree
  root from `GET status`, not a discovered path), and renders:
  - loading state while the report is in flight;
  - a real change list with paragraph before → after, table/formatting
    locations and target pair labels from the engine;
  - a derived change-class summary (text / table / formatting / metadata
    counts) computed only from the returned change classes;
  - `BinaryFallback` as its typed size/SHA state with no rows;
  - every failed read / transport failure as `role="alert"` text;
  - a typed `docx-diff-worktree-unknown` state and **no request** when the root
    is unknown;
  - the unchanged `openInWord` primary action;
  - no page image and no text diff (WORD-04 still unimplemented).
- `SourceControlScreen.onOpenDocxDiff` now passes `(entry, status.worktreeRoot)`;
  `App` stores and forwards it. Every other surface is untouched.

## 2. Context pack — stopped (product decision required)

The dialog is presentation-only and takes `sources: {id,title,content}[]`. There
is no honest source set or export target in this build:

- **No full-text route for a `.docx` selection.** Only
  `GET /api/documents/{id}/markdown` (Markdown) exists. `GET
  /api/documents/{id}/preview` is bounded to 60 lines × 160 chars ≤ 8 KB — a
  page-1 *preview*, not document text. Feeding it into a "context pack" would
  publish a truncation as document content; dropping DOCX selections would
  silently omit selected documents.
- **The dialog's export action writes a file named `context.md`.** No route
  writes a context pack, and there is no defined destination or pack model
  (the design shows `Packs/context-<date>.md`; `ContextExportService.ExportToFile`
  requires an explicit path). Mounting with `onCopy` only would still render
  **Export context.md** and display "Exported!" without writing — an inert
  control that claims success, which the hard rules forbid.
- Therefore `ContextPackDialog` stays unmounted. `App.dialogs.test.tsx`'s
  negative assertions remain valid and were not weakened. No stub was added.

## 3. Missing resources — stopped (write route + broken transport)

Listing missing documents needs no new route: `GET
/api/workspaces/{workspaceId}/documents` already returns `availabilityStatus`,
and the shell already loads it. The blocker is the card's two actions:

- **`Remove reference` has no route.** `MissingResourceService.RemoveReferenceAsync`
  exists in Core but no controller exposes it. It deletes a database reference
  (not a file), i.e. a mutation — adding it is not a read-only route, and where
  removal lives (card vs. canvas vs. list) is a product decision. The design's
  missing-resource overlay offers Continue offline / Locate… / Retry, not
  Remove.
- **`LocateMovedFileDialog` cannot reach the API in the desktop product.**
  It fetches relative URLs (`/api/documents/...`) without the session token.
  The desktop page origin is `https://appassets.html2sav`, while the privileged
  API is `http://127.0.0.1:<port>`; the host injects the token header only for
  loopback API URLs. Relative requests therefore never reach the API. The
  component's tests pass only because they mock `fetch` by URL substring.
  Mounting it as-is would expose a broken overlay; fixing its transport is a
  change to another shipped subsystem, not a mount.

`MissingResourceCard` and `LocateMovedFileDialog` therefore stay unmounted, and
the existing negative assertion in `App.dialogs.test.tsx` is unchanged.

## 4. Cleanups (shipped)

1. **`SearchPanel.tsx`** — results are now a real multi-select listbox:
   `role="listbox"` + `aria-multiselectable="true"` + `aria-label="Search results"`
   on the results container, `role="option"` + `aria-selected` on each row,
   `tabIndex={0}` and Enter/Space parity with the click handler. The checkbox
   inside the option was replaced by an `aria-hidden` visual indicator so the
   option owns the selection state and contains no interactive descendant. The
   old bare `aria-selected` on a role-less `<div>` is gone.
2. **`SourceControlPanel.tsx`** — the single selected row uses
   `aria-current="true"` (omitted when not selected) instead of an invalid
   `aria-selected` on `<li role="listitem">`.
3. **`src/DocumentIDE.Web/src/App.css`** — deleted; verified no file imports it
   (only `index.css` is imported, by `main.tsx`).

---

## Files changed

**New**

- `src/DocumentIDE.Web/src/features/source-control/docxDiffApi.ts` / `.test.ts`
- `docs/sprints/handoffs/UI-04-HANDOFF.md` (this file)

**Modified**

- `src/DocumentIDE.Web/src/features/source-control/DocxDiffScreen.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/features/source-control/SourceControlScreen.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/App.tsx`
- `src/DocumentIDE.Web/src/App.shell.test.tsx`
- `src/DocumentIDE.Web/src/features/search/SearchPanel.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/features/source-control/SourceControlPanel.tsx` / `.test.tsx`

**Deleted**

- `src/DocumentIDE.Web/src/App.css`

No backend file, controller, route, service, `.pen`, `docs/quality/**`,
`scripts/verify-release.ps1`, `SPRINT-STATUS.md`, manifest or roadmap was
changed. No new runtime dependency.

## Existing tests changed (deliberate, complete list)

1. `DocxDiffScreen.test.tsx` — the screen's contract changed from a static
   typed-error render to a real route call, so its tests were rebuilt around a
   real-shaped payload. **Kept** every original assertion: screen test id,
   `Document diff` heading, path/`Modified`/`DOCX · OOXML` metadata, no
   `markdown-diff`, no `role="img"`, no "Page 1 preview", the read-only note,
   `POST /api/word/open` with `{documentId:'docx-1'}`, and no opener for an
   unregistered document. **Changed:** the Word test's
   `expect(fetchMock).toHaveBeenCalledTimes(1)` / `mock.calls[0]` encoded "this
   screen makes no request"; it now locates the `/api/word/open` call by URL.
   **Added:** real report renders (paragraph before/after, table/formatting
   locations, target pair, class summary, exact request body with the worktree
   root), binary fallback is typed with sizes/SHA and no rows, failed read is a
   typed alert with no rows, transport failure is typed, loading state, and no
   request without a worktree root. No assertion was deleted to go green.
2. `SourceControlScreen.test.tsx` — one assertion. `onOpenDocxDiff` now receives
   the true worktree root as its second argument; the expected call became
   `(objectContaining({path}), 'C:\\docs')`. The intent (a `.docx` row routes to
   screen 06) is unchanged.
3. `App.shell.test.tsx` — additive only: the fetch mock now serves
   `POST /api/source-control/docx-diff`, and the UI-03 screen-06 test additionally
   asserts the real change row, the engine's after-text and the exact request
   body (`path: 'C:/Docs'`, `documentPath: 'Report.docx'`, `target: 'Staged'`).
   No existing assertion was removed or weakened.
4. `SourceControlPanel.test.tsx` — six assertions changed from
   `aria-selected` to `aria-current` (`'true'` when selected, attribute absent
   otherwise) because `aria-selected` is not supported on `listitem`. One
   negative assertion (`not.toHaveAttribute('aria-selected')`) was added.
5. `SearchPanel.test.tsx` — one test added (listbox/multi-selectable semantics,
   `option` rows, click and Space toggle). No existing assertion changed.
6. `App.dialogs.test.tsx` — **unchanged**; its context-pack and missing-resource
   negatives still hold because steps 2 and 3 stopped.

## Verification

- `npx tsc -b` → **exit 0**
- `npx oxlint` → **exit 0**, **35 warnings**, all pre-existing
  `react(only-export-components)`; none in a changed file.
- `npx vitest run` → **87 files / 620 tests passed, 0 failed, 0 skipped**.
- `dotnet build DocumentIDE.sln --nologo` → **0 errors, 162 warnings**
  (pre-existing package/analyzer warnings; this worktree baseline includes the
  merged HIST-04 work).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **1052 total, 1050 passed,
  2 failed, 0 skipped**. Both failures are the expected packaging tests that
  need a prepared release tree (`artifacts/` is absent in a fresh worktree):
  - `DocumentIDE.IntegrationTests.Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets`
  - `DocumentIDE.IntegrationTests.Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`
- `dotnet test tests/DocumentIDE.Core.Tests` → **473/473 passed, 0 skipped**.
- `dotnet test tests/DocumentIDE.SecurityTests` → **162/162 passed, 0 skipped**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **12/12 passed, 0 skipped**.
- `git diff --check` → clean.
- Flake: none observed. No backend route was added, so the integration suite was
  run once (the twice rule applies only to added routes).

### TDD evidence

1. `docxDiffApi.test.ts` was written first; the first run failed at module
   resolution (`Failed to resolve import "./docxDiffApi"`). After the mapper it
   was **13/13**, including the unknown-status/unknown-kind "never rows" cases.
2. `DocxDiffScreen.test.tsx` was rewritten before the screen: the run failed
   **9 of 10** (missing `docx-diff` after the real payload, missing class
   summary, missing worktree-unknown testid). After the screen, the focused run
   was **23/23** (mapper + screen).
3. Cleanups were driven by the existing a11y assertions: the SearchPanel test
   failed to find `role="listbox"` before the markup change; the
   SourceControlPanel run failed on the old `aria-selected` expectations before
   the `aria-current` implementation.

## Deviations / residual risks

1. **Screen 06 target rule.** A file with both staged and unstaged changes opens
   the staged pair (HEAD ↔ index); the screen always labels the pair it shows.
   There is no pair toggle — the design's segmented control switches
   Markdown/Document diff, which does not apply to a `.docx`-only screen.
2. **`DocumentList` still uses `aria-selected` on `<tr>`** (pre-existing; a
   table row is not a grid row here). It was outside the two files the pass
   named and its test asserts that attribute; it should be fixed with the same
   `aria-current`/checkbox pattern in a later a11y pass.
3. **Context pack** remains unmounted; see §2 for the two missing product
   decisions (DOCX content representation, export destination/model).
4. **Missing resources** remains unmounted; see §3 (no remove route, broken
   locate transport in the desktop host).
5. The screen 06 inspector shows only real fields. It does not claim Words /
   Paragraph count / Styles / snapshot ids, because the engine report does not
   carry them.
