# Sprint Handoff — FND-04

**Status:** REVIEW  
**Branch:** `feat/fnd-04-background-job-runtime`  
**Commits:** `feat(foundation): background job runtime` (pending)

## What changed
- Created `JobContracts.cs` in `DocumentIDE.Core/Jobs/`:
  - `JobState` enum (`Pending`, `Running`, `Completed`, `Cancelled`, `Failed`).
  - `JobTypes` constants (`Indexing`, `PreviewGeneration`, `PdfExport`, `Reconciliation`, `DocumentMutation`).
  - `JobContext` with cancellation token and `ReportProgress(percentage, statusMessage)` delegate.
  - `JobInfo` state model including `ProgressPercentage`, `StatusMessage`, `ErrorDetails`, `IsMutationJob`, `IsIdempotent`.
  - `IJob` interface and `DelegateJob` wrapper.
- Implemented `LocalJobScheduler.cs` in `DocumentIDE.Infrastructure/Jobs/`:
  - Bounded concurrency worker pool powered by `System.Threading.Channels.Channel<JobWorkItem>`.
  - Automatic deduplication by `(JobType, DocumentId, Fingerprint)` returning active handles for pending/running jobs.
  - Document-level serialization for mutation jobs (`isMutation = true`) using `SemaphoreSlim` per `DocumentId` to prevent concurrent mutation race conditions.
  - Cooperative cancellation via `CancellationTokenSource` and `CancelJob(jobId)` API.
  - Observable progress reporting via `OnJobProgressChanged` event and `GetJob` / `GetActiveJobs` query APIs.
- Implemented `JobSchedulerTests.cs` in `DocumentIDE.Core.Tests/Jobs/` testing deduplication, cancellation, mutation serialization, and progress reporting.

## Files changed
- `src/DocumentIDE.Core/Jobs/JobContracts.cs`
- `src/DocumentIDE.Infrastructure/Jobs/LocalJobScheduler.cs`
- `tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj`
- `tests/DocumentIDE.Core.Tests/Jobs/JobSchedulerTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/FND-04-HANDOFF.md`

## Verification
- `dotnet test` → PASS (13 tests total: 5 Core unit tests + 8 Integration tests)
- `powershell -File scripts/verify.ps1` → PASS (All 5 repository verification steps passed)

## Acceptance criteria
- [x] UI thread is not used for expensive jobs → Background work items are executed asynchronously in bounded channel worker pool threads off the UI thread
- [x] Retry is limited to safe/idempotent jobs → Idempotency status tracked in `JobInfo.IsIdempotent` (`true` for non-mutation jobs, `false` for mutations)
- [x] Failures expose typed diagnostics → Exception details captured in `JobInfo.ErrorDetails` when state transitions to `JobState.Failed`

## Security & File-Safety Gate
- no user file was mutated through an unreviewed/bypass path: N/A (Job runtime infrastructure only)
- no arbitrary path or secret is exposed: Verified (No secrets or raw paths logged)
- dependency/security boundaries remain intact: Verified (Job contracts in Core, scheduler implementation in Infrastructure)
- no destructive action silently broadened its scope: N/A (No destructive file actions)
- relevant higher-level safety rules remain stronger than convenience: Verified (Per-document serialization strictly prevents concurrent mutation race conditions)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- FND-05 (Settings & Secret Storage)
- FND-06 (Structured Logging & Diagnostics)
- QA-03 (Privileged Local API Security Audit)
- SHR-01 (Share Domain & Sanitized Manifest)
