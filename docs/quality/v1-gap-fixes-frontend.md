# V1 Gap Fixes — Frontend / App-Shell Half

**Scope:** close the frontend + app-shell half of the REL-03 integration gaps that
made V1 report `BLOCKED` — specifically **G1, G3, G5, G6, G7** (the surfaces a
user could not reach from the shipped shell). This report covers only the
frontend work; it does **not** claim the V1 verdict flips on its own. Every
gap below still needs the backend routes listed in **§6 (Lane B coordination)**
to be a fully working product path in the packaged `DocumentIDE.Desktop.exe`
+ `DocumentIDE.ShareHost.exe`.

**Branch:** `fix/v1-gap-app-shell` (base `integration/parallel-batches` @ `72c0678`)
**Verification (this branch):**

| Command (in `src/DocumentIDE.Web`) | Result |
|---|---|
| `npx vitest run` | **363 passed / 52 files** (baseline `72c0678`: 353 passed / 50 files) |
| `npx tsc -b` | exit 0 |
| `npx oxlint` | exit 0 (warnings only, pre-existing `only-export-components` class) |
| `npm run build` | exit 0 (`tsc -b && vite build`) |

No existing test was weakened or removed; all changes are additive except the
placeholder → real replacements recorded per gap.

---

## 1. G1 — Graph view is a placeholder

**Unreachable before:** `DocumentGraph.tsx` rendered node titles as plain text
with a dummy position-shift click. No edges, no explicit-vs-inferred semantics,
no navigate/open. GR-05 (`graphLayoutWorker.ts`, `GraphScopeControls.tsx`) and
AI-04 semantic top-K were imported only by tests.

**Wired now:**

| File | What it does |
|---|---|
| `src/features/graph/DocumentGraph.tsx` | Real SVG graph: nodes selectable, edges rendered; explicit edges **solid**, inferred edges **dashed** (`strokeDasharray={inferred ? '6 4' : undefined}`, :261); mounts `GraphScopeControls` (:215), `computeBoundedGraph` (:128), `filterSemanticEdgesTopK` (:117); double-click opens a Markdown node in the editor / a DOCX node in Word (:160-170). |
| `src/features/graph/graphApi.ts` | Loads `GET /api/workspaces/{id}/graph`; classifies origins truthfully — `Explicit`/`Manual` → solid, `Inferred`/`Semantic` → dashed, **unknown origin downgraded to inferred** (never promoted to factual). |
| `src/features/graph/graphStore.ts` | Holds loaded `nodes`/`edges` (`setGraph`/`clearGraph`). |
| `src/features/graph/GraphScopeControls.tsx`, `graphLayoutWorker.ts` | Now mounted/used by the shipped view (were test-only). |

**Tests:** `graphApi.test.ts` (origin classification, payload mapping, route +
token), `DocumentGraph.test.tsx` (explicit edge has no `stroke-dasharray`;
inferred edge has `6 4`; double-click Markdown node opens editor).

**Residual risk:** the graph payload comes from a route that does not exist yet
in the privileged host (§6). With no route, the view degrades to the empty
state (`fetchWorkspaceGraph` throws, caught, last graph kept).

---

## 2. G3 — Search unreachable

**Unreachable before:** `searchStore.ts` fetched `/api/search`, but
`SearchPanel.tsx` was imported only by its own test; `App.tsx` mounted only
canvas/list/graph + batch.

**Wired now:**

| File | What it does |
|---|---|
| `src/App.tsx` | Tool-bar **Search** button toggles `SearchPanel` (:200); result selection routes back to the canvas view; the "Batch operation" action opens the batch panel. |
| `src/features/search/searchStore.ts` | **Unchanged** — the `/api/search?q=` fetch contract was preserved exactly. |

**Tests:** `App.shell.test.tsx` → "G3: mounts the search panel from the tool bar".

**Residual risk:** the `/api/search` server route still does not exist in any
host (§6) — the panel mounts and issues the request, but search returns the
store's error state until the endpoint is served.

---

## 3. G5 — Share lifecycle unreachable

**Unreachable before:** `ShareManager`/`ShareDialog` were imported only by their
own tests; no create/stop/revoke endpoint; callbacks unwired
(`SHR-11-HANDOFF.md:45`).

**Wired now:**

