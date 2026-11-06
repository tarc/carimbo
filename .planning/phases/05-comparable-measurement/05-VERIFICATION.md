---
phase: 05-comparable-measurement
verified: 2026-11-06T05:20:00Z
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
  - python/src/carimbo_evals/ground_truth.py
  - python/src/carimbo_evals/matching.py
  - python/src/carimbo_evals/runner.py
  - python/src/carimbo_evals/slices.py
  - python/src/carimbo_evals/stats.py
  - python/src/carimbo_evals/summary.py
  - python/src/carimbo_evals/tags.py
covered_digest: "v4:sha256:7887651e14ff41f958939d44457690c9edc8f1ccd89d0909aa28d985562d5790"
behavior_unverified: 0
overrides_applied: 0
re_verification:
  previous_status: passed
  previous_score: 4/4
  gaps_closed: []
  gaps_remaining: []
  regressions: []
---

# Phase 5: Comparable Measurement Verification Report

**Phase Goal:** As a developer, I want to grade full-dataset runs field by field with confidence intervals, a silent-error rate and tagged failures, and compare any two runs case by case, so that I can tell real quality changes from noise.
**Verified:** 2026-11-06T05:20:00Z
**Status:** passed
**Re-verification:** Yes. The earlier report (code at bcbaf6d) was `passed`, and `verification.status` read `stale` because five covered source files changed afterwards (`cli.py`, `compare.py`, `grader.py`, `runner.py`, `summary.py`; `ground_truth.py` also changed and is now covered). This report checks the code at HEAD e5d8c82 with a clean working tree. Everything relied on below was rerun here.

MVP-mode note: the goal is a user story. The outcome clause ("tell real quality changes from noise") is checked through the McNemar verdict, the intervals, the incomplete flag and the compare refusals.

## What changed since the previous report

The second code-review fix round (`05-REVIEW-FIX.md`, round 2, commits 85c13ef to d4e0f07, plus D-49 in 27fe1cb) touched the covered code. I read the full diff of `python/src/carimbo_evals/` between bcbaf6d and HEAD:

- `grader.py`: `load_run_state` now returns `(state, problem)`; a missing, unreadable, non-object or `case_ids`-less `run.json` marks the run incomplete (`incomplete_detail.run_state` = `missing` or `damaged`). `_read_records` rejects non-object lines, non-string `case_id` and ids outside `[A-Za-z0-9._-]`. `_cache_misses` counts `cache_miss` infrastructure failures. `run_id` falls back to the directory name.
- `summary.py`: `primary_metric.headline_row` and `second_row` are `null` when there is no `test` scope; `incomplete_detail.cache_miss` and `run_state` appear only when they fire; the `unpriced` label reads `unpriced (notional)`.
- `cli.py`: `run --allow-test-split`; without it `run` exits 2 when any selected case has manifest split `test`.
- `compare.py`: `first` and `second` carry `incomplete` and `stopped_reason`; `compare.md` shows each run's state.
- `ground_truth.py`: `ET.ParseError` becomes `ValueError`. `runner.py`: the two all-zero re-draw loops are gone from `new_traceparent`.

No change touches `tags.py`, `stats.py`, `matching.py`, `slices.py`, the tag order, `TAGS_VERSION`, `GRADER_VERSION` (`grader-003`) or the summary version (4).

## Regrading the published dev runs at HEAD no longer reproduces their summary.json byte for byte

This must be stated plainly, because the previous report said "identical apart from `meta.graded_at`" and that was true at bcbaf6d and is not true now. I regraded both published dev runs with `carimbo-evals grade --run evals/results/<RUN> --cases data/dataset --out <scratch dir>` and deep-compared every key and list element against the committed `summary.json` (a recursive walk, not a text diff). The raw bytes differ for both runs. The differing paths are exactly:

