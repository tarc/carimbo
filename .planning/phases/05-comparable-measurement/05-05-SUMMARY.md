---
phase: 05-comparable-measurement
plan: 05
subsystem: evals
tags: [correct-invoice-rate, wilson, slices-1, mcnemar, bootstrap, summary-v4]

requires:
  - phase: 05-comparable-measurement
    provides: per-case correct flag, split, base_case, rendering, case_tags (05-03) and the D-42 to D-47 records (05-02)
provides:
  - stats.py with wilson, rate, mcnemar_exact, is_significant, nearest_rank, bootstrap_sums, bootstrap_intervals (stdlib only)
  - slices.py with SLICES_VERSION slices-1 and the 24 declared slices over 11 dimensions
  - summary keys overall, headline, slices, field_stats, field_by_slice, primary_metric, stats, slices_version
  - summary.md sections "Correct invoices" and "Rendering"
affects: [05-06, 05-07, 05-08, 05-09, 05-10, 05-11, 05-12, 05-13, phase-06]

actuals:
  tokens: 21000
  tasks: 3
  commits: 3
plan_head_before: 537c709cff4669f0f2407b47ae13a593461f6f1d
plan_head_after: 173c58252fbeb66bc06c9617246657e79cc6367e

tech-stack:
  added: []
  patterns:
    - "One rounding helper (quantize4): 4 places ROUND_HALF_UP in a local Decimal context; rates and bounds all go through it"
    - "Wilson n is the distinct base_case values of the row, so a base and its twins are one draw"
    - "Bootstrap uses random.Random.random only; ratios sorted by an int/int float key, ranks in integer arithmetic"
    - "scipy is a test-time cross-check only; stats.py imports no third-party module"

key-files:
  created:
    - python/src/carimbo_evals/stats.py
    - python/src/carimbo_evals/slices.py
    - python/tests/test_stats.py
    - python/tests/test_summary_v4.py
  modified:
    - python/src/carimbo_evals/summary.py

key-decisions:
  - "A case enters a row's denominator when correct is not None; excluded counts only infrastructure_failure and harness_error, so refused, truncated and schema_invalid cases stay in and a model cannot raise its rate by truncating (D-02)"
  - "bootstrap_intervals reuses rate() for the bounds: Decimal division quantized half up gives the same value as a Fraction, since a non-terminating quotient cannot sit on a 4-place tie"
  - "item.* and installment.* entries of field_by_slice are {correct, n, rate} here; plan 05-07 adds invoices and the bootstrap ci95"

patterns-established:
  - "full scope appears only when run_meta.manifest_case_ids are all present and every case has a split"
  - "render_markdown labels every dev figure (dev) and the primary row (primary); a run with no headline prints the overall row only"

requirements-completed: []

duration: 6min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 05: the correct-invoice rate on headline rows, with intervals and slices Summary

**A graded run now states its correct-invoice rate on text and image rows per split with cases, invoices, excluded and a Wilson 95 percent interval (n = distinct base invoices), over 24 declared slices whose counts equal the manifest, plus the exact McNemar, nearest-rank and seeded-bootstrap statistics pinned for plans 05-07 and 05-08.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-11-05T21:24:47Z
- **Tasks:** 3 (one tracer, two auto with tdd)
- **Files:** 5 (4 created, 1 modified)

## Accomplishments

- Tracer: three real test-split text cases (two right, one with a wrong invoice total) go through `grade_run` and `write_summary`; `headline.test.text` reads cases 3, invoices 3, correct 2, rate 0.6667, ci95 [0.2077, 0.9385]. A skeleton run has `headline == {}` and still has `overall`.
- Headline rows text, image and pooled per split, with the six pinned counts (test 60/60 and 40/5, dev 30/30 and 24/3, full 90/90 and 64/8) asserted against `data/dataset/manifest.json`.
- `slices.py`: 24 slices over 11 dimensions at `slices-1`; the 24-row table of cases/invoices for test, dev and full is a literal in `test_summary_v4.py`.
- `field_stats` (27 header fields per scope with n, invoices, rate, ci95) while `field_accuracy` entries stay exactly `{correct, n}`; `field_by_slice` with header fields as Wilson rates over distinct invoices and row columns as pooled counts and a rate.
- summary.md: `## Correct invoices` (dev rows labelled `(dev)`, test text `(primary)`), `## Rendering` (the degradation curve as a table), and a `## Cases` heading ahead of the case table.
- stats.py: `mcnemar_exact`, `is_significant`, `nearest_rank`, `bootstrap_sums`, `bootstrap_intervals`; McNemar equals `scipy.stats.binomtest` for every b, c below 40 within 1e-12, Wilson equals `proportion_ci(method="wilson")` for k, n up to 60 within 5e-5, and both bootstrap pins hold.

## Task Commits

1. **Task 1 (tracer): correct-invoice rate with a Wilson interval** - `3b55f28` (feat)
2. **Task 2: slices, header field stats, field-by-slice and the markdown tables** - `ecd3559` (feat)
3. **Task 3: McNemar, nearest rank and the seeded bootstrap** - `173c582` (feat)

## Verification

- Tracer feedback gate: `<verify>` is automated only; it ran and passed before expansion.
- `just py-check` exit 0, 3,125 passed (2,850 baseline plus the 05-04 tests and this plan's). `ruff check`, `ruff format --check` and `pyright -p python` clean.
- Acceptance: `stats.py` imports only stdlib; `summary.py` imports nothing from `grader`; `len(SLICES) == 24`; `dev text (dev)` appears in the markdown; the literal `[[3, 1], [2, 2], [3, 1], [1, 3]]` is in `test_stats.py`.

## Deviations from Plan

None - plan executed exactly as written. One expectation of mine, not of the plan, was corrected while writing a test: the test split's 100 cases cover 60 distinct invoices (not 65).

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-05-09 is mitigated (every row prints cases, invoices and excluded; Wilson n is distinct base_case; counts pinned against the manifest). T-05-10 is mitigated (Decimal in a local context, one rounding helper, `random.Random.random` as the only stream).

## Requirements

EVAL-05 is delivered in part: rows, intervals, slices and the pinned statistics. `requirements.ready-ids` reports it blocked, because sibling plans 05-07, 05-09, 05-10, 05-11 and 05-13 also declare it and have no SUMMARY, so nothing was marked complete.

## Next Phase Readiness

Plan 05-07 can add the bootstrap interval to `item.*` and `installment.*` entries of `field_by_slice` and the `bootstrap` and `field_by_slice` keys of `stats`, using `bootstrap_intervals` with `BOOTSTRAP_SEED` and `DEFAULT_RESAMPLES`. Plan 05-08 can use `mcnemar_exact`, `is_significant` and `nearest_rank`.

## Self-Check: PASSED

- Files exist: stats.py, slices.py, test_stats.py, test_summary_v4.py (checked).
- Commits `3b55f28`, `ecd3559`, `173c582` are ancestors of HEAD; `git rev-list --count` from the ledger base gives 3.
