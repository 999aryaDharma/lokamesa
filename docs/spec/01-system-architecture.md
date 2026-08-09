# 01 — System Architecture

**Project:** Document IDE  
**Document type:** Technical architecture specification  
**Status:** Baseline for implementation  
**Source of truth:** `00-master-product-technical-spec.md`

---

## 1. Purpose

This document defines the top-level architecture for Document IDE: process boundaries, runtime responsibilities, module boundaries, privileged versus read-only surfaces, background work, persistence, and integration points.

Document IDE is a Windows-first, local-first application for working with DOCX and Markdown. It must preserve local files as the source of truth while providing a rich visual workspace, real-time search, strict batch operations, Word integration, Markdown editing, graph navigation, PDF finalization, and optional read-only sharing through ngrok.

---

## 2. Architectural Drivers

The architecture MUST optimize for these requirements:

1. Local filesystem is authoritative.
2. DOCX deep editing happens in Microsoft Word Desktop.
3. Markdown editing happens inside Document IDE.
4. The UI should support a Figma-like clean canvas without sacrificing file safety.
5. Search must feel real-time for workspaces under 1,000 files.
6. Batch operations must be deterministic, previewable, reversible, and strict.
7. File changes made outside the app must reconcile back into the workspace.
8. Public share mode must be physically isolated from privileged local APIs.
9. Core functionality must work offline.
10. No paid cloud/server dependency is required for the core product.

---

## 3. Recommended Technology Shape

The preferred architecture is:

```text
Windows Desktop Shell
        │
        ├── WebView-based Frontend
        │
        └── Local Application Backend
              ├── Workspace Service
              ├── Filesystem / Identity Service
              ├── Search / Indexing
              ├── DOCX / OpenXML
              ├── Word Automation
              ├── Markdown
              ├── Graph
              ├── Batch Operations
              ├── History / Recovery
              ├── PDF
              ├── Sharing
              └── Semantic Search
```

Recommended implementation family:

- Windows host/backend: C# / .NET
- Desktop shell: WinUI 3 or WPF
- Embedded frontend: WebView2
- Local API: ASP.NET Core/Kestrel or an equivalent in-process HTTP boundary
- Frontend: React + TypeScript
- Local database: SQLite
- Full-text index: SQLite FTS5
- DOCX structural access: Open XML SDK
- Word automation: Microsoft Office Interop/COM behind an adapter
- Public tunnel: ngrok behind a `ShareTransport` abstraction

Exact package versions MUST be pinned by the implementation repository after technical spikes. This specification defines contracts, not transient package versions.

---

## 4. Process Model

### 4.1 Preferred initial model

Use one desktop application process where possible, with clearly separated logical modules.

```text
DocumentIDE.exe
├── Desktop shell
├── WebView2 UI
├── Local backend host
├── SQLite
├── Background job scheduler
└── Integration adapters
```

The public ngrok-facing share server MUST run in a separate `DocumentIDE.ShareHost.exe` process and consume only explicit sanitized share artifacts/manifests. See the post-audit hardening section below.

### 4.2 Optional later isolation

Word automation and public sharing may later be moved into helper processes if crash isolation or security hardening requires it.

Do not begin with unnecessary microservices.

---

## 5. Frontend / Backend Boundary

The frontend MUST NOT:

- directly write arbitrary local paths,
- invoke COM directly,
- perform OOXML mutation directly,
- open SQLite directly,
- decide whether a destructive operation is safe.

The frontend MAY:

- present canvas,
- request metadata,
- request previews,
- build user selections,
- request batch operation previews,
- submit approved operation plans,
- render Markdown editor state,
- render graph state.

The backend owns all privileged actions.

---

## 6. Backend Module Boundaries

### 6.1 Workspace Module

Responsibilities:

- create/open/delete named workspaces,
- attach/detach folder sources,
- persist canvas/list/graph view state,
- persist virtual groups and collections,
- expose current workspace state.

Does NOT own physical file contents.

### 6.2 Filesystem Module

Responsibilities:

- canonicalize paths,
- monitor folder sources,
- detect create/modify/rename/delete,
- track file identities,
- reconcile startup state,
- perform explicit physical operations,
- send delete operations to Windows Recycle Bin.

