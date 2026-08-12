# Sprint Handoff — GR-04

**Status:** DONE  
**Branch:** `feat/gr-04-manual-relations`  
**Commits:** `feat(graph): manual relations implementation`

## What changed
- Implemented `ManualRelationService` in `src/DocumentIDE.Core/Graph/ManualRelationService.cs`:
  1. Controlled vocabulary for manual relation types (`References`, `RelatedTo`, `DependsOn`).
  2. Creation, update, and deletion of manual relations via `IRelationRepository`.
  3. Strict guarantee: manual relations do NOT modify any Markdown file on disk and survive Markdown reparsing (`ReplaceDerivedRelationsAsync`).
  4. Manual relation origin is stored as `Manual`.
- Implemented comprehensive unit tests in `tests/DocumentIDE.Core.Tests/Graph/ManualRelationTests.cs`:
  - `ManualRelation_SurvivesMarkdownReparse`
  - `DeleteManualRelation_DoesNotEditMarkdown`
  - `ManualRelation_HasManualOrigin`
  - `CreateManualRelation_WithRelatedToType_Persisted`
  - `CreateManualRelation_WithDependsOnType_Persisted`
  - `UpdateManualRelation_ChangesType`
  - `CreateManualRelation_MissingTarget_StillPersists`
  - `CreateManualRelation_DoesNotModifyMarkdown`

## Files changed
- `src/DocumentIDE.Core/Graph/ManualRelationService.cs`
- `tests/DocumentIDE.Core.Tests/Graph/ManualRelationTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/GR-04-HANDOFF.md`

## Verification
- `dotnet test` → All 326 Core tests passed (100%).

## Acceptance criteria
- [x] Manual relations survive Markdown reparse without modifying Markdown files on disk.
- [x] Controlled vocabulary for manual relation types supported.
- [x] Origin stored explicitly as `Manual`.
