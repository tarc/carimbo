---
phase: 02-validated-extraction
plan: 06
subsystem: evals
tags: [grader, summary, validation_failed, attempts, findings, eval-contract-2, offline]

requires:
  - phase: 02-validated-extraction
    provides: "02-01 grader v2 (GroundTruth, 27 FIELDS, GRADED_STATUSES); 02-02 contract 2 response shape (outcome.findings, response.attempts) recorded in D-24"
provides:
  - "validation_failed graded on its candidate invoice (all 27 fields) and counted as caught, never as success"
  - "Per-case caught, validator {errors, warnings, rule_ids}, attempt_count, attempt_statuses on every graded case"
  - "summary.json validation block {caught, with_warnings, rule_counts} and attempts block {total, repaired, max}"
  - "summary.md attempts and findings columns and the validation_failed (caught) header line"
affects: [02-07, 02-08, 02-09, 02-10, phase 5 EVAL-06]

plan_head_before: 96349aa59ba863bfde8e82cb9740cdb9b9ad09fc
plan_head_after: c04c4277c318248a6ca011a4eaa798bb66a832ce

estimate:
  tokens: 45000
  raw_tokens: 45000
  tasks: 2
  confidence: low
actuals:
  tokens: 5000
  tasks: 2
  commits: 4

tech-stack:
  added: []
  patterns:
    - "Offline grader reads only the stored record: findings and attempts are optional members, so a contract 1 record grades with attempt_count null and a zero validator block"
    - "Typed failures (refused, truncated, infrastructure_failure, harness_error) carry no field grades; validation_failed is graded on its candidate and flagged caught"
    - "Run aggregates ignore cases without an attempt count instead of guessing one"

key-files:
  created: []
  modified:
    - python/src/carimbo_evals/grader.py
    - python/src/carimbo_evals/summary.py
    - python/tests/test_grader.py

key-decisions:
  - "validation_failed sits in both OUTCOME_STATUSES and GRADED_STATUSES, so its candidate enters the field_accuracy and schema_validity denominators (it is a wrong-or-right answer that the validators caught), while counts keep it separate from success"
  - "validator.rule_ids are per case sorted and unique; summary rule_counts count cases (not findings) per rule id, keys sorted, so the JSON stays deterministic"
  - "The findings column renders 0E/0W for any case that has a validator block (including harness errors) and - only when the block is absent, following the plan literally"
  - "GRADER_VERSION grader-002 and SUMMARY_VERSION 2 unchanged (set by 02-01, still within the unreleased phase)"

patterns-established:
  - "_v2_record test helper wraps _record with findings and attempts for contract 2 grading tests"

requirements-completed: [EXT-04, API-01]

coverage:
  - id: D1
    description: "A validation_failed record is graded on its candidate (27 fields), counted under validation_failed and as caught, never as success"
    requirement: EXT-04
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_validation_failed_candidate_is_graded_on_all_fields_and_counted_as_caught"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_validation_failed_is_caught_and_graded_while_typed_failures_stay_ungraded"
        status: pass
    human_judgment: false
  - id: D2
    description: "Every graded case records validator outcome and attempt count and statuses; contract 1 records grade with attempt_count null; malformed members never crash the grader"
    requirement: API-01
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_repaired_success_is_not_caught_and_keeps_its_attempt_history"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_contract_1_record_without_findings_or_attempts_grades_with_null_attempts"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_malformed_findings_and_attempts_members_never_crash_the_grader"
        status: pass
    human_judgment: false
  - id: D3
    description: "Refusals, truncations, infrastructure failures and harness errors still carry no field grades"
    requirement: EXT-04
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_refused_attempt_carries_no_field_grades_but_keeps_attempts_and_a_zero_validator"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_typed_failures_are_counted_but_never_graded_as_wrong"
        status: pass
    human_judgment: false
  - id: D4
    description: "summary.json validation and attempts blocks and summary.md attempts/findings columns and caught line"
    requirement: API-01
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_summary_aggregates_what_the_validators_caught_and_what_repair_cost"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_summary_markdown_shows_attempts_findings_and_the_caught_line"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_summary_json_stays_sorted_and_deterministic_with_the_new_blocks"
        status: pass
      - kind: other
        ref: "devenv shell -- just py-check"
        status: pass
    human_judgment: false

duration: 5min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 06: the offline grader understands validator outcomes and attempts Summary

