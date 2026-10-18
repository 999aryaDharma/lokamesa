# ADR-003 — Wildboard Workspace Kind and Content Vault

**Status:** Accepted (owner decision, 2026-10-18)

> Accepted as the governing decision for roadmap versions `1.7.0`–`1.9.0`. Acceptance authorizes specification and implementation; it does not waive evidence. Each sprint `WB-01`…`WB-05` must attach the test evidence listed below to its handoff, and a failing item reopens the affected decision rather than being waived.

## Context

Document IDE V1 models exactly one kind of workspace. Migration `001_Initial` establishes:

```text
workspaces(id, name, active_view, canvas_viewport_json, ...)
folder_sources(workspace_id, canonical_path, ...)
documents(id, current_path, document_type, index_status, ...)
workspace_documents(workspace_id, document_id, canvas_x, canvas_y, position_mode, pinned)
```

Every canvas object is therefore a tracked document or a folder source, and `02-domain-data-model.md` states that the schema "stores workspace and derived metadata, not authoritative document contents".

A second product direction is now requested: a **Wildboard** — a deliberately unstructured "second brain" canvas for brainstorming, collecting ideas, visuals, and random writing. The owner also requested **functional parity with Spatial** (get-spatial.com) and a **Medium-style rich writing surface**.

These requirements touch three areas that the change-control policy reserves for an ADR:

- **source-of-truth** for Wildboard content,
- **database identity model** (new workspace kind and object table),
- **product scope** that is not expressible inside the existing `documents` model.

Hard constraints that this ADR must not weaken:

- local filesystem remains the source of truth for document content,
- Markdown remains plain `.md`,
- canvas drag never physically moves files,
- `space`/workspace metadata must not become an authoritative copy of file contents,
- cloud-first storage is out of scope.

## Decision

1. **Workspace kind.** Add `workspaces.kind` with values `documents` (default) and `wildboard`. Existing rows migrate to `documents` with no behavioural change.

