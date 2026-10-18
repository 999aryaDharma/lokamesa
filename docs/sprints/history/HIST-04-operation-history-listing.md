# HIST-04 — Operation History Listing API

**Status:** READY
**Priority:** P1
**Kind:** feature
**Recommended branch:** `feat/operation-history-listing-api`

## Goal

Give Document IDE History a real data source: a persisted, paged listing of operations with their
outcome and storage usage, so screen 10's **Operations** tab renders real rows instead of a typed
unavailable state.

## Why this sprint exists

UI-03 delivered screen 10 structurally (`HistoryScreen` with an `Operations | Git` segmented control,
the shipped `HistoryPanel` and `UndoConflictDialog`), but the **Operations** tab is data-blocked. Verified
during UI-03 part B:

- the API exposes per-plan routes only — `POST /api/batch-operations/{planId}/apply|undo` and
  `GET /api/batch-operations/{planId}/history`;
- there is **no route that lists operations** and **no route for history storage usage**;
- operation type, status and per-file undo outcomes are **not persisted**, so even a listing route could
  not populate the shipped panel's chips or the storage meter.

The shell therefore renders a typed unavailable state (`history-operations-unavailable`) and names the
existing routes. That is honest, but it is not the design. This sprint closes the gap without weakening
anything.

## Depends On

- `HIST-01` — History storage (`DONE`)
- `HIST-02` — Undo (`DONE`)
- `BAT-04` — reviewed apply (the write path that must record operations)

## Required Reading

- `AGENTS.md`
- `docs/specs/02-domain-data-model.md` (history tables)
- `docs/specs/07-batch-operation-engine.md`
- `docs/specs/13-source-control-git-worktree-spec.md` (only for the typed-outcome/route conventions)
- `docs/sprints/ui/UI-03-mac-screens-slicing.md` (screen 10 mapping)
- `src/DocumentIDE.Web/src/features/history/HistoryScreen.tsx` (the `OperationHistorySource` seam)
- `src/DocumentIDE.Web/src/features/history/HistoryPanel.tsx` (what the panel needs)

## In Scope

- Persist one operation record per applied/undone operation: id, kind, plan id, started/completed time,
  outcome (`Applied` / `Undone` / `Failed`), affected file count, and per-file outcomes.
- Persist/report history storage usage (used bytes, capacity) from the existing storage implementation.
- Append-only, privileged, paginated read routes:
  - `GET api/history/operations?skip=&limit=` (bounded; typed page envelope)
  - `GET api/history/storage`
- Wire the `OperationHistorySource` seam in `HistoryScreen` to those routes so the shipped panel renders
  real rows, real outcome chips and the real meter.
- Keep `UndoConflictDialog` reachable and actionable from a real operation row.

## Out of Scope

- Re-designing `HistoryPanel` or `HistoryScreen`.
- Changing undo semantics or the recovery-snapshot rules.
- A unified Document IDE + Git timeline (that separation is deliberate).
- Unbounded payloads: the listing is paged and bounded.

## Planned Files

- migration for the operation record (next free number)
- `src/DocumentIDE.Core/History/OperationHistoryRecord.cs` (+ repository contract)
- `src/DocumentIDE.Infrastructure/History/*` (repository + storage usage)
- `src/DocumentIDE.Api/Controllers/*` (append-only routes)
- `src/DocumentIDE.Web/src/features/history/HistoryScreen.tsx` (wire the seam)
- tests in IntegrationTests + Core.Tests + the History component tests

## Required Tests

- [ ] An applied operation is listed with the correct outcome and file count; an undone one shows
      `Undone`; a failed one shows `Failed`.
- [ ] Pagination is bounded (`limit` clamped), `skip` works, and an empty history returns an empty page
      (distinct from "unavailable").
- [ ] Storage usage reflects real recorded bytes and capacity; the meter is not fabricated.
- [ ] A route failure stays typed and the shell renders the unavailable state, not an empty list.
- [ ] Missing/invalid token `401`, hostile Origin/Host `403` (same guard as every privileged route).
- [ ] Screen 10 renders real rows end-to-end (component test against the real payload shape).
- [ ] Undo from a listed operation opens the conflict dialog when it conflicts, and completes otherwise.

At minimum the first behaviour must be demonstrated by a failing test before implementation.

## Security & File-Safety Gate

- [ ] no user file mutated through an unreviewed path
- [ ] no arbitrary shell command / path injection
- [ ] no repository escape
- [ ] no secret/credential output
- [ ] no destructive action broadened
- [ ] listing is read-only; undo keeps its existing reviewed/guarded semantics

## Acceptance Criteria

- [ ] Screen 10's Operations tab renders real operations with real outcome chips and the real storage meter.
- [ ] `history-operations-unavailable` is only shown for a genuine failure, never for "no data".
- [ ] No fabricated rows, counts or meter values anywhere.

## Definition of Done

- [ ] all steps complete; required tests pass; affected suite green; no skipped required test
- [ ] `git diff` self-reviewed; no unintended deletions
- [ ] handoff written (`docs/sprints/handoffs/HIST-04-HANDOFF.md`)
- [ ] board row left to the controller

## Commit Guidance

```text
feat(history): persist and expose the operation history listing
```
