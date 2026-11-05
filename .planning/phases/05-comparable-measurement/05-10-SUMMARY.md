---
phase: 05-comparable-measurement
plan: 10
subsystem: evals
tags: [e2e, scripted-host, full-dataset, compare, silent-errors, d-20, offline-gate]

requires:
  - phase: 05-comparable-measurement
    provides: the corruption catalogue (05-04), summary v4 and the grader (05-05 to 05-07), compare (05-08), run --split and grade --out (05-09)
provides:
  - python/tests/test_e2e_full_dataset.py, three e2e tests over all 154 dataset cases against the scripted model
  - CHECK_SHA and the budget line that plan 05-11's spend approval relies on
affects: [05-11, 05-13, phase-06]

actuals:
  tokens: 4400
  tasks: 3
  commits: 2
plan_head_before: 107939fcf1f1fd6d7d27e9bbab8b0c52a8e48540
plan_head_after: 127c922c2875ab11fae73a1df56c0d23df9ebb8f

tech-stack:
  added: []
  patterns:
    - "A module-scoped scripted host (one 2.7 s start) whose <sha256 of the PDF>.json answers are rewritten between runs"
    - "Seeded answers come from corruptions.corrupted(), so the e2e proof and the pytest self-tests share one catalogue"

key-files:
  created:
    - python/tests/test_e2e_full_dataset.py
  modified: []

key-decisions:
  - "Every scripted run passes --max-cost-usd 100 and --cache-mode off: at scripted usage a full run with repairs exceeds the default US$1.00 cap"
  - "Expected statuses are asserted per case (not in aggregate), so a validator change that moves a kind between silent and caught fails the test by design and needs a review of D-05"

patterns-established:
  - "The offline gate of a phase is recorded as a commit hash (CHECK_SHA) in the SUMMARY of the plan that closes it"

requirements-completed: []

coverage:
  - id: D1
    description: "All 154 cases answered with ground truth over HTTP grade to a full summary: six headline rows 60/60, 40/5, 30/30, 24/3, 90/90, 64/8, rate 1.0 with ci95 upper bound 1.0, latency, cache and cost totals"
    requirement: EVAL-05
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_full_dataset.py#test_ground_truth_for_all_154_cases_grades_to_a_full_summary"
        status: pass
    human_judgment: false
  - id: D2
    description: "Seeded answers get the status the real validators give them, with tags, silent-error split, failure-tag totals, typed failures kept in or out of the denominator, and an incomplete run flagged"
    requirement: EVAL-04
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_full_dataset.py#test_seeded_answers_prove_summary_tags_silent_errors_and_compare"
        status: pass
    human_judgment: false
  - id: D3
    description: "compare lists exactly the seeded cases as regressed, nfe-0005 as not comparable, verdict significant on the test text row; a regrade at another tolerance is refused with exit 2"
    requirement: EVAL-08
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_full_dataset.py#test_seeded_answers_prove_summary_tags_silent_errors_and_compare"
        status: pass
    human_judgment: false
  - id: D4
    description: "run --split dev over HTTP records exactly the 54 dev cases; the summary has a dev row only"
    requirement: EVAL-05
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_full_dataset.py#test_dev_split_runs_only_the_dev_cases"
        status: pass
    human_judgment: false
  - id: D5
    description: "Every offline gate green on a named commit before the spend is requested"
    verification:
      - kind: integration
        ref: "devenv shell -- just check (exit 0 at 127c922c2875ab11fae73a1df56c0d23df9ebb8f)"
        status: pass
    human_judgment: false

duration: 11min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 10: the full-dataset summary proven offline over real HTTP Summary

**One e2e module drives the documented `run`, `grade` and `compare` commands over all 154 dataset cases against the scripted model, with ground-truth and seeded answers, and proves summary rows, statuses, failure tags, the silent-error split and `compare` together at no cost; `just check` is green on commit `127c922c2875ab11fae73a1df56c0d23df9ebb8f`.**

