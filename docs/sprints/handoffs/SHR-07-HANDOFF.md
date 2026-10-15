# Sprint Handoff — SHR-07 — Share Download Permissions

**Status:** DONE
**Branch:** `security/shr-07-share-download-permissions`
**Commits:** `security(sharing): share download permissions`

## What changed
- `SnapshotSharePublisher` (`src/DocumentIDE.Core/Sharing/SnapshotSharePublisher.cs`, additive only): honors the owner-chosen `Share.AllowOriginalDownload`/`Share.AllowPdfDownload` policy in the frozen manifest (both default OFF on `Share`; previously forced OFF) and prepares `originals/`/`pdfs/` artifacts only as policy requires. New optional `SnapshotSourceDocument.OriginalBytes`/`PdfBytes` carry owner-side bytes; ON without bytes (or over the 10 MiB per-artifact budget) throws `InvalidOperationException`, never a half-published share. OFF shares never get `originals/`/`pdfs/` bytes even if bytes are supplied. Filenames match the ShareHost routes exactly (`originals/{DisplayName}`, `pdfs/{ChangeExtension(DisplayName, ".pdf")}` with sanitized manifest names); writes reuse the existing atomic temp+move path (extracted to `WriteArtifactAtomically`, behavior unchanged). New `original`/`pdf` `ShareSnapshotArtifact` records with content hashes.
- New owner/viewer UI (`src/DocumentIDE.Web/src/features/sharing/SharePermissions.tsx` + test, 6/6 vitest): `SharePermissions` owner toggles defaulting OFF with `onChange` policy reporting; `ShareDownloadButtons` viewer component omits (never disables) buttons for OFF/missing downloads.
- New `tests/DocumentIDE.IntegrationTests/Sharing/DownloadPermissionTests.cs` (9 tests): OFF original/PDF denied (403 + artifact-route bypass denied + no bytes on disk), ON preserved in manifest, ON serves exact prepared bytes (200, PDF content-type), permission flip updates route behavior per request (403→404→403, proving no cached/UI decision), nonmember direct-ID denied (404) with permission ON, policy-only preparation (OFF drops supplied bytes; ON-without-bytes throws).
- Planned-file mapping (per sprint rule): `DownloadEndpoints.cs` was NOT created — the permission-gated `download-original`/`download-pdf` routes plus the QA-05 `originals/`/`pdfs/` artifact gate already exist inline in `src/DocumentIDE.ShareHost/Program.cs` (SHR-02/QA-05). Duplicating them into a new file would fork security-critical routes; `Program.cs` needed no change (endpoints already re-read the manifest per request). No `ShareManifest` change needed either: `AllowOriginalDownload`/`AllowPdfDownload` already exist (SHR-01) with OFF defaults on `Share`.

## Files changed
- `src/DocumentIDE.Core/Sharing/SnapshotSharePublisher.cs` (additive: optional record fields, policy-honoring flags, conditional artifact prep, extracted atomic-write helper)
- `src/DocumentIDE.Web/src/features/sharing/SharePermissions.tsx` (new — planned file, exact path)
- `src/DocumentIDE.Web/src/features/sharing/SharePermissions.test.tsx` (new)
- `tests/DocumentIDE.IntegrationTests/Sharing/DownloadPermissionTests.cs` (new — planned file, exact path)
- `docs/sprints/SPRINT-STATUS.md` (SHR-07 → DONE)

## Verification
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~DownloadPermissionTests` → PASS (9/9; Red: `OriginalPermission_On_IsPreservedInManifest` + `PdfPermission_On_IsPreservedInManifest` failed with `False` before the publisher change, OFF/route tests passed throughout)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~Sharing` → PASS (38/38, incl. unchanged SHR-04 `SnapshotShareTests` 8/8)
- `dotnet test tests/DocumentIDE.SecurityTests` → PASS (91/91, incl. QA-05 `PublicShareAttackSurfaceTests` — endpoints untouched)
- `dotnet test tests/DocumentIDE.Core.Tests --filter FullyQualifiedName~Sharing` → PASS (89/89)
- `dotnet test tests/DocumentIDE.ArchitectureTests` → PASS (7/7)
- Web `npm test` → PASS (210/210, 35 files); `npm run lint` clean for `sharing/`; `npx tsc -b` clean. UI Red proven by removing the component (suite fails to collect) then restoring (6/6).
- Note: the new test class raises only `DownloadsPerMinutePerIp` to 1000 via the QA-05 `WithWebHostBuilder` override so its 11 sequential probes never trip the 10/min budget; rate-limit enforcement itself stays covered by QA-05.

## Security & File-Safety Gate Confirmation
- [x] no user file was mutated through an unreviewed/bypass path (publisher writes only under the app-owned share root, atomically; sources only read; OFF shares get no download bytes)
- [x] no arbitrary path or secret is exposed (artifact names come from sanitized manifest `DisplayName`; resolution via `GetArtifactPath` containment; manifest carries no physical paths — asserted by SHR-04 tests, still passing)
- [x] dependency/security boundaries remain intact (no ShareHost change; publisher still references no Word/Interop/Infrastructure — ArchitectureTests 7/7; no POST/PUT/PATCH/DELETE added)
- [x] no destructive action silently broadened its scope (ON requires explicit owner bytes per document; missing bytes throw typed errors; republish still frozen-rejected)
- [x] relevant higher-level safety rules remain stronger than convenience (server-side 403/404 enforcement proven by direct-URL/direct-ID tests; UI omission is presentation only)

## Acceptance criteria
- [x] UI hiding is not the enforcement boundary (`OriginalDownload_Off_CannotBeDownloaded` hits routes directly: 403; `NonmemberDirectId_WithPermissionOn_ReturnsNotFound`: 404 with permission ON; `ChangingPermission_UpdatesRouteBehavior` proves per-request server-side decisions)

## Deviations / spec conflicts
- Planned `DownloadEndpoints.cs` not created: equivalent routes already exist inline in `Program.cs` (SHR-02, hardened by QA-05); per the sprint's planned-files rule the existing location was kept and no duplicate abstraction was introduced. No spec wording weakened.

## Risks discovered
- Two members with identical sanitized `DisplayName` and downloads ON would share one `originals/` filename (last-write-wins; endpoint serves the same file for both IDs). Pre-existing endpoint design (SHR-02); owner-side doc-ID uniqueness does not guarantee filename uniqueness. Flagged for SHR-11 owner UI validation.
- `originals/`/`pdfs/` artifact gate applies when a manifest exists (QA-05 residual); unchanged by this sprint.

## Next eligible sprint(s)
- `SHR-11` — Owner Share Management UI (still BLOCKED on SHR-05 + SHR-09; SHR-07 side now satisfied)
- `SHR-05` — Membership-Safe Live Share (unaffected; SHR-04 files not refactored beyond additive publisher extension)
