# File Safety & Security — Always On

Document IDE manipulates real user documents. Safety beats convenience.

## Privileged local API

Must retain:

- loopback scoping,
- Host validation,
- dedicated WebView Origin validation,
- per-startup 256-bit session authorization,
- no wildcard CORS,
- no state-changing GET.

## File mutation

A destructive mutation path must not bypass:

```text
reviewed intent/scope
→ current identity/fingerprint validation
→ external lock/editing check
→ recovery-capacity check
→ recovery snapshot
→ temp mutation
→ validation
→ atomic replace
→ reindex/history
```

If recovery cannot safely be created, mutation is blocked.

## Paths

- canonicalize,
- distinguish basename from path,
- enforce source-root containment,
- reject traversal,
- respect junction/reparse-point policy.

## Word

All Word COM automation runs through the dedicated STA queue with maximum concurrency exactly 1.

## Sharing

- explicit ShareMember only,
- Live content may refresh; membership never expands automatically,
- ShareHost is separate from privileged process,
- ShareHost must not depend on Word/batch/unrestricted mutation services,
- token scope/download permissions/CSP/sanitization/rate limits are enforced server-side.

Never claim ShareHost is an OS sandbox unless the isolation ADR/tests prove it.
