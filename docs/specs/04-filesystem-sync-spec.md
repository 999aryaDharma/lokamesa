# 04 — Filesystem Synchronization & Identity Specification

**Project:** Document IDE  
**Status:** Safety-critical baseline  
**Depends on:** `01-system-architecture.md`, `02-domain-data-model.md`

---

## 1. Purpose

Defines how Document IDE tracks local files and folders while running, reconciles changes after downtime, preserves workspace identity through rename/move, and handles missing/locked/conflicting files.

---

## 2. Source-of-Truth Rule

The physical filesystem is authoritative.

Application cache may be stale.

On disagreement:

```text
filesystem wins
```

except that application-owned virtual metadata such as canvas positions/groups remains independent.

---

## 3. Supported Change Types

Track:

- create file,
- modify file,
- rename file,
- move file,
- delete file,
- create directory,
- rename directory,
- move directory,
- delete directory.

Only supported `.docx` and `.md` documents enter document indexing.

---

## 4. Runtime Watcher

Use Windows filesystem watchers for every active folder source.

Watcher output is a hint, not authoritative truth.

Events must be:

- debounced,
- coalesced,
- verified against filesystem state.

---

## 5. Why Verification Is Required

Watcher systems can:

- emit duplicate events,
- split one save into several events,
- overflow event buffers,
- reorder related rename/create/delete observations,
- miss events during process downtime.

Therefore every event pipeline ends in a filesystem stat/identity verification.

---

## 6. Event Debounce

Recommended conceptual debounce:

```text
event
→ collect per canonical path/identity for short interval
→ merge event sequence
→ inspect final state
```

Do not re-index a DOCX three times because Word produced multiple transient events for one save.

---

## 7. Stable File Identity

Preferred identity on Windows/NTFS:

```text
volume identity + file ID
```

Application identity:

```text
DocumentId
```

Path is an attribute, not identity.

---

## 8. Rename Handling

Scenario:

```text
BA Januari.docx
→ BA Januari FINAL.docx
```

If filesystem identity matches:

```text
update current_path + filename
preserve DocumentId
preserve canvas position
preserve groups
preserve collections
preserve graph state
```

---

## 9. Move Handling

Move within monitored source and same volume:

- preserve identity when available.

Move between monitored folders:

- update source association/path,
- preserve workspace identity.

Cross-volume move may appear as delete + create.

Fallback correlation may use:

- filename,
- size,
- modified time,
- content fingerprint,
- recent operation journal.

Never auto-correlate ambiguous files.

---

## 10. Explicit App-Originated Move

When Document IDE performs `Move physical file...`, it knows source and destination.

Record a short-lived operation journal:

```text
PhysicalMoveIntent
- document_id
- source
- destination
- started_at
```

Use this to correlate watcher events reliably.

---

## 11. Modification Handling

On supported document modification:

```text
mark stale
→ fingerprint
→ if changed: queue re-index
→ invalidate derived previews/embeddings
```

If timestamp changed but fingerprint did not, avoid unnecessary deep re-index.

---

## 12. Startup Behavior

Selected UX:

```text
Hybrid startup
```

Flow:

```text
load cached workspace immediately
→ display objects
→ mark unverified state subtly if needed
→ quick reconciliation
→ update differences
```

Do not block the entire UI on reparsing all files.

---

## 13. Quick Reconciliation

For each known source:

1. enumerate supported files,
2. collect path,
3. filesystem identity,
4. size,
5. modified timestamp,
6. compare with database.

Only changed/unknown documents are fingerprinted/parsed deeply.

---

## 14. Full Reconciliation

Trigger full reconciliation when:

- watcher overflow occurs,
- user requests refresh/repair,
- database indicates interrupted prior scan,
- source comes back after being unavailable,
- suspicious event sequence occurs.

---

## 15. Missing File

When known document no longer exists:

```text
availability = missing
```

Do not destroy workspace presentation.

Show last known path.

Actions:

- Locate file
- Remove from workspace

---

## 16. Locate File

