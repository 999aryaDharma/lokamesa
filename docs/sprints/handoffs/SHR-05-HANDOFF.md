# Sprint Handoff — SHR-05 — Membership-Safe Live Share

**Status:** DONE
**Branch:** `security/shr-05-membership-safe-live-share`
**Commits:** `security(sharing): membership-safe live share`

## What changed
- New owner-side `LiveSharePublisher` (`src/DocumentIDE.Core/Sharing/LiveSharePublisher.cs`): live means content updates for existing members only. Initial `Publish` freezes the explicit member set at revision 1; `HandleFileChange` routes FS-03 `FileChangeEvent`s through the current member set (member Created/Modified/Renamed → artifact refresh + `LiveRevision` bump; unknown documents → `IgnoredNonMember`, deletions → `IgnoredChangeKind`, unresolvable content → `IgnoredNoResolvedContent` — never published). `NoteCollectionChanged` is a deliberate membership no-op. Explicit owner-only `AddMember`/`RemoveMember` mutate membership (artifact publish/delete + revision bump). `Subscribe` wires a real FS-03 `FileChangeCoordinator.OnFileChanged` and returns a detaching `IDisposable`. Markdown renders via injected `ISnapshotMarkdownRenderer` (reused SHR-04 abstraction, no new Core dependency); DOCX previews are copied verbatim from owner-provided WORD-04 cache bytes and the publisher throws if DOCX bytes are missing instead of rendering. All manifest/artifact writes are atomic (temp + move); live artifact replace uses overwrite:true because live artifacts are rewritten by design (snapshot SHR-04 used overwrite:false for write-once frozen artifacts). Original files are never copied and both download flags are forced OFF.
- Additive `ShareManifest.LiveRevision` (int, default 0; earlier-sprint manifests deserialize unchanged).
- New `tests/DocumentIDE.IntegrationTests/Sharing/LiveShareMembershipTests.cs` (7 tests): new-file-not-public (Red-first), collection-change-no-auto-publish, member-update-after-refresh (incl. live ShareHost preview serving), nonmember-refresh typed error, explicit add/remove (incl. viewer 404 after remove), atomic versioning (no tmp debris, revision 1→2), real-coordinator watcher subscription refreshing members only.
- No ShareHost change: the existing manifest-driven viewer endpoints (`/snapshot`, `/documents/{id}/preview`, generic manifest/documents/artifact routes) read `manifest.json` from disk, so refreshed live revisions are served with no new public route and no new privileged dependency.

## Files changed
- `src/DocumentIDE.Core/Sharing/LiveSharePublisher.cs` (new — planned file, exact name)
- `src/DocumentIDE.Core/Sharing/ShareManifest.cs` (added `LiveRevision`)
- `tests/DocumentIDE.IntegrationTests/Sharing/LiveShareMembershipTests.cs` (new — planned file, exact path)
- `docs/sprints/SPRINT-STATUS.md` (SHR-05 → DONE; SHR-06/SHR-11 recalculated)

## Verification
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~LiveShareMembershipTests` → PASS (7/7; Red confirmed via CS0246 before implementation, then IOException overwrite bug caught by tests and fixed)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~Sharing` → PASS (36/36)
- `dotnet test tests/DocumentIDE.SecurityTests` → PASS (91/91, incl. QA-05 `PublicShareAttackSurfaceTests`)
- `dotnet test tests/DocumentIDE.Core.Tests --filter FullyQualifiedName~Sharing` → PASS (89/89)
- `dotnet test tests/DocumentIDE.ArchitectureTests` → PASS (7/7)

## Security & File-Safety Gate Confirmation
- [x] no user file was mutated through an unreviewed/bypass path (publisher writes only under the app-owned share root, atomically; source files only read; RemoveMember deletes only app-owned `previews/*.html` via containment-checked paths)
- [x] no arbitrary path or secret is exposed (manifest via `ShareManifest.Create` sanitization; artifact resolution via `GetArtifactPath` containment; doc-ID traversal rejected with typed error)
- [x] dependency/security boundaries remain intact (Core-only publisher reusing `ISnapshotMarkdownRenderer`; no Word/Interop/Infrastructure; no ShareHost change, no POST/PUT/PATCH/DELETE added)
- [x] no destructive action silently broadened its scope (membership changes require explicit owner ops; watcher Created for unknown docs is ignored; Deleted never removes published artifacts implicitly)
- [x] relevant higher-level safety rules remain stronger than convenience (live content ≠ live membership enforced by typed outcomes and tests)

## Acceptance criteria
- [x] Live content ≠ live membership (`NewFileInSharedFolder_IsNotPublic`, `CollectionMembershipChange_DoesNotAutoPublish`, `WatcherSubscription_RefreshesMemberOnly` + ShareHost preview serving refreshed member content in `ExistingMemberUpdate_AppearsAfterRefresh`)

## Deviations / spec conflicts
None. Two mapping notes (per sprint § planned-files rule): `ISnapshotMarkdownRenderer` from SHR-04 is reused instead of a duplicate live renderer abstraction; live artifact records reuse the existing `ShareSnapshotArtifact` list (keyed update-or-add by DocumentId) instead of a parallel live-artifact collection. No `Program.cs` wire-up was needed — documented above.

## Risks discovered
- Publisher live IDs become artifact filenames (`previews/{docId}.html`); doc IDs with separators/traversal are rejected with a typed error, same as SHR-04.
- `BumpRevision` reads the on-disk manifest revision under an instance lock; concurrent publishers on the same share root from different processes could interleave revisions (same single-owner-process assumption as SHR-04).

## Next eligible sprint(s)
- `SHR-06` — Share-Scoped Search & Graph (now unblocked on the SHR-05 side; still needs SRCH-02 — DONE — and GR-03 — DONE — verify status flip)
- `SHR-11` — Owner Share Management UI (now unblocked on the SHR-05 side; still needs SHR-07 and SHR-09 — verify status flip)
