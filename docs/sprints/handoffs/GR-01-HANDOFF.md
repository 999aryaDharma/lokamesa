# Sprint Handoff — GR-01

**Status:** DONE  
**Branch:** `feat/gr-01-relation-store-extraction`  
**Commits:** `b1c3843`

## What changed
- Created SQLite migration `006_RelationStore` (`relations` table with workspace/source/target/type/origin/confidence/metadata_json columns and indexes) and registered it in `MigrationRunner`.
- Implemented `Relation` domain model and `IRelationRepository` contract separating `RelationOrigin` (`Explicit`, `Inferred`, `Manual`) from `RelationType` (`MarkdownLink`, `Wikilink`, `FrontmatterDependency`, `ManualRelation`, `SemanticSuggestion`).
- Implemented `RelationRepository` with support for derived relation replacement during reparse (`ReplaceDerivedRelationsAsync`), keeping manual and inferred relations stable and distinct.
- Added comprehensive integration tests covering reparse updates, manual relation stability across refreshes, distinct origin tracking, and database restart persistence.

## Files changed
- `src/DocumentIDE.Core/Domain/Relation.cs`
- `src/DocumentIDE.Core/Domain/Repositories/IRelationRepository.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/006_RelationStore.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/RelationRepository.cs`
- `tests/DocumentIDE.IntegrationTests/Graph/RelationRepositoryTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/GR-01-HANDOFF.md`

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path
- [x] no arbitrary path or secret is exposed
- [x] dependency/security boundaries remain intact
- [x] no destructive action silently broadened its scope
- [x] relevant higher-level safety rules remain stronger than convenience

## Verification
- `& "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe" test` → PASS (445 tests passed across Architecture, Core.Tests, and IntegrationTests).

## Acceptance criteria
- [x] Semantic relation cannot masquerade as explicit (verified by `OriginAndTypeRemainDistinct_SemanticCannotMasqueradeAsExplicit` test in `RelationRepositoryTests.cs`).

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `GR-02` — Backlinks & Reference Inspector (Dependencies `GR-01`, `MD-04` now both `DONE`).
