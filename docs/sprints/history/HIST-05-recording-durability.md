# HIST-05 — At-Least-Once Operation Recording and Backfill

**Status:** READY
**Priority:** P1
**Kind:** durability fix + migration
**Recommended branch:** `fix/history-recording-durability`

## Goal

Close the at-most-once gap in the operation-history listing: if the database write fails after a mutation
has already been applied, the operation currently vanishes from the listing. Also backfill operations
recorded before HIST-04.

## Depends On

- HIST-04 — operation listing — DONE
- DATA-01 — uses migration `013`, so this sprint takes **`014`**

## The gap, precisely

`BatchApplyEngine` (Core) calls the optional `IOperationHistoryRecorder` **after** the mutation has been
committed, inside a catch-all that must not mask the mutation outcome. So:

```
mutate -> commit -> record FAILS -> API returns 409 -> the change exists on disk,
                                                       the operation is absent from History
```

The per-file JSONL audit still exists (`apply-{planId}.jsonl`), so the data is recoverable, but the
listing lies by omission.

## In Scope

1. **Provisional row before mutation.** When a recorder is supplied, write the operation row as
   `Pending` **before** the first mutation. If that write fails, **fail closed**: do not mutate, return a
   typed failure. This mirrors the existing product rule "if recovery cannot be created, mutation is
   blocked".
2. **Final upsert after.** The existing `RecordAppliedAsync` upsert moves the row to its real outcome.
   A retry must still produce exactly one row.
3. **Undo** updates the same row (unchanged), including when the apply row was provisional.
4. **Backfill** existing `apply-{planId}.jsonl` records into `operation_history` when the row is absent.
   Bounded, idempotent, startup-only, and it must never overwrite a row that already exists. Migration
   `014` may create the column/enum support; the backfill itself must be safe to run on every start.
5. `IOperationHistoryRecorder` stays **optional** in Core: a host/test that supplies none keeps working
   unchanged.

## Out of Scope

- Changing apply/undo mutation semantics, recovery, or the plan-review flow.
- Making the JSONL audit redundant.
- UI changes (screen 10 already renders the listed rows).

## Stop-and-Report Clause

If failing closed on a provisional-write failure would contradict spec 07 §25 (plan status transitions) or
any higher-level rule, **stop and report the exact conflict** with the smallest safe alternative (for
example: record the provisional row outside the mutation transaction but surface the failure as a typed
warning and keep the JSONL as the audit of record).

## Required Tests

- [ ] Recorder failure **before** mutation: no file changed, typed failure, no history row.
- [ ] Success: exactly one row, final outcome, correct file count.
- [ ] Apply succeeds but the **final** upsert fails: the provisional row still exists (the operation is
      listed), and the failure is surfaced the way the surrounding code surfaces it.
- [ ] Retry after a 409 does not duplicate the row.
- [ ] Undo updates the same row (applied → undone), including a backfilled one.
- [ ] Backfill: an orphan JSONL apply record becomes a row; running twice creates no duplicate; an
      existing row is not overwritten.
- [ ] No recorder supplied → behaviour identical to today.

## Definition of Done

- [ ] required tests pass; Integration (twice, migration), Core, Security, Architecture green
- [ ] handoff with the exact transaction boundary and the fail-closed decision recorded
- [ ] board row left to the controller

## Commit Guidance

```text
fix(history): record operations before mutation and backfill the listing
```
