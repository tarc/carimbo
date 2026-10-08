---
phase: 01-walking-skeleton
plan: 15
subsystem: testing
tags: [eval-runner, cost-cap, jsonl, httpx2, pytest, gap-closure]

requires:
  - phase: 01-walking-skeleton
    provides: "Plan 01-09 runner, grader and CLI (cost cap, resume, offline grading) and plan 01-13 py-check gate"
provides:
  - "Cost cap that charges reserve_usd for every harness error that may have reached the provider, live and for every prior record on resume"
  - "assumed_usd in RunReport, run.json and the CLI summary line, separate from spent_usd"
  - "http.error_type in each record (additive, record_version stays 1)"
  - "Grader that reads cases.jsonl on the newline delimiter only, so raw U+2028, U+2029 and U+0085 no longer make a run ungradable"
  - "CorruptRunError: resume over a corrupt cases.jsonl exits 2 naming the line, leaves the file untouched, no traceback"
affects: [01-16, 06-reports, eval-runner, ci-eval-gate]

actuals:
  tokens: 9950
  tasks: 3
  commits: 6
plan_head_before: cec93c096acefe7fb45f760ad9e901fcd2a599d4
plan_head_after: 09d2cf895e3ff9391f0a7f49a3bff69d2508e378

tech-stack:
  added: []
  patterns:
    - "Conservative cost charging: a harness error is charged unless provably pre-provider (HTTP 400/401/404/413/415, or a connect/pool failure before the request was sent)"
    - "JSONL is split on the writer's own delimiter (newline), never on Unicode line separators"
    - "Validate the whole file before repairing a torn tail, so a corrupt file is never modified"

key-files:
  created: []
  modified:
    - python/src/carimbo_evals/runner.py
    - python/src/carimbo_evals/cli.py
    - python/src/carimbo_evals/grader.py
    - python/tests/test_runner.py
    - python/tests/test_grader.py

key-decisions:
  - "A harness error is charged reserve_usd against the cap unless provably pre-provider; records written before error_type existed are charged unless their status is a pre-provider one"
  - "assumed_usd is reported next to spent_usd and never folded into it, so spent stays money the endpoint priced"
  - "CorruptRunError is caught in the CLI together with FileExistsError only; any other ValueError from a run stays visible as a bug"

patterns-established:
  - "may_have_reached_provider(record) is the single pure predicate for what the cap counts"
  - "Tests that care about serialization write fixtures with the production serializer (sort_keys, ensure_ascii=False)"

requirements-completed: [EVAL-01, EVAL-02]

coverage:
  - id: D1
    description: "A server error, timeout or protocol error after dispatch is charged the reserve against the cost cap, so repeated failures stop dispatch with cost_cap and CLI exit 3"
    requirement: EVAL-01
    verification:
      - kind: e2e
        ref: "python/tests/test_runner.py#test_cli_stops_at_the_cap_when_server_errors_may_have_cost_money"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_a_server_error_is_charged_the_reserve_against_the_cap"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_transport_errors_are_charged_unless_the_request_never_left_the_client"
        status: pass
    human_judgment: false
  - id: D2
    description: "Pre-provider rejections (400, 401, 404, 413, 415) and never-sent transport errors are not charged"
    requirement: EVAL-01
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_rejections_before_any_provider_call_are_not_charged"
        status: pass
    human_judgment: false
  - id: D3
    description: "On --resume every prior harness-error record that may have reached the provider is charged, not only the last per case"
    requirement: EVAL-01
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_resume_charges_every_prior_harness_error_not_only_the_last"
        status: pass
    human_judgment: false
  - id: D4
    description: "run.json and the CLI summary line report assumed_usd separately from spent_usd"
    requirement: EVAL-01
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_run_json_describes_the_run"
        status: pass
      - kind: e2e
        ref: "python/tests/test_runner.py#test_cli_exits_0_when_every_case_completed"
        status: pass
    human_judgment: false
  - id: D5
    description: "carimbo-evals grade grades a run whose raw output contains U+2028, U+2029 or U+0085 written by the runner's serializer; torn final line ignored, corrupt middle line still exit 2"
    requirement: EVAL-02
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_raw_output_with_unicode_line_separators_is_graded"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_corrupt_middle_line_fails_grading_but_a_torn_final_line_is_ignored"
        status: pass
    human_judgment: false
  - id: D6
    description: "Resuming over a corrupt non-final line exits 2 naming the line, prints no traceback and leaves cases.jsonl byte-identical"
    requirement: EVAL-01
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_resume_with_a_corrupt_middle_line_raises_and_leaves_the_file_unchanged"
        status: pass
      - kind: e2e
        ref: "python/tests/test_runner.py#test_cli_resume_on_a_corrupt_run_exits_2_with_the_line_number"
        status: pass
    human_judgment: false

