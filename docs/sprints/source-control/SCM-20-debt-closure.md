# SCM-20 — Source Control Debt Closure

**Status:** READY
**Priority:** P1
**Kind:** refactor + hardening
**Recommended branch:** `feat/scm-debt-closure`

## Goal

Close the three Source Control residuals recorded in the v1.0.0 readiness report (R-2, R-4 and the
caller-named repository path), without changing any SCM outcome that the spec actually promises.

## Depends On

- SCM-17 (hardening + release checklist) — DONE
- SCM-18 (route surface) — DONE
- SCM-19 (DOCX diff surface) — DONE

## In Scope

### 1. One registration path (report R-2)

`Program.cs` registers the SCM services inline and `SourceControlServiceRegistration.AddSourceControlServices`
registers them again for the desktop host. Consolidate to a single composition that both hosts call, so a
service can never be added to one host and silently missing from the other (the exact class of gap that
SCM-19's both-roots test was written to catch).

Hard requirement: `DocxDiffProvider_IsRegisteredInBothCompositionRoots` (SCM-19) and every existing SCM
host test must still pass **unchanged**. If consolidating would require changing those tests, stop and
report instead.

### 2. `.git` on the stage path (report R-4)

`GitDocumentPath` now rejects `.git` segments for every document surface. Staging still accepts a
`.git/...` path and reports `Succeeded` while Git ignores it, so the typed outcome is imprecise. Reject
`.git` segments in the stage path validator with the same typed `PathRejected` shape used elsewhere.

Do **not** widen or narrow any other path rule.

### 3. Caller-named `repositoryPath` on SCM read routes

SCM-03 deliberately accepts a caller-named repository path on a privileged, session-guarded API, and the
*document* path is containment-checked. The residual is that the repository path itself is not tied to a
discovered workspace.

Preferred fix: accept a repository path only when it is (a) canonically rooted, (b) a real Git worktree,
and (c) either a discovered workspace root or a registered worktree of one. Everything else stays a typed
outcome.

**Stop-and-report clause:** if that restriction would break the documented SCM-03 contract or more than a
handful of existing tests, do not force it. Report the exact conflict and propose the smallest safe
tightening instead. A silent semantic change to the SCM surface is worse than a documented residual.

## Out of Scope

- Branch management, new SCM features, the web screens.
- The diff engine, its limits or classification.
- Anything in `docs/quality/**`, `scripts/verify-release.ps1`, `.pen`.

## Required Tests

- [ ] A service resolved from the API host and from `AddSourceControlServices` is the same graph (no
      duplicated registration, no host-only service).
- [ ] `.git/config` (and a nested `.git/...`) is rejected typed by stage **before** Git runs, and the index
      is unchanged.
- [ ] A repository path outside the workspace set is refused typed; a real workspace root still works.
- [ ] Existing SCM route/host tests pass unchanged.

## Definition of Done

- [ ] required tests pass; Integration/Core/Architecture/Security green; no skipped required test
- [ ] handoff written (`docs/sprints/handoffs/SCM-20-HANDOFF.md`)
- [ ] board row left to the controller

## Commit Guidance

```text
refactor(scm): consolidate registration and close the remaining path residuals
```
