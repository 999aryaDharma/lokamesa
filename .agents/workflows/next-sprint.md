# Next Sprint

## Description

Select the best next dependency-ready sprint without coding it.

## Steps

1. Read `docs/sprints/SPRINT-STATUS.md`.
2. Read `docs/sprints/sprint-manifest.json`.
3. Identify all sprints whose dependencies are DONE and which are not DONE/IN_PROGRESS.
4. Rank by:
   - P0 before P1/P2,
   - critical dependency path,
   - safety/security foundations before convenience features,
   - reduced future blocking.
5. Identify file/module overlap with any current IN_PROGRESS sprint.
6. Recommend:
   - primary next sprint,
   - optional safe parallel sprint,
   - required implementation agent/reviewer pairing.
7. Do not modify code.
