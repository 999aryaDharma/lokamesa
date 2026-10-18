# Sprint Handoff — FS-06 Missing File & Folder Recovery

**Status:** DONE
**Branch:** `feat/fs-06-missing-file-folder-recovery`
**Commits:** (final commit hash in SPRINT-STATUS.md row)

## What changed

- New `MissingResourceService` (`src/DocumentIDE.Core/Filesystem/`): typed
  probe / locate-with-verification / remove-reference / source-probe for
  missing files and folder sources. Missing state is persisted on the record
  (`AvailabilityStatus`, `LastKnownPath`); records are never deleted except by
  explicit `RemoveReferenceAsync`, which deletes only the DB row.
- New `MissingResourceCard.tsx` (+ vitest): renders missing file/folder
  objects per workspace-canvas spec §32 with Locate / Remove actions, and a
  permission-denied label distinct from missing.
- New `MissingResourceTests.cs`: 7 integration/unit tests covering all
  Required Tests and both Acceptance Criteria.

## Files changed

- `src/DocumentIDE.Core/Filesystem/MissingResourceService.cs` (new)
- `src/DocumentIDE.Web/src/features/files/MissingResourceCard.tsx` (new)
- `src/DocumentIDE.Web/src/features/files/MissingResourceCard.test.tsx` (new)
- `tests/DocumentIDE.IntegrationTests/Filesystem/MissingResourceTests.cs` (new)
- `docs/sprints/SPRINT-STATUS.md` (FS-06 row → DONE; no other rows touched)

Reuse mapping: no equivalent file existed. `StartupReconciler` (FS-04) already
marks missing during startup scans; this sprint adds the dedicated
locate/remove/probe service + UI it did not provide. `LocateMovedFileDialog`
(FS-05) already covers candidate search UI and was reused as-is (not modified).

## Verification

- `dotnet test tests/... --filter "FullyQualifiedName~MissingResourceTests"` → PASS (7/7)
- `dotnet test tests/... --filter "FullyQualifiedName~Filesystem"` → PASS (59/59, 0 skipped)
- `npm test -- --run src/features/files/MissingResourceCard.test.tsx` → PASS (4/4)
- `npm test -- --run` (full web suite) → PASS (26 files, 157/157)
- `npx tsc -b` → PASS (no errors)
- TDD Red demonstrated: initial build failed with
  `CS0246: MissingResourceService could not be found` before implementation.

## Acceptance criteria

- [x] Permission-denied is distinct from missing — `ClassifyProbeFailure` maps
      `UnauthorizedAccessException → PermissionDenied`, not-found → Missing,
      `IOException → Locked`; card renders "Access denied" vs "File missing".
      Evidence: `Permission_Denied_Is_Distinct_From_Missing` test + card test.
- [x] Unavailable drive does not destroy workspace layout — `ProbeSourceAsync`
      marks source/children missing, deletes nothing; canvas rows survive.
      Evidence: `Unavailable_Drive_Preserves_Layout` test.
- Required tests: deleted-file-missing ✓, locate-rebinds-DocumentId ✓,
  remove-never-deletes-physical ✓ (hash-verified). Canvas restore ✓ via
  in-place `DocumentId` rebind (workspace_documents untouched).
  Uncertain-locate-requires-confirmation ✓ (fails closed, mutates nothing).

## Deviations / spec conflicts

- Persisted source `AvailabilityStatus` stays `"missing"` (not `"unavailable"`)
  for inaccessible roots, for FS-01/FS-04 reader compatibility (`FolderSourceService.GetFolderSourcesAsync`
  and the FS-04 test assert the `"missing"` mapping). The drive-vs-path
  distinction lives in the typed `SourceProbeStatus` (`UnavailableDrive` vs
  `MissingPath`), not in the stored string. No higher-level rule weakened.
- `LocateAsync` fails closed on unsupported extensions (`.docx`/`.md` only)
  even when `confirmed=true` — indexing only supports those types.
- `LocateAsync` refuses targets already tracked by a different `DocumentId`
  (by path or NTFS identity) — prevents identity theft/duplicates.

## Risks discovered

- `ProbeDocumentAsync` distinguishes permission-denied by listing the parent
  directory; exotic ACLs that allow listing but deny file stat still classify
  as missing. Acceptable: the file is genuinely unreachable and the record is
  preserved either way.
- `FileMove` between `File.Exists` and fingerprint in `LocateAsync` returns
  typed `candidate-not-found` but does not retry; the user can retry Locate.

## Security & file-safety gate

- [x] no user file was mutated through an unreviewed/bypass path — service
      performs zero filesystem writes (reads/stat only); `RemoveReference`
      performs zero filesystem calls.
- [x] no arbitrary path or secret is exposed — candidate paths are
      user-chosen/explicit; no path logging added.
- [x] dependency/security boundaries remain intact — Core service depends only
      on domain repositories + `IFileIdentityProvider`; no Word/Canvas/Search/Share touched.
- [x] no destructive action silently broadened its scope — DB delete only via
      explicit remove-reference; locate refuses ambiguous/foreign targets.
- [x] relevant higher-level safety rules remain stronger than convenience —
      filesystem remains source of truth; uncertain rebind fails closed.

## Next eligible sprint(s)

- `CAN-06` Position Persistence & Pinning (still BLOCKED on `CAN-05`)
