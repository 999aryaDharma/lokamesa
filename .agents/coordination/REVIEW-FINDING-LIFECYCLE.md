# Review Finding Lifecycle

A reviewer finding has states:

```text
PROPOSED
ACCEPTED
REJECTED_WITH_EVIDENCE
FIXED
VERIFIED
```

## Reviewer

Reports evidence; does not assume every concern is automatically correct.

## Implementer

For each finding:
- compare against spec,
- reproduce where applicable,
- accept or reject with evidence.

If accepted:
1. add/strengthen regression test first,
2. fix smallest root cause,
3. run affected suite,
4. report evidence.

## Final reviewer

Verifies fixes and gives final verdict.

Disagreement that changes architecture/product behavior escalates to:
- higher-level spec,
- ADR/change request,
- user/product-owner decision.
