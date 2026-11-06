---
phase: 05-comparable-measurement
plan: 02
subsystem: evals
tags: [decisions, grader-003, correct-invoice-rate, failure-tags, compare, mcnemar, wilson]

requires:
  - phase: 04-synthetic-dataset
    provides: dataset-001 (frozen), D-40 and D-41, the open budget question
provides:
  - D-42 correct-invoice rate and the grader-003 rules, declared before any number exists
  - D-43 failure-tags-1 order and the holdout freeze rule
  - D-44 headline rows, Wilson and bootstrap intervals, the 11 declared slices
  - D-45 compare contract (one exact McNemar test, refusals, exit codes)
  - D-46 D-26 discharge for Phase 5 and the capped live dev run
  - D-47 DANFE wrap hazards and the derived render_hazards list
affects: [05-01, 05-03, 05-04, 05-05, 05-06, 05-07, 05-08, 05-09, 05-10, 05-11, 05-12, 05-13, phase-06]

actuals:
  tokens: 14000
  tasks: 2
  commits: 2
plan_head_before: 986a17ff9b8a8e48f4459869680ecae5fafa1239
plan_head_after: fb6a925b9c887a8a151b34796166dab121b18ad8

tech-stack:
  added: []
  patterns:
    - "Decision records cite CONTEXT D-nn once in a reading guide; superseding pointers sit under the older heading"

key-files:
  created: []
  modified:
    - docs/DECISIONS.md
    - justfile
    - README.md
    - AGENTS.md
    - .planning/REQUIREMENTS.md

key-decisions:
  - "D-42: primary metric correct_invoice_rate, tolerance 0.00 by default, row matching by linear_sum_assignment with a 7-column score and minimum 6"
  - "D-43: failure-tags-1 puts merged_rows and split_rows ahead of the generic row tags and adds rule 0 schema_invalid; the list freezes before the first live run that includes a test case"
  - "D-45: compare runs one exact McNemar test on text-layer cases, refuses mismatched runs with no override, exits 0 when it ran and 2 on a refusal"
  - "D-46: one live dev run through just dev-split at a US$2.50 cap, alias claude-haiku-4-5; D-26 discharged for Phase 5"
  - "D-47: wrap hazards are never forgiven; each case carries a derived render_hazards list"

requirements-completed: [EVAL-03, EVAL-04, EVAL-05, EVAL-06, EVAL-07, EVAL-08]

duration: 5min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 02: the phase decision records D-42 to D-47 Summary

**Primary metric (correct-invoice rate), grader-003 rules, failure-tag order and freeze, intervals, the compare contract, the D-26 discharge with its US$2.50 dev run, and the DANFE wrap hazards are on record in DECISIONS D-42 to D-47, with the docs-check range at D-18 to D-47 everywhere.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-11-05T20:39:38Z
- **Completed:** 2026-11-05T20:42:00Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- D-42 declares `correct_invoice_rate` with its denominator, exact tolerance, normalization, silent-error payment values and the row-matching score before any grader code or number exists (tracer task, verified by `just docs-check` and the drift test).
- D-43 to D-47 record the tag list and freeze rule (with `live model run` defined), the headline rows, Wilson and bootstrap intervals and 11 slices, the compare contract, the D-26 discharge with the dev run and the replay limit (`cache_miss`), and the two wrap hazards with measured counts.
- Pointer lines `**Refined by:**` added under D-05, D-20, D-24, D-26 and D-40; the MEAS-03 note in REQUIREMENTS.md names D-45; no requirement status changed.

## Task Commits

1. **Task 1 (tracer): D-42 and the docs gate through D-42** - `e32da51` (docs)
2. **Task 2: D-43 to D-47, pointers, range D-47, MEAS-03 note** - `fb6a925` (docs)

**Plan metadata:** recorded in the following docs commit.

## Files Created/Modified
- `docs/DECISIONS.md` - records D-42 to D-47 and pointer lines
- `justfile` - docs-check comment and loop through D-47
- `README.md`, `AGENTS.md` - `just docs-check` rows name D-18 to D-47
- `.planning/REQUIREMENTS.md` - MEAS-03 sub-line for the one slice moved into Phase 5

## Decisions Made
The six records themselves are the decisions; they restate CONTEXT D-01 to D-22 and RESEARCH findings without changing any. The only departures from CONTEXT's draft are the ones the plan prescribed and D-43 explains (tag order, rule 0).

## Deviations from Plan

None - plan executed exactly as written. One wording adjustment inside D-42's rationale: the tolerance reason cites what the DANFE prints (totals to the cent, quantities and unit prices to four decimals) rather than a looser phrasing.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
Later plans build against D-42 to D-47. Plan 05-01 owns the scipy pin and `python/pyproject.toml`; this plan touched neither. Records for the dev run's tag-list outcome are due in plan 05-12.

## Self-Check: PASSED

- `docs/DECISIONS.md` headings D-42 to D-47 present in order above the Open questions heading; `just docs-check` prints `docs ok`; `test_readme_drift.py` 27 passed.
- Commits `e32da51` and `fb6a925` are ancestors of HEAD.

---
*Phase: 05-comparable-measurement*
*Completed: 2026-11-05*
