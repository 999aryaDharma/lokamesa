# Sprint Handoff — SHR-11 — Owner Share Management UI

**Status:** DONE
**Branch:** `feat/shr-11-owner-share-management-ui`
**Commits:** (see git log on branch)

## What changed
- New owner share-creation dialog `ShareDialog.tsx`: explicit checkbox member selection from available selection/collection documents (initial selection filtered against available docs), Snapshot/Live radio (defaults Snapshot), download toggles reusing SHR-07 `SharePermissions` (defaults OFF). `onCreate` fires only on explicit Create click with `{ mode, memberDocumentIds, allowOriginalDownload, allowPdfDownload }`; unselected documents are never in the payload (no hidden auto-publish).
- New owner management component `ShareManager.tsx`: exact member list with per-member explicit Remove buttons, add-member form where typing alone fires nothing (submit/Add click required), active/stopped/tunnel-error status badge with tunnel detail alert, share URL display, Copy link / Revoke / Regenerate link buttons (all explicit callbacks). Pure `resolveShareLinkAccess(state, presentedToken)` mirrors the backend access decision; `mapTransportStateToShareStatus` maps SHR-09 `TransportStatusState` (Stopped→stopped, Starting/Running→active, Failed→tunnel-error).
- New colocated tests `ShareDialog.test.tsx` (4) + `ShareManager.test.tsx` (10).

## Files changed
- `src/DocumentIDE.Web/src/features/sharing/ShareDialog.tsx` (new — planned file, exact path)
- `src/DocumentIDE.Web/src/features/sharing/ShareManager.tsx` (new — planned file, exact path)
- `src/DocumentIDE.Web/src/features/sharing/ShareDialog.test.tsx` (new — colocated per CAN-05 ruling; planned `tests/frontend/sharing/ShareManager.test.tsx` path is stale, outside the vitest root, same mapping as BAT-06)
- `src/DocumentIDE.Web/src/features/sharing/ShareManager.test.tsx` (new — colocated per CAN-05 ruling, see above)
- `docs/sprints/SPRINT-STATUS.md` (SHR-11 → DONE)

## Verification
- Red-first per step: `ShareManager.test.tsx` failed to collect before `ShareManager.tsx` existed; status tests 2 failed before badge; add/remove tests 2 failed before form/buttons; link tests 4 failed before `resolveShareLinkAccess`/buttons; `ShareDialog.test.tsx` failed to collect before `ShareDialog.tsx` existed.
- `npx vitest run src/features/sharing/` (from `src/DocumentIDE.Web`) → PASS (20/20, 3 files)
- `npm test` (from `src/DocumentIDE.Web`) → PASS (38 files, 226 passed — 225 pre-existing + net new; full run after final test showed 38 passed files)
- `npx tsc -b` → clean (exit 0; one readonly-fixture type error found by tsc and fixed)
- `npm run lint` → exit 0 (warnings only; new file triggers the same `react(only-export-components)` fast-refresh warning class as pre-existing `FolderPicker.tsx`/`QuickLook.tsx`)
- No backend code touched; no C# test runs needed (UI-only sprint).

## Security & File-Safety Gate Confirmation
- [x] no user file was mutated through an unreviewed/bypass path (pure UI components; all mutations flow through explicit owner callbacks, no filesystem access)
- [x] no arbitrary path or secret is exposed (only sanitized display names and the public share URL are rendered; share token itself is never rendered, only passed to the access-decision mirror; no local paths)
- [x] dependency/security boundaries remain intact (reuses SHR-07 `SharePermissions` for download intent; enforcement stays server-side; no ShareHost/transport change)
- [x] no destructive action silently broadened its scope (add/remove/revoke/regenerate all require explicit button clicks; typing/selecting alone fires no callback — proven by tests)
- [x] relevant higher-level safety rules remain stronger than convenience (explicit membership per spec §5/§29; live-membership rule from SHR-05 preserved: dialog freezes selection at create, manager adds only via explicit action)

## Acceptance criteria
- [x] No hidden automatic publishing (dialog payload contains only checked members; typing/checking without Create fires nothing; manager add requires submit click — all proven by tests)
- [x] User can see exact members (test: renders exactly `Alpha.md`, `Beta.docx`)
- [x] Revoked link fails (test: `resolveShareLinkAccess` returns `{allowed:false, reason:'revoked'}` for revoked share; stopped/wrong-token cases also denied — mirrors `ShareTokenService.VerifyTokenForShare` + spec §17/§18)
- [x] Adding member requires explicit action (test: typing fires no callback; Add click fires once with the id; remove likewise requires its button)

## Deviations / spec conflicts
- Test path mapping (per sprint planned-files rule, same as BAT-06): planned `tests/frontend/sharing/ShareManager.test.tsx` sits outside the `src/DocumentIDE.Web` vitest root and would never execute; tests colocated as `src/DocumentIDE.Web/src/features/sharing/*.test.tsx` per CAN-05 ruling. Component paths created exactly as planned. No spec wording weakened.

## Risks discovered
- `resolveShareLinkAccess` is an owner-side mirror, not enforcement: actual viewer denial still happens server-side (SHR-03 token auth, spec §17/§18). If the backend decision table ever changes, the mirror must be updated to match.
- Components are callback-driven with no API wiring: REL-03 (or a follow-up) must wire `onCreate`/`onAddMember`/etc. to the privileged localhost API with Origin/Host/session authorization; until then the UI proves intent but performs no real share lifecycle.

## Next eligible sprint(s)
- `REL-03` — V1 Readiness Gate (SHR-11 side now satisfied; still needs REL-02, CTX-04 — DONE, AI-04 — DONE, PDF-04)