## Performance

- **Duration:** 11 min (22:22Z to 22:33Z)
- **Tasks:** 3 (one tracer, two auto)
- **Files:** 1 created (python/tests/test_e2e_full_dataset.py, 17.4 kB), none modified

## CHECK_SHA and the budget line (read by plan 05-11 Task 1)

- **CHECK_SHA:** `127c922c2875ab11fae73a1df56c0d23df9ebb8f`. This is the output of `git rev-parse HEAD` after `devenv shell -- just check` exited 0 (all gates in the chain: dotnet-check, py-check with 3231 passed and 16 deselected, schema-check, datagen-check, dataset-check, decision-cases-check, the 16 e2e tests, docs-check, secrets-check). `git status --porcelain -- . ':(exclude).planning'` printed nothing before and after the run, so the regenerated artifacts were byte-identical to the committed ones. The only commits after CHECK_SHA are this SUMMARY and the STATE/ROADMAP update, which touch `.planning` only.
- **Budget line:** phase spend so far US$0, per-run cap US$2.50, phase cap US$5, estimate US$1.2 to 2.2 on `claude-haiku-4-5` (rough, from 3 skeleton cases).
- **How measured:** `just check` ran in the foreground of this session (3 min 45 s wall). The cap and the recipe chain were read from the justfile, not from memory: `just --dry-run dev-split` prints `_dev-split-run 2.50 read_write` (count 1), and the `_dev-split-run` body passes `run --cases data/dataset --split dev ... --max-cost-usd {{ max_cost }} --cache-mode {{ cache_mode }}` and then `grade --run ... --cases data/dataset`. Provider keys, presence only: `CARIMBO_ANTHROPIC_API_KEY` unset and `ANTHROPIC_API_KEY` unset, as expected, since the human runs the paid step in plan 05-11. `just dev-split` was not run; `ls evals/runs` holds no `dev-split-*` directory.

## Accomplishments

- Tracer: with ground-truth answers all 154 cases came back over HTTP (`concurrency 4`, `--max-cost-usd 100`, `--cache-mode off`) and `grade --resamples 500` wrote a summary with `counts.success` 154, every case correct, and the six headline rows equal to the committed dataset: test text 60/60, test image 40/5, dev text 30/30, dev image 24/3, full text 90/90, full image 64/8 (cases/invoices). `headline.test.text` has rate 1.0 and `ci95[1]` 1.0; `incomplete` is False; `failure_tags.wrong_values` 0; `slices.test["rendering=rotation/mild"].cases` 5; `items.full.recall.value` 1.0; model latency p50 and p95 1234; `cache.answered_attempts` 154; `totals.cost_usd` above 0; `dataset.version` `dataset-001`. It passed first time in 6 s, with a plain `pytest python/tests -q` deselecting it.
- Seeded run (`run-seeded`): cases sorted by id take `ROTATION[index % 10]` (ground truth, the four silent kinds, the five caught kinds); `nfe-0002` refused, `nfe-0003` truncated (`max_tokens`, `{`), `nfe-0004` a `digit_count` corruption (schema_invalid), `nfe-0005` a scripted `server_error` 500. Per case, the status equals the expected one, a ground-truth case is correct, a seeded case has `failure_tags == [tag]`, `nfe-0004` has `["schema_invalid"]`, refused and truncated have no wrong values and are not correct, and `nfe-0005` has `correct` null.
- **Status counts of `run-seeded`:** success 75, validation_failed 75, refused 1, truncated 1, schema_invalid 1, infrastructure_failure 1 (total 154). Seeded tags: date_wrong 14; digit_substitution, digit_transposition, invented_row, tax_code_wrong, text_near_miss, text_wrong, thousands_misparse and value_from_other_box 15 each; 16 ground-truth cases among the 75 successes. `validation.caught` is 75, `headline.full.pooled` is 153 cases with 1 excluded, `incomplete` is True with `incomplete_detail.infrastructure_failure` 1. `silent_errors.overall`: success_cases 75, silent 59, payment 14, other 45. `wrong_on_success` has neither `totals.invoice_total` nor `access_key`.
- `compare --first run-truth --second run-seeded`: exit 0; `regressed` lists the 137 cases that are seeded or overridden except `nfe-0005`; `not_comparable` is `["nfe-0005"]`; `improved` is empty; verdict row `test text`, `significant`, c 0; the printed report contains `case sampling only`. A regrade of the seeded run at `--tolerance 0.01` into a new directory, compared against the truth run, exits 2 with `regrade` in stderr.
- Dev split: `run --split dev` recorded exactly the 54 manifest dev ids; `set(summary["headline"]) == {"dev"}`; text 30/30 and image 24/3.
- This module is also the tracer for the live recipe's command line: it runs the same `run --split dev` and `grade` calls the recipe makes.

