# Sprint Handoff — WORD-03

**Status:** DONE
**Branch:** `perf/word-03-dedicated-sta-word-automation-queue`
**Commits:** (see git log for `perf(word): dedicated sta word automation queue`)

## What changed

- New `WordAutomationQueue` (`src/DocumentIDE.Infrastructure/Word/WordAutomationQueue.cs`):
  single dedicated STA worker thread (`Thread` with `SetApartmentState(STA)`,
  background, named `WordAutomationSTA`) draining a `BlockingCollection`
  FIFO queue. Concurrency of Word COM work is exactly 1 by construction
  (one worker, one item at a time); `MaxObservedConcurrency` counter proves it.
- Cancellation applies **between** documents: an item whose token is cancelled
  before execution starts never runs (task transitions to Canceled); a
  running COM operation always runs to completion (never aborted mid-write).
- `TryReleaseComObject` releases COM proxies deterministically
  (`Marshal.ReleaseComObject` loop, safe on null/non-COM). Queue keeps
  serving after a faulted operation (exceptions propagate to the caller).
- WORD-01 glue: `WordDesktopLauncher` accepts an optional
  `WordAutomationQueue` and routes the launch delegate through it when
  provided; falls back to the pre-existing `ComLock` otherwise.
  Registered as singleton in `src/DocumentIDE.Api/Program.cs`.
  Indexing/OpenXML paths never enqueue, so they stay independently concurrent
  (proven by `NonQueuedWork_RemainsConcurrentWhileQueueIsBusy`).

## Files changed

- `src/DocumentIDE.Infrastructure/Word/WordAutomationQueue.cs` (new)
- `src/DocumentIDE.Infrastructure/Word/WordDesktopLauncher.cs` (optional queue routing glue)
- `src/DocumentIDE.Api/Program.cs` (singleton registration)
- `tests/DocumentIDE.IntegrationTests/Word/WordAutomationQueueTests.cs` (new, 8 tests)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~WordAutomationQueueTests` → PASS (8/8)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~Word` → PASS (23/23)
- `dotnet test tests/DocumentIDE.Core.Tests --filter FullyQualifiedName~Word` → PASS (25/25)

## Acceptance criteria

- [x] Indexing/OpenXML remains independently concurrent (dedicated background-task test; queue only serializes enqueued COM work)
- [x] Max observed COM operations = 1 (8 concurrent callers, `MaxObservedConcurrency == 1`)
- [x] Worker is STA (apartment asserted on thread and inside operation)
- [x] No repeated orphan Word process (single reused worker; `Dispose` terminates it, `IsWorkerAlive == false`)

## Deviations / spec conflicts

None. No downstream PDF/QuickLook behavior implemented; the generic
`EnqueueAsync<T>` surface is what PDF-01/WORD-04 will use for
ExportPdf/RenderPreview.

## Risks discovered

- `EnqueueAsync(Func<Task>)` overload blocks the STA worker on the inner
  task; COM operations must stay synchronous delegates. Documented in code.
- Real Word COM soak (orphan-process watch over an extended session) still
  needs a Windows agent with Word installed; unit-level disposal is proven.

## Next eligible sprint(s)

- PDF-01 (Single DOCX PDF Export) — unblocked
- WORD-04 (High-Fidelity Quick Look) — unblocked

## Security & file-safety gate

- [x] no user file was mutated through an unreviewed/bypass path (queue executes caller delegates only; no file I/O of its own)
- [x] no arbitrary path or secret is exposed (no paths/secrets in queue API)
- [x] dependency/security boundaries remain intact (ShareHost untouched; queue lives in Infrastructure.Word, not referenced by ShareHost)
- [x] no destructive action silently broadened its scope (no destructive action added)
- [x] relevant higher-level safety rules remain stronger than convenience (COM concurrency exactly 1 enforced by construction, AGENTS.md rule preserved)
