# 10 — Read-Only Sharing, Security & ngrok Specification

**Project:** Document IDE  
**Status:** Security-critical baseline  
**Depends on:** `01-system-architecture.md`, `02-domain-data-model.md`

---

## 1. Purpose

Defines public read-only sharing from the user's own machine using ngrok, with Snapshot/Live modes, secret-link access, scoped content, restricted public APIs, optional downloads, and zero paid-server dependency.

---

## 2. Hosting Model

The user's computer is the content origin.

```text
Document IDE
→ local restricted share server
→ ngrok tunnel
→ public URL
→ viewer
```

If app/laptop/tunnel stops:

```text
share unavailable
```

Accepted behavior.

---

## 3. Access Model

Anyone with the secret link can view.

No account/login required.

Security comes from:

- unguessable share token,
- restricted server,
- explicit membership,
- no write routes.

---

## 4. Token

Generate high-entropy cryptographically secure token.

Public route:

```text
/share/<secret-token>
```

Store token hash, not plaintext, where feasible.

Ngrok hostname alone is not authorization.

---

## 5. Share Membership

User must explicitly select:

- documents,
- collection,
- or supported workspace subset.

Public server can resolve only member document IDs.

---

## 6. Snapshot Mode

On create:

```text
shared source set
→ create local publish artifacts
→ freeze content
```

Later source saves do not update snapshot.

---

## 7. Live Mode

On source save:

```text
watcher
→ index/preview update
→ share manifest/artifact refresh
```

Viewer receives latest saved local state for documents that are already explicit Share members. Live mode never auto-adds newly created files or later Collection members.

---

## 8. Download Permissions

Per share:

```text
Allow original download = OFF default
Allow PDF download      = OFF default
```

If OFF:

- routes for those downloads should return not found/forbidden,
- UI should not render download buttons.

---

## 9. Path Privacy

Never expose:

```text
D:\Kerjaan\BA\...
```

Viewer sees virtual names/structure only.

---

## 10. Public API Design

Allowed conceptual endpoints:

```text
GET /share/{token}
GET /share/{token}/documents
GET /share/{token}/documents/{id}
GET /share/{token}/documents/{id}/preview
GET /share/{token}/search?q=
GET /share/{token}/graph
GET /share/{token}/documents/{id}/download-original   (permission)
GET /share/{token}/documents/{id}/download-pdf        (permission)
```

No POST/PUT/PATCH/DELETE mutation endpoints.

---

## 11. Forbidden Design

Never expose:

```text
GET /file?path=...
```

Never pass through internal `/api/*` routes.

---

## 12. Path Traversal Protection

Any artifact resolution:

- starts from known internal artifact ID,
- canonicalizes application-owned artifact path,
- verifies it is under permitted share storage.

No user-controlled path concatenation.

---

## 13. Local Server Binding

Privileged internal API:

```text
loopback only
```

Public share server may also bind loopback and be exposed only via ngrok.

Do not listen broadly on LAN by default.

---

## 14. Ngrok Adapter

Define:

```text
IShareTransport
- Start(localUrl) -> PublicEndpoint
- Stop()
- GetStatus()
```

V1:

```text
NgrokTransport
```

Keep provider-specific code isolated.

---

## 15. Credentials

Ngrok credential/token setup:

- user configures according to ngrok requirements,
- store sensitive token via Windows secret protection,
- never log token.

---

## 16. Start Share Flow

```text
user chooses files
→ mode Snapshot/Live
→ permissions
→ create Share record
→ prepare artifacts/manifest
→ start restricted server
→ start ngrok
→ create final URL with /share/<secret>
→ show Copy Link
```

---

## 17. Stop Share

```text
Stop Share
→ mark share inactive
→ public routes reject token
→ stop transport if no other active share needs it
```

Old link must no longer access content.

---

## 18. Revoke / Regenerate

Support:

- revoke,
- create replacement share with new token.

Do not reactivate old token when regenerating.

