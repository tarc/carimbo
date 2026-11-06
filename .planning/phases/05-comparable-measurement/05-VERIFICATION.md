---
phase: 05-comparable-measurement
verified: 2026-11-06T04:30:00Z
status: passed
score: 4/4 must-haves verified
covered_files:
  - .planning/phases/05-comparable-measurement/05-01-PLAN.md
  - .planning/phases/05-comparable-measurement/05-01-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-02-PLAN.md
  - .planning/phases/05-comparable-measurement/05-02-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-03-PLAN.md
  - .planning/phases/05-comparable-measurement/05-03-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-04-PLAN.md
  - .planning/phases/05-comparable-measurement/05-04-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-05-PLAN.md
  - .planning/phases/05-comparable-measurement/05-05-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-06-PLAN.md
  - .planning/phases/05-comparable-measurement/05-06-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-07-PLAN.md
  - .planning/phases/05-comparable-measurement/05-07-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-08-PLAN.md
  - .planning/phases/05-comparable-measurement/05-08-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-09-PLAN.md
  - .planning/phases/05-comparable-measurement/05-09-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-10-PLAN.md
  - .planning/phases/05-comparable-measurement/05-10-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-11-PLAN.md
  - .planning/phases/05-comparable-measurement/05-11-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-12-PLAN.md
  - .planning/phases/05-comparable-measurement/05-12-SUMMARY.md
  - .planning/phases/05-comparable-measurement/05-13-PLAN.md
  - .planning/phases/05-comparable-measurement/05-13-SUMMARY.md
  - python/src/carimbo_evals/cli.py
  - python/src/carimbo_evals/compare.py
  - python/src/carimbo_evals/corruptions.py
  - python/src/carimbo_evals/grader.py
  - python/src/carimbo_evals/matching.py
  - python/src/carimbo_evals/runner.py
  - python/src/carimbo_evals/slices.py
  - python/src/carimbo_evals/stats.py
  - python/src/carimbo_evals/summary.py
  - python/src/carimbo_evals/tags.py
covered_digest: "v4:sha256:23faca463464a5e12b951e0b16cf677af042e8a397dba33d2555f1520609f17e"
behavior_unverified: 0
overrides_applied: 0
---

# Phase 5: Comparable Measurement Verification Report

**Phase Goal:** As a developer, I want to grade full-dataset runs field by field with confidence intervals, a silent-error rate and tagged failures, and compare any two runs case by case, so that I can tell real quality changes from noise.
**Verified:** 2026-11-06
**Status:** passed
**Re-verification:** No, initial verification (code verified at HEAD bcbaf6d, after the review fix pass)

MVP-mode note: the goal is a user story; the outcome clause ("tell real quality changes from noise") is checked by the McNemar verdict, the intervals, the incomplete flag and the compare refusals below.

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Graders score schema validity, exact match (IDs, CNPJ, access key) and numeric tolerance (configurable); line items matched by optimal assignment; self-tests show ground truth perfect and corruptions wrong | VERIFIED | `grader.py` `_schema_grades` runs jsonschema and strict Pydantic; `_same_id`/`normalize_id` for access_key, CNPJ and tax id; `total_within_tolerance` with `--tolerance` on `grade`; `matching.assign_rows` uses `scipy.optimize.linear_sum_assignment`. My own run: on nfe-0001, ground truth gave 0 wrong values; a reversed/shuffled item list gave 0 wrong values (`order_preserved` False); invoice_total +0.01 is wrong at tolerance 0 and right at 0.01; a swapped access-key digit gave `digit_transposition`; swapped issuer/recipient gave `party_swap`; a deleted row gave `dropped_row`. `test_selftests.py` runs all 154 cases (ground truth perfect with no tag, one seeded corruption per tag scoring wrong with exactly its tag). |
| 2 | Full-dataset run summary reports per-field and per-slice accuracy with n and CIs, plus cost, cache hit rate and latency percentiles; infrastructure failures counted apart from wrong answers | VERIFIED | `summary.py`: `field_stats`, `field_by_slice` (Wilson on header fields, seeded bootstrap on item/installment columns, each with `n`, `invoices`, `ci95`), `headline`, `slices`, `totals` (notional and incurred cost), `cache.hit_rate`, `latency_ms` and `model_latency_ms` (nearest-rank p50/p95). `correct` is null for `infrastructure_failure` and `harness_error`, which are left out of the denominator and counted under `excluded` and `incomplete_detail`. Wilson values re-derived independently: 12/30 gives [0.2459, 0.5768], 4/16 gives [0.1018, 0.495], 0/3 gives [0.0, 0.5615], all matching the committed dev summaries. |
| 3 | Summary reports the silent-error rate; failures are auto-tagged by taxonomy and counted | VERIFIED | `silent_block`: success cases with at least one wrong value, split into `payment`/`other`, with Wilson CI; `validation_failed` is caught and never silent. `failure_tags` counts every tag in `TAG_ORDER` (zeros kept, sum equals `wrong_values`, `untagged` reported). The published dev run shows 4 silent of 16 success cases (0.25, [0.1018, 0.495]) and 827 tagged-or-untagged wrong values. The tag list is frozen as `failure-tags-1` (D-43, D-48). |
| 4 | `compare` lists per-field deltas and regressed cases between two runs and refuses runs whose dataset or grader versions differ | VERIFIED | `compare.py` produces `field_deltas`, `regressed`, `worsened`, `improved`, `status_changed`, `not_comparable` and an exact McNemar verdict (`mcnemar_exact`, p tested as an exact fraction). CLI refusals, run by me, all exit 2 with no override: grader version mismatch, dataset manifest hash mismatch, a version 3 (old) summary, an unparseable file. Comparing the committed dev run with its replay exits 0 and prints "no change". |

