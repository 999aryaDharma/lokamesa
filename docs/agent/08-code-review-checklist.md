# Sprint Code Review Checklist

Reviewer checks:

## Correctness
- Does code satisfy every acceptance criterion?
- Is error behavior explicit?
- Are race conditions addressed?

## Boundaries
- Is implementation in correct module?
- Did frontend bypass privileged backend?
- Did ShareHost gain a privileged dependency?

## File safety
- Any direct overwrite bypassing safe-write?
- Any path from untrusted input reaching filesystem unchecked?
- Any silent scope expansion?

## Search/batch
- Any fuzzy/semantic path entering destructive operation?
- Is stale-plan detection intact?

## Performance
- Any obvious full-scan/O(n²) path in frequently repeated interaction?
- Any expensive work on UI thread?
- Any Word COM parallelism?

## Tests
- Did new behavior start from a failing test?
- Do tests include failure path?
- Is a required suite skipped?
