---
phase: 05-comparable-measurement
plan: 04
subsystem: evals
tags: [failure-tags-1, eval-07, eval-04, corruptions, self-tests]

requires:
  - phase: 05-comparable-measurement
    provides: matching.py row assignment, wrong-value dicts and exact grading (05-03); D-43 (05-02)
provides:
  - tags.py with failure-tags-1 (TAGS_VERSION, TAG_ORDER, CaseContext, PREDICATES, first_match, tag_case)
  - grader.diff_invoice, and tag and rule on every wrong value, failure_tags on every case record
  - corruptions.py with the shared seeded catalogue (CORRUPTIONS, corrupted, stored_record)
  - test_tags.py and test_selftests.py proving the rules over all 154 cases
affects: [05-05, 05-06, 05-07, 05-08, 05-09, 05-10, 05-11, 05-12, 05-13, phase-06]

actuals:
  tokens: 16400
  tasks: 3
  commits: 3
plan_head_before: 10e96fc9475a53b0d71f587d4de4230daa27303b
plan_head_after: 21a7dad4321378388e7e9e6b187ca779673fab47

tech-stack:
  added: []
  patterns:
    - "An ordered first-match rule list over the graded diff, rule number = index in TAG_ORDER (same shape as DecisionRules.Label)"
    - "Row-assignment facts (trailing tail, merged and split cells, crossover) are precomputed once in a frozen CaseContext"
    - "One corruption catalogue in src, keyed by the tag it must produce, shared by pytest and the e2e proof"

key-files:
  created:
    - python/src/carimbo_evals/tags.py
    - python/src/carimbo_evals/corruptions.py
    - python/tests/test_tags.py
    - python/tests/test_selftests.py
  modified:
    - python/src/carimbo_evals/grader.py

key-decisions:
  - "merged_rows and split_rows also require the absorbed or extra row's own value to be non-zero, so a dropped or invented zero row is never read as a merge or split"
  - "party_swap requires the two expected party values to differ, so a party that is its own counterpart shows no crossover"
  - "text_wrong needs a string on both sides; a null prediction on a schema-valid answer is outside the rule and stays untagged"
  - "The battery runs on all 154 cases with every pin held; the sampled path is not used"

patterns-established:
  - "diff_invoice(status, invoice, truth, tolerance) is the entry point for self-tests; it returns fields, tagged wrong, items, installments and the graded invoice"
  - "A schema_invalid case takes rule 0 on every wrong value, count fields included"

requirements-completed: [EVAL-04, EVAL-07]

coverage:
  - id: D1
    description: "Every wrong value of a graded case carries one tag and its rule number from the ordered failure-tags-1 list; each case lists its sorted unique tags"
    requirement: "EVAL-07"
    verification:
      - kind: unit
        ref: "python/tests/test_tags.py#test_each_rule_tags_its_hand_built_case"
        status: pass
      - kind: unit
        ref: "python/tests/test_tags.py#test_tag_order_is_pinned"
        status: pass
    human_judgment: false
  - id: D2
    description: "Ground truth scores perfect with no tag on 154 of 154 cases, and every tag has a seeded corruption that produces exactly that tag, with the applicable counts pinned"
    requirement: "EVAL-04"
    verification:
      - kind: unit
        ref: "python/tests/test_selftests.py#test_ground_truth_scores_perfect_with_no_tag"
        status: pass
      - kind: unit
        ref: "python/tests/test_selftests.py#test_a_corruption_produces_wrong_values_tagged_by_exactly_its_tag"
        status: pass
    human_judgment: false
  - id: D3
    description: "Deleted, duplicated and swapped rows give exact precision and recall; infrastructure outcomes stay distinct from wrong answers"
    requirement: "EVAL-04"
    verification:
      - kind: unit
        ref: "python/tests/test_selftests.py#test_a_deleted_row_costs_recall_exactly_and_never_precision"
        status: pass
      - kind: unit
        ref: "python/tests/test_selftests.py#test_an_excluded_status_is_neither_correct_nor_wrong"
        status: pass
    human_judgment: false
  - id: D4
    description: "Whether rules proven on seeded corruptions describe real model failures (the untagged bucket of the live dev run)"
    verification: []
    human_judgment: true
    rationale: "Only the live dev run produces real failures; plan 05-12 reviews the untagged bucket against it"

