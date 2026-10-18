# Original User Request

## Initial Request — 2026-08-10T03:59:46Z

Execute parallel implementation of 10 ready sprints for Document IDE across 4 functional clusters. Follow strict TDD (Red -> Green -> Refactor) and repository guardrails in AGENTS.md.

Working directory: D:/document-ide
Integrity mode: development

## Requirements

### R1. Filesystem & Recovery Cluster (FS-04, FS-05, FS-07, HIST-01)
- FS-04: Non-blocking startup reconciliation of attached FolderSource records against filesystem state.
- FS-05: Rename & move correlation using NTFS volume/file ID and fallback content fingerprinting.
- FS-07: Explicit physical file operations (create, move, rename, send to Windows Recycle Bin).
- HIST-01: 30-day pre-mutation recovery snapshot storage with 5 GiB budget tracking.

### R2. Indexing & Document Parsers Cluster (IDX-01, IDX-03)
- IDX-01: OpenXML DOCX ZIP package preflight validation and basic metadata extraction.
- IDX-03: Markdown structural parser for AST, YAML frontmatter, heading tree, and wikilinks.

### R3. Word Integration Cluster (WORD-01)
- WORD-01: Open DOCX files directly in Microsoft Word Desktop via COM automation with strict concurrency limit of 1.

### R4. Security, Sharing & Foundation Infrastructure Cluster (SHR-01, QA-03, FND-06)
- SHR-01: Share domain entities, database schema, and sanitized manifest generator.
- QA-03: Privileged local API security audit verifying Host/Origin validation and session tokens.
- FND-06: Structured file and console logging with diagnostic reporting.

## Acceptance Criteria

### Verification & Safety
- [ ] Each sprint branch passes its required unit/integration tests using TDD.
- [ ] powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 passes cleanly (ALL VERIFICATION STEPS PASSED SUCCESSFULLY!).
- [ ] Non-negotiable safety rules strictly enforced:
  - Local filesystem remains source of truth.
  - Source mutation requires recovery snapshot before edit.
  - Word COM concurrency = 1.
  - Privileged API rejects unauthorized cross-site/unauthenticated requests.
- [ ] docs/sprints/SPRINT-STATUS.md updated for each completed sprint.
- [ ] Handoff reports created in docs/sprints/handoffs/ using SPRINT-HANDOFF-TEMPLATE.md.