### 6.3 Document Identity Module

Responsibilities:

- maintain stable `DocumentId`,
- associate NTFS/file identity where available,
- track current and last-known paths,
- detect rename/move,
- manage fallback fingerprint matching.

### 6.4 Indexing Module

Responsibilities:

- parse changed documents,
- update metadata index,
- update FTS content,
- schedule thumbnails/previews,
- update relation extraction.

### 6.5 Search Module

Responsibilities:

- parse user query syntax,
- execute exact/fuzzy/metadata queries,
- optionally execute semantic search,
- rank results,
- return contextual snippets.

Destructive matching MUST NOT call semantic search.

### 6.6 DOCX Module

Responsibilities:

- read DOCX package,
- extract body/table/header/footer text,
- extract section/page metadata,
- perform strict OOXML mutations,
- support logical text matching across runs,
- validate package after mutation.

### 6.7 Word Automation Module

Responsibilities:

- open DOCX in Microsoft Word,
- detect open/editing state,
- export Word-rendered PDF,
- generate high-fidelity preview when requested.

Word COM calls MUST be isolated behind a service interface.

### 6.8 Markdown Module

Responsibilities:

- safe read/write,
- parse Markdown structure,
- extract headings/frontmatter/links/wikilinks,
- provide preview model,
- participate in graph indexing.

### 6.9 Graph Module

Responsibilities:

- explicit relations,
- backlinks,
- manual relations,
- semantic suggested relations,
- graph layout state independent from canvas layout.

### 6.10 Batch Operation Module

Responsibilities:

- define strict operations,
- create preview plans,
- detect stale fingerprints,
- validate locks,
- create recovery snapshots,
- apply safely,
- produce per-file outcomes.

### 6.11 History Module

Responsibilities:

- 30-day local recovery history,
- operation metadata,
- recovery snapshot lifecycle,
- conflict-aware undo.

### 6.12 PDF Module

Responsibilities:

- single DOCX → PDF,
- batch DOCX → separate PDFs,
- ordered merge of generated PDFs,
- output collision handling.

### 6.13 Sharing Module

Responsibilities:

- create Snapshot/Live shares,
- build restricted publish model,
- run read-only share HTTP routes,
- manage token-scoped access,
- manage ngrok lifecycle through adapter.

### 6.14 Semantic Module

Responsibilities:

- local embeddings by default,
- optional external provider,
- semantic document search,
- inferred graph edges.

---

## 7. Storage Architecture

### 7.1 Source files

Remain in user-selected locations.

```text
D:\Kerjaan\BA\*.docx
D:\Projects\...\docs\*.md
```

### 7.2 Application state

Store under application-managed local state, for example:

```text
%LOCALAPPDATA%\DocumentIDE\
```

Logical contents:

```text
DocumentIDE/
├── app.db
├── cache/
│   ├── thumbnails/
│   ├── previews/
│   └── embeddings/
├── history/
├── shares/
├── temp/
└── logs/
```

No `.documentide` files are required inside source folders.

---

## 8. Local API Boundaries

Suggested API groups:

```text
/api/workspaces
/api/folders
/api/documents
/api/search
/api/canvas
/api/groups
/api/collections
/api/graph
/api/batch-operations
/api/history
/api/pdf
/api/shares
/api/settings
```

Privileged local API MUST bind to loopback only by default.

Public share endpoints MUST live under a separate route family:

```text
/share/{token}/...
```

and MUST NOT proxy arbitrary internal API routes.

---

## 9. Background Job System

Longer work must run as cancellable background jobs.

Job types:

- source reconciliation,
- DOCX parse,
- Markdown parse,
- full-text indexing,
- thumbnail generation,
- high-fidelity preview generation,
- embedding generation,
- graph update,
- PDF export,
- merge,
- batch apply.

Requirements:

- deduplicate duplicate jobs for same document/fingerprint,
- document-level serialization for mutation jobs,
- bounded concurrency,
- visible progress for user-triggered expensive jobs,
- retry only where idempotent/safe.

---

## 10. Event Flow

### 10.1 Word save

