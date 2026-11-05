---
phase: 05-comparable-measurement
plan: 07
subsystem: evals
tags: [line-items, bootstrap, field-by-slice, latency, nearest-rank, byte-stable, summary-v4]

requires:
  - phase: 05-comparable-measurement
    provides: stats.py bootstrap_intervals, nearest_rank, rate and the field_by_slice shape (05-05), per-case items and installments blocks (05-03), summary blocks of 05-06
provides:
  - summary keys items and installments (precision, recall, F1, all-values-correct, per-column accuracy, order_preserved), one block per scope
  - invoices and a seeded bootstrap ci95 on every item.* and installment.* entry of field_by_slice
  - stats.bootstrap and stats.field_by_slice; grade_run(resamples=)
  - _latency as min, p50, p95, max, n by nearest rank
  - summary.md model latency bullet and "## Items" section
affects: [05-08, 05-09, 05-10, 05-11, 05-12, 05-13, phase-06]

actuals:
  tokens: 7500
  tasks: 3
  commits: 3
plan_head_before: 0393973e64f784db8b1c7d58da7a195382d9a241
plan_head_after: 8b4587ebf5f5fea9979bc9ba0c0fcaa839672a01

tech-stack:
  added: []
  patterns:
    - "One resampling unit per base_case: a vector per invoice sums its cases, so a base and its twins move together"
    - "Every slice's bootstrap starts from BOOTSTRAP_SEED, so a slice's interval depends on no other slice and not on slice order"
    - "Row columns of field_by_slice are always present (17 per slice), with n 0 and null rate and ci95 when nothing was graded"

key-files:
  created: []
  modified:
    - python/src/carimbo_evals/summary.py
    - python/src/carimbo_evals/grader.py
    - python/tests/test_summary_v4.py
    - python/tests/test_grader.py

key-decisions:
  - "Item and installment column entries of field_by_slice are emitted for all 17 columns even when no case has a row block, because the plan requires n 0, invoices 0, rate None and ci95 None on every row column of a refused-only run"
  - "The invoices figure on a column entry is the slice's invoice count among cases with a row block, not the count of invoices where that column had n above 0"
  - "latency_ms and model_latency_ms share the new shape; the plan's three median pins moved, wall-clock replay time stays in latency_ms only"

patterns-established:
  - "row_block(cases, key, seed, resamples) and column_intervals(cases, seed, resamples) are the two entry points of row-level statistics"

requirements-completed: []

coverage:
  - id: D1
    description: "items and installments blocks per scope with precision, recall, F1, per-column accuracy and all-values-correct rate, each with a seeded bootstrap interval over invoices, plus a non-gating order_preserved count"
    requirement: EVAL-05
    verification:
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_tracer_a_stored_run_with_a_dropped_row_reports_item_precision_and_recall"
        status: pass
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_the_pinned_interval_of_a_three_case_run_at_10000_resamples"
        status: pass
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_a_base_and_its_twin_are_one_resampling_unit"
        status: pass
    human_judgment: false
  - id: D2
    description: "every item and installment column of field_by_slice carries invoices and a bootstrap ci95, null only at n 0; stats.bootstrap and stats.field_by_slice record the method"
    requirement: EVAL-05
    verification:
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_every_field_by_slice_entry_has_an_interval"
        status: pass
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_a_slice_interval_depends_on_no_other_slice"
        status: pass
    human_judgment: false
  - id: D3
    description: "nearest-rank latency blocks and a byte-stable regrade, also with shuffled cases.jsonl lines"
    requirement: EVAL-05
    verification:
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_a_regrade_is_byte_identical_apart_from_graded_at"
        status: pass
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_shuffled_lines_of_cases_jsonl_give_a_byte_identical_summary"
        status: pass
      - kind: unit
        ref: "python/tests/test_summary_v4.py#test_a_replay_reports_the_original_model_latency_not_its_own_wall_time"
        status: pass
    human_judgment: false

duration: 7min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 07: line-item metrics with bootstrap intervals, latency percentiles and byte-stable regrades Summary

**A graded run now reports how well rows were found (precision, recall, F1, all-values-correct) and how accurate each item and installment column is, with seeded bootstrap intervals that resample invoices rather than rows, an interval on every per-slice column figure, nearest-rank latency percentiles, and a regrade that reproduces the summary byte for byte.**

## Performance

- **Duration:** 7 min (21:50:35Z to 21:57Z)
- **Tasks:** 3 (one tracer, two auto, all tdd)
- **Files:** 4 modified, none created

## Accomplishments

