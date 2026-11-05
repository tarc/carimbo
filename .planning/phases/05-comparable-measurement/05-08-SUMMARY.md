---
phase: 05-comparable-measurement
plan: 08
subsystem: evals
tags: [compare, mcnemar, refusals, summary-json, eval-08]

requires:
  - phase: 05-comparable-measurement
    provides: mcnemar_exact and is_significant (05-05), summary v4 case records with wrong values and tags (05-04 to 05-06)
provides:
  - compare.py COMPARE_VERSION, MAX_SUMMARY_BYTES, REGRADE_HINT, CompareRefusal, load_summary, check_comparable, compare_summaries, render_markdown, write_comparison
  - the `carimbo-evals compare --first --second [--out]` command, exit 0 whenever the comparison ran and 2 on a refusal, with no override option
  - compare.json and compare.md with verdict, regressed, worsened, improved, not_comparable, changed, field_deltas
affects: [05-09, 05-10, 05-13, phase-06]

actuals:
  tokens: 11000
  tasks: 3
  commits: 3
plan_head_before: 9610b47fcc68584cbbaee64d568de5fb3d22d096
plan_head_after: a3b2e8a3a57da032ffc86d201326b98343f16c80

tech-stack:
  added: []
  patterns:
    - "A pure module reads untrusted summary files: size check before parsing, a fixed-order shape validation, every failure one exception type mapped to exit 2"
    - "A verdict is decided on an exact Fraction through is_significant; the float in the output is a rendering only"

key-files:
  created:
    - python/src/carimbo_evals/compare.py
    - python/tests/test_compare.py
  modified:
    - python/src/carimbo_evals/cli.py

key-decisions:
  - "The regrade hint reads `carimbo-evals grade --run RUN --cases CASES`: the grade command has no --out option, and it writes the summary next to the run"
  - "Field deltas count, per run, the cases that run graded among the shared comparable cases, and print n for both, so a run that refused a case is not credited with wrong fields it never produced"
  - "The verdict carries a `reason` for every outcome (not only the no-row one) so the printed report always says why"

patterns-established:
  - "compare reads only summary.json: removing cases.jsonl from both run directories changes nothing"

requirements-completed: []

coverage:
  - id: D1
    description: "compare reads two summary.json files, names regressed cases with the flipped values and their tags, and works without cases.jsonl"
    requirement: EVAL-08
    verification:
      - kind: unit
        ref: "python/tests/test_compare.py#test_tracer_compare_names_a_regressed_case_and_its_flipped_value"
        status: pass
      - kind: unit
        ref: "python/tests/test_compare.py#test_tracer_compare_needs_no_cases_jsonl"
        status: pass
    human_judgment: false
  - id: D2
    description: "Refusal contract: manifest hash, grader version, tolerance (as Decimal), decision summaries, pre-version-4 summaries and malformed or oversized files exit 2 with a named reason, and the command has no override"
    requirement: EVAL-08
    verification:
      - kind: unit
        ref: "python/tests/test_compare.py#test_a_hostile_or_malformed_summary_is_refused_without_a_traceback"
        status: pass
      - kind: unit
        ref: "python/tests/test_compare.py#test_the_command_has_no_override_option"
        status: pass
    human_judgment: false
  - id: D3
    description: "Exact McNemar verdict on the text-layer row (5 discordant one way is p 1/16 inconclusive, 6 is 1/32 significant, exactly 1/20 is not significant), the four D-17 case lists, changed config and descriptive field deltas, with the case-sampling-only scope sentence in every report"
    requirement: EVAL-08
    verification:
      - kind: unit
        ref: "python/tests/test_compare.py#test_the_exact_mcnemar_verdict_at_its_boundary"
        status: pass
      - kind: unit
        ref: "python/tests/test_compare.py#test_regressed_worsened_and_improved_follow_the_case_rules"
        status: pass
      - kind: integration
        ref: "devenv shell -- just py-check"
        status: pass
    human_judgment: false

duration: 8min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 08: compare two graded runs case by case Summary

**`carimbo-evals compare` reads two summary.json files and prints which cases regressed, worsened or improved with the values that flipped and their tags, plus an exact McNemar verdict on the text-layer test cases, and refuses with exit 2 (no override) any pair that differs in dataset manifest, grader version or tolerance, any decision or pre-version-4 summary, and any malformed or oversized file.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-11-05T22:00:46Z
- **Completed:** 2026-11-05T22:08:00Z
- **Tasks:** 3 (one tracer, two auto, all tdd)
- **Files:** 3 (2 created, 1 modified)

## Accomplishments