---

## 19. Search in Share

Viewer may search only within shared member set.

Do not query entire workspace then filter only UI results. Enforce scope at backend query.

---

## 20. Graph in Share

If graph is shown:

- include only shared nodes,
- include only relations where both endpoints are allowed,
- no hidden source paths.

---

## 21. Snapshot Artifact Model

Possible artifacts:

- sanitized metadata JSON,
- rendered Markdown HTML,
- DOCX preview images/PDF,
- optional original,
- optional PDF.

Do not expose local working cache paths.

---

## 22. Security Headers

Public share server should configure appropriate:

- content security policy,
- no-sniff,
- frame policy as appropriate,
- referrer policy,
- cache control based on Snapshot/Live.

---

## 23. Rate Limiting

Even without login, apply basic local rate limiting to protect the laptop.

Examples:

- requests per IP/time window,
- concurrent expensive preview limits.

Search routes should operate only on prepared/indexed content.

---

## 24. No Mutation

Public routes MUST NOT:

- edit Markdown,
- launch Word,
- batch replace,
- rename,
- move,
- delete,
- create files,
- update collections/groups.

---

## 25. Viewer UX

Read-only UI may provide:

- folder/collection browsing,
- previews,
- search,
- graph,
- metadata,
- allowed downloads.

No disabled edit controls; omit them.

---

## 26. Audit

Local owner UI may show:

- share status,
- creation time,
- mode,
- member count,
- download permissions.

Detailed analytics are optional and not required.

---

## 27. Failure States

Show owner:

```text
ngrok unavailable
credentials missing
tunnel stopped
local server error
```

Viewer gets generic unavailable response without internal details.

---

## 28. Acceptance Criteria

Must pass security tests:

- token A cannot access share B,
- nonmember document ID returns denied/not found,
- path traversal fails,
- original download OFF cannot be bypassed,
- public API cannot call internal mutation endpoints,
- stopped share link fails,
- local physical paths never appear in public payload.

---

# 29. Post-Audit Sharing Hardening Requirements

This section is normative and supersedes weaker wording elsewhere in this file.

### Separate ShareHost

Public sharing runs in `DocumentIDE.ShareHost.exe`, separate from the privileged desktop process. ShareHost consumes only explicit share manifests, sanitized metadata, prepared previews/download artifacts, and share-scoped search data. It must not reference/inject Word automation, batch mutation, OpenXML mutation, unrestricted filesystem mutation, or privileged workspace-secret services.

Separate process is not claimed as a full OS sandbox until restricted-token/AppContainer/ACL hardening is verified.

### Live membership

`Live` means content updates for existing members only. New files in a shared folder and later Collection membership changes do not become public automatically. Owner action is required to add/remove Share members.

### Token

Use 256-bit cryptographically secure randomness, fixed encoding/length, early malformed/oversized input rejection, one-way stored hash where practical, and fixed-time hash verification.

### CSP and sanitization

Start from:

```text
default-src 'none'
script-src 'self'
connect-src 'self'
img-src 'self' data:
font-src 'self'
object-src 'none'
base-uri 'none'
frame-ancestors 'none'
form-action 'none'
```

No `unsafe-eval` and no generic script `unsafe-inline`. Sanitize rendered Markdown/HTML with a maintained sanitizer such as DOMPurify or equivalent. Public requests must never directly trigger Word COM rendering.

### Rate limits

Initial configurable V1 defaults:

```text
general reads        120 requests/minute/IP
search                30 requests/minute/IP
downloads             10 requests/minute/IP
concurrent downloads   2/IP
```

Return `429` with retry information.

### Acceptance additions

- malformed/oversized tokens fail cheaply,
- fixed-time token verification is used,
- Live Share cannot auto-publish new membership,
- stopped/nonmember access fails,
- ShareHost dependency graph excludes privileged services,
- injected script payloads are neutralized by CSP/sanitization,
- rate limits are enforced server-side.