| Run | Differing paths |
|-----|-----------------|
| `dev-split-20261106T001800Z` (live) | `meta.graded_at`, `primary_metric.headline_row` (`test.text` becomes `null`), `primary_metric.second_row` (`test.image` becomes `null`) |
| `dev-split-20261106T021526Z` (replay) | `meta.graded_at`, `primary_metric.headline_row`, `primary_metric.second_row`, `incomplete_detail.cache_miss` (absent becomes `3`) |

`summary.md` differs in two kinds of line: the cost line in both runs (`(0 unpriced)` becomes `, 0 unpriced (notional)`, and `(3 unpriced)` becomes `, 3 unpriced (notional)`), and on the replay only, the INCOMPLETE line (`3 infrastructure_failure` becomes `3 infrastructure_failure (3 cache_miss)`). All other lines are identical.

No grade, rate, interval, count, tag or `cases` entry moved: the walk found zero differences outside the paths above. The committed `summary.json` files are therefore still valid `compare` input (I compared them, see Truth 4) but they are not what HEAD would write. The two committed files still carry `headline_row: "test.text"`, a pointer into a `headline` that has only the `dev` scope; D-49 and the README (line 221) state this. `grade` on the published directory itself still refuses ("holds a published summary; regrade into a new directory with --out", exit 2), so the committed files remain verbatim. The tree stayed clean (`git status --short` empty) after my regrades.

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Graders score schema validity, exact match (IDs, CNPJ, access key) and numeric tolerance (configurable); line items matched by optimal assignment; self-tests show ground truth perfect and corruptions wrong | VERIFIED | `grader.py` schema grading (jsonschema plus strict Pydantic), `normalize_id` comparison for access key, CNPJ and tax id, `--tolerance` on `grade`, and `matching.assign_rows` on `scipy.optimize.linear_sum_assignment`. `python/tests/test_selftests.py` (184 collected tests) runs all 154 cases: ground truth scores perfect with no tag, and one seeded corruption per tag scores wrong with exactly its tag. It passed in the full run. The second fix round did not touch this logic, and a targeted rerun of the self-test, run-state and compare tests passed (195 passed). |
| 2 | Full-dataset run summary reports per-field and per-slice accuracy with n and CIs, plus cost, cache hit rate and latency percentiles; infrastructure failures counted apart from wrong answers | VERIFIED | The regraded dev summary has `field_stats`, `field_by_slice`, `headline`, `slices`, `totals` (incurred and notional cost), `cache` (`hit_rate`, `hits`, `answered_attempts`), `latency_ms` (`p50` 31919, `p95` 125670, `n` 51) and `model_latency_ms`. Infrastructure and harness cases get `correct: null`, are excluded from the denominator and counted in `incomplete_detail`. Wilson intervals recomputed independently here: 12/30 gives [0.2459, 0.5768], 0/3 gives [0.0, 0.5615], 4/16 gives [0.1018, 0.495], all matching the summary. |
| 3 | Summary reports the silent-error rate; failures are auto-tagged by taxonomy and counted | VERIFIED | `silent_errors`: 4 silent of 16 success cases, rate 0.25, ci95 [0.1018, 0.495], split payment 0 and other 4. `failure_tags` carries per-case and per-value counts for every tag in the frozen list, zeros kept, plus `untagged`. The regrade at HEAD reproduces these numbers exactly (zero differences in those blocks). The list is frozen as `failure-tags-1` under `grader-003` (D-48). |
| 4 | `compare` lists per-field deltas and regressed cases between two runs and refuses runs whose dataset or grader versions differ | VERIFIED | I ran `carimbo-evals compare` on the published pair: exit 0, "no change" from the exact McNemar test on 30 dev text cases, 51 shared cases, 3 only in the second. `compare.md` now also prints each run's state (`incomplete, stopped: cost_cap` and `incomplete`). Refusals, each exit 2 with no override flag: grader version `grader-002` ("grader versions differ"), a different dataset manifest hash ("dataset manifests differ"), a version 3 summary ("comes from an older grader"), an unparseable file ("cannot be read as JSON"). |

**Score:** 4/4 truths verified (0 present, behavior-unverified)

