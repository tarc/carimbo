---
phase: 05-comparable-measurement
plan: 06
subsystem: evals
tags: [silent-errors, payment-values, failure-tags, incomplete-runs, render-hazards, summary-v4]

requires:
  - phase: 05-comparable-measurement
    provides: wrong values with failure tags (05-04), summary scopes, correct_row and Wilson stats (05-05), D-42 to D-47 records (05-02)
provides:
  - grader.py PAYMENT_HEADER_PATHS, is_payment_value, load_run_state; a `payment` flag on every wrong value
  - run_meta keys stopped_reason and cases_not_run; case key render_hazards on every graded record
  - slices.py HAZARD_NUMBER_MIN and render_hazards (derived from the ground truth, not a slice)
  - summary keys silent_errors, wrong_on_success, failure_tags, tags_version, incomplete, incomplete_detail, render_hazards
  - summary.md sections "Silent errors" and "Failure tags", an INCOMPLETE bullet and hazard notes under "Correct invoices"
affects: [05-07, 05-08, 05-09, 05-10, 05-11, 05-12, 05-13, phase-06]

actuals:
  tokens: 12400
  tasks: 2
  commits: 2
plan_head_before: 4e8811709b4b4f862ffa6a9d61be9a3bb66077c5
plan_head_after: 037d2804309ed8ae7890fca8f9b6da4378322d33

tech-stack:
  added: []
  patterns:
    - "The payment flag is computed once in diff_invoice from scope and column; the summary only reads it"
    - "A damaged or missing run.json degrades to {} and statuses alone still mark the run incomplete"
    - "A derived per-case property (render_hazards) is recorded on every case before any early return and reported beside the result"

key-files:
  created:
    - python/tests/test_silent_errors.py
  modified:
    - python/src/carimbo_evals/grader.py
    - python/src/carimbo_evals/slices.py
    - python/src/carimbo_evals/summary.py
    - python/tests/test_grader.py

key-decisions:
  - "failure_tags counts every wrong value of every graded case (validation_failed included) so the counts add up to the wrong-value total; wrong_on_success and silent_errors count success cases only"
  - "The hazard notes are printed as bullets after the Correct invoices table, not between its rows, because a markdown table cannot hold a line between rows"
  - "summary_version stays 4: the new keys extend the version 4 summary introduced in 05-05"

patterns-established:
  - "silent_block(cases) reuses stats.rate and stats.wilson with n = distinct base_case, like every other rate in the summary"

requirements-completed: []

duration: 14min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 06: silent errors, tag counts and honest run status in the summary Summary

**A graded run now reports how many invoices passed the validators and were still wrong, split into payment and other with a Wilson interval, counts every failure tag (19 keys, adding up to the wrong-value total), flags unfinished runs as INCOMPLETE, and shows where the DANFE wrap hazard sits without ever forgiving a wrapped value.**

## Performance

- **Duration:** 14 min
- **Completed:** 2026-11-05T21:45:00Z
- **Tasks:** 2 (one tracer, one auto, both tdd)
- **Files:** 5 (1 created, 4 modified)

## Accomplishments

- Tracer: a stored run over `data/dataset` (one right, one success with a wrong item total, one success with a wrong item description, one validation_failed with a wrong invoice total) gives `silent_errors.overall` success_cases 3, silent 2, payment 1, other 1, rate 0.6667, read from `summary.json` on disk. The validation_failed case is caught and not counted.
- `is_payment_value` implements the D-06 list exactly: five header paths, the item `total` column and the installment `due_date` and `amount` columns. Missing and extra rows and installments already land on `total` and `amount`, so they qualify. Every wrong value carries `payment`.
- `silent_block` per scope: rate and ci95 are null with no success case, never 0.0; a success with zero wrong values and rate 0.0 is tested apart.
- `wrong_on_success` (header path or `item.<column>` / `installment.<column>`, sorted) over success cases only.
- `failure_tags`: version, wrong_values, `counts` and `cases` for all 19 tags with zeros kept, `untagged`. Over a run mixing six corruption kinds the counts sum to the wrong-value total.
- `incomplete` and `incomplete_detail` from infrastructure_failure, harness_error, a cost-cap stop and selected cases with no record; `load_run_state` returns `{}` for a missing, torn or non-object run.json (T-05-11, T-05-12).
- `render_hazards` reproduces the D-47 measurements from the ground truth: unit_wrap on 43 cases, number_wrap on exactly the 18 confusable cases, 31 of 60 test and 12 of 30 dev text-layer cases carry at least one. `PA` for `PAR` stays wrong, tagged `text_wrong`, and a hazard-carrying correct answer stays correct. No hazard is a slice.

## Task Commits

1. **Task 1 (tracer): silent-error rate with the payment and other split** - `b5bec37` (feat)
2. **Task 2: wrong-on-success counts, tag counts, incomplete flag, wrap hazards and their sections** - `037d280` (feat)

## Verification

- Tracer feedback gate: the `<verify>` is automated only (22 tests passed) and was re-run before expansion.
- `devenv shell -- just py-check` exit 0, 3,168 passed (3,125 baseline plus this plan's 43). `ruff check`, `ruff format --check` and `pyright -p python` clean.
- `pytest python/tests/test_grader.py -k "infrastructure or harness or incomplete"` selects 13 tests, all pass (plan minimum 3).
- Acceptance: `payment + other == silent` is asserted in a test; every wrong value has `payment`; `failure_tags.counts` has 19 keys; `grep -c render_hazards grader.py` prints 2 (baseline 0); no hazard name is in `SLICES`; `summary["incomplete"]` is False for the clean fixtures.

## Deviations from Plan

None - plan executed exactly as written. Two small presentation choices within the plan's latitude: the dev and test hazard notes are bullets after the `## Correct invoices` table (a table cannot hold a line between rows), and the Silent errors table labels the dev scope `(dev)` like the other tables.

## Issues Encountered

None. My first draft of the missing-row test used a 3-item minimum while `nfe-0003` has 2 items; the assertion was loosened to what the test needs.

## Known Stubs

None.

## Threat Flags

None. T-05-11 is mitigated (`incomplete` and `incomplete_detail` from statuses and run.json, an INCOMPLETE banner, a test per trigger). T-05-12 is mitigated (`load_run_state` returns `{}` on a missing or malformed file, tested with four damaged shapes, and harness cases still mark the run incomplete). T-05-SC: no package added.

## Requirements

EVAL-04, EVAL-06 and EVAL-07 are delivered in part. `requirements.ready-ids` reports all three blocked, because sibling plans in the phase also declare them and have no SUMMARY yet, so nothing was marked complete.

## Next Phase Readiness

Plans 05-07 onward can read `silent_errors`, `failure_tags`, `incomplete` and `render_hazards` from any graded summary. Plan 05-13 can cite the hazard counts beside the dev result (assumption A1 stays open until the live dev run). The README task of the phase's last plan has new figures to describe.

## Self-Check: PASSED

- Files exist: test_silent_errors.py, grader.py, slices.py, summary.py, test_grader.py (checked).
- Commits `b5bec37` and `037d280` are ancestors of HEAD; `git rev-list --count` from the ledger base gives 2.