```text
Word Ctrl+S
→ filesystem watcher event
→ debounce/coalesce
→ verify file identity + fingerprint
→ mark document Modified
→ parse changed DOCX
→ update metadata + FTS
→ invalidate preview/thumbnail as needed
→ update search/graph
→ mark Ready
```

### 10.2 Batch operation

```text
User selects target
→ POST preview
→ backend scans deterministic matches
→ returns operation plan + fingerprints
→ user reviews
→ POST apply(plan_id)
→ stale check
→ lock check
→ snapshot
→ strict mutation
→ validate
→ atomic replace
→ index updated files
→ history recorded
```

### 10.3 Public share

```text
User starts share
→ build Snapshot or Live manifest
→ start restricted share server
→ start NgrokTransport
→ receive public base URL
→ expose base URL + /share/<secret>
```

---

## 11. Security Boundaries

### Privileged local surface

May:

- read configured local sources,
- mutate files after explicit user actions,
- control Word,
- create history snapshots.

Accessible only locally.

### Public read-only surface

May only:

- resolve documents explicitly included in share,
- return sanitized metadata,
- return previews,
- return optional downloads based on permissions,
- run search inside the share dataset.

It MUST NOT:

- accept local file paths,
- expose workspace filesystem paths,
- call batch operations,
- expose internal settings,
- expose general local search,
- open Word,
- mutate files.

---

## 12. Error Strategy

Every operation result SHOULD use a typed result model:

```text
Success
PartialSuccess
ValidationError
Locked
Conflict
NotFound
Unauthorized
InternalError
```

Batch results must include per-file outcomes.

No generic “Operation failed” without affected resource details.

---

## 13. Availability / Offline Model

Works fully offline:

- workspace,
- canvas,
- list,
- graph explicit edges,
- indexing,
- exact/fuzzy search,
- DOCX metadata,
- Markdown editing,
- batch operations,
- history,
- Word integration,
- PDF.

Requires internet only for:

- ngrok public sharing,
- optional external semantic/AI provider.

---

## 14. Architectural Non-Goals

Do NOT introduce:

- distributed services,
- Elasticsearch/OpenSearch,
- mandatory user accounts,
- cloud object storage,
- Electron-only filesystem logic when native .NET integration is required,
- direct COM usage from frontend,
- generic arbitrary filesystem RPC.

---

## 15. Acceptance Criteria

Architecture is acceptable when:

- privileged file writes cannot be initiated from arbitrary public routes,
- all local mutations flow through safe-write services,
- Word automation is adapter-isolated,
- SQLite can rebuild derived index from local source files,
- workspace metadata can disappear without making user documents unreadable,
- public sharing can be disabled without affecting core app,
- each major subsystem has a clear interface and unit-testable boundary.

---

## 16. Cross-References

Read next:

- `02-domain-data-model.md`
- `04-filesystem-sync-spec.md`
- `05-search-indexing-spec.md`
- `06-docx-openxml-word-integration.md`
- `07-batch-operation-engine.md`
- `10-sharing-security-ngrok.md`

---

# 17. Post-Audit Security & Concurrency Requirements

This section supersedes weaker wording elsewhere in this file.

### Privileged local API

Loopback binding is not authentication. Require Host/Origin validation plus a 256-bit per-startup session secret. Reject foreign/`null` origins, wildcard CORS, and state-changing GET requests. Prefer attaching the secret from the native WebView2 boundary so it is not persisted in frontend storage.

### Share process boundary

`DocumentIDE.ShareHost.exe` is mandatory for public sharing. It must not reference or inject Word automation, batch mutation, unrestricted filesystem mutation, or privileged workspace-secret services. Process separation is not called a full sandbox until restricted-token/AppContainer/ACL hardening is verified.

### Word queue

All Word COM work uses one dedicated STA worker, `concurrency = 1`.

### SQLite

Every application database connection uses:

```sql
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;
```

Keep writer transactions short.

### Acceptance additions

- foreign browser origins cannot invoke privileged API actions,
- ShareHost is a separate process with a narrow dependency graph,
- Word COM maximum concurrency is exactly 1,
- SQLite WAL is active.