**Score:** 4/4 truths verified (0 present, behavior-unverified)

### Plan-level must-haves

Plan 05-01 pins `scipy==1.18.1` exactly (`python/pyproject.toml` line 16) behind the blocking supply-chain checkpoint. The lock is covered by the `uv sync --locked` step in the passing gates. The other plans' truths are the same four contracts above split into parts (tracer, tags and self-tests, intervals and slices, silent errors, item metrics and bootstrap, compare, `run --split` and `grade --out`, offline full-dataset proof, live dev run, tag review, README). I found no plan truth that contradicts the code.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `python/src/carimbo_evals/grader.py` | Whole-invoice grading, `correct`/`wrong` per case | VERIFIED | 548 lines, wired into `cli.py grade` and the runner. |
| `python/src/carimbo_evals/matching.py` | Optimal assignment of rows | VERIFIED | `linear_sum_assignment` with a deterministic index-distance tie break. |
| `python/src/carimbo_evals/tags.py`, `corruptions.py` | Ordered first-match tag list and shared corruption catalogue | VERIFIED | Used by `grader.diff_invoice` and by the self-tests and the e2e proof. |
| `python/src/carimbo_evals/stats.py` | Wilson, seeded bootstrap, exact McNemar, nearest rank | VERIFIED | stdlib only; values cross-checked above. |
| `python/src/carimbo_evals/summary.py` | Version 4 summary with all blocks | VERIFIED | `SUMMARY_VERSION = 4`, `GRADER_VERSION = "grader-003"`. |
| `python/src/carimbo_evals/compare.py` | `compare` with refusals | VERIFIED | Validates shape before use (WR-02 fix); `COMPARE_VERSION = "compare-1"`. |
| `evals/results/dev-split-*` (2 runs) | Published live run and its replay, verbatim | VERIFIED | I regraded both into a scratch directory with the HEAD grader: `summary.json` is identical to the committed one apart from `meta.graded_at`. `grade` on the published directory itself refuses (guard works). |
| `docs/DECISIONS.md` D-42 to D-48 | Decision records | VERIFIED | Present; `just docs-check` prints `docs ok`. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `cli.py grade` | `grader.grade_run` | direct call with `--tolerance`, `--resamples`, `--out` | WIRED | Ran it twice on the published runs. |
| `grader.diff_invoice` | `matching.diff_rows`, `tags.tag_case` | called per case | WIRED | Self-test output above shows tags on wrong values. |
| `grader.grade_run` | `summary.build_summary` | `run_meta` with `stopped_reason` and `cases_not_run` | WIRED | Dev summaries show `incomplete: true` with `stopped_reason: cost_cap` and `cases_not_run: 3`. |
| `cli.py compare` | `compare.load_summary`/`check_comparable`/`compare_summaries` | CLI | WIRED | Exit 2 on refusals, 0 on success. |
| `runner.run_cases` | `run.json` `stopped_reason: in_progress` marker | written before first dispatch (CR-01 fix) | WIRED | Covered by `test_runner.py` tests named in 05-REVIEW-FIX.md; they passed in my full run. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `summary.json` rates and intervals | `correct`, `wrong` per case | `cases.jsonl` records graded against the XML ground truth | Yes, regraded summaries equal the committed ones | FLOWING |
| `compare.json` deltas | per-case `fields`, `items.columns` from both summaries | `summary.cases` | Yes, e2e asserts the regressed list equals the seeded corruptions | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Whole offline test suite | `uv run --project python pytest python/tests -q` | 3240 passed, 16 deselected | PASS |
| 154-case end-to-end proof over HTTP (scripted model) | `pytest python/tests/test_e2e_full_dataset.py -m e2e` | 3 passed | PASS |
| Docs gate | `just docs-check` | `docs ok` | PASS |
| README drift gate | `pytest python/tests/test_readme_drift.py` | 27 passed | PASS |
| Regrade is stable | `carimbo-evals grade --out` on both published runs | identical to committed except `graded_at` | PASS |
| Compare refusals | grader, manifest, old-version, malformed summaries | 4 refusals, exit 2 each | PASS |
| Tree untouched by verification | `git status --short` before writing this report | clean | PASS |

