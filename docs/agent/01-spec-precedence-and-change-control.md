# Spec Precedence & Change Control

## Precedence

1. Master product spec
2. Subsystem spec
3. Implementation roadmap
4. Sprint spec
5. Existing implementation
6. Comments

## If specs conflict

Do not guess.

Record:
- exact conflicting statements,
- affected sprint,
- safest temporary behavior.

If the higher-level requirement is unambiguous, follow it and update the lower-level doc in the same change.

## Architecture changes

Any change to:
- process boundary,
- source-of-truth,
- public security boundary,
- file mutation semantics,
- database identity model,
- Word integration model,

requires an ADR in `docs/decisions/`.

## Product-scope changes

Do not implement a new product behavior merely because a library makes it easy.
Update/approve the master spec first.
