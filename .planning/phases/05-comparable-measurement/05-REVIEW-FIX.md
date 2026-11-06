---
phase: 05-comparable-measurement
fixed_at: 2026-11-06T05:00:00Z
review_path: .planning/phases/05-comparable-measurement/05-REVIEW.md
iteration: 2
findings_in_scope: 14
fixed: 13
skipped: 1
status: partial
rounds:
  - round: 1
    findings_in_scope: 5
    fixed: 5
    skipped: 0
  - round: 2
    findings_in_scope: 9
    fixed: 8
    skipped: 1
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

## Round 2

**Fixed at:** 2026-11-06
**Iteration:** 2
**Scope:** `fix_scope: all`, minus CR-01 and WR-01 to WR-04 (round 1). The human asked to address everything still open: Item A (the residual gap of CR-01, not in REVIEW.md) and IN-01 to IN-08, plus decision record D-49.

**Summary:**
- Findings in scope: 9 (Item A, IN-01 to IN-08)
- Fixed: 8
- Skipped: 1 (IN-07, by the human's choice)
- Frontmatter counts cover both rounds: 5 + 9 in scope, 5 + 8 fixed, 0 + 1 skipped.

### Item A: A run directory whose run.json is missing or damaged still graded as complete

**Not a REVIEW.md finding.** It is the residual gap that round 1 recorded under CR-01 ("a run directory whose `run.json` was deleted by hand still grades as complete") and that the human decided to close.
**Files modified:** `python/src/carimbo_evals/grader.py`, `python/src/carimbo_evals/summary.py`, `python/tests/test_grader.py`, `python/tests/test_matching.py`, `python/tests/test_silent_errors.py`, `python/tests/test_summary_v4.py`
**Commit:** 85c13ef
**Status:** fixed: requires human verification (completeness logic)
**Applied fix:** `load_run_state` returns `(state, problem)`. `grade_run` still succeeds and never crashes, but a run directory with no `run.json` is `incomplete: true` with `incomplete_detail.run_state: "missing"`, and one whose `run.json` is unreadable, not an object, or has no `case_ids` list is `"damaged"`. The INCOMPLETE line of `summary.md` ends with `run.json missing` or `run.json damaged`. The `run_state` key exists only when it fires, so a run with a good `run.json` keeps its exact summary (checked on the regrades below). The decision grader does not read `run.json`, so decision-run grading does not change.
**Deliberate reversal:** `test_a_missing_or_damaged_run_json_grades_without_error_and_is_not_incomplete` pinned the opposite behaviour. The human decided to change it, so it is rewritten as `test_a_missing_or_damaged_run_json_grades_without_error_and_is_incomplete` (missing, torn JSON, empty file, a list, `case_ids` not a list, no `case_ids`) and now asserts the exact `incomplete_detail` and the INCOMPLETE line. This is a reversal, not a weakened test. The test helpers that wrote a run directory without a `run.json` (`_write_run` in `test_grader.py`, `test_matching.py`, `test_summary_v4.py`, `test_silent_errors.py`) now write a valid one, because they expect a complete run.
**Beyond the brief:** a `run.json` that is a JSON object without a `case_ids` list also counts as damaged, because it cannot show that every selected case ran. All nine published `run.json` files have a `case_ids` list. A `run.json` that is a directory is damaged, not a crash.
**Tests:** the rewritten test above, `test_a_run_json_that_is_a_directory_is_damaged_not_a_crash`, `test_a_good_run_json_adds_no_run_state_key`. The first fails without the fix.

### IN-01: Stale comment says per-item grading is future work

**Files modified:** `python/src/carimbo_evals/summary.py`
**Commit:** ef2ecc0
**Applied fix:** the comment now reads "Item and installment columns are graded separately (`matching.diff_rows`)." Comment only, so no test.

### IN-02: `primary_metric.headline_row` points at a row a dev-only summary does not have

**Files modified:** `python/src/carimbo_evals/summary.py`, `python/tests/test_summary_v4.py`
**Commit:** 4841345
**Applied fix:** `headline_row` and `second_row` are `null` when the summary has no `test` scope, and `test.text` and `test.image` when it does.
**Tests:** `test_the_summary_declares_its_primary_metric_and_interval_method` changed only as needed: its case now has `split="test"` (a split-less case has no test scope), and it also asserts the pointer resolves to a row. New: `test_a_summary_without_a_test_scope_points_at_no_headline_row` (dev-only and split-less) and `test_a_summary_with_dev_and_test_cases_keeps_both_pointers`. They fail without the fix.

### IN-03: Protection of the `test` split is by recipe absence only

**Files modified:** `python/src/carimbo_evals/cli.py`, `python/tests/test_runner.py`, `python/tests/test_repo_layout.py`, `python/tests/test_e2e_full_dataset.py`
**Commit:** aacd1d6
**Status:** fixed: requires human verification (a barrier)
**Applied fix:** `carimbo-evals run` takes `--allow-test-split`. Without it, `run` exits 2 when any case of the final selection has manifest split `test`, which covers `--split test`, a run with no `--split` over a manifest with test cases (all 154), and `--case` naming a test case. The check runs after the selection and before any dispatch and before `run_cases` creates `--out`. The message says a test-split run is a human decision and names D-48. A directory with no manifest splits (`data/skeleton`) is unaffected. `python/tests/test_e2e_full_dataset.py` passes the flag on its three full-dataset runs (no model output, D-43) and not on its dev-split run. No justfile recipe passes it.
**Strengthened layout test:** `test_dev_split_recipe_is_capped_to_the_dev_cases` now calls `_test_split_leaks(justfile)`, which rejects `--allow-test-split` anywhere and any `--split` whose value is not a literal `dev` (a quoted `"test"`, `--split=test`, a recipe parameter, a shell variable). `test_the_test_split_leak_check_catches_every_spelling` pins the checker itself. This replaces the weaker `"--split test" not in justfile`, which it subsumes.
**Tests:** each of the three selection paths refused and each allowed with the flag, `test_cli_dev_runs_need_no_flag_and_a_directory_without_splits_is_unaffected`, and `test_the_real_command_refuses_the_whole_dataset_without_the_flag` (the console script on data/dataset, three selections, nothing written). Six of them fail without the fix.
**Note:** the API-key check still comes first, so without `CARIMBO_EVAL_API_KEY` the exit is 2 for that reason instead.

### IN-04: Replay summaries print "unpriced" and fold cache misses into infrastructure failures

**Files modified:** `python/src/carimbo_evals/grader.py`, `python/src/carimbo_evals/summary.py`, `python/tests/test_grader.py`
**Commit:** 6448979
**Applied fix:** the cost line reads `... notional, N unpriced (notional)`. `grade_run` counts the records that ended as an infrastructure failure with failure kind `cache_miss` and passes the count in `run_meta`; `incomplete_detail.cache_miss` exists only when above 0, and the INCOMPLETE line reads `3 infrastructure_failure (3 cache_miss)`. The count is computed from the records, not stored per case, so no case entry gains a key and no grade moves.
**Tests:** `test_cache_misses_are_counted_in_the_incomplete_detail_and_the_incomplete_line` (fails without the fix), `test_no_cache_miss_key_appears_when_there_is_none`. One existing assertion changed by design: the replay markdown line in `test_replay_markdown_shows_incurred_and_notional_cost_the_hit_line_and_the_hits_column` now expects the new label.

### IN-05: `grade` leaks tracebacks for malformed run files and builds a path from the record's `case_id`

**Files modified:** `python/src/carimbo_evals/grader.py`, `python/src/carimbo_evals/ground_truth.py`, `python/tests/test_grader.py`
**Commit:** f3a5007
**Applied fix:** `_read_records` validates each non-blank line as the runner's `_load_existing` does (a JSON object with a string `case_id`) and raises `ValueError` naming the line; `case_id` must fullmatch `[A-Za-z0-9._-]+` before any path is built (a trailing newline or NUL fails). A torn final line is still ignored. `invoice_from_xml` turns `ET.ParseError` into `ValueError`. `grade` already catches `ValueError`, so it exits 2 with a message. A record without `run_id` no longer raises `KeyError` in `grade_run` (the run directory name is used), which the brief's "reports these cleanly" implies.
**Tests:** eleven parametrized bad-line cases, `test_grade_command_reports_a_bad_record_without_a_traceback`, `test_a_torn_final_line_is_still_ignored`, `test_a_record_without_run_id_still_grades`, `test_malformed_xml_ground_truth_is_a_value_error_and_a_clean_cli_error`. They fail without the fix (14 failures against the old code in `test_grader.py`).

### IN-06: `compare` does not surface that either run is incomplete

**Files modified:** `python/src/carimbo_evals/compare.py`, `python/tests/test_compare.py`
**Commit:** ae21961
**Applied fix:** `first` and `second` in `compare.json` gain `incomplete` (bool, or null for a summary that does not say) and `stopped_reason`. `compare.md` shows the state beside each run id: `complete`, `incomplete, stopped: cost_cap`, `incomplete`, or `completeness not recorded`. Additive: `COMPARE_VERSION` stays `compare-1`, and the verdict and every classification are untouched.
**Placement:** the brief said "compare.json and compare.md"; the state sits in the header bullets of `compare.md`, not in the Same/Changed sections, because those hold dataset, grader, tolerance and run configuration, and the run state is a property of each run rather than a configuration.
**Tests:** `test_compare_json_and_markdown_say_which_run_is_incomplete_and_why`, `test_an_incomplete_run_without_a_stop_reason_and_a_summary_that_says_nothing`, `test_the_published_summaries_still_compare_and_report_their_run_state` (fail without the fix), and `test_the_run_state_changes_neither_the_verdict_nor_a_classification` (a guard that passes either way, by design).

### IN-07: One third of the wrong values in the published dev run are `untagged` (observation only, tags frozen under D-48)

**File:** `python/src/carimbo_evals/tags.py` (frozen)
**Status:** skipped: tags are frozen by D-48, and the human chose freeze-as-is.
**Reason:** the review itself asked for no change ("record it for the next tag-list version"). D-48 freezes `failure-tags-1`, `TAG_ORDER`, `TAGS_VERSION` and `GRADER_VERSION` before the `test` split is run, and the human chose `freeze-as-is` on 2026-11-06. No code change, no test.
**Original issue:** 265 of 827 wrong values are `untagged`, mostly multi-character identifier errors and a null for a present `recipient.ie`, which no digit rule covers.

### IN-08: Impossible-state guard: re-draw loop for an all-zero trace and span id

**Category:** necessity
**Files modified:** `python/src/carimbo_evals/runner.py`, `python/tests/test_runner.py`
**Commit:** d4e0f07
**Status:** fixed: requires human verification (it removes code)
**Applied fix:** both `while` loops are gone from `new_traceparent`; each id is one `secrets.token_hex` draw, and the docstring states the W3C rule and why there is no re-draw.
**Tests:** `test_new_traceparent_is_two_draws_with_no_redraw_loop` forces all-zero draws only to count them (two draws; fails against the old loops, which redraw), and `test_new_traceparent_has_the_w3c_shape_and_the_trace_id_it_returns`.

### D-49: decision record (not a finding)

**Files modified:** `docs/DECISIONS.md`, `justfile`, `README.md`, `AGENTS.md`
**Commit:** 27fe1cb
`## D-49` sits above `## Open questions` and records the run state, the `in_progress` marker from CR-01, the cache-miss count, the `--allow-test-split` barrier (no recipe passes it), and the null primary-metric pointers. D-42, D-44, D-46 and D-48 carry `**Refined by:** D-49 (2026-11-06)`. The docs range is `D-18 to D-49` in the justfile comment, the justfile loop (now ends with `D-49`), README.md and AGENTS.md; AGENTS.md has one added line on `--allow-test-split`. The README's `- Phase 5:` bullet and Status row were not touched. Not changed either: README.md line 60 still says "Decisions D-42 to D-48 ... record the grader ...", which is narrative the orchestrator owns.

## Round 2 constraint checks

- **Published numbers did not move.** Both published runs were regraded with `carimbo-evals grade --run evals/results/<RUN> --cases data/dataset --out <dir>` into `/tmp/claude-1000/-home-tarci-projects-carimbo/7ba805a1-4cf0-4038-a9f0-7683c19a19db/scratchpad/regrade-check2/<RUN>` and deep-compared with the committed `summary.json` (a recursive walk over every key and list element). Differing paths:
  - `dev-split-20261106T001800Z` (3): `meta.graded_at`, `primary_metric.headline_row` (`test.text` to null), `primary_metric.second_row` (`test.image` to null).
  - `dev-split-20261106T021526Z` (4): `meta.graded_at`, `primary_metric.headline_row`, `primary_metric.second_row`, `incomplete_detail.cache_miss` (added, 3).
  - `summary.md` lines that differ: the cost line in both (`(0 unpriced)` to `, 0 unpriced (notional)` and `(3 unpriced)` to `, 3 unpriced (notional)`), and on the replay only the INCOMPLETE line (`3 infrastructure_failure` to `3 infrastructure_failure (3 cache_miss)`). Nothing else differs; no `cases` entry, count, rate, interval or tag moved.
- **Frozen items:** `tags.py`, `TAG_ORDER`, `TAGS_VERSION`, `GRADER_VERSION` and the summary version (4) are unchanged. `compare` on the two committed summaries exits 0 (the live run reads `incomplete, stopped: cost_cap`, the replay `incomplete`).
- **Untouched paths:** nothing under `dotnet/`, `data/`, `schema/`, `evals/results/` or `.cache/` (git status clean after the gates). No live recipe, no `secretspec`, no key read, no test split run against a provider.
- **Where verification ran:** in the main checkout on `gsd/phase-05-comparable-measurement`, as the orchestrator instructed, with no isolated worktree and no fast-forward step. The regrades and all three gates ran there, so the numbers are reproducible from this tree. Nothing was pushed.
- `just py-check`: exit 0. 3278 passed, 17 deselected, 1 warning (the existing dataset size-budget warning); ruff, format and pyright clean. Round 1 ended at 3240 passed, 16 deselected.
- `just e2e`: exit 0. 17 passed, 3278 deselected.
- `just check` (run once): exit 0. dotnet 2175 of 2175 passed (0 failed, 0 skipped); python 3278 passed, 17 deselected; e2e 17 passed; `docs ok`, `secrets ok`. No flaky test appeared, so nothing was rerun.

---

_Fixed: 2026-11-06 (round 1 and round 2)_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 2_
