# Sprint Handoff — SHR-06 — Share-Scoped Search & Graph

**Status:** DONE
**Branch:** `feat/shr-06-share-scoped-search-graph`
**Commits:** `security(sharing): share-scoped search & graph`

## What changed
- New ShareHost `ShareSearchService` (`src/DocumentIDE.ShareHost/Search/ShareSearchService.cs`): server-side share-scoped search built only from prepared share artifacts — the frozen manifest (member set + virtual display names) plus per-member `previews/{docId}.html` artifacts. Only manifest member IDs are iterated (ShareMember constraint enforced in the backend query; no workspace/index access, so no "whole workspace then UI-filtered" stage). Deterministic exact ranking: filename-exact, then filename-partial, then body (manifest order within tiers). Empty/oversized queries return empty results; missing/unresolvable preview artifacts are skipped; results carry only `documentId`, `displayName`, `matchSource`, `snippet`, and virtual `previewUrl` (viewer navigation) — no local paths.
- New ShareHost `ShareGraphService` (`src/DocumentIDE.ShareHost/Graph/ShareGraphService.cs`): nodes are exactly the manifest members (id + virtual display name + virtual preview URL); edges come from the prepared `graph.json` share artifact (absent/malformed/oversized → member nodes with no edges, never a throw) and are filtered server-side to both-endpoints-members. Edges touching a private node are omitted entirely, so viewers cannot probe nonmember existence. Only endpoint ids + relation labels are emitted; any path-like artifact fields are ignored.
- `src/DocumentIDE.ShareHost/Program.cs`: registered both services as singletons and added token-scoped `GET /share/{id}/search?q=` and `GET /share/{id}/graph` (missing manifest → 404 without leaking paths; traversal → 403; global viewer CSP/rate-limit middleware applies unchanged, incl. the 30/min search budget).
- New `tests/DocumentIDE.IntegrationTests/Sharing/ShareScopeTests.cs` (8 tests): search-cannot-find-nonmember-content (Red-first: stray nonmember preview bytes on disk stay invisible while member content stays findable), unknown-share 404 without path leak (search + graph), empty-query empty results, result viewer-navigation (previewUrl serves the member), graph-omits-edge-to-private-node (both directions), graph-without-artifact (members, no edges), payload-contains-no-local-path (manifest + search + graph).
- Consequential SHR-08 test update: `ShareWebSecurityTests.RateLimit_SearchReads_Return429` expected 404 while the search route was reserved-but-unimplemented (its own comment said the budget "must apply even before the SHR-06 search implementation lands"); now expects 200 with empty results for the 30 in-budget requests. The 429 enforcement assertion is unchanged.

## Files changed
- `src/DocumentIDE.ShareHost/Search/ShareSearchService.cs` (new — planned file, exact path)
- `src/DocumentIDE.ShareHost/Graph/ShareGraphService.cs` (new — planned file, exact path)
- `src/DocumentIDE.ShareHost/Program.cs` (service registration + `/search` + `/graph` endpoints + `LoadShareManifest` helper)
- `tests/DocumentIDE.IntegrationTests/Sharing/ShareScopeTests.cs` (new — planned file, exact path)
- `tests/DocumentIDE.IntegrationTests/Sharing/ShareWebSecurityTests.cs` (search rate-limit expectation 404 → 200, comment updated)
- `docs/sprints/SPRINT-STATUS.md` (SHR-06 → DONE)
- `docs/sprints/handoffs/SHR-06-HANDOFF.md` (this file)

## Verification
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~ShareScopeTests"` → PASS (8/8; Red confirmed: 5 failed with NotFound before implementation, incl. `Search_CannotFindNonmemberContent`)
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~Sharing"` → PASS (53/53; includes updated SHR-08 search rate-limit test)
- `dotnet test tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj` → PASS (91/91, incl. QA-05 `PublicShareAttackSurfaceTests`)
- `dotnet test tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj` → PASS (7/7, incl. ShareHost privileged-dependency boundary)
- New ShareHost sources verified free of Word/Interop/Infrastructure/Mutation/Secret identifiers (boundary grep, no matches).

## Security & File-Safety Gate Confirmation
- [x] no user file was mutated through an unreviewed/bypass path (both services are read-only: manifest/preview/graph artifacts only read; no POST/PUT/PATCH/DELETE added)
- [x] no arbitrary path or secret is exposed (artifact resolution via `GetArtifactPath` containment; payloads carry virtual ids/names/URLs only; `Payload_ContainsNoLocalPath` asserts manifest + search + graph)
- [x] dependency/security boundaries remain intact (ShareHost references Core only; no Word/Interop/Infrastructure/Api/Desktop; narrow csproj graph untouched; QA-05 + architecture suites pass unchanged)
- [x] no destructive action silently broadened its scope (search/graph are read-only viewer routes; membership still owner-explicit per SHR-05)
- [x] relevant higher-level safety rules remain stronger than convenience (scope enforced at backend query per spec §19, not UI-filtered; graph both-endpoints rule per spec §20)

## Acceptance criteria
- [x] Scope enforcement is server-side (`Search_CannotFindNonmemberContent`: stray nonmember bytes on disk never match; `Graph_OmitsEdgeToPrivateNode`: private-touching edges omitted with the node itself undisclosed)

## Deviations / spec conflicts
- Planned branch was `security/shr-06-…`; implemented on `feat/shr-06-share-scoped-search-graph` per the dispatch task instruction. No spec impact.
- SHR-08 `RateLimit_SearchReads_Return429` expectation updated 404 → 200 (documented above); the test's own comment anticipated the SHR-06 implementation changing the in-budget status. Rate-limit behavior itself unchanged.
- No owner-side search-index/graph publisher was added: the sprint's planned files are ShareHost-side services plus tests, and the "share-scoped search index/representation" is satisfied by the manifest + prepared preview/graph artifacts the existing SHR-04/SHR-05 publishers already freeze. A future owner-side `graph.json` publisher can drop the artifact into the share root with no ShareHost change.

## Risks discovered
- Preview-HTML text extraction is tag-stripping, not semantic parsing: search matches rendered text (headings/body/tables-as-rendered) rather than structured SRCH-02 tiers. Acceptable for a viewer-scoped search over frozen artifacts; documented ranking is filename-exact > filename-partial > body.
- `graph.json` has no owner-side publisher yet: shares published before such a publisher exists serve member nodes with zero edges (tested). Edge `relationType`/`relationOrigin` values are passed through from the artifact; a future publisher should restrict them to the `RelationType`/`RelationOrigin` vocabularies.

## Next eligible sprint(s)
- None — terminal sprint (no downstream). SHR-11 (Owner Share Management UI) is unaffected.