### Behavior-dependent items introduced by the second fix round

| Item | Status | Evidence |
|------|--------|----------|
| A run with no usable `run.json` grades as incomplete | VERIFIED by execution | I copied the live run to a scratch directory and removed `run.json`: `grade` exited 0, `incomplete: true`, `incomplete_detail.run_state: "missing"`, summary line ends `run.json missing`. With `{"torn` written as `run.json`: `run_state: "damaged"`, `run.json damaged`. Named test `test_a_missing_or_damaged_run_json_grades_without_error_and_is_incomplete` passes. The unmodified published runs have a good `run.json` and add no `run_state` key. |
| `run` refuses held-out `test` cases without `--allow-test-split` | VERIFIED by execution | With a dummy eval key and an unreachable URL: `--split test` (100 cases) exits 2, `--case nfe-0003` (a test case) exits 2, and with no `--split` the same refusal fires (100 test cases). The message names D-48, and `--out` was not created in the refusing cases. The key check comes first, as documented. I did not run with the flag, so no dispatch was attempted. The layout test that scans the justfile for `--allow-test-split` and any non-literal `--split` value passes. |
| Killed or narrowed runs stay incomplete (CR-01, from round 1) | VERIFIED by named tests | `run.json` is written with `stopped_reason: "in_progress"` before the first dispatch (`runner.py` `STOPPED_IN_PROGRESS`); the three CR-01 tests passed in the full suite. |

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `python/src/carimbo_evals/grader.py` | Whole-invoice grading, run-state and record validation | VERIFIED | Read at HEAD; wired through `cli.py grade`. |
| `python/src/carimbo_evals/matching.py`, `tags.py`, `corruptions.py`, `stats.py`, `slices.py` | Matching, frozen tags, corruption catalogue, statistics, slices | VERIFIED | Unchanged since the earlier report (`git diff-tree` shows none of them in the fix-round diff). |
| `python/src/carimbo_evals/summary.py` | Version 4 summary | VERIFIED | `SUMMARY_VERSION` 4, `GRADER_VERSION` `grader-003`. |
| `python/src/carimbo_evals/compare.py` | `compare` with refusals and run state | VERIFIED | Run in the checks above. |
| `python/src/carimbo_evals/cli.py`, `runner.py`, `ground_truth.py` | Commands, run marker, XML error handling | VERIFIED | Barrier and run-state behavior executed above. |
| `evals/results/dev-split-*` (2 runs) | Published live run and replay, verbatim | VERIFIED | Untouched in git; the regrade guard refuses in place. See the byte-for-byte note above. |
| `docs/DECISIONS.md` D-42 to D-49 | Decision records | VERIFIED | D-48 and D-49 read in full; `just docs-check` prints `docs ok`. D-49 matches the code on run state, cache misses, the flag and the null pointers. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `cli.py grade` | `grader.grade_run` | direct call | WIRED | Ran it on both published runs and on two damaged copies. |
| `grader.grade_run` | `summary.build_summary` | `run_meta` with `stopped_reason`, `cases_not_run`, `run_state`, `cache_miss` | WIRED | `cache_miss` 3 and `run_state` values appear in the outputs. |
| `cli.run` | `runner.read_case_splits` | test-split check on the final selection | WIRED | Refusals executed above. |
| `cli.py compare` | `compare.compare_summaries` / `render_markdown` | CLI | WIRED | Exit 0 and four exit-2 refusals executed. |
| `runner.run_cases` | `run.json` `in_progress` marker | written before first dispatch | WIRED | Covered by passing tests. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `summary.json` rates and intervals | `correct`, `wrong` per case | `cases.jsonl` graded against the XML ground truth | Yes; the regrade at HEAD reproduces every committed number | FLOWING |
| `compare.json` deltas and run state | per-case `fields`, `items.columns`, `incomplete`, `stopped_reason` | both input summaries | Yes; run state shown for the published pair | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Python gate | `devenv shell -- just py-check` | exit 0; 3278 passed, 17 deselected, 1 warning (the existing dataset size-budget warning); ruff, format, pyright clean | PASS |
| Offline end-to-end proof | `devenv shell -- just e2e` | exit 0; 17 passed, 3278 deselected | PASS |
| Docs gate | `devenv shell -- just docs-check` | `docs ok` | PASS |
| Secrets gate | `devenv shell -- just secrets-check` | `secrets ok` | PASS |
| Targeted behavior tests (self-tests, run state, test-split barrier, compare state, in-progress marker) | `pytest -k "killed or narrower or missing_or_damaged or refuses_the_whole or status_changed or incomplete_and_why or selftest or in_progress"` | 195 passed | PASS |
| Regrade published runs | `carimbo-evals grade --out` on both | exit 0, only the paths listed above differ | PASS (with the stated byte-for-byte caveat) |
| `just check` (full gate including dotnet) | not rerun by me; the caller reports exit 0 at HEAD, and the fix report records dotnet 2175 of 2175 | The dotnet side is outside this phase's code | not independently rerun |
| Tree untouched | `git status --short` after all checks | clean | PASS |

