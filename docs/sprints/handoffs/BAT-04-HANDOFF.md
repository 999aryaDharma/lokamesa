# Sprint Handoff — BAT-04 — Transactional Safe Apply Engine

**Status:** DONE
**Branch:** `fix/bat-04-transactional-safe-apply-engine`
**Commits:** `2487fd2` (implementation), `02f236e` (review fix round 1); completion metadata in the following docs commit.

## What changed
- New operation-agnostic transactional boundary `BatchApplyEngine.ApplyAsync(planId, mutate, validate, ct)`: plan gate (`CanApply`: Previewed, unexpired, not completed/failed/executing) → per-file checks in deterministic ordinal order → single recovery snapshot for all ready files (fail-closed) → per-file temp-mutate-validate-atomic-replace → typed per-file outcomes + reindex on success.
- Step 1: process-wide per-file application locks (`ConcurrentDictionary` of `SemaphoreSlim`, deterministic acquisition order, eviction when idle including the outer-`finally` path).
- Step 2: fingerprint equality — bounded read with reparse-point guard, SHA-256 hex ordinal compare, byte-identical semantics to the BAT-03 preview read; mismatch → `Skipped/Stale`.
- Step 3: external-edit probe (`WordEditStateProbe`: `~$` lock-file convention + exclusive-open probe); `Locked` → `Skipped/OpenInWord`, `Unknown` → `Skipped/LockUnknown` — no certainty invented (WORD-02 still IN_PROGRESS; probe self-describes as heuristic).
- Step 4: one `RecoverySnapshotService.CreateSnapshotAsync` before any mutation; any failure → all files `Failure/RecoveryBlocked`, plan `Failed`, result `Blocked`, nothing mutated.
- Step 5: `AtomicFileReplacer` — same-directory temp (`~apply-<guid>.tmp`, ignored by the watcher temp filter) → mutate → validate → strict `File.Replace` with deliberately no non-atomic fallback; rollback on any failure.
- Step 6: every outcome recorded (`Success`/`Skipped`/`Failure` incl. `MutationFailed`) + reindex trigger on success only. Cancel-path recording uses `CancellationToken.None`; snapshot catch orders `OperationCanceledException` first so cancellation stays typed.
- Result model `BatchApplyResult` (Completed/Blocked/Canceled) with typed reason codes: `UnknownDocument`, `InvalidPath`, `Unavailable`, `Stale`, `OpenInWord`, `LockUnknown`, `RecoveryBlocked`, `MutationFailed`, `Cancelled`.
- No downstream operation logic (mutator is an injected delegate), no API surface added (apply endpoint stays 404 per existing BAT-03 test), no new dependencies.

## Files changed
- `src/DocumentIDE.Core/Batch/BatchApplyEngine.cs` (new: engine + Core contracts)
- `src/DocumentIDE.Infrastructure/Filesystem/AtomicFileReplacer.cs` (new)
- `src/DocumentIDE.Infrastructure/Filesystem/WordEditStateProbe.cs` (new)
- `tests/DocumentIDE.IntegrationTests/Batch/SafeApplyTests.cs` (new, 19 tests)

Planned-file mapping: all three planned files created under planned names; no equivalent pre-existed. `StrictTextMutator.cs` was touched mid-sprint (one-line delegation) and reverted byte-identical in the fix round — net diff excludes it.

## Verification
SDK executable: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- `scripts/verify.ps1` → PASS on final tree: build, 428 Core + 192 Integration + 7 Architecture tests, Web build, 18 files / 80 Web tests, 0 skipped; log `artifacts/bat-04-verify.log`.
- Explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped (solution config omits it); log `artifacts/bat-04-security.log`.
- Focused `... --filter FullyQualifiedName~SafeApply` → 19/19 (was 14 before fix round); `~Batch` → 39/39; `~StrictTextMutator` → 8/8.
- TDD evidence: 6 implementation cycles with RED first (compile RED for mandated first behavior, behavioral RED for stale/lock/history/capacity/cancellation), plus fix-round RED reproductions against `2487fd2` for both Important findings; full log in `.superpowers/sdd/BAT-04-transactional-safe-apply-engine/final-report.md`.
- Task review of `2487fd2` returned Spec ❌ (2 Important: unrecorded mutation-failure outcomes; genuine-token cancellation escape; plus 4 minors). Fix round `02f236e` scoped re-reviewed: all findings ADDRESSED, no new breakage.

