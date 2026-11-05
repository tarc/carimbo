---
phase: 05-comparable-measurement
plan: 03
subsystem: evals
tags: [grader-003, row-matching, linear-sum-assignment, correct-invoice-rate, tolerance]

requires:
  - phase: 05-comparable-measurement
    provides: D-42 to D-47 decision records (05-02) and the pinned scipy 1.18.1 (05-01)
provides:
  - normalize.py with normalize_id, normalize_name and parse_decimal
  - matching.py with optimal row assignment (D-42 integer score), column rules and the per-row diff
  - grade_case writes correct, a wrong list, items and installments row statistics per case
  - exact numeric grading by default (DEFAULT_TOLERANCE 0.00), grader-003, summary version 4
  - per-case dataset metadata (split, base_case, rendering, severity, barcode_readable, case_tags)
  - test_matching.py proofs over all 154 cases
affects: [05-04, 05-05, 05-06, 05-07, 05-08, 05-09, 05-10, 05-11, 05-12, 05-13, phase-06]

actuals:
  tokens: 19500
  tasks: 3
  commits: 3
plan_head_before: 3bd939a8b0ce3252da5b870e888751715b79c616
plan_head_after: d3b7f523c5d23cf8c40c1daf5c33306064dea0f4

tech-stack:
  added: []
  patterns:
    - "numpy and scipy are imported lazily inside assign_rows; empty lists never import them"
    - "Row cost is (full weight - credit) * 10**6 + index distance, so equal-score ties are deterministic"
    - "A wrong value is one dict with path, scope, column, row, predicted_row, event, expected, predicted, within_cent"

key-files:
  created:
    - python/src/carimbo_evals/normalize.py
    - python/src/carimbo_evals/matching.py
    - python/tests/test_matching.py
  modified:
    - python/src/carimbo_evals/grader.py
    - python/src/carimbo_evals/summary.py
    - python/tests/test_grader.py
    - python/tests/test_vectors.py
    - python/tests/test_e2e_fake.py

key-decisions:
  - "Matching ignores --tolerance (row_score compares at tolerance 0), so a regrade never moves a pair"
  - "assign_rows returns without importing numpy or scipy when either side has no rows, not only when both are empty"
  - "A matched pair's wrong columns, an unmatched true row (missing, on items[j].total or installments[j].amount) and an unmatched predicted row (extra, on items[+i].total) are the only wrong-value shapes"
  - "tolerance_text renders at least two decimals and keeps finer ones; summary tolerance is now 0.00 for a default grading"

patterns-established:
  - "Predicted indices in a wrong list and an Assignment refer to the cleaned list (non-dict elements dropped)"

requirements-completed: [EVAL-03, EVAL-04]

duration: 15min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 03: a stored run graded on the whole invoice Summary

**`carimbo-evals grade` now scores the 27 header fields, all 14 item columns and every installment, matches rows by `linear_sum_assignment` over the D-42 integer score, compares numbers as exact Decimals by default, and writes `correct` and a `wrong` list per case (grader-003, summary version 4).**

## Performance

- **Duration:** 15 min
- **Started:** 2026-11-05T20:51:00Z
- **Completed:** 2026-11-05T21:06:00Z
- **Tasks:** 3 (one tracer, two auto with tdd)
- **Files modified:** 8 (3 created)

## Accomplishments

- Tracer: a stored run of real dataset cases goes through `grade_run` to `summary.json`; the untouched nfe-0001 is `correct` True, a case with a middle row removed is `correct` False with `items[k].total` `missing` and `item_count` wrong.
- `matching.py`: `assign_rows` (cost `(14 - credit) * 10**6 + abs(i - j)`, minimum 6 for items, 2 for installments), `column_right` (name, id, Decimal and exact-date rules), `diff_rows` with `missing` and `extra` events, row statistics and `order_preserved`.
- Exact numbers: `DEFAULT_TOLERANCE` is `Decimal("0.00")`; a value off by R$0.01 is wrong and flagged `within_cent`; the summary counts those values and the cases made only of them (`within_cent: {values, cases}`), so both views come from one grading (invariant test).
- Status semantics: `correct` False for refused, truncated, schema_invalid and validation_failed (which stays `caught`), None for infrastructure_failure and harness_error.
- Per-case metadata from `manifest.json` through `load_case_metadata`; a skeleton case gets `split` None and `base_case` equal to its id.
- `test_matching.py` proves, over all 154 cases: self match, reversal and seeded shuffle with no wrong value (141 cases with `order_preserved` False under reversal), one deleted row (141), one duplicated row (154), 36 confusable partners at exactly 7 and every other pair at most 5, installments, empty and single-row inputs, determinism.

