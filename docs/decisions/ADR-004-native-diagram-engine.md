# ADR-004 — Native Diagram Engine (No Third-Party Whiteboard)

**Status:** Accepted (owner decision, 2026-10-18)

## Context

Lokamesa needs whiteboard/diagram capability inside **Loka** — shapes, connectors, freehand, and a freeform board. Three delivery models were compared in `docs/future-improvement/specs/1.10.0-diagram-boards.md`:

- **A — native engine** on the existing canvas.
- **B — embed Excalidraw** (`@excalidraw/excalidraw` 0.18.1, MIT, React 19 compatible).
- **C — embed draw.io** (Apache 2.0 code, restricted stencils, mxGraph).
- **D — hybrid** of native and embedded.

Constraints that decided the outcome:

- `ui.pen` is the visual source of truth; an embedded editor has its own visual language.
- The product must work offline; Excalidraw loads fonts from a CDN unless assets are self-hosted.
- `ADR-003` already defines `space_objects` + a file vault; boards must fit that model.
- The product identity is local-first, open-format, and free of foreign runtime dependencies.
- The team can afford 6 sprints for a native engine; it cannot afford two engines to maintain indefinitely.

## Decision

1. Build a **native diagram engine**. No third-party whiteboard engine is embedded.
2. Reuse the existing canvas interaction layer (pan/zoom/transform, selection, group drag, spatial index).
3. Store boards as `vault/boards/<id>.json` with the envelope `lokamesa.board.v1`.
4. Adopt the **patterns** — not the code — observed in Excalidraw's MIT source:
   - single flat element model with `type` discriminator,
   - `isDeleted` instead of physical deletion,
   - fractional `index` for ordering,
   - `version` / `updated` / `created` lifecycle fields,
   - bidirectional binding via `startBinding`/`endBinding` + `boundElements`,
   - text-in-shape via `containerId`,
   - delta-based undo with inverted entries,
   - separation of `elements` (document) from `appState` (UI).
5. Deliberate deviations from the reference implementation:
   - **no `versionNonce` persisted** (no collaboration; keeps JSON diff-friendly),
   - **unified undo** with the application history,
   - **document links** on elements (`linkedDocumentId`),
   - **layer panel** as a non-canvas alternative,
   - **freehand ink included in the first version** (T2),
   - **template library** (flowchart, mind map, diagram skeleton),
   - **SVG, PNG, and PDF export**,
   - **no telemetry, no AI, no network access**.
6. Collaboration (real-time multi-user) is explicitly out of scope.

## Alternatives Considered

- **Embed Excalidraw (MIT).** Fastest route to T0–T2 and proven UX, but introduces a foreign visual language, a foreign data model, a large bundle, a separate undo stack, and a dependency whose upgrades can break stored scenes. Rejected as a core capability.
- **Embed draw.io (Apache 2.0).** Strongest structured diagramming, but heavy (mxGraph), desktop-oriented, and its bundled stencils carry additional restrictions. Rejected.
- **Hybrid.** Best of both, but two engines, two data models, two undo systems, and two export paths forever. Rejected.
- **tldraw.** Source-available, not OSI open source; production use requires a paid licence or a watermark. Rejected on licensing grounds.

## Consequences

### Positive

- One design language, one persistence model, one undo system, one export path.
- Board JSON is ours, versioned, and readable without Lokamesa.
- No new runtime dependency; no CDN, no watermark, no licence key.
- Board elements can link to documents and participate in backlinks/graph — something an embedded editor cannot do.

### Negative / residual risk

- Six sprints before T1 is usable, versus three for an embed.
- The team owns connector routing, text-in-shape layout, snapping, and undo semantics.
- Feature parity with Excalidraw must be earned, not inherited.
- Undo/redo for a scene graph is the highest-risk item and must be designed in `DB-02`, not retrofitted.

## Security & File-Safety Impact

- Board files live in the vault and are not `documents`; they cannot reach the Word, batch, or Git mutation paths.
- Board paths are containment-checked by the `WB-02` vault rules.
- SVG/PNG export must be sanitized: no scripts, no external references.
- No network access is required to open or edit a board.
- Boards are excluded from ShareHost and the privileged API by default.

## Test / Experiment Evidence

Required before `Accepted` items are marked done:

- JSON round-trip and schema-version migration test,
- fractional-index ordering test (insert between neighbours),
- binding test (arrow stays attached after move and resize),
- undo/redo test covering create, move, resize, style, text, connector, group, delete,
- deterministic export test (same scene → identical SVG),
- 5,000-element performance gate,
- path-containment and sanitization tests.

Evidence is attached to the handoffs of `DB-01`…`DB-06`.

## Supersedes

None.

## Superseded By

None.
