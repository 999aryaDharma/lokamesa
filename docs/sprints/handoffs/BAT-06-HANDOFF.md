# Sprint Handoff — BAT-06 — Occurrence Review UX

**Status:** DONE
**Branch:** `feat/bat-06-occurrence-review-ux`
**Commits:** `2bda93a` (implementation); completion metadata in the following docs commit.

## What changed
- New `SearchReplaceReview.tsx` occurrence-centric review composed over the BAT-03 panel pipeline (no second selection path, zero network calls): matches grouped by file + origin (Body/Table/Header/Footer headings), context snippet + old→new preview per occurrence, file/occurrence checkboxes routed to the panel's `edit()` (disabled when uneditable or file failed), recursive folder scope as a named `role="tree"` (root + included subfolders + excluded branches with `aria-level`), Folder-only gating preserved ("Not applicable" elsewhere), single persist path (panel `save()` PUT `/{planId}/selection` built solely from `preview.files`).
- Panel refactor: occurrence markup moved into the component; `BatchPreviewPanel.tsx` keeps ownership of polling, creation-cancel, Save, Apply-disabled. Only the moved markup + 1 import removed; zero test assertions removed.
- Acceptance "Review UI never expands scope itself": component renders only `preview.files`/occurrences; payload contains only preview-returned IDs (`sentIds ⊆ returnedIds` asserted); scope tree built only from resolver fields and Folder-gated.

## Files changed
- `src/DocumentIDE.Web/src/features/batch/SearchReplaceReview.tsx` (new, at planned path)
- `src/DocumentIDE.Web/src/features/batch/SearchReplaceReview.test.tsx` (new; planned `tests/frontend/batch/` path is stale — that tree sits outside the `src/DocumentIDE.Web` vitest root and never runs under `npm test`; colocated per established convention)
- `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.tsx` (markup moved out)
- `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.test.tsx` (one appended integration test + `within` import)

## Verification
- `scripts/verify.ps1` → PASS on final tree: build, 428 Core + 200 Integration + 7 Architecture tests, Web build, 19 files / 89 Web tests, 0 skipped; log `artifacts/bat-06-verify.log`.
- Explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped; log `artifacts/bat-06-security.log`.
- Reviewer focused run `npx vitest run src/features/batch` → 3 files, 30 passed, 0 failed.
- TDD evidence in `.superpowers/sdd/BAT-06-occurrence-review-ux/final-report.md` (4 RED runs incl. mandated first behavior; report-only — single squashed commit, recorded honestly; "green-at-birth" regression tests disclosed as such).
- Task review: **Spec ✅, Quality Approved**; Low/Info only (see risks).

## Acceptance criteria
- [x] Review UI never expands scope itself — renders preview-returned IDs only; payload provenance test.
- [x] Unchecked occurrence is not applied — payload omission + stale re-preview test.
- [x] Header/table labels are visible — origin headings test.
- [x] Scope tree matches resolver — Folder-gated tree texts/levels test + no-tree-outside-Folder test.

## Security & file-safety gate
- [x] No user file mutated through an unreviewed/bypass path — display-only component; persist path reuses reviewed-plan selection.
- [x] No arbitrary path or secret exposed — renders IDs/context strings from preview DTO only.
- [x] Dependency/security boundaries intact — frontend only, no new packages.
- [x] No destructive action silently broadened its scope — no scope synthesis; failed files non-editable.
- [x] Higher-level safety rules stronger than convenience — BAT-03 contracts (Folder-only exclusions, Apply-disabled, unmount-cancel) verified unregressed.

## Deviations / spec conflicts
- Test placed colocated instead of planned `tests/frontend/batch/` (stale path outside vitest root; would never execute). Component path created as planned. No spec weakened.

## Risks discovered
- Deferred Lows: old→new preview shows live form find/replacement (editable post-preview; display-only, payload unaffected; preview DTO carries no query fields); report count miscount (29 vs actual 30, narrative only); RED-first repo-unverifiable (process note: commit red stages / ledger them next sprints); static treeitems not focusable (display tree, no regression).
- `tests/frontend/**` orphaned from `npm test` repo-wide — needs a chore-sprint test-locator cleanup (not BAT-06).

## Next eligible sprint(s)
- `BAT-07` — Regex Advanced Replace (dep BAT-06) — closes the batch operation chain.
- Other READY unchanged: `DOCX-04`, `SHR-08`, `CAN-04`, `QA-01`, `QA-02`, `CAN-08`, `BAT-08`, `BAT-09`, `BAT-10`, `HIST-02`.
