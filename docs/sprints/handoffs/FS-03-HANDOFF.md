# Sprint Handoff — FS-03

**Status:** DONE  
**Branch:** `feat/fs-03-runtime-file-watcher`  
**Commits:** `3d16a60`

## What changed
- Created `FileChangeType` enum and `FileChangeEvent` class in `DocumentIDE.Core.Filesystem`.
- Created `RawWatcherEventArgs` and `ISourceWatcher` interface in `DocumentIDE.Core.Filesystem`.
- Implemented `SourceWatcher` in `DocumentIDE.Infrastructure.Filesystem` wrapping Windows `FileSystemWatcher` per active `FolderSource`.
- Implemented `FileChangeCoordinator` in `DocumentIDE.Core.Filesystem` to handle non-blocking raw event collection, debouncing/coalescing rapid Word save bursts, physical stat verification via `DocumentIdentityService`, typed event publication (`Created`, `Modified`, `Renamed`, `Deleted`), and scheduling background `Reconciliation` jobs on watcher overflow.
- Added `IJobScheduler` interface to `DocumentIDE.Core.Jobs` and implemented it on `LocalJobScheduler`.
- Added unit and integration test suite in `tests/DocumentIDE.IntegrationTests/Filesystem/SourceWatcherTests.cs` covering Word save burst coalescing, duplicate event deduplication, watcher overflow reconciliation scheduling, real filesystem watcher creation events, deletion handling, and ignored file filtering.

## Files changed
- `src/DocumentIDE.Core/Filesystem/FileChangeType.cs`
- `src/DocumentIDE.Core/Filesystem/FileChangeEvent.cs`
- `src/DocumentIDE.Core/Filesystem/RawWatcherEventArgs.cs`
- `src/DocumentIDE.Core/Filesystem/ISourceWatcher.cs`
- `src/DocumentIDE.Core/Filesystem/FileChangeCoordinator.cs`
- `src/DocumentIDE.Infrastructure/Filesystem/SourceWatcher.cs`
- `src/DocumentIDE.Core/Jobs/JobContracts.cs`
- `src/DocumentIDE.Infrastructure/Jobs/LocalJobScheduler.cs`
- `tests/DocumentIDE.IntegrationTests/Filesystem/SourceWatcherTests.cs`
- `docs/sprints/handoffs/FS-03-HANDOFF.md`

## Verification
- `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` → PASS (All .NET build, .NET unit & integration tests, frontend build, frontend unit tests, and architecture rules passed cleanly)

## Acceptance criteria
- [x] Watcher events are hints only — verified by `FileChangeCoordinator` inspecting physical filesystem state (`File.Exists`, `DocumentIdentityService.ResolveDocumentAsync`) before emitting logical events.
- [x] Expensive parse does not happen inside raw watcher callback — raw callbacks only enqueue paths to background timer without doing file stat or DB access.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path (FS-03 is strictly read-only watcher observation)
- [x] no arbitrary path or secret is exposed (only canonical source paths watched)
- [x] dependency/security boundaries remain intact (Core defines interfaces, Infrastructure implements)
- [x] no destructive action silently broadened its scope (N/A — no destructive operations in watcher)
- [x] relevant higher-level safety rules remain stronger than convenience (physical filesystem remains source of truth)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `FS-04` — Hybrid Startup Reconciliation (Depends on FS-02, FS-03)
- `FS-05` — Rename & Move Correlation (Depends on FS-02, FS-03)
- `WORD-02` — Word Editing/Lock Detection (Depends on WORD-01, FS-03)
- `IDX-06` — Incremental Index Scheduler (Depends on IDX-05, FS-03)
- `SHR-05` — Membership-Safe Live Share (Depends on SHR-04, FS-03)
- `HIST-02` — Conflict-Aware Undo (Depends on HIST-01, BAT-04, FS-03)