duration: 11min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 04: every wrong value tagged, and the grader proven on seeded corruptions Summary

**failure-tags-1 is an ordered 19-rule list over the graded diff and the row assignment; every wrong value now carries a tag and a rule number, and a shared 18-kind corruption catalogue proves the grader over all 154 cases (ground truth perfect, each corruption yielding exactly its tag, exact row precision and recall).**

## Performance

- **Duration:** 11 min
- **Started:** 2026-11-05T21:12:33Z
- **Completed:** 2026-11-05T21:22:08Z
- **Tasks:** 3 (one tracer, two auto with tdd)
- **Files modified:** 5 (4 created)

## Accomplishments

- Tracer: a dropped middle row gives `failure_tags == ["dropped_row"]` with the same tag and rule on `items[k].total` and `item_count`; losing the last two rows gives `trailing_rows_lost` on both rows and the count; an extra row gives `invented_row`; a `schema_invalid` record takes rule 0 on every wrong value; an unexplained value is `untagged` at rule 18.
- `tags.py`: `TAG_ORDER` (19 names, pinned by `test_tag_order_is_pinned`), `PREDICATES` (one per tag, asserted equal to `TAG_ORDER`), `CaseContext` with the precomputed trailing tail, crossover, merged and split cells, `first_match` and `tag_case`. `tags.py` imports only `normalize`, `matching` and the standard library.
- `grader.diff_invoice(status, invoice, truth, tolerance)` returns `fields`, tagged `wrong`, `items`, `installments` and the graded `invoice`; `grade_case` calls it and writes `failure_tags` (sorted, unique, `[]` before any early return).
- `corruptions.py`: 18 seeded corruptions keyed by the tag they must produce, `corrupted(tag, invoice, seed)` seeded with `random.Random(f"{seed}-{tag}")`, and `stored_record` (record and contract version 3; `harness_error` gives a record that is not `completed` and has no response).
- `test_selftests.py` over the 154 cases (90 distinct ground truths by `xml_sha256`): ground truth perfect on 154 of 154 with no tag; every corruption yields only its own tag and rule on every applicable case; applicable counts held exactly at 117 (`trailing_rows_lost`, `dropped_row`), 128 (`merged_rows`, `value_from_other_row`), 41 (`zero_for_blank`) and 154 for the other 13 kinds; delete one row gives precision 1 and recall (n-1)/n, duplicate gives n/(n+1) and 1, swap gives 1 and 1 with `correct` True and `order_preserved` False; `infrastructure_failure` and `harness_error` give `correct` None, `refused` and `truncated` give `correct` False, all with `wrong` `[]`.

## Task Commits

1. **Task 1 (tracer): tags travel from grade_run to the case record** - `eda34e4` (feat)
2. **Task 2: the 19 rules of failure-tags-1 with a case per tag and both sides of each threshold** - `056dd91` (feat)
3. **Task 3: seeded corruption catalogue and the EVAL-04 self-tests** - `21a7dad` (test)

## Verification