- Tracer: two graded runs over `data/dataset`, the second with a wrong invoice total, give `regressed` with `nfe-0002`, `totals.invoice_total became_wrong (digit_substitution)`, exit 0. The same output with `cases.jsonl` deleted from both runs. A forged `grader-000` exits 2 naming both versions and `regrade both runs with the current grader`.
- Refusal contract in the order of D-18: root object, decision summary (`compare covers extraction runs only`), older summary (`REGRADE_HINT`), unknown version, string `grader_version` and `run_id`, a finite Decimal tolerance, a dataset with a string manifest hash (null refused), `config` object, `cases` list with unique string ids, `correct` true/false/null and `wrong` entries with string paths. `MAX_SUMMARY_BYTES` (64 MiB) is checked before parsing. 20 hostile shapes, a missing path, invalid JSON, an empty directory and the size limit all exit 2 with no traceback, in both argument orders. The committed decision and grader-002 fixtures are refused and `git status --porcelain evals/results` stays empty.
- `check_comparable` compares the manifest hash, then the grader version, then the tolerance as `Decimal` (`0` equals `0.00`).
- Verdict: row `test text` when shared comparable test text-layer cases exist, else the text-layer splits joined and labelled; image-only cases never enter. `p` is a `Fraction` decided by `is_significant` only; `float(` appears once, for the display `p_value`. Boundaries tested: 5 one way is `1/16` inconclusive, 6 is `1/32` significant (both directions), 3 and 3 is `1` inconclusive, and a patched `Fraction(1, 20)` is inconclusive.
- Case lists per D-17, sorted by case id with flipped values sorted by path; equal non-zero wrong counts in both-wrong cases appear in no list; a null `correct` in either run goes to `not_comparable` only and out of the McNemar counts and field deltas. Swapping the runs swaps regressed with improved and b with c.
- `changed` (model, prompt, repair budget, cache mode, salts, schema hash, pricing) prints only differing keys; different case sets compare on the intersection and print `excluded`.
- Field deltas cover the 27 header fields and every item and installment column, descriptive only.
- The report prints the scope sentence `case sampling only, not run-to-run model variation` in every verdict.

## Task Commits

1. **Task 1 (tracer): compare prints a regressed case and refuses a different grader version** - `adb7137` (feat)
2. **Task 2: the whole refusal contract with shape validation** - `f86a49f` (feat)
3. **Task 3: McNemar verdict, case lists, changed config, field deltas and the report** - `a3b2e8a` (feat)

## Files Created/Modified

- `python/src/carimbo_evals/compare.py` - the pure comparison module (stdlib, `stats`, `summary` and `matching` constants only; no HTTP client, never opens `cases.jsonl`)
- `python/src/carimbo_evals/cli.py` - the `compare` command and the docstring exit codes
- `python/tests/test_compare.py` - 30 tests: real graded runs for the tracer, in-memory summaries for the rest

## Decisions Made

- The regrade hint omits the plan's `--out DIR`, because `carimbo-evals grade` has no such option.
- Field deltas count each run's own graded cases among the shared comparable ones and print both `n`.
- The verdict `reason` is always present.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The regrade hint named a CLI option that does not exist**
- **Found during:** Task 1
- **Issue:** the plan's `REGRADE_HINT` ends in `--out DIR`, but `carimbo-evals grade` takes `--run`, `--cases`, `--tolerance`, `--schema` and writes next to the run, so the advice would fail with a usage error.
- **Fix:** the hint is `regrade both runs with the current grader: carimbo-evals grade --run RUN --cases CASES`. The required sentence is unchanged.
- **Files modified:** python/src/carimbo_evals/compare.py
- **Committed in:** adb7137

---

**Total deviations:** 1 auto-fixed (1 bug in the plan's text)
**Impact on plan:** none on behavior; the printed advice now works when followed.

## Issues Encountered

- My first tracer assertion expected the tag `untagged`; the +1 corruption of the invoice total classifies as `digit_substitution`, so the test now asserts the tag the grader assigns.
- Task 2 was committed with one pyright error (a `str | None` set element); fixed and amended into the same unpushed commit before moving on. The final `just py-check` is clean.

## Known Stubs

None.

## Threat Flags

None. T-05-15 is mitigated (shape validation up front, one exception type, exit 2, no eval, no pickle; 20 hostile shapes tested). T-05-16 is mitigated (`MAX_SUMMARY_BYTES` before parsing, work bounded by the case intersection; tested by patching the limit to 10). T-05-17 is mitigated (refusal on manifest, grader and tolerance; a parameter-set test shows the command has exactly `first`, `second`, `out`). T-05-SC: no package added.

## Requirements

EVAL-08 is delivered by this plan but not marked complete: `requirements.ready-ids` reports it blocked, because plans 05-10 and 05-13 also declare it and have no SUMMARY yet (05-02 already has one).

## Verification

- `pytest python/tests/test_compare.py -q`: 30 passed (the tracer feedback gate was an automated verify and was re-run before expansion).
- `devenv shell -- just py-check`: exit 0, 3217 passed, 13 deselected. `ruff check`, `ruff format --check` and `pyright -p python` clean.
- Acceptance: `float(` appears once in compare.py (the display value); compare.py imports no HTTP client; `compare.json` written with `--out` holds `verdict`, `regressed`, `worsened`, `improved`, `not_comparable`, `changed`, `field_deltas`.

## Next Phase Readiness

Phase 6's gate can build on the exit codes (0 compared, 2 refused) and on `compare.json`. Plan 05-10's end-to-end proof can call `compare` on two graded real runs. A README paragraph describing `compare` belongs to the phase's last plan.

## Self-Check: PASSED

- Files exist: compare.py, test_compare.py, cli.py (checked).
- Commits `adb7137`, `f86a49f` and `a3b2e8a` are ancestors of HEAD; `git rev-list --count` from the ledger base gives 3.