## Task Commits

1. **Task 1 (tracer): whole-invoice grading with optimal row assignment** - `4b8ab2c` (feat)
2. **Task 2: exact numbers by default, status semantics, dataset metadata, pins moved** - `f2c4b3f` (feat)
3. **Task 3: the assignment proven over all 154 cases** - `d3b7f52` (test)

## Verification

- Tracer feedback gate: `<verify>` is automated only; re-run end to end after the commit, passed; expanded.
- `just py-check`: exit 0, 2,850 passed (baseline 2,810). `pytest python/tests/test_e2e_fake.py -m e2e`: 7 passed. `pyright -p python`: 0 errors.
- `data/vectors/validator-vectors.json` untouched (`git diff --stat origin/main` prints nothing for it).
- Acceptance: `linear_sum_assignment` is imported inside `assign_rows`; `summary.py` imports nothing from `grader`; `normalize_id` and `normalize_name` still import from `carimbo_evals.grader`; `build_summary(grades, {...}, None)` still works; `field_accuracy` entries are still `{correct, n}` over the 27 header booleans.

## Deviations from Plan

### Plan claim contradicted by the data

**1. [Plan truth, not a code defect] A copied confusable partner leaves several wrong values on the row, not one**
- **Found during:** Task 3
- **Issue:** The truth "when a model copies one partner into the other's slot the rows stay paired, with one wrong value on the row" does not match `dataset-001`. The two partners differ in `code`, one of `quantity` or `unit_price`, `total` and the derived ICMS columns (for example nfe-0009 differs in five columns), so copying a partner puts several columns wrong on that row.
- **Fix:** No code change. The proof asserts what holds and matters: the rows stay paired (no `missing` or `extra` event) and the wrong columns are exactly the columns where the partners differ, including `code`. The "one wrong value" case is the transposition proof: replacing only the differing `quantity` or `unit_price` with the partner's digit-transposed value leaves exactly one wrong value on the row.
- **Files modified:** python/tests/test_matching.py
- **Commit:** d3b7f52

### Auto-fixed Issues

**2. [Rule 3 - Blocking] Task 1 landed the grader change with the old default tolerance**
- **Found during:** Task 1 verification
- **Issue:** Setting `DEFAULT_TOLERANCE` to 0.00 in Task 1 broke `test_summary_carries_config_totals_latency_and_dataset`, whose pin the plan moves in Task 2.
- **Fix:** Kept 0.01 for the Task 1 commit so each commit is green, switched it in Task 2 together with the pins.
- **Commit:** 4b8ab2c, f2c4b3f

**Total deviations:** 1 plan-claim correction, 1 sequencing fix. **Impact:** none on the delivered behavior; no proof was relaxed, and no matching.py fix was needed after Task 3.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-05-04 is mitigated as planned: `clean_rows` drops non-dict rows and treats a non-list as no rows, `column_right` treats a non-string as wrong and `parse_decimal` returns None on bad input; tests cover `None`, strings, dicts and mixed lists.

## Requirements

EVAL-03 (row matching: adjacency, empty, ordering) is delivered by this plan's matcher and its proofs, and is marked complete. EVAL-04 (tolerance boundary and precision) has its core edges here (inclusive boundary, Decimal precision), but the rest of its self-tests belong to plan 05-04, which also declares it, so the shared-ID gate keeps it Pending until that plan finishes.

## Next Phase Readiness

Every later plan can read `correct`, the `wrong` list, the `items` and `installments` statistics and the case metadata from `summary.json`. Ready for 05-04.

## Self-Check: PASSED

- Files exist: normalize.py, matching.py, test_matching.py (checked).
- Commits `4b8ab2c`, `f2c4b3f`, `d3b7f52` are ancestors of HEAD.