**Offline grader and summary now grade validation_failed candidates as caught (never success), record per-case validator findings and attempt counts from contract 2 records, and aggregate caught, per-rule and repair-attempt blocks into summary.json and summary.md**

## Performance

- **Duration:** 5 min
- **Started:** 2026-10-08T15:04:05Z
- **Completed:** 2026-10-08T15:08:56Z
- **Tasks:** 2 (Task 1 tracer, Task 2 auto; both TDD)
- **Files modified:** 3

## Accomplishments
- `validation_failed` joins `OUTCOME_STATUSES` and `GRADED_STATUSES`; its candidate `outcome.invoice` is graded exactly like a success (27 fields, total delta, both schema validity grades) and flagged `caught`.
- Every case dict (graded or not) carries `caught`, `validator` (`errors`, `warnings`, sorted unique `rule_ids` from `outcome.findings`), `attempt_count` and `attempt_statuses` (from `response.attempts`); a contract 1 record gives `attempt_count` null and a zero validator block, and malformed members are ignored without crashing.
- `summary.json` gains a `validation` block (`caught`, `with_warnings`, `rule_counts`) and an `attempts` block (`total`, `repaired`, `max`); `summary.md` gains `attempts` and `findings` columns (`2E/1W`) and a `validation_failed (caught): N` header line.
- Typed failures stay out of the wrong-answer denominators, so caught, wrong and typed-failure counts remain separate (input to EVAL-06).

## Task Commits

Each task was committed atomically (TDD: RED then GREEN):

1. **Task 1: validation_failed, findings and attempts through grade_run (tracer)**
   - `c215db9` test (RED, 8 failing tests)
   - `62829e0` feat (GREEN)
2. **Task 2: run-level validation and attempts aggregates**
   - `d76fe0c` test (RED, 5 failing tests)
   - `c04c427` feat (GREEN)

**Plan metadata:** see the docs(02-06) commit following this summary.

## Files Created/Modified
- `python/src/carimbo_evals/grader.py` - `_validator_block`, `_attempt_statuses`, validation_failed grading and the four new per-case keys
- `python/src/carimbo_evals/summary.py` - `validation_failed` status, `_validation` and `_attempts` aggregates, new markdown columns and caught line
- `python/tests/test_grader.py` - `_finding` and `_v2_record` helpers, 13 new tests; `test_typed_failures_are_counted_but_never_graded_as_wrong` counts dict gains `validation_failed: 0`

## Decisions Made
See `key-decisions` in the frontmatter. In short: the validation_failed candidate enters field and schema denominators but is counted separately from success; rule counts are per case; the findings column shows `0E/0W` whenever a validator block exists.

## Deviations from Plan

None - plan executed exactly as written.

The tracer feedback gate ran in `end-of-phase` mode with an automated-only `<verify>`: the tracer's tests (grade_run over validation_failed, repaired and typed-failure records) were re-run and passed, so expansion to Task 2 proceeded. The one existing test that asserts the full `counts` dict was updated for the new status key, which the plan's status-vocabulary change implies.

**Total deviations:** 0
**Impact on plan:** None.

## Issues Encountered
- `python3` is not on PATH in this environment, so the first scripted edit failed; edits were redone with the Edit tool. A pyright typing error in a test helper (findings parameter typed too narrowly for a deliberately malformed list) was fixed before the Task 1 GREEN commit.

## Known Stubs

None.

## Threat Flags

None. The grader still imports no HTTP client (existing offline subprocess test passes); no package was added and `python/uv.lock` is unchanged (T-02-19, T-02-20, T-02-SC mitigated).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Offline grading understands the contract 2 vocabulary; 02-07 onwards can rely on `caught`, `validator`, `attempt_count` and the `validation` and `attempts` summary blocks.
- Verification: `devenv shell -- just py-check` exits 0 (265 passed, 4 deselected). `just e2e` was not run here (it needs the .NET scripted host and is not part of this plan's verification).

## Self-Check: PASSED

- Modified files exist: grader.py, summary.py, test_grader.py (FOUND).
- Commits c215db9, 62829e0, d76fe0c, c04c427 are ancestors of HEAD (FOUND); `git rev-list --count` from the plan base gives 4.
- Acceptance criteria re-run: grep counts 2/4/0 (Task 1) and 4/4 (Task 2) meet the minimums; py-check green.

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