- Tracer feedback gate: the `<verify>` is automated only; run after the commit, 129 passed (test_tags, test_grader, test_matching); expanded.
- Task 2: `pytest python/tests/test_tags.py` 52 passed; `pyright -p python` 0 errors. RED for the rule tests was observed before the predicates existed (41 failed on the planned assertions, the tag came out `untagged` or the tracer tag instead of the planned one).
- Task 3: `pytest python/tests/test_selftests.py -q --durations=3` 184 passed in 19.27 s for the module; the module-scoped battery fixture (18 kinds by 90 ground truths, 1,620 diffs) is 12.75 s of that, the 154 ground-truth cases and the row tests the rest.
- `just py-check` exit 0, 3,086 passed (baseline 2,857 before this plan's test files).
- Acceptance: `set(CORRUPTIONS) == set(TAG_ORDER) - {"schema_invalid"}`; corruptions.py and tags.py import no HTTP client; the pins 117, 128 and 41 are literals in test_selftests.py and the module has no sampling branch.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] A merge or split rule would have tagged a zero-valued row**
- **Found during:** Task 2 (design of the rules)
- **Issue:** The plan asks for `p[col] == t[col] + u[col]` with `t[col]` non-zero. If the unmatched true row `u` has a zero total or quantity, every matched pair satisfies it, so any dropped zero row would be tagged `merged_rows` (and an extra row with a zero value `split_rows`).
- **Fix:** `merged_rows` also requires `u[col]` non-zero, `split_rows` requires `q[col]` non-zero. The seeded corruptions and every pin hold unchanged (the merged corruption already declares itself not applicable on a zero total).
- **Files modified:** python/src/carimbo_evals/tags.py (test `test_a_row_that_matches_nothing_in_the_answer_is_not_called_merged_or_split`)
- **Commit:** 056dd91

**2. [Rule 1 - Bug] A party that equals its own counterpart would show a false crossover**
- **Found during:** Task 2
- **Issue:** The crossover test "predicted issuer equals expected recipient" also fires when the expected issuer and recipient values are equal, where no crossover happened.
- **Fix:** The crossover requires the two expected values to differ.
- **Files modified:** python/src/carimbo_evals/tags.py
- **Commit:** 056dd91

**3. [Plan shape] `diff_invoice` also returns the graded `invoice`**
- **Issue:** The plan lists `fields`, `wrong`, `items` and `installments`; `grade_case` still needs the graded invoice (empty for a `schema_invalid` answer) for `total_delta`.
- **Fix:** One extra key, `invoice`. No consumer of the plan's four keys is affected.
- **Commit:** eda34e4

**Process note:** For Task 1 the grader change and `tags.py` were written before the tracer tests were run, so there is no separate RED run for the tracer; Task 2 followed RED then GREEN. Two of my own test constants were wrong at first (a transposed `unit_price` that is also a thousands misparse, and a similarity pair that was 0.909 rather than just below 0.9) and were corrected in the tests; no rule was changed to fit them.

**Total deviations:** 2 rule tightenings (Rule 1), 1 return-shape addition. **Impact:** none on any pin or on the measured counts; no proof was relaxed.

## Issues Encountered

None. The prototype's numbers (ground truth 154 of 154, 18 kinds each producing exactly its tag, applicable counts 117, 128 and 41) reproduced on the real code without a pin change.

## Known Stubs

None.

## Threat Flags

None. T-05-07 is accepted as planned (SequenceMatcher runs only on name, description, code and unit strings of a bounded model output). T-05-08 is mitigated: `test_tag_order_is_pinned` and `test_every_tag_has_exactly_one_predicate` make any change to the list a visible diff; no recipe runs the test split.

## Requirements

EVAL-04 and EVAL-07 are both declared by later plans of this phase that have no SUMMARY yet, so `requirements.ready-ids` reports both as blocked and nothing was marked complete in REQUIREMENTS.md. Each is marked when its last declaring plan finishes.

## Next Phase Readiness

Every wrong value in `summary.json` cases now carries `tag` and `rule`, and each case carries `failure_tags`; the failure-tag counts and the `untagged` bucket can be aggregated by plan 05-05 onward. The catalogue is ready for the e2e proof of plan 05-10. The `untagged` review against real failures belongs to plan 05-12, and the freeze to plan 05-13.

## Self-Check: PASSED

- Files exist: tags.py, corruptions.py, test_tags.py, test_selftests.py (checked).
- Commits `eda34e4`, `056dd91`, `21a7dad` are ancestors of HEAD.