### Probe Execution

Step 7c: SKIPPED. No probe scripts are declared by the phase plans.

### Requirements Coverage

Plan frontmatter across the 13 plans declares EVAL-03 (4 plans), EVAL-04 (6), EVAL-05 (7), EVAL-06 (5), EVAL-07 (7) and EVAL-08 (4). All six appear in REQUIREMENTS.md as `[x]` and `Complete` in the traceability table (lines 76 to 81 and 204 to 209) mapped to Phase 5. No ID mapped to Phase 5 is unclaimed.

| Requirement | Status | Evidence |
|-------------|--------|----------|
| EVAL-03 | SATISFIED | Truth 1. |
| EVAL-04 | SATISFIED | Truth 1 self-tests; infrastructure and harness cases are `correct: null`, excluded and counted apart (Truth 2); the replay's cache misses are now named. |
| EVAL-05 | SATISFIED | Truth 2. |
| EVAL-06 | SATISFIED | Truth 3, `silent_errors`. |
| EVAL-07 | SATISFIED | Truth 3, `failure_tags`, frozen list. |
| EVAL-08 | SATISFIED | Truth 4. |

No orphaned requirements.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | - | No `TBD`, `FIXME` or `XXX` in `python/src/carimbo_evals/*.py`, `docs/DECISIONS.md` or `justfile` | - | No debt markers. |

Review findings: the earlier report listed IN-01 to IN-08 and the CR-01 residual as open. At HEAD, the CR-01 residual and IN-01 to IN-06 and IN-08 are fixed (read in the diff above and exercised by passing tests). IN-07 (265 of 827 wrong values `untagged`) stays open by the human's choice, because the tag list is frozen under D-48; it is a measured limitation, not a defect.

### Human Verification Required

None. The two items the fix report flagged as "requires human verification" (the completeness logic and the test-split barrier) were exercised here by running the commands, and their named tests pass.

### Gaps Summary

No gaps. The phase delivers the grader, the version 4 summary with intervals, the silent-error rate and frozen tag counts, and a `compare` that refuses mismatched runs, each checked in code and by execution at HEAD after the second review-fix round.

Honest limitations, none of them goal failures:
- The published `summary.json` files are not what HEAD would write (see the section above). Their numbers are unchanged, but a byte-level `diff` against a fresh regrade fails, and their `primary_metric.headline_row` still points at a `test.text` row that a dev-only summary does not contain.
- The only live evidence is one dev-split run (51 of 54 cases, stopped by the US$2.50 cap) and its replay. About 40% of text-layer cases are correct; the `test` split has not been run (by design, until Phase 6).
- The full 154-case proof is offline with a scripted model, not a live run.
- I did not rerun the dotnet half of `just check`; the code in scope for this phase is Python.

---

_Verified: 2026-11-06T05:20:00Z_
_Verifier: Claude (gsd-verifier)_