User can manually locate a missing file.

Before reconnecting:

- compare type,
- filename if relevant,
- size,
- fingerprint where possible.

If clearly same file:

- rebind path/identity,
- preserve DocumentId.

If uncertain:

- require explicit confirmation.

---

## 17. Missing Folder Source

Source unavailable due to:

- drive disconnected,
- path renamed,
- permission issue.

Keep folder object as unavailable.

Do not automatically remove child workspace metadata.

---

## 18. Permission Errors

Differentiate:

```text
missing
permission_denied
locked
```

Do not mislabel access denied as deleted.

---

## 19. Word Lock Detection

Detect potential Word editing using multiple signals where practical:

- lock/temp file convention,
- exclusive access failure,
- Word automation document list,
- file change state.

Expose:

```text
Editing in Word
```

No single fragile heuristic should be considered absolute.

---

## 20. Mutation Lock

Before application mutation:

- acquire application document-level lock,
- verify external lock/open state,
- verify expected fingerprint.

If external editing detected:

```text
skip / open Word / retry after save
```

---

## 21. Conflict Detection

Any previewed destructive operation includes expected content fingerprint.

At apply time:

```text
current fingerprint != expected
→ stale plan
→ reject mutation
→ rescan/repreview
```

---

## 22. Physical Rename from App

Explicit rename flow:

```text
validate new name
→ validate path legality
→ ensure destination absent
→ filesystem rename
→ watcher correlation
→ update database
```

Do not rewrite document contents.

---

## 23. Delete

Default:

```text
Windows Recycle Bin
```

After deletion:

- document becomes missing until reconciliation confirms,
- retain workspace metadata/history.

Permanent delete is out of scope for default V1 behavior.

---

## 24. Directory Recursion

Folder sources are recursively monitored/indexed.

Search & Replace folder scope also recursively includes subfolders by default.

Watcher must handle dynamic creation of nested directories.

---

## 25. Symlinks / Junctions

Windows junctions/symlinks can cause cycles or access outside expected roots.

Policy:

- canonicalize,
- detect reparse points,
- prevent infinite recursion,
- do not silently traverse outside configured source roots without explicit product decision.

---

## 26. Case Sensitivity

Windows paths are normally case-insensitive.

Store canonical paths and compare using platform-appropriate semantics.

Do not assume path string casing implies a different file.

---

## 27. Network Drives

V1 may support local/attached drives best-effort.

For network drives:

- file IDs may behave differently,
- watcher reliability may differ,
- latency may be high.

Mark network-drive support as best-effort unless explicitly validated.

---

## 28. State Machine

```text
Unknown
→ Queued
→ Indexing
→ Ready
```

External save:

```text
Ready
→ Stale
→ Indexing
→ Ready
```

Missing:

```text
Ready
→ Missing
```

Reconnected:

```text
Missing
→ Queued
→ Indexing
→ Ready
```

Error:

```text
Indexing
→ Error
→ Retry/Repair
```

---

## 29. Acceptance Criteria

Must pass:

- Word save updates search without workspace reload,
- app restart detects files changed while app was closed,
- file rename preserves canvas position,
- missing file remains visible as missing,
- watcher duplicates do not create duplicate `Document` records,
- stale batch plans are rejected,
- physical deletes go to Recycle Bin,
- an unavailable source does not erase workspace state.

---

# 30. Post-Audit Filesystem Hardening

### Junction/symlink containment

Canonicalize configured roots and resolved reparse-point targets. A reparse point that resolves outside the configured source root MUST NOT be silently traversed. The same containment policy applies to indexing, sharing, batch scope, and explicit filesystem operations.

### Ambiguous cross-volume correlation

Require manual confirmation when more than one plausible candidate exists, including duplicate content fingerprints, duplicate copies, multiple filename/size/mtime matches, or cases where the old file and a candidate both still exist. Identical content does not prove identical document identity.

### Acceptance additions

- escaped junction/symlink content is not indexed or shared,
- ambiguous cross-volume candidates never auto-bind.