duration: 6min
completed: 2026-10-08
status: complete
---

# Phase 1 Plan 15: a cost cap that sees every possibly-paid call, and a grader and resume that survive real JSONL Summary

**Eval runner charges the reserve for every harness error that may have reached the provider (live and across resumes, reported as assumed_usd), the grader splits JSONL on the newline only, and resume over a corrupt cases.jsonl is exit 2 with the line number.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-10-08T03:17:05Z
- **Completed:** 2026-10-08T03:22:00Z
- **Tasks:** 3
- **Files modified:** 5

## Accomplishments

- WR-02 closed: `may_have_reached_provider(record)` decides what the cap counts. Only HTTP 400, 401, 404, 413 and 415 (answered before any provider call) and ConnectError, ConnectTimeout, PoolTimeout (the request never left the client) are free; everything else, including 5xx, read and write timeouts, protocol errors and HTTP 200 without a JSON body, is charged `reserve_usd`. The CLI tracer (4 cases, all HTTP 500, cap 0.10, reserve 0.05) now dispatches exactly 2 cases and exits 3 with `assumed=0.10`; before the fix it dispatched all 4 and exited 4.
- On `--resume`, `run_cases` charges every prior record of a selected case instead of only the last one (two earlier 500s with cap 0.14 now stop dispatch; a last-record-only implementation would dispatch).
- `assumed_usd` is a new `RunReport` field, a `run.json` key and an `assumed=` item in the CLI summary line. `http.error_type` (exception class name, or null for HTTP responses) is added to each record. `record_version` stays 1 and the grader ignores both additions.
- WR-01 closed: `_read_records` splits on `"\n"` only. The regression test serializes with the runner's exact options (`sort_keys=True`, `ensure_ascii=False`), asserts the file really contains a raw U+2028 and grades a run whose raw output and `http.error` carry U+2028, U+2029 and U+0085.
- WR-05 closed: `CorruptRunError(ValueError)` is raised by `_load_existing` for a complete line that is not a JSON object with a string `case_id`, naming the file and line. Validation runs before the torn-tail truncation, so a corrupt file is never modified. The CLI maps it to exit 2.

## Task Commits

Each task was committed atomically (TDD: RED then GREEN):

1. **Task 1 (tracer): server error charged against the cap, CLI to run.json to exit code**
   - RED `fe647a3` (test): 16 tests failing before the change
   - GREEN `5d7990d` (feat)
2. **Task 2: grader reads JSONL on the writer's newline delimiter**
   - RED `427c850` (test)
   - GREEN `4021173` (fix)
3. **Task 3: resume over a corrupt cases.jsonl is a usage error naming the line**
   - RED `70f91d1` (test)
   - GREEN `09d2cf8` (fix)

**Plan metadata:** recorded in the follow-up `docs(01-15)` commits (SUMMARY, then STATE/ROADMAP/REQUIREMENTS).

## TDD Gate Compliance

Every task has a `test(01-15)` commit before its `feat`/`fix(01-15)` commit. `workflow.tdd_mode` is not enabled in `.planning/config.json`, so `gsd_run check tdd-red-evidence` was not required; RED was assessed semantically from the pytest output.

