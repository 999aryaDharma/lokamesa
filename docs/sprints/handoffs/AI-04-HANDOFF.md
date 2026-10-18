# Sprint Handoff — AI-04 (Semantic Graph Suggestions)

**Status:** DONE  
**Branch:** `feat/ai-04-semantic-graph-suggestions`  
**Commits:** Pending commit

## What changed
- Implemented `SemanticRelationService` in `src/DocumentIDE.Core/Graph/SemanticRelationService.cs` providing cosine similarity vector matching, top-K filtering (default K=5), minimum confidence thresholds, and dashed semantic edge generation (`RelationOrigin.Semantic`).
- Added support for filtering and toggling inferred-edge mode (`EnableInferredEdges` option).
- Added `RelationOrigin.Semantic` constant in `src/DocumentIDE.Core/Domain/Relation.cs`.
- Handled edge cases including unavailable embedding provider and stale embeddings.
- Added comprehensive unit test suite in `tests/DocumentIDE.Core.Tests/Graph/SemanticRelationTests.cs` following Red -> Green -> Refactor TDD cycle.

## Files changed
- `src/DocumentIDE.Core/Domain/Relation.cs`
- `src/DocumentIDE.Core/Graph/SemanticRelationService.cs`
- `tests/DocumentIDE.Core.Tests/Graph/SemanticRelationTests.cs`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test tests\DocumentIDE.Core.Tests\DocumentIDE.Core.Tests.csproj --filter "FullyQualifiedName~SemanticRelationTests"` → PASS (7/7 tests passed)

## Acceptance criteria
- [x] Compute top-K (`K=5` default) neighbors per document (Verified: `NoDocumentGetsMoreThanKDefaultSemanticEdges`).
- [x] Apply minimum configurable confidence threshold (Verified: `LowConfidenceEdgeIsOmitted`).
- [x] Persist origin=`semantic` and confidence (Verified: `RelationOrigin.Semantic` persisted and confidence set).
- [x] Render dashed edges (Verified: `InferredEdgeIsDashed` returns `IsDashed = true`).
- [x] Allow inferred-edge filter/off mode (Verified: `InferredEdgeFilterOffMode_OmitsInferredEdges`).
- [x] Semantic edge is never treated as explicit dependency (Verified: `SemanticEdge_NeverTreatedAsExplicitDependency`).

## Deviations / spec conflicts
None.

## Security & File-Safety Gate
- [x] No user file was mutated through an unreviewed/bypass path: N/A - Semantic relation generation operates in memory and relation store.
- [x] No arbitrary path or secret is exposed: Verified - strictly handles document IDs and vector embeddings.
- [x] Dependency/security boundaries remain intact: Verified.
- [x] No destructive action silently broadened its scope: Verified.
- [x] Relevant higher-level safety rules remain stronger than convenience: Verified.

## Risks discovered
None.

## Next eligible sprint(s)
- Downstream sprints dependent on AI-04 / CTX-04.