| File | What it does |
|---|---|
| `src/features/sharing/SharePanel.tsx` (new) | Container that mounts `ShareDialog` (create) and `ShareManager` (manage) on top of the real API: create only on the explicit **Create share** click; wiring for stop / revoke / regenerate / add-member / remove-member; copy-link; load + action failures surface as `role="alert"`. |
| `src/features/sharing/shareApi.ts` (new) | Privileged-API client: `GET/POST /api/shares`, `POST /api/shares/{id}/stop|revoke|regenerate`, `POST /api/shares/{id}/members`, `DELETE /api/shares/{id}/members/{documentId}`. Injects `X-DocumentIDE-Session-Token`. |
| `src/features/sharing/ShareManager.tsx` | Added the previously missing **Stop** action (`onStop`) so the create/stop/revoke lifecycle §90 names is complete. |
| `src/App.tsx` | Tool-bar **Shares** button mounts `SharePanel` (:242); member candidates come from the shared canvas objects (no parallel document store). |

**Tests:** `SharePanel.test.tsx` (load, explicit create with exact payload,
stop/revoke/regenerate/member wiring, load-failure alert); `ShareManager.test.tsx`
(+ "stopping a share requires an explicit stop action").

**Safety preserved:** nothing is published until the explicit Create click;
membership stays explicit (SHR-05); only the public share URL is displayed/
copied — the share token is never rendered (SHR-11).

**Residual risk:** share lifecycle routes do not exist in the privileged host
(§6) — the panel is reachable and drives the API, but a real share cannot be
created until Lane B serves these routes.

---

## 4. G6 — Workspace attach and canvas bootstrap unreachable

**Unreachable before:** `canvasStore.setObjects`/`upsertObject` were called only
from tests; `DocumentCanvas` was mounted without `folderFiles`/
`onReleaseDocuments`; `AddFolderSource` was unmounted.

**Wired now:**

| File | What it does |
|---|---|
| `src/features/workspace/workspaceApi.ts` (new) | Loads `GET /api/workspaces/{id}/documents` (ReconciliationController) and `GET /api/workspaces/{id}/folder-sources` (FolderSourcesController). |
| `src/features/workspace/workspaceBootstrap.ts` (new) | Maps DTOs → canvas objects (metadata only, never a filesystem move), builds the folder→files map for the picker, and degrades to an empty-but-valid result on loader failure. |
| `src/App.tsx` | On startup calls `bootstrapWorkspace` then `setObjects` (:95-101); passes `workspaceId`, `folderFiles`, `onReleaseDocuments` to `DocumentCanvas` (:209-212); releasing folder files calls `upsertObject`; tool-bar **Add folder** mounts `AddFolderSource`, which re-bootstraps on attach (:242→ add-folder aside). |
| `src/features/canvas/DocumentCanvas.tsx` | Uses `applyClick`/`groupDrag` (SelectionController) for selection + group drag (:442, :496) and `recordDrag` (CAN-06) to persist manual positions (:511). |
| `src/features/canvas/canvasStore.ts`, `nodes/DocumentNode.tsx` | Added `setSelection`; added `onMouseDown` drag hook. |

**Tests:** `workspaceApi.test.ts`, `workspaceBootstrap.test.ts`,
`DocumentCanvas.test.tsx` (selection + drag persistence), and
`App.shell.test.tsx` → "G6: bootstraps the canvas from the workspace API".

**Residual risk:** `POST /api/workspaces/{id}/folder-sources` (attach) and the
shipped-desktop hosting of the reconciliation/folder routes are Lane B / host
items (§6). With the API unavailable the canvas is empty but valid — no crash.

---

## 5. G7 — Markdown editing is a stub

**Unreachable before:** `MarkdownEditor.tsx` was a demo (`documentId = 'dummy'`,
empty load, `console.log` save) and mounted nowhere.

**Wired now:**

| File | What it does |
|---|---|
| `src/features/markdown/MarkdownEditor.tsx` | Real source-first editor: loads `GET /api/documents/{id}/markdown`, detects changes, saves via `PUT` (Save button + `Ctrl+S`), optional autosave, status/dirty indicators, error alert. Preview renders the **inert escaped source** in a `<pre>` — never `dangerouslySetInnerHTML` (spec 08 §6). |
| `src/features/markdown/markdownApi.ts` (new) | `GET`/`PUT /api/documents/{id}/markdown` client (stable DocumentId, never a path). |
| `src/App.tsx` | Markdown overlay: graph double-click (`onOpenDocument`) and canvas double-click (`onOpenMarkdownDocument`) open the editor (:213, :253); Close returns to the view. |
| `src/features/canvas/DocumentCanvas.tsx` | `isMarkdownObject` routes Markdown double-click to the internal editor and DOCX to Word (:249, :606-609). |

**Tests:** `MarkdownEditor.test.tsx` (load/save/change-detection/`Ctrl+S`/
inert preview), `DocumentCanvas.test.tsx` → "double-clicking a Markdown document
opens the internal editor, not Word", `App.shell.test.tsx` → "G1/G7:
double-clicking a Markdown graph node opens the Markdown editor".

