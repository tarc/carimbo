---
phase: 05-comparable-measurement
fixed_at: 2026-11-06T00:00:00Z
review_path: .planning/phases/05-comparable-measurement/05-REVIEW.md
iteration: 1
findings_in_scope: 5
fixed: 5
skipped: 0
status: all_fixed
---

# Phase 5: Code Review Fix Report

**Fixed at:** 2026-11-06
**Source review:** .planning/phases/05-comparable-measurement/05-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 5 (CR-01, WR-01 to WR-04; the human excluded all IN findings)
- Fixed: 5
- Skipped: 0

## Fixed Issues

### CR-01: A run killed before its end, or resumed on a narrower selection, grades as `incomplete: false`

**Files modified:** `python/src/carimbo_evals/runner.py`, `python/tests/test_runner.py`
**Commit:** 0a1f43a
**Status:** fixed: requires human verification (completeness logic)
**Applied fix:** `run_cases` writes `run.json` with `stopped_reason: "in_progress"` before the first dispatch and replaces it atomically only when the run ends normally. A killed or interrupted run keeps the marker, so the grader already flags it (`stopped: in_progress`, `cases_not_run` from the provisional `case_ids`) and prints the INCOMPLETE line. On `--resume`, `case_ids` is the union of the earlier `run.json` and the current selection, so `--resume --case X` after a cost-cap stop still reports the unrun cases. Cost accounting is untouched: it is derived from `cases.jsonl` only.
**Deviation from the suggested fix:** the review also proposed flagging a missing `run.json` as incomplete in the grader (`run_state_missing`). Not done. The existing test `test_a_missing_or_damaged_run_json_grades_without_error_and_is_not_incomplete` pins the opposite, decision and skeleton-style runs grade without a `run.json`, and the constraint is not to weaken existing tests. The provisional `run.json` closes the hazard at the source instead. The remaining gap: a run directory whose `run.json` was deleted by hand still grades as complete.
**Tests:** `test_run_json_says_in_progress_from_the_first_dispatch_until_the_run_ends`, `test_a_run_killed_before_its_end_grades_as_incomplete` (path 1), `test_resuming_with_a_narrower_selection_after_a_cap_stop_stays_incomplete` (path 2), `test_resuming_a_killed_run_keeps_its_accounting_and_completes_it` and `test_resuming_the_same_selection_keeps_run_json_case_ids_unchanged` (accounting and stability guards). The first three fail without the fix.

### WR-01: `compare` lists a case that degraded to refused or truncated as "improved" (and the reverse as "worsened")

**Files modified:** `python/src/carimbo_evals/compare.py`, `python/tests/test_compare.py`
**Commit:** b92c890
**Status:** fixed: requires human verification (classification logic)
**Applied fix:** wrong-value counts decide `worsened`/`improved` only when both statuses are in `GRADED_STATUSES`. Otherwise a status change goes to a new `status_changed` list (in `compare.json`, and as a `status changed` section after `improved` in `compare.md`). Same non-graded status on both sides appears in no list. `COMPARE_VERSION` is unchanged (the key is additive, no compare output is committed).
**Test:** `test_a_case_that_became_refused_or_truncated_is_a_status_change_not_an_improvement` (fails without the fix).

### WR-02: `compare` raises raw tracebacks on structurally odd summaries, contradicting its stated contract

**Files modified:** `python/src/carimbo_evals/compare.py`, `python/tests/test_compare.py`
**Commit:** aadf4cf
**Applied fix:** `_validate_case` now refuses `fields` that is not an object, `items`/`installments` that are not objects, `columns` that is not an object, and cell `correct`/`n` that are not real integers (bool, float including `Infinity` and `NaN`, and strings). Chosen over the catch-all `try/except` so the refusal names the case and member.
**Tests:** eight new `HOSTILE` entries, including the `Infinity` count (`OverflowError` before the fix; checked directly against the previous code), a list for `items`, a string count, a bool count and a NaN count.

### WR-03: `summary.cache.hit_rate` rounds with a different rule than every other rate

**Files modified:** `python/src/carimbo_evals/summary.py`, `python/tests/test_grader.py`
**Commit:** a7fafe6
**Applied fix:** `hit_rate` is `rate(hits, answered)`. `test_the_hit_rate_is_rounded_to_four_places` is untouched and still passes (1 of 3 is 0.3333).
**Test:** `test_the_hit_rate_rounds_exact_halves_up_like_every_other_rate` (1/32 gives 0.0313 and 5/32 gives 0.1563; fails without the fix). The decision summary has its own `hit_rate` in `decision_summary.py`; it was not in the finding and is unchanged.

### WR-04: `run_cases --resume` truncates the torn tail before it checks the overrides, so "nothing has been written when this raises" is false

**Files modified:** `python/src/carimbo_evals/runner.py`, `python/tests/test_runner.py`
**Commit:** 98b9152
**Applied fix:** `_load_existing` now returns `(records, torn_tail_length)` and never writes; the new `_drop_torn_tail` runs in `run_cases` after `_require_same_overrides` has passed. A refused resume leaves `cases.jsonl` and `run.json` byte-identical.
**Test:** `test_a_refused_resume_leaves_a_torn_final_line_in_place` (fails without the fix). `test_resume_repairs_a_torn_final_line` and the corrupt-line tests still pass.

## Constraint checks

- **Published numbers:** both published runs were regraded into `/tmp/claude-1000/-home-tarci-projects-carimbo/7ba805a1-4cf0-4038-a9f0-7683c19a19db/scratchpad/regrade-check/<RUN>` (nothing written under `evals/results`). For `dev-split-20261106T001800Z` and `dev-split-20261106T021526Z`, `summary.json` equals the committed file apart from `meta.graded_at`, and `summary.md` is byte-identical. No new key was added to `incomplete_detail`.
- **Frozen items:** `tags.py`, `TAG_ORDER`, `TAGS_VERSION`, `GRADER_VERSION` and the summary version (4) are unchanged. No change to a case's `correct`, `wrong` or tags.
- **Untouched paths:** nothing under `dotnet/`, `data/`, `schema/`, `evals/results/` or `.cache/`; `AgentToolboxFaultTests` not touched. No live recipe, no `secretspec`, no key read, no test split.
- **Where verification ran:** the edits and the regrade ran in an isolated worktree under `.claude/worktrees/` (since removed). After the fast-forward of `gsd/phase-05-comparable-measurement` to the five fix commits, the final gates ran in the main checkout, so the counts below are reproducible there.
- `just py-check`: 3240 passed, 16 deselected, 1 warning (the existing dataset size-budget warning); ruff, format and pyright clean.
- `just e2e`: 16 passed, 3240 deselected.
- `just check` (run once): exit 0. dotnet 2173 of 2173 passed (0 failed, 0 skipped); python 3240 passed, 16 deselected; e2e 16 passed; `docs ok`, `secrets ok`. `A_tool_that_outlives_the_call_timeout_is_a_timeout_on_the_real_session` did not fail, so no rerun was needed.

---

_Fixed: 2026-11-06_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
