# Sprint Handoff — WORD-02

**Status:** DONE
**Branch:** `feat/word-02-word-editing-lock-detection`
**Commits:** (see git log for `feat(word): word editing/lock detection`)

## What changed

- New `WordEditingStateDetector`
  (`src/DocumentIDE.Infrastructure/Word/WordEditingStateDetector.cs`,
  self-contained: detector + `WordEditingState`/`WordEditingSignal`/
  `WordEditingConfidence`/`WordMutationPreflightResult`/`WordPreflightDecision`
  records, no new Core/Infrastructure files outside the planned file):
  - Step 1: combines three signals — Word owner-lock/temp file presence
    (`~$` + full-name and `~$` + first-char-replaced variants, same directory),
    exclusive-access probe failure (`FileShare.None` open attempt;
    sharing-violation/access-denied reads as positive), and an injectable
    Word automation document-list probe (defaults to not-open when Word is
    unavailable; probe exceptions become a typed negative signal, never a
    silent fallback).
  - Step 2: exposes "Editing in Word" state (`IsEditing` + `Summary`).
  - Step 3: `Refresh(path)` re-evaluates on filesystem/process changes
    (FS-03 watcher hook point), caches per canonical path, raises
    `StateChanged` only when the verdict flips.
  - Step 4: no single heuristic is infallible — every verdict carries the
    per-signal breakdown; one positive signal yields `Low` confidence
    ("possibly"), two or more yield `High` ("likely").
  - Step 5: `CheckMutationPreflight` returns a typed verdict for mutation
    preflight — `Allowed`, `BlockedEditingInWord` (skip / open Word /
    retry after save), or `FileMissing` (missing is never mislabeled).
- New `EditingInWordBadge.tsx` (inspector): renders the "Editing in Word"
  badge with confidence label (`likely`/`possibly`) and optional
  "Open Word" action; renders nothing when not editing.
- Note: `WordAutomationQueue` (WORD-03, commit `2faa3a6` on the parent
  branch) is the execution path the automation probe will use for the
  document-list query once PDF-01/WORD-04 add those operations.

## Files changed

- `src/DocumentIDE.Infrastructure/Word/WordEditingStateDetector.cs` (new)
- `src/DocumentIDE.Web/src/features/inspector/EditingInWordBadge.tsx` (new)
- `src/DocumentIDE.Web/src/features/inspector/EditingInWordBadge.test.tsx` (new, 4 tests)
- `tests/DocumentIDE.IntegrationTests/Word/WordEditingStateTests.cs` (new, 7 tests)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~WordEditingStateTests` → PASS (7/7)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter FullyQualifiedName~Word` → PASS (30/30)
- `npm test` (DocumentIDE.Web) → PASS (26 files, 157/157)
- `npm run build` (tsc + vite) → PASS

## Acceptance criteria

- [x] Open Word doc shows editing state (owner-lock fixture test)
- [x] Closed Word doc clears state (`Refresh` flips verdict, `StateChanged` fires)
- [x] Batch preflight can block/skip open doc (`BlockedEditingInWord` vs `Allowed`)
- [x] No single heuristic is treated as infallible (per-signal breakdown + Low confidence on one positive; badge says "possibly")

## Deviations / spec conflicts

None. Detector is intentionally stateless-evaluate + explicit `Refresh`
(the FS-03 watcher calls `Refresh`; no background polling thread added, to
avoid a second concurrency surface next to the WORD-03 STA queue).

## Risks discovered

- Owner-lock filename truncation differs across Word versions; both common
  variants are recognized, but an exotic Word build could evade that one
  signal — the other two signals still apply, and confidence labeling
  keeps this visible rather than silent.
- Exclusive-access probe opens the file briefly; it takes no write lock and
  holds the handle only for the probe duration.

## Next eligible sprint(s)

- WORD-04 (High-Fidelity Quick Look) also consumes editing state for safe
  preview invalidation; PDF-01 unblocked via WORD-03.

## Security & file-safety gate

- [x] no user file was mutated through an unreviewed/bypass path (read-only probes; lock-file checks are existence checks; probe stream is read-only and immediately disposed)
- [x] no arbitrary path or secret is exposed (paths stay in-process; no new endpoints)
- [x] dependency/security boundaries remain intact (no ShareHost/frontend-privileged changes; no COM invoked — probe is injectable)
- [x] no destructive action silently broadened its scope (preflight only blocks; it never mutates)
- [x] relevant higher-level safety rules remain stronger than convenience (open docs block mutation preflight instead of being silently overwritten)