**Residual risk:** `GET`/`PUT /api/documents/{id}/markdown` does not exist in
any host (§6) — the editor is reachable and safe, but loading/saving a real
`.md` file requires Lane B to serve these routes.

---

## 6. Lane B coordination — routes / host items still required

These are **not** in this lane (privileged host / API controllers / ShareHost).
Until they exist, the frontend is *reachable* but degrades to an empty/error
state, which is honest, not a false success.

| Gap | Required backend route(s) | Owning area |
|---|---|---|
| G1 | `GET /api/workspaces/{workspaceId}/graph` → `{ nodes:[{documentId,displayName?,documentType?}], edges:[{sourceDocumentId,targetDocumentId,relationType,relationOrigin,confidence?}] }` | Privileged API / graph service |
| G3 | `GET /api/search?q=` → `{ results, totalCount, executionTimeMs, activeFilterChips?, error? }` | Privileged API / index |
| G5 | `GET/POST /api/shares`; `POST /api/shares/{id}/stop\|revoke\|regenerate`; `POST /api/shares/{id}/members`; `DELETE /api/shares/{id}/members/{documentId}` | Privileged API + ShareHost |
| G6 | Host the existing `ReconciliationController` (`/api/workspaces/{id}/documents`) and `FolderSourcesController` (`GET`/`POST /api/workspaces/{id}/folder-sources`) in the shipped desktop host | `WebAppHost` / DI |
| G7 | `GET/PUT /api/documents/{documentId}/markdown` (`{documentId,source}` ↔ `{source}`) | Privileged API / safe-write |

**Host-injection contract to confirm (Pencil B / host):**
`window.__DOCUMENTIDE_WORKSPACE_ID__` selects the active workspace; the shell
falls back to `'default'` when absent. The API base/token globals
(`__DOCUMENTIDE_API_URL__`, `__DOCUMENTIDE_SESSION_TOKEN__`) are the same ones
already used by `OpenInWordAction` and the graph/markdown/share clients.

---

## 7. Files changed (this branch)

**New**
- `src/DocumentIDE.Web/src/App.shell.test.tsx`
- `src/DocumentIDE.Web/src/features/graph/graphApi.ts`, `graphApi.test.ts`
- `src/DocumentIDE.Web/src/features/markdown/markdownApi.ts`
- `src/DocumentIDE.Web/src/features/sharing/shareApi.ts`, `SharePanel.tsx`, `SharePanel.test.tsx`
- `src/DocumentIDE.Web/src/features/workspace/workspaceApi.ts`, `workspaceApi.test.ts`,
  `workspaceBootstrap.ts`, `workspaceBootstrap.test.ts`
- `docs/quality/v1-gap-fixes-frontend.md` (this file)

**Modified**
- `src/DocumentIDE.Web/src/App.tsx`
- `src/DocumentIDE.Web/src/features/graph/{DocumentGraph.tsx,DocumentGraph.test.tsx,graphStore.ts,GraphScopeControls.tsx,graphLayoutWorker.ts}`
- `src/DocumentIDE.Web/src/features/markdown/{MarkdownEditor.tsx,MarkdownEditor.test.tsx}`
- `src/DocumentIDE.Web/src/features/sharing/{ShareManager.tsx,ShareManager.test.tsx}`
- `src/DocumentIDE.Web/src/features/canvas/{DocumentCanvas.tsx,DocumentCanvas.test.tsx,canvasStore.ts,nodes/DocumentNode.tsx}`

No Lane A files (`source-control/**`, `.NET SourceControl`), no Lane B files
(`src/DocumentIDE.Api/Controllers/**`, `WebView/WebAppHost.cs`, `ShareHost/**`,
`features/batch/BatchPreviewPanel.tsx`), and no `.NET`/host files were touched.
`docs/quality/v1-readiness-report.md`, `scripts/verify-release.ps1`, and
`SPRINT-STATUS` were not modified.

---

## 8. Remaining risks / not claimed

- **Verdict is unchanged.** REL-03's verdict can only flip after Lane B serves
  the routes in §6 and the readiness gate is re-run (`scripts/verify-release.ps1`).
- **G2 (batch apply/undo), G4 (open-in-Word host), G8 (PDF), G9 (E2E admin
  test)** are out of this lane and remain open.
- The graph payload envelope mirrors the ShareHost SHR-06 viewer shape so owner
  and viewer stay aligned; if Lane B chooses a different shape, `graphApi.ts`
  mapping must be updated in lockstep.
- `resolveShareLinkAccess` remains an owner-side mirror, not enforcement
  (server-side denial is ShareHost's); unchanged by this work.
