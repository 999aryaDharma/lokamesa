# Security Audit

## Description

Audit current implementation against Document IDE's explicit threat/safety model.

## Steps

1. Do not modify code first.
2. Review:
   - privileged localhost API,
   - path traversal/root containment/reparse points,
   - file mutation/recovery,
   - stale plans/Word locks,
   - DOCX resource exhaustion,
   - Share token/membership/scope,
   - ShareHost dependency boundary,
   - CSP/sanitization/rate limits/download authorization,
   - secret leakage.
3. Trace actual code paths, not only docs.
4. Report only applicable findings with exploit/failure path.
5. Classify CRITICAL/HIGH/MEDIUM/LOW.
6. For each finding specify regression test required.
7. Do not silently fix before findings are reviewed/accepted.
