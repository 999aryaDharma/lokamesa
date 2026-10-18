# UI-04 — Final V1 UI Surfaces

**Status:** DONE
**Priority:** P0
**Kind:** ui
**Branch:** `feat/ui-04-final-surfaces`

> **Process note:** this contract was dispatched as an inline brief (the same text in the lane prompt)
> and written down here after completion so the board row and the handoff have a stable reference.
> The inline brief is reproduced below; the handoff carries the evidence.

## Goal

Close the remaining V1 UI gaps after SCM-19 and HIST-04 landed, without inventing product behaviour.

## Scope, in priority order

1. **Screen 06 — real DOCX diff.** Replace the hardcoded `status="failed"` panel with
   `POST /api/source-control/docx-diff`, passing the **true worktree root** from `GET status` (the route
   validates the document path before any Git call, so it cannot discover the root itself). Map the
   engine report onto `DocxDiffPanel`; `BinaryFallback` and failures stay typed, never rows.
2. **Context pack.** Only if a thin read-only route over `ContextPackService`/`ContextExportService`
   exists without inventing semantics. **Stop and report otherwise.**
3. **Missing resources.** Only if listing and acting need no invented route or broken overlay.
   **Stop and report otherwise.**
4. **Cleanups.** The two invalid `aria-selected` instances; the dead `App.css`.

## Outcome

| Step | Result |
| --- | --- |
| 1 Screen 06 | **Shipped** — real route, typed loading/no-root/failed/transport states |
| 2 Context pack | **Stopped** — no full-text source for `.docx` (only a bounded preview, which would be false as document text) and no defined destination for the `context.md` export; mounting would fake an export |
| 3 Missing resources | **Stopped** — `Remove reference` has no route, and `LocateMovedFileDialog` fetches without the session token so it cannot reach the loopback API from `https://appassets.html2sav`; mounting would expose a broken overlay |
| 4 Cleanups | **Shipped** — `SearchPanel` is a real `listbox`/`option`; `SourceControlPanel` uses `aria-current`; `App.css` deleted |

`DocumentList`'s `aria-selected` on `<tr>` is **valid** (the `row` role supports it) and was intentionally
left alone.

## Non-negotiables

- No fabricated data, no inert controls, no stubs. Absent beats fake.
- No change to file-safety, SCM, batch, sharing, history or undo semantics; no new runtime dependency.
- One owner of `App.tsx`.

## Follow-ups created by the stops

- **CTX-05** — context-pack source + export destination, then mount `ContextPackDialog`.
- **FS-09** — `remove reference` route, session-token-aware `LocateMovedFileDialog`, then mount both cards.

## Evidence

`docs/sprints/handoffs/UI-04-HANDOFF.md`; frontend **87 files / 620 tests**, tsc 0, oxlint 0.
