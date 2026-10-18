# SCM-19 — DOCX Diff API Surface

**Status:** READY
**Priority:** P0
**Kind:** feature
**Recommended branch:** `feat/docx-diff-api-surface`

## Goal

Expose the already-tested SCM-05 document-aware DOCX diff engine over the privileged API so design
screen 06 renders a real structural report instead of a typed "not available in this build" state.

## Why this sprint exists

SCM-05 is `DONE` and shipped `DocxGitDiffProvider.GetDocumentDiffAsync(worktreeRoot, relativePath,
GitDiffTarget target, originalPath?)` with its own tests. A scan finds it is **registered in no DI
container and exposed by no controller**. UI-03's `DocxDiffScreen` therefore hardcodes
`status="failed"` with "A document-aware comparison is not available for this change in this build."

This is the same class of gap SCM-15/16 had before SCM-18: the engine exists and is tested; only the
surface was never shipped.

## Depends On

- `SCM-05` — DOCX Document-Aware Git Diff (`DONE`)
- `SCM-02` — repository/worktree discovery (`DONE`)

## Required Reading

- `AGENTS.md`
- `docs/specs/13-source-control-git-worktree-spec.md` (§9.3 document-aware diff, §20 outcomes)
- `docs/specs/06-docx-openxml-word-integration.md`
- `src/DocumentIDE.Infrastructure/SourceControl/DocxGitDiffProvider.cs`
- `src/DocumentIDE.Core/SourceControl/DocxDiffContracts.cs`
- `tests/DocumentIDE.IntegrationTests/SourceControl/DocxGitDiffTests.cs` (what is already proven)
- the append-only region pattern in `SourceControlController.cs`

## In Scope

- Register `DocxGitDiffProvider` in **both** composition paths:
  - `src/DocumentIDE.Api/Program.cs` (the API host), and
  - `src/DocumentIDE.Infrastructure/SourceControl/SourceControlServiceRegistration.cs`
    (`AddSourceControlServices`, which the desktop host calls) — otherwise the shipped product 404s the
    screen even though the dev API serves it.
- Append a route to `SourceControlController`:
  - `POST api/source-control/docx-diff` with `{ path, documentPath, target, originalPath? }`.
- Typed DTOs in the existing SCM style (`status` / `isSuccess` / `errorCode` / `message` + the report).
- Read-only: this route must **not** take the per-worktree mutation queue.
- `400` only for input rejected before any Git or filesystem access; every inspectable outcome
  (not a repository, Git unavailable, path rejected, document missing, unsupported/non-DOCX target,
  preflight rejection, diff failure) stays `200` with a typed body.

## Out of Scope

- Any change to the diff engine, its limits, or its classification.
- Rendering a page image (WORD-04 stays unimplemented).
- Word/COM interaction of any kind — this route never opens or mutates a document.
- Wiring the web screen (that is a follow-up UI commit; this sprint produces the surface it needs).
- Branch management, unrelated refactors.

## Planned Files

- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` (append-only region: 1 route + DTOs)
- `src/DocumentIDE.Api/Program.cs` (append-only DI)
- `src/DocumentIDE.Infrastructure/SourceControl/SourceControlServiceRegistration.cs` (append-only DI)
- `tests/DocumentIDE.IntegrationTests/SourceControl/DocxDiffApiTests.cs`

## Required Tests

- [ ] A real repository with two committed DOCX versions returns a **real** structural report: change
      classes present, paragraph-level before/after content, and the summary counts — not an empty or
      placeholder payload.
- [ ] A `.md`/non-DOCX path is refused typed (the engine's own unsupported outcome), not diffed as text.
- [ ] Path escape (absolute, rooted, `..`, outside the worktree) is rejected **before** any Git runs
      (recording runner asserts zero invocations).
- [ ] A document that does not exist at the compared version stays a typed outcome.
- [ ] Not-a-repository and Git-missing are typed `200` outcomes.
- [ ] Missing/invalid token `401`; hostile Origin/Host `403`.
- [ ] The route is read-only: no mutation-queue acquisition, no commit/index change, document bytes
      unchanged after the call.

At minimum the first behaviour must be demonstrated by a failing test before implementation.

## Security & File-Safety Gate

- [ ] no user file mutated through any path (this route is strictly read-only)
- [ ] no shell string built from a path; structured arguments only
- [ ] no repository escape through path handling
- [ ] no secret/credential output (reuse the existing redaction if diagnostics are surfaced)
- [ ] no destructive action broadened
- [ ] higher-level safety rules stay stronger than convenience

## Acceptance Criteria

- [ ] `POST api/source-control/docx-diff` returns a real document-aware report from a real repository.
- [ ] The route is reachable from **both** the API host and the desktop host.
- [ ] No diff-engine behaviour changed; no DOCX is opened, parsed into Word, or written.
- [ ] The route set stays append-only.

## Definition of Done

- [ ] all steps complete; required tests pass; affected suite green; no skipped required test
- [ ] `git diff` self-reviewed; no unintended deletions
- [ ] handoff written (`docs/sprints/handoffs/SCM-19-HANDOFF.md`)
- [ ] board row left to the controller

## Commit Guidance

```text
feat(scm): expose the document-aware DOCX diff over the privileged API
```
