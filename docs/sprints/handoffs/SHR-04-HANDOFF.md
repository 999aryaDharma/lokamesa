# Sprint Handoff — SHR-04 — Snapshot Share

**Status:** DONE
**Branch:** `security/shr-04-snapshot-share`
**Commits:** `security(sharing): snapshot share`

## What changed
- New owner-side `SnapshotSharePublisher` (`src/DocumentIDE.Core/Sharing/SnapshotSharePublisher.cs`): freezes explicit member artifacts at publication time. Markdown sources are rendered via injected `ISnapshotMarkdownRenderer` (owner-side MD-02 pattern; Core takes no Markdown/HTML dependency); DOCX previews are copied verbatim from owner-provided WORD-04 cache bytes — the publisher has no `IQuickLookRenderer`/Word reference and throws if DOCX bytes are missing instead of rendering. Only allowed public metadata is copied via `ShareManifest.Create` (no physical paths); manifest carries frozen `SnapshotVersion = shr04-v1`; manifest + artifacts are written atomically (temp + move); republishing a frozen share throws `InvalidOperationException` so later local edits can never silently rewrite the snapshot. Original files are never copied (`originals/` absent; both download flags forced OFF — original download stays absent unless SHR-07 later enables it).
- Additive `ShareManifest.SnapshotVersion` (nullable; earlier-sprint manifests deserialize unchanged).
- New ShareHost `SnapshotEndpoints` (`src/DocumentIDE.ShareHost/Endpoints/SnapshotEndpoints.cs`, wired in `Program.cs`): token-scoped `GET /share/{id}/snapshot` (frozen manifest + member list) and `GET /share/{id}/documents/{docId}/preview` (frozen preview HTML, re-sanitized at the public boundary, 413 over budget). Nonmember IDs return 404; traversal returns 403/404 via `ShareTokenService` containment. References Core + maintained sanitizer only — no Word/Interop/Infrastructure/mutation references (proven by test at assembly, ctor, and code-identifier level).
- New `tests/DocumentIDE.IntegrationTests/Sharing/SnapshotShareTests.cs` (8 tests): frozen-snapshot-on-later-edit (Red-first), DOCX-bytes-verbatim, Markdown-sanitized-at-publish, no-physical-paths, frozen-versioned-manifest, nonmember-404, preview-served/download-absent, ShareHost-never-invokes-Word.

## Files changed
- `src/DocumentIDE.Core/Sharing/SnapshotSharePublisher.cs` (new — planned file, exact name)
- `src/DocumentIDE.Core/Sharing/ShareManifest.cs` (added `SnapshotVersion`)
- `src/DocumentIDE.ShareHost/Endpoints/SnapshotEndpoints.cs` (new — planned file, exact path)
- `src/DocumentIDE.ShareHost/Program.cs` (wire-up call only)
- `tests/DocumentIDE.IntegrationTests/Sharing/SnapshotShareTests.cs` (new — planned file, exact path)
- `docs/sprints/SPRINT-STATUS.md` (SHR-04 → DONE)

## Verification
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~SnapshotShareTests` → PASS (8/8; Red confirmed via CS0246 before implementation)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~Sharing` → PASS (29/29)
- `dotnet test tests/DocumentIDE.SecurityTests` → PASS (91/91, incl. QA-05 `PublicShareAttackSurfaceTests`)
- `dotnet test tests/DocumentIDE.Core.Tests --filter FullyQualifiedName~Sharing` → PASS (89/89)
- `dotnet test tests/DocumentIDE.ArchitectureTests` → PASS (7/7)

## Security & File-Safety Gate Confirmation
- [x] no user file was mutated through an unreviewed/bypass path (publisher writes only under the app-owned share root, atomically; source files only read)
- [x] no arbitrary path or secret is exposed (manifest via `ShareManifest.Create` sanitization; artifact resolution via `GetArtifactPath` containment; test asserts no drive paths/`LocalApplicationData`)
- [x] dependency/security boundaries remain intact (ShareHost references Core only; no Word/Interop/Infrastructure; no POST/PUT/PATCH/DELETE added)
- [x] no destructive action silently broadened its scope (republish rejected with typed error; membership frozen at publish)
- [x] relevant higher-level safety rules remain stronger than convenience (ShareHost never invokes Word; original download OFF default with 403 + no `originals/` bytes)

## Acceptance criteria
- [x] Original download absent unless permission later enables it (`MemberPreview_IsServed_OriginalDownload_AbsentUnlessEnabled`: preview 200, `download-original` 403, no `originals/` dir; flags forced OFF at publish, SHR-07 owns enabling)
- [x] Later local edit does not change Snapshot (`LaterLocalEdit_DoesNotChangeSnapshot`)
- [x] Nonmember ID returns not found/denied (`NonmemberDocument_ReturnsNotFound_OnViewerEndpoints`)
- [x] ShareHost does not invoke Word (`ShareHost_DoesNotInvokeWord`)

## Deviations / spec conflicts
None. One mapping note (per sprint § planned-files rule): `SnapshotVersion` was added to the existing SHR-01 `ShareManifest` rather than creating a duplicate manifest abstraction.

## Risks discovered
- Publisher snapshot IDs become artifact filenames (`previews/{docId}.html`); doc IDs with separators/traversal are rejected with a typed error, but a future caller generating doc IDs from filenames should sanitize first.
- New snapshot content requires a new share (republish throws); live updates are SHR-05's scope.

## Next eligible sprint(s)
- `SHR-05` — Membership-Safe Live Share (now unblocked on the SHR-04 side; still needs FS-03 — DONE — verify status flip)
- `SHR-07` — Share Download Permissions (now unblocked on the SHR-04 side; still needs PDF-01 — DONE — verify status flip)
