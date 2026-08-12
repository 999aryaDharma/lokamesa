# Sprint Handoff — AI-02 — Local Embedding Provider

**Status:** DONE  
**Branch:** `feat/ai-02-local-embedding-provider`  
**Commits:** `90c3d39`

## What changed
- Implemented `LocalEmbeddingProvider` implementing `IEmbeddingProvider` for local offline vector embedding generation. Uses deterministic n-gram & word feature hashing vectorizer with sublinear TF scaling and L2 normalization.
- Enforced bounded CPU, memory, and concurrency controls using `SemaphoreSlim`, max character limits per document, and `CancellationToken` checks.
- Created `SqliteEmbeddingStore` implementing `IEmbeddingStore` with SQLite migration `009_EmbeddingStore` for persisting rebuildable document embedding vectors to SQLite table `document_embeddings`.
- Added support for index rebuilding from current documents via `RebuildIndexAsync`.
- Added integration test suite `LocalEmbeddingProviderTests` covering offline embedding generation, index rebuilding, bounded resource/concurrency execution with cancellation, unavailable model edge cases, and content fingerprint staleness invalidation.

## Files changed
- `src/DocumentIDE.Infrastructure/Semantic/LocalEmbeddingProvider.cs`
- `src/DocumentIDE.Infrastructure/Semantic/SqliteEmbeddingStore.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/009_EmbeddingStore.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`
- `tests/DocumentIDE.IntegrationTests/Semantic/LocalEmbeddingProviderTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/AI-02-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter "FullyQualifiedName~LocalEmbeddingProviderTests"` → PASS (5 tests)
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test` → PASS (177 total tests across ArchitectureTests, Core.Tests, IntegrationTests, SecurityTests)

## Acceptance criteria
- [x] No source document leaves machine (`IsExternal` is `false`, pure offline local execution with zero network calls).
- [x] Embedding generation works offline.
- [x] Index rebuilds from documents.
- [x] Resource use is bounded during batch embedding.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path (embeddings are derived read-only data)
- [x] no arbitrary path or secret is exposed
- [x] dependency/security boundaries remain intact (`LocalEmbeddingProvider` is encapsulated in `Infrastructure.Semantic`)
- [x] no destructive action silently broadened its scope
- [x] relevant higher-level safety rules remain stronger than convenience

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `AI-04` — Semantic Graph Suggestions (unblocked and now `READY`)
