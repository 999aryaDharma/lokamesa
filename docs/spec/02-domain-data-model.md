# 02 — Domain & Data Model Specification

**Project:** Document IDE  
**Status:** Baseline  
**Depends on:** `01-system-architecture.md`

---

## 1. Purpose

This document defines persistent domain entities, identities, relationships, invariants, and proposed SQLite schema boundaries.

The schema stores **workspace and derived metadata**, not authoritative document contents.

---

## 2. Core Invariants

1. A physical file can appear in multiple workspaces.
2. A workspace does not own the physical file.
3. Physical path is mutable; `DocumentId` is stable.
4. Canvas state is workspace-specific.
5. Graph state is workspace-specific.
6. Groups and collections are virtual.
7. Smart-group membership is derived from rules.
8. Derived metadata can be rebuilt.
9. History snapshots are temporary and expire after 30 days.
10. Public shares reference explicit document IDs, never arbitrary paths.

---

## 3. Identifier Strategy

Use application IDs independent of filesystem paths.

Recommended:

```text
WorkspaceId   UUID
DocumentId    UUID
GroupId       UUID
CollectionId  UUID
RelationId    UUID
OperationId   UUID
ShareId       UUID
```

Filesystem identity is stored as an external identity attribute.

---

## 4. Workspace

```text
Workspace
- id
- name
- created_at
- updated_at
- active_view
- canvas_viewport_json
- list_state_json
- graph_viewport_json
```

Constraints:

- name required,
- user-facing duplicate names may be allowed but unique IDs are authoritative.

---

## 5. FolderSource

```text
FolderSource
- id
- workspace_id
- canonical_path
- filesystem_volume_id
- filesystem_object_id
- display_name
- availability_status
- added_at
- last_reconciled_at
```

Availability:

```text
available
missing
permission_denied
error
```

Folder source belongs to one workspace record. The same physical folder in another workspace gets another `FolderSource`.

---

## 6. Document

Represents one physical file identity.

```text
Document
- id
- filesystem_volume_id
- filesystem_object_id
- current_path
- last_known_path
- filename
- extension
- document_type
- size_bytes
- modified_at
- content_fingerprint
- availability_status
- index_status
- created_at
- updated_at
```

Document types:

```text
docx
markdown
```

Availability:

```text
available
missing
locked
permission_denied
error
```

Index status:

```text
unknown
queued
indexing
ready
stale
error
```

---

## 7. WorkspaceDocument

Stores workspace-specific presentation of a physical document.

```text
WorkspaceDocument
- workspace_id
- document_id
- source_folder_id
- visible_on_canvas
- canvas_x
- canvas_y
- width
- height
- position_mode
- pinned
- last_manual_move_at
- created_at
- updated_at
```

Position mode:

```text
auto
manual
pinned
```

Invariant:

- canvas position does not imply filesystem location.

---

## 8. FolderCanvasState

Folder objects also need workspace positioning.

```text
FolderCanvasState
- workspace_id
- folder_source_id
- canvas_x
- canvas_y
- width
- height
- position_mode
- pinned
- expanded_state
```

`expanded_state` refers to UI state only; opening files on canvas creates/activates `WorkspaceDocument` rows.

---

## 9. Group

```text
Group
- id
- workspace_id
- parent_group_id
- name
- group_type
- rule_json
- canvas_x
- canvas_y
- width
- height
- position_mode
- pinned
- created_at
- updated_at
```

Group types:

```text
manual
smart
```

Nesting constraint:

```text
max depth = 3
```

The backend must reject creation that exceeds the configured maximum.

---

## 10. GroupMember

Used only for manual groups.

```text
GroupMember
- group_id
- document_id
- created_at
```

A document MAY belong to multiple groups.

Dropping a document into a virtual group does not alter the physical file.

---

## 11. Collection

```text
Collection
- id
- workspace_id
- name
- description
- created_at
- updated_at
```

---

## 12. CollectionMember

```text
CollectionMember
- collection_id
- document_id
- sort_order
- added_at
```

Used for persistent cross-folder sets and ordered PDF/context workflows.

---

## 13. DocumentMetadata

Derived document metadata.

```text
DocumentMetadata
- document_id
- page_count
- section_count
- table_count
- image_count
- paper_summary
- orientation_summary
- margin_summary
- primary_font
- primary_font_size
- header_present
- footer_present
- headings_json
- extracted_at
- parser_version
```

For Markdown fields that do not apply, use null values rather than invented defaults.

---

## 14. SearchContent

FTS5 virtual table should store normalized searchable content.

Logical fields:

```text
document_id
filename
body
headings
tables
headers
footers
frontmatter
```

Do not store arbitrary binary DOCX data.

---

## 15. MarkdownMetadata

```text
MarkdownMetadata
- document_id
- frontmatter_json
- headings_json
- outbound_links_json
- wikilinks_json
- code_block_count
- updated_at
```

---

## 16. Relation

```text
Relation
- id
- workspace_id
- source_document_id
- target_document_id
- relation_type
- relation_origin
- confidence
- metadata_json
- created_at
- updated_at
```