## Acceptance criteria
- [x] Cancellation never leaves half-written original — `Apply_CancellationLeavesNoHalfWrittenOriginal` (injected OCE) + `Apply_GenuineTokenCancellationReturnsTypedCanceledResult` (real canceled token + token-honoring recorder: typed `Canceled`, plan `Failed`, originals byte-identical, no temps). Holds by construction: mutation on temp, atomic `File.Replace` commit, rollback before recording.
- [x] Injected mutation failure leaves original unchanged — `Apply_MutationFailureLeavesOriginalUnchanged` (sprint-mandated first behavior, compile RED first).
- [x] Stale file is rejected — `Apply_StaleFileIsRejected`.
- [x] Open Word doc safely skipped/blocked — `Apply_OpenWordDocIsSkipped` + `Apply_UnknownExternalEditStateSkipsSafely`.
- [x] Successful file gets history record — `Apply_SuccessfulFileGetsHistoryRecord` (+ snapshot-content test).

## Security & file-safety gate
- [x] No user file mutated through an unreviewed/bypass path — apply requires Previewed+unexpired plan, fingerprint equality, lock/edit checks; only selected fingerprinted files; mutation via temp+atomic replace.
- [x] No arbitrary path or secret exposed — no API surface added; paths resolve from the document repository only; typed codes, no raw paths in results.
- [x] Dependency/security boundaries intact — Core abstractions + Infrastructure implementations, no new packages; Docx→Filesystem coupling introduced mid-sprint was reverted.
- [x] No destructive action silently broadened — fingerprint-less selected files yield typed `Skipped/Unavailable` instead of silent narrowing.
- [x] Higher-level safety rules stronger than convenience — snapshot fail-closed, stale rejection, `LockUnknown` skip, strict atomic replace, canceled/blocked plans require re-preview.

## Deviations / spec conflicts
- No spec weakened. Strict atomic replace has no `File.Copy` fallback (unlike DOCX-03's helper): a failed `File.Replace` fails the commit with the original unchanged — the safe direction.
- Canceled/blocked plans are stored as `Failed` (re-preview required); a dedicated plan status would touch the BAT-01 enum — out of scope.
- Production wiring is downstream: DI registration, SQLite history recorder, reindex trigger (→ `IncrementalIndexCoordinator`), and the apply API endpoint await BAT-05/11.

## Risks discovered
- Narrow race on two *simultaneous* same-plan applies (both read `Previewed` before either marks `Executing`); sequential repeats are rejected by `CanApply` and the stale check prevents double mutation in practice. Hardening needs a conditional status transition in BAT-01/03 repository code — tracked follow-up, not fixed here.
- Recorder hard-failure (non-token) during check-phase/cancel-path recording still escapes untyped and strands the plan in `Executing`; only the mutation-failure path is guarded. Same follow-up area.
- `TriggerReindexAsync` wraps `OperationCanceledException`; a future token-honoring reindex trigger could misreport last-file post-commit cancellation. No production trigger exists yet.
- Lock eviction is inherently racy (documented in code); content safety rests on fingerprint check + atomic replace.
- WORD-02 (real Word lock detection) still IN_PROGRESS; `WordEditStateProbe` is heuristic and can be strengthened without changing the engine.

## Next eligible sprint(s)
- `BAT-05` — Exact Search & Replace Operation (deps BAT-04, DOCX-03 DONE) — batch longpole.
- `BAT-08`, `BAT-09`, `BAT-10`, `HIST-02` — unblocked by this sprint.
- Older READY unchanged: `DOCX-04`, `CAN-04`, `CAN-08`, `QA-01`, `QA-02`, `SHR-08`.
