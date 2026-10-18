# CTX-06 — Context Pack Export and Real Token Estimate

**Status:** READY
**Priority:** P2
**Kind:** feature completion
**Recommended branch:** `feat/context-pack-export`

## Goal

Finish the context-pack surface: a real export (no lying button) and the token estimate that the server
actually computed.

## Depends On

- CTX-04 — context export service — DONE
- UI-05 — `POST api/context/pack`, mounted dialog (copy-only) — DONE

## The gap, precisely

- The dialog renders **Export context.md** only when `onExportToFile` is supplied, and the shell supplies
  none, so export does not exist.
- The dialog displays a **client-side** token estimate, while `POST api/context/pack` returns the real
  `tokenEstimate` from `ContextExportService`. The two can disagree.

## In Scope

1. **Export.** Supply a real `onExportToFile` from the shell that writes the exact combined Markdown the
   route returned (not a re-render) to a user-visible location. Preferred: a Blob download named
   `context.md`. The desktop WebView2 host currently has **no** download handling; if downloads are not
   deterministic without it, add an explicit `CoreWebView2.DownloadStarting` handler that allows the
   download and saves to the user's Downloads folder (creating it if needed) with a deterministic name.
   Report exactly which mechanism you used and prove it with a test (host-level if you add the handler).
2. **Honest success.** The "exported" state may only be shown after the download has actually started/
   completed. No toast that claims a write that did not happen.
3. **Real estimate.** Display the route's `tokenEstimate` (per-source and total as available). Remove the
   client-side recomputation, or keep it only as an explicitly labelled fallback when no route result
   exists. Pin both numbers in a test.
4. Keep copy-to-clipboard working and keep the copy-only path when export is unavailable.

## Out of Scope

- Changing the pack builder, its caps, or the DOCX extracted-text policy.
- A new persistence model for packs.
- `App.tsx` changes beyond supplying the export callback and the estimate (this is the only UI lane in this
  batch, so no contention).

## Required Tests

- [ ] Export writes/triggers the exact `markdown` returned by the route (assert byte-for-byte on the
      captured payload), with the correct filename.
- [ ] No export control when the callback is absent (existing UI-05 behaviour preserved).
- [ ] "Exported" state is not shown when the download fails.
- [ ] The displayed estimate equals the route's `tokenEstimate` (frontend test with a real-shaped payload).
- [ ] Copy still works.
- [ ] If a host download handler is added: a test proves the handler is wired and the file lands where the
      handler says.

## Definition of Done

- [ ] required tests pass; frontend gates (`tsc -b`, `oxlint`, `vitest run`) and affected backend suites green
- [ ] handoff with the export mechanism and the estimate source recorded
- [ ] board row left to the controller

## Commit Guidance

```text
feat(context): export the real pack and show the server token estimate
```