Origins:

```text
markdown_link
wikilink
frontmatter
manual
semantic
```

Recommended relation types:

```text
references
depends_on
related_to
implements
supersedes
```

Do not over-normalize relation vocabulary in V1. Preserve relation origin separately from semantic type.

---

## 17. GraphNodeState

```text
GraphNodeState
- workspace_id
- document_id
- x
- y
- pinned
- updated_at
```

Graph coordinates MUST remain separate from canvas coordinates.

---

## 18. OperationHistory

```text
OperationHistory
- id
- workspace_id
- operation_type
- status
- parameters_json
- affected_file_count
- affected_occurrence_count
- created_at
- completed_at
- undone_at
```

Statuses:

```text
previewed
applying
completed
partial
failed
undone
```

---

## 19. RecoverySnapshot

```text
RecoverySnapshot
- id
- operation_id
- document_id
- snapshot_path
- original_fingerprint
- post_operation_fingerprint
- created_at
- expires_at
```

Snapshots expire after:

```text
30 days
```

unless manually pinned by a future feature.

---

## 20. BatchOperationPlan

Preview plans should be persisted temporarily or held in an in-memory + durable hybrid store.

```text
BatchOperationPlan
- id
- workspace_id
- operation_type
- scope_json
- parameters_json
- status
- created_at
- expires_at
```

---

## 21. BatchOperationPlanFile

```text
BatchOperationPlanFile
- plan_id
- document_id
- expected_fingerprint
- match_count
- selected
- plan_payload_json
```

This supports stale detection between preview and apply.

---

## 22. Share

```text
Share
- id
- workspace_id
- token_hash
- mode
- allow_original_download
- allow_pdf_download
- status
- created_at
- stopped_at
```

Modes:

```text
snapshot
live
```

Statuses:

```text
inactive
starting
active
stopped
error
```

Never store the raw secret token if avoidable; store a hash and only show the generated link when created/copied.

---

## 23. ShareMember

```text
ShareMember
- share_id
- document_id
- snapshot_manifest_path
- sort_order
```

---

## 24. ShareSnapshotArtifact

```text
ShareSnapshotArtifact
- share_id
- document_id
- artifact_type
- path
- fingerprint
- created_at
```

Artifact types:

```text
preview
rendered_markdown
metadata
original
pdf
```

`original` and `pdf` artifacts only exist if needed by permissions/workflow.

---

## 25. EmbeddingRecord

```text
EmbeddingRecord
- document_id
- provider
- model_id
- content_fingerprint
- vector_reference
- created_at
```

Embedding storage is derived and rebuildable.

---

## 26. AppSettings

Logical settings:

```text
theme
default_workspace_id
history_retention_days = 30
local_ai_provider
external_ai_enabled
ngrok_configuration_reference
```

Secrets MUST be stored in an OS-protected secret store rather than plaintext fields.

---

## 27. Indexes

Recommended indexes:

```text
Document(filesystem_volume_id, filesystem_object_id)
Document(current_path)
WorkspaceDocument(workspace_id, document_id)
FolderSource(workspace_id)
Group(workspace_id)
Collection(workspace_id)
Relation(workspace_id, source_document_id)
Relation(workspace_id, target_document_id)
OperationHistory(workspace_id, created_at)
RecoverySnapshot(expires_at)
Share(status)
```

---

## 28. Cascading Behavior

Deleting a workspace:

- delete workspace presentation metadata,
- delete groups/collections/graph states,
- delete workspace-specific share definitions,
- MUST NOT delete physical documents.

Removing a folder source:

- removes source from that workspace,
- MUST NOT delete physical folder,
- workspace document references may be retained if still reachable through another source/collection according to implementation policy.

Deleting a physical file through explicit command:

- use Recycle Bin,
- retain document record as missing until reconciliation/user cleanup.

---

## 29. Migration Policy

Use explicit schema migrations.

Requirements:

- database schema version,
- forward-only migrations in normal releases,
- backup app database before risky migration,
- caches remain rebuildable.

---

## 30. Acceptance Criteria

Data model is correct when:

- file rename does not require new `DocumentId`,
- same document can appear in multiple workspaces,
- graph and canvas positions are independent,
- groups do not imply physical movement,
- history is operation-centric,
- shares cannot resolve non-member documents,
- derived metadata/index can be deleted and rebuilt from source files.

---

# 31. Post-Audit Data Model Amendments

These fields/invariants are normative.

### RecoverySnapshot

Add `size_bytes`. Retention remains 30 days. Recommended V1 history budget is 5 GiB with warning at 80%. Unexpired snapshots are not silently evicted to make room for a new destructive operation.

### Share

Add `token_format_version`. Share tokens use 256-bit cryptographically secure randomness; persist only a hash where practical and verify hashes in fixed time.

### ShareMember

Membership is explicit and stable. If a Share is created from a Collection, copy current Collection members into Share membership. Later folder/Collection changes do not automatically add public members.

### AppSettings

Add:

```text
history_storage_budget_bytes = 5 GiB
history_warning_ratio = 0.80
```

### SQLite startup

```sql
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;
```
