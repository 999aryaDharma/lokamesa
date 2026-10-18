# Sprint Handoff — AI-01 — Embedding Provider Abstraction

**Status:** DONE  
**Branch:** `feat/ai-01-embedding-provider-abstraction`  
**Commits:** `pending`

## What changed
- Defined local-first semantic provider contract `IEmbeddingProvider` with properties for `ProviderId`, `ModelId`, `Dimensions`, `IsExternal`, and methods for `IsAvailableAsync` and `GenerateEmbeddingsAsync`.
- Implemented `EmbeddingCoordinator` for managing vector embeddings, normalized input text preparation, bounded batch generation, cancellation support, staleness detection/invalidation, and optional external AI opt-in checks via `AppSettings`.
- Implemented `DocumentEmbedding` domain model, `EmbeddingResult` typed outcome model, and `IEmbeddingStore` / `InMemoryEmbeddingStore`.
- Added unit tests covering mock provider round-trips, normalized text preparation, content fingerprint and model ID staleness invalidations, bounded batching with cancellation, unconfigured provider handling, and external AI opt-in settings.

## Files changed
- `src/DocumentIDE.Core/Semantic/IEmbeddingProvider.cs`
- `src/DocumentIDE.Core/Semantic/DocumentEmbedding.cs`
- `src/DocumentIDE.Core/Semantic/EmbeddingResult.cs`
- `src/DocumentIDE.Core/Semantic/IEmbeddingStore.cs`
- `src/DocumentIDE.Core/Semantic/InMemoryEmbeddingStore.cs`
- `src/DocumentIDE.Core/Semantic/EmbeddingCoordinator.cs`
- `src/DocumentIDE.Core/Settings/AppSettings.cs`
- `tests/DocumentIDE.Core.Tests/Semantic/EmbeddingCoordinatorTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/AI-01-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter "FullyQualifiedName~EmbeddingCoordinatorTests"` → PASS (9 tests)
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test` → PASS (527 total tests across ArchitectureTests, Core.Tests, IntegrationTests)

## Acceptance criteria
- [x] External AI is optional (`EnableExternalEmbeddingProvider` defaults to `false`, disabled external provider returns `Disabled` result without error, core operates without provider).
- [x] Mock provider round-trip demonstrated in unit test suite.
- [x] Changing content/model invalidates embedding automatically in store and coordinator query.
- [x] Core exact search works with no provider configured without throwing exceptions.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path (embeddings are derived read-only data)
- [x] no arbitrary path or secret is exposed (`ISecretStore` and `ISettingsService` integration used for configuration)
- [x] dependency/security boundaries remain intact (`IEmbeddingProvider` keeps core decoupled from specific ML libraries)
- [x] no destructive action silently broadened its scope
- [x] relevant higher-level safety rules remain stronger than convenience

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `AI-02` — Local Embedding Provider (unblocked and now `READY`)