- Tracer: two real dataset cases, one with its last item dropped, give `items.overall` with precision 1.0, recall (rows - 1) / rows, intervals of two floats, and `stats.bootstrap == {seed 20261105, resamples 200, unit base_case, method percentile, rng random.Random.random}`. A base and its twin with the same dropped row give `ci95[0] == ci95[1]`; a refused-only run gives null values and null intervals, never 0.0.
- One pinned test at 10,000 resamples (three cases, precision 0.9091 [0.8333, 1.0], recall 0.8333 [0.75, 1.0], `total` column [0.75, 1.0]) fails on any change of stream, rank rule or rounding.
- `column_intervals`: one `bootstrap_intervals` call per slice covers its 17 columns. All 72 slices (24 slices x test, dev, full) of the 154-case metadata run have `invoices` and a `ci95` on every column with n above 0, in `test_every_field_by_slice_entry_has_an_interval`. A slice's intervals are identical whatever the order of cases.
- Latency: `{min, p50, p95, max, n}` by nearest rank; a replayed run reports the original `model_latency_ms` percentiles while its own wall time stays in `latency_ms`. Ties and a single value are defined.
- Byte-stable: regrade twice, and regrade with the lines of `cases.jsonl` reversed, differ only in `meta.graded_at`, in summary.json and summary.md.
- summary.md: a `- model latency: p50 X ms, p95 Y ms (original latency, also on a replay)` bullet and a `## Items` table per scope.

## Task Commits

1. **Task 1 (tracer): item and installment metrics with a seeded bootstrap over invoices** - `3d2bc4e` (feat)
2. **Task 2: a seeded bootstrap interval on every item and installment column of field_by_slice** - `12f9fd3` (feat)
3. **Task 3: nearest-rank latency, the items section and a byte-stable regrade** - `8b4587e` (feat)

## Verification

- Tracer feedback gate: the tracer's `<verify>` is automated only; it ran and passed (20 tests) before expansion.
- `devenv shell -- just py-check` exit 0: 3,187 passed (3,168 baseline plus 19 net new), `ruff check`, `ruff format --check` and `pyright -p python` clean. The one warning (dataset size above the 18 MB line) pre-dates this plan. `test_decision_grader.py` passes unchanged.
- Acceptance: `model_latency_ms` has keys `min, p50, p95, max, n` and no `median`; `field_accuracy` entries still hold exactly `correct` and `n`; `test_every_field_by_slice_entry_has_an_interval` exists and passes; `test_a_regrade_is_byte_identical_apart_from_graded_at` asserts equal summary.json texts after removing `graded_at`.
- **Measured `grade_run` time (Assumption A2):** a 154-case tmp run built from the ground-truth invoices (item and installment blocks on every case) takes 10.2 s end to end at the default 10,000 resamples, with 72 slice draws plus the scope blocks, on this machine (i7-12700H, pure Python). At `resamples=500` the same regrade takes 1.6 s. The prototype figure was 10.9 s, so A2 holds and the 10,000 default stays; `--resamples` (plan 05-09) is the knob.
- The test suite did not grow noticeably: tests use 200 resamples except the one pinned test at 10,000, so no `resamples` pass-through in test_grader.py was needed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `_column_stats` emitted only the columns some case had a block for**
- **Found during:** Task 2
- **Issue:** the plan requires `n` 0, `invoices` 0, `rate` None and `ci95` None on every row column of every slice for a refused-only run, and a walk over "every entry" in Test 4. With 05-05's `_column_stats`, a run without row blocks had no `item.*` or `installment.*` entries at all, so those requirements could not hold and a reader could not tell "no rows graded" from "column missing".
- **Fix:** `_column_stats` now iterates the declared `ITEM_COLUMNS` and `INSTALLMENT_COLUMNS` and always emits the 17 entries (zeros when absent). Entries for runs that do have blocks are unchanged apart from the new `invoices` and `ci95`.
- **Files modified:** python/src/carimbo_evals/summary.py
- **Verification:** `test_a_run_of_only_refused_cases_has_null_slice_intervals_and_zero_counts`, `test_every_field_by_slice_entry_has_an_interval`
- **Commit:** `12f9fd3`

**Total deviations:** 1 auto-fixed (1 bug). **Impact:** none beyond the entries now being total; the change is within this plan's own contract for `field_by_slice`.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-05-13 is mitigated (resampling unit is `base_case` per scope and per slice, recorded in `stats.bootstrap`; tests that a base and its twin move together; `invoices` on every slice entry; `stats.field_by_slice` and D-44 state the interval runs narrow on few invoices). T-05-14 is mitigated (byte-stable regrade tests, fixed seed, seed and resamples recorded). T-05-SC: no package added.

## Requirements

EVAL-05 is delivered in part and not marked complete: `requirements.ready-ids` reports it blocked, because sibling plans 05-09, 05-10, 05-11 and 05-13 also declare it and have no SUMMARY yet.

## Next Phase Readiness

Plan 05-08 (compare) and the later plans can read `items`, `installments`, `stats.bootstrap` and the nearest-rank latency blocks from any graded summary; plan 05-09 adds the `grade --resamples` option that feeds `grade_run(resamples=)`. The README task of the phase's last plan has the A2 figure (10.2 s at 10,000 resamples) to cite. Few-invoice slices (3, 5 or 8 invoices on rendering slices) have narrow bootstrap intervals; read `invoices` beside every interval.

## Self-Check: PASSED

- Files exist: summary.py, grader.py, test_summary_v4.py, test_grader.py (checked).
- Commits `3d2bc4e`, `12f9fd3`, `8b4587e` are ancestors of HEAD; `git rev-list --count` from the ledger base gives 3.