### Probe Execution

Step 7c: SKIPPED. No probe scripts are declared by the phase plans.

### Requirements Coverage

Every requirement ID in the plan frontmatters appears in REQUIREMENTS.md (marked `[x]` and `Complete` in the traceability table). Plan 05-02 and 05-13 declare all six; no ID mapped to Phase 5 is unclaimed.

| Requirement | Source Plans | Description | Status | Evidence |
|-------------|--------------|-------------|--------|----------|
| EVAL-03 | 05-01, 05-02, 05-03, 05-13 | Schema validity, exact match, numeric tolerance, optimal row assignment | SATISFIED | Truth 1. |
| EVAL-04 | 05-02, 05-03, 05-04, 05-06, 05-10, 05-13 | Grader self-tests; infrastructure failures distinct from wrong answers | SATISFIED | `test_selftests.py` over 154 cases; `correct` null and `excluded`/`incomplete` for infra and harness cases; e2e seeded statuses. |
| EVAL-05 | 05-02, 05-05, 05-07, 05-09, 05-10, 05-11, 05-13 | Per-field and per-slice accuracy with n and CIs, cost, cache hit rate, latency percentiles | SATISFIED | Truth 2. |
| EVAL-06 | 05-02, 05-06, 05-10, 05-11, 05-13 | Silent-error rate | SATISFIED | Truth 3, `silent_block`. |
| EVAL-07 | 05-02, 05-04, 05-06, 05-10, 05-11, 05-12, 05-13 | Failure taxonomy auto-tagging | SATISFIED | Truth 3, `tags.py`, `failure_tags`. |
| EVAL-08 | 05-02, 05-08, 05-10, 05-13 | `compare` deltas, regressed cases, refuses mismatched dataset or grader | SATISFIED | Truth 4. |

No orphaned requirements.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | - | `TBD`, `FIXME`, `XXX` in `carimbo_evals`, the phase tests, `DECISIONS.md`, `justfile` | - | No debt markers. |

Open review findings IN-01 to IN-08 (all info, recorded in `05-REVIEW-DISPOSITION.md`, excluded from the fix pass by the human) do not block the goal:

- IN-02: `primary_metric.headline_row` names `test.text`, which a dev-only summary does not contain.
- IN-04: replay summaries fold cache misses into infrastructure failures (visible in the published replay, 3 cases).
- IN-06: `compare` does not surface that either run is incomplete (the published pair is in fact incomplete or has infrastructure failures, and `compare` is silent about it).
- IN-07: 265 of 827 wrong values in the dev run are `untagged`; the tag list is frozen under D-48, so this is a measured limitation, not a defect.
- Known residual from CR-01: a run directory whose `run.json` was deleted by hand still grades as complete.

### Human Verification Required

None. Everything the goal asserts is exercised by tests or by commands I ran. Behavior-dependent items (incomplete detection after a kill or a narrowed resume, compare classification of refused/truncated cases) have named passing tests from the review fix pass.

### Gaps Summary

No gaps. The phase delivers the grader, the version 4 summary with intervals, silent-error rate and frozen tag counts, and a `compare` that refuses mismatched runs, each checked in code and by execution at HEAD.

Honest limitations, none of them goal failures:
- The only live evidence is one dev-split run (51 of 54 cases, stopped by the US$2.50 cap) and its replay. About 40% of text-layer cases are correct, and the `test` split has not been run (by design, until Phase 6). The phase is about measurement, and the README states these limits.
- The full 154-case proof is offline with a scripted model, not a live run.
- README Status for Phase 5 still reads "in progress" and ROADMAP shows `[ ]`. Per AGENTS.md these are updated after verification, on the phase branch, before `/gsd-ship`.

---

_Verified: 2026-11-06_
_Verifier: Claude (gsd-verifier)_