2. **Content vault.** Wildboard content is stored as **real files in a per-workspace vault**:

   ```text
   <vault>/notes/<id>.md      # text stickies and sheets
   <vault>/links/<id>.md      # link objects (URL in YAML frontmatter)
   <vault>/assets/<hash>.<ext># imported media (from 1.9.0)
   ```

   The vault is app-managed at `%LOCALAPPDATA%\DocumentIDE\vaults\<workspaceId>\`. Relocation to a user-chosen folder is deferred to a later version; `1.7.0` exposes an "open vault folder" action **and an "export vault" (zip) action**, because app-managed storage is otherwise neither discoverable nor backed up. Files are written atomically (temp → validate → move/replace) **and every replace first takes a recovery snapshot of the previous content; if the snapshot cannot be created, the write is blocked**. Deletion moves the file into a vault trash (`<vault>/.trash/`) instead of unlinking it, so an accidental delete stays recoverable.

3. **Placement model.** A new table stores canvas placement and object kind:

   ```text
   space_objects(id, workspace_id, kind, canvas_x, canvas_y, width, height,
                 vault_relative_path, mime_type, title, availability_status,
                 created_at, updated_at)
   ```

   `kind ∈ {text, sheet, image, video, link, webclip, stack, label}`. `vault_relative_path` is `NULL` only for metadata-only kinds (`stack`, `label`).

4. **Stacks are metadata-only.** `stack_members(stack_id, object_id)` provides a flat, non-nesting grouping. A stack never creates, moves, or renames a physical folder.

5. **Subsystem isolation.** Wildboard workspaces do not expose Microsoft Word/DOCX integration, the batch mutation engine, PDF export, sharing, folder sources, or Git source control. Gating is enforced on both sides:
   - **backend** — every workspace-scoped document route rejects a `wildboard` workspace with `409 { errorCode: "WorkspaceKindNotSupported" }`; document-id routes are unreachable because wildboard objects are never `documents` rows;
   - **frontend** — document-only panels and tool toggles are not mounted for a `wildboard` workspace, driven by a server-reported capability set.

   Disabling a control without the backend rejection is not sufficient.

6. **Rich editor (from 1.8.0).** A Medium-style block editor edits the same Markdown files. Markdown remains the canonical stored representation, and a **Source** toggle preserves raw editing. This amends the "source-first" wording of the Markdown specification; it does not change file format or safety semantics.

7. **No cloud sync.** Wildboard is local-first. Backup/export is filesystem-based. Spatial's planned iCloud sync is explicitly not adopted.

8. **Wildboard objects are not `documents`.** They do not inherit `index_status`, Git semantics, or document metadata. Promotion of a wildboard file into a tracked document is an explicit later feature (1.9.0), not automatic.

## Alternatives Considered

- **Separate process/window (ShareHost-style).** Maximum isolation, but requires IPC, a second database, a duplicated canvas engine, and additional packaging. Rejected for the current scope; this ADR may be superseded if isolation requirements grow.
- **Content stored in SQLite.** Fastest to implement, but makes the database an authoritative content store, contradicting `02-domain-data-model.md` and the local-first rule. Rejected.
- **Wildboard as a normal `FolderSource` workspace.** Every object becomes a tracked `Document`, inheriting Word/Git/batch behaviour and document metadata. Rejected: it inverts the intended low-stakes semantics and clutters the document model.
- **Reusing `documents` with a `is_wildboard` flag.** Rejected: couples unrelated subsystems and makes later isolation harder to enforce.
- **Adopting Spatial's iCloud sync for parity.** Rejected: violates the local-first, filesystem-as-source-of-truth constraint.

## Consequences

### Positive

- The existing canvas engine (pan/zoom/transform, selection controller, group drag, pinning) is reused; only object renderers and the object source differ.
- Content stays portable and inspectable as ordinary files; users can back up or open the vault with any editor.
- Document safety rules stay untouched because wildboard content never enters the mutation engine.
- Workspace `kind` gives a single, enforceable gate for hiding document-only subsystems.

### Negative / residual risk

- Two object stores (`documents` / `space_objects`) must remain disciplined; shared features (search, sharing) need explicit bridging rather than accidental coupling.
- UI gating must be enforced at the surface level, not only by disabling controls; a missed gate would expose Word/Git actions in a wildboard.
- A vault under application data is not discoverable by default; `1.7.0` therefore ships an "open vault folder" action, an "export vault" (zip) action, and a documented backup story.
- Media and video storage can grow without bound; retention and size limits are required in 1.9.0.
- Functional parity with Spatial remains incomplete for network-dependent features (webclips, X import) until sanitization and SSRF protections are specified.

## Security & File-Safety Impact

- Vault root is resolved and canonicalized once; every object path is validated as a normalized relative path, rejecting absolute paths, `..` traversal, and reparse-point/symlink escape.
- Media import validates size limits and content type; bytes are stored, never executed.
- Link objects store a URL as data. No automatic network fetch occurs in 1.7.0; webclips (1.9.0) require sanitization, an explicit user action, and SSRF controls.
- Wildboard content is never exposed through the privileged local API or ShareHost by default. Public sharing continues to reference explicit document IDs only.
- No new shell invocation path is introduced; no filename or metadata value reaches a command line.
- Deletion semantics: removing a `space_object` removes its placement; deleting the backing file requires explicit user confirmation and moves the file to the vault trash, never an immediate unlink.
- **Source-mutation discipline applies to vault content.** A replace is snapshot → temp write → validate → atomic replace, and a failed snapshot blocks the write. This is the same non-negotiable the document mutation engine follows; wildboard content is user source, not derived data.

## Test / Experiment Evidence

Required evidence, attached to the handoff of the owning sprint (`WB-01`…`WB-05`):

- migration `012` idempotency and default-`documents` backfill,
- workspace-kind gating tests (Word/batch/PDF/Git surfaces absent for `wildboard`),
- path-traversal and reparse-point rejection tests for vault paths,
- atomic-write behaviour under simulated failure,
- flat stack model invariants (no nesting, deleting a stack preserves members),
- canvas reuse performance gate at 1,000 objects,
- round-trip integrity: rich-editor save → Markdown file → source view preserves content.

## Supersedes

None.

## Superseded By

None.