## Timing

- The module alone: 3 passed in 16.8 s (host start 2.7 s once, ground truth run 3.3 s, seeded test 8.9 s; the dev-split test is the remainder).
- `just e2e`: 16 passed in 54.6 s, against a 13-test baseline of about 60 s, so the module adds no measurable time beyond the plan's 15 s allowance.

## Task Commits

1. **Task 1 (tracer): ground-truth answers for all 154 cases grade to a full summary** - `2152075` (test)
2. **Task 2: seeded answers through the real validators, typed failures, compare and a dev-split run** - `127c922` (test)
3. **Task 3: every offline gate green and the recipe confirmed** - no file change, so no commit; its evidence is CHECK_SHA above

## Verification

- `devenv shell -- uv run --project python pytest python/tests/test_e2e_full_dataset.py -q -m e2e --durations=3`: 3 passed.
- `devenv shell -- just e2e`: 16 passed, 3231 deselected.
- `devenv shell -- just check`: exit 0 on `127c922`.
- Acceptance: the module has 3 tests under a module-level `pytest.mark.e2e` (plain `pytest python/tests/test_e2e_full_dataset.py -q` deselected the module at Task 1; the default run deselects the e2e marker); `corrupted(` is called in the module (2 occurrences, one in the definition of `seeded_answers` and one in the docstring reference) and no corruption logic is inline; the run commands pass `--max-cost-usd 100` and `--cache-mode off`; summary.json is read from the run directory, written by the console script. `ruff check`, `ruff format --check` and `pyright -p python` are clean.
- Plan-end gate: `just check` exits 0 on CHECK_SHA.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- `ruff check --fix` removed the `Counter` import while only the tracer existed; Task 2 restored it. No other friction.

## Known Stubs

None.

## Threat Flags

None. T-05-21 is mitigated: the module starts `Carimbo.ScriptedHost` only, no provider key is set for it (`CARIMBO_ANTHROPIC_API_KEY` and `ANTHROPIC_API_KEY` were both unset in this session), and CI has none. T-05-22 is mitigated: seeded answers come from `corruptions.corrupted()` and the expected status is asserted per case. T-05-SC: no package added.

## Requirements

EVAL-04, EVAL-05, EVAL-06, EVAL-07 and EVAL-08 are exercised by this plan and none was marked complete: `requirements.ready-ids` reports all five blocked, because sibling plans (05-11, 05-12 and 05-13 declare them too) have no SUMMARY yet.

## Next Phase Readiness

Plan 05-11 can ask for the spend (`just dev-split`, per-run cap US$2.50, phase cap US$5, nothing spent so far) knowing every offline gate is green on `127c922c2875ab11fae73a1df56c0d23df9ebb8f`. The README narrative for the full-dataset proof belongs to plan 05-13.

## Self-Check: PASSED

- File exists: python/tests/test_e2e_full_dataset.py.
- Commits `2152075` and `127c922` are ancestors of HEAD; `git rev-list --count` from the ledger base gives 2.