- **Task 1 RED:** 16 failures. The charging tests failed on the planned behavior (the CLI tracer got exit 4 and 4 dispatches instead of exit 3 and 2; the unit tests failed on the missing `assumed_usd` field and `http.error_type` key). Valid RED: the target tests executed and failed on the planned assertions.
- **Task 2 RED:** `test_raw_output_with_unicode_line_separators_is_graded` failed with `JSONDecodeError: Unterminated string` (the `str.splitlines` split inside the string value), exactly the WR-01 symptom. The corrupt-middle/torn-final test passed on the old code by design: it pins behavior the fix must preserve, not new behavior.
- **Task 3 RED:** the three CLI cases failed with the real symptom (exit 1 from a raw JSONDecodeError, KeyError and TypeError). The in-process tests failed with `AttributeError: module 'carimbo_evals.runner' has no attribute 'CorruptRunError'`, a weaker RED because the exception class is the new contract; the CLI tests cover the behavioral half of the same requirement.

## Files Created/Modified

- `python/src/carimbo_evals/runner.py` - `NO_PROVIDER_CALL_STATUSES`, `UNSENT_ERROR_TYPES`, `may_have_reached_provider`, `CorruptRunError`, `RunReport.assumed_usd`, `http.error_type`, `_load_existing` returning all records with validation, charging of live and prior harness errors, `assumed_usd` in run.json
- `python/src/carimbo_evals/cli.py` - `assumed=` in the summary line; `CorruptRunError` caught next to `FileExistsError` and mapped to exit 2
- `python/src/carimbo_evals/grader.py` - `_read_records` splits on `"\n"` only
- `python/tests/test_runner.py` - WR-02 cap tests (live, parametrized statuses and exception classes, resume, CLI exit 3) and WR-05 corrupt-resume tests
- `python/tests/test_grader.py` - `_write_run` serializes like the runner; WR-01 separator test and corrupt/torn test

## Decisions Made

- Conservative charging: a harness error is charged unless provably pre-provider. Older records without `error_type` are charged unless their status is a pre-provider one.
- `assumed_usd` stays out of `spent_usd`, so spent remains money the endpoint priced.
- The CLI catches `CorruptRunError` only (with `FileExistsError`), not `ValueError`, so other bugs stay visible.
- An additional test (`test_a_corrupt_line_is_not_repaired_even_when_the_file_also_ends_torn`) pins that a torn tail is not truncated when an earlier line is corrupt.

## Deviations from Plan

None - plan executed exactly as written. One addition beyond the listed tests (the corrupt-plus-torn-tail test above) pins the "never modify a corrupt file" requirement; it needed no production change.

## Issues Encountered

- `python3` is not on the bare PATH; multi-line test edits were applied with a scratch script run through `devenv shell -- uv run --project python python`.

## Known Stubs

None.

## Threat Flags

None. The new `http.error_type` field and `assumed=` summary item add only an exception class name and a decimal amount (T-01-46, accepted); no dependency, lockfile or `record_version` change.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 01-16 (review disposition) can cite commits `fe647a3`, `5d7990d` (WR-02), `427c850`, `4021173` (WR-01), `70f91d1`, `09d2cf8` (WR-05).
- `just py-check` passes (144 passed, 3 deselected). WR-04 (datagen `--case` manifest) and IN-01..IN-07 remain open by plan scope.

## Self-Check: PASSED

- Modified files present: runner.py, cli.py, grader.py, test_runner.py, test_grader.py.
- Commits `fe647a3`, `5d7990d`, `427c850`, `4021173`, `70f91d1`, `09d2cf8` are ancestors of HEAD; `git rev-list --count` from the ledger base gives 6.
- All task acceptance criteria re-run and passing; `devenv shell -- just py-check` exits 0; `python/uv.lock` and `python/pyproject.toml` unchanged.

---
*Phase: 01-walking-skeleton*
*Completed: 2026-10-08*
