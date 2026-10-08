---
phase: 01-walking-skeleton
plan: 09
subsystem: evals
tags: [python, pydantic, datamodel-code-generator, typer, httpx2, jsonschema, eval-runner, grader, cost-cap]
status: complete

requires:
  - phase: 01-walking-skeleton
    provides: "Tracer runner/grader (01-03), canonical schema/invoice.schema.json (01-04), committed data/skeleton cases and manifest (01-07)"
provides:
  - "carimbo_models.generated.{Invoice, Party}: Pydantic 2 models generated from the canonical schema, extra=forbid, with a byte-for-byte freshness test in the default suite"
  - "carimbo_evals.runner.run_cases(..., max_cost_usd, reserve_usd, resume, on_record): bounded concurrency, Decimal cost cap, resume, run.json; RunReport{spent_usd, unpriced_cases, stopped_reason, skipped}"
  - "carimbo-evals run (exit 0/2/3/4) and carimbo-evals grade commands"
  - "carimbo_evals.grader: all D-04 field grades, normalize_name, independent jsonschema and strict-Pydantic validity grades, grade_run(..., schema_path)"
  - "carimbo_evals.summary: build_summary, write_summary -> summary.json (summary_version 1, grader_version grader-001) and summary.md"
affects: [01-10, 01-11, 01-12, 01-13]

actuals:
  tokens: 34650
  tasks: 3
  commits: 7
plan_head_before: aaaa51e3613ad5b7e1311641021e795aef1e02e4
plan_head_after: 4e1001fe56d22f719b357a43ce8f65772e6c33bb

tech-stack:
  added: []
  patterns:
    - "Typer app with an explicit @app.callback() so run and grade stay subcommands even before the second command exists"
    - "Record write and cost accounting happen with no await between them, so cancellation can only land between complete JSONL lines"
    - "Cost cap check uses spent + assumed-for-unpriced + max(reserve, largest cost seen) <= cap, evaluated after a free slot opens"
    - "Offline purity proven in a subprocess that replaces socket.socket and asserts httpx2 never enters sys.modules"

key-files:
  created:
    - python/src/carimbo_models/generated.py
    - python/src/carimbo_evals/cli.py
    - python/src/carimbo_evals/summary.py
    - python/src/carimbo_evals/money.py
    - python/tests/test_models.py
    - python/tests/test_runner.py
    - python/tests/test_grader.py
  modified:
    - python/src/carimbo_evals/runner.py
    - python/src/carimbo_evals/grader.py
    - python/tests/test_e2e_fake.py

key-decisions:
  - "Unpriced cases (null or unparsable cost_usd) are charged at reserve_usd against the cap but are not added to spent_usd; they are reported in unpriced_cases, so spent_usd is only money the endpoint actually priced"
  - "resume counts the cost of already-completed records against the cap; RunReport.completed/harness_errors/skipped describe the current invocation while spent_usd covers the whole run"
  - "A non-empty cases.jsonl without --resume raises FileExistsError (CLI exit 2) so a case can never get a second record by accident; resume truncates a torn final line before appending"
  - "Field names in the grader and summary are dotted (issuer.cnpj, recipient.name) to match the D-04 field list; schema validity is graded separately as schema_valid_jsonschema and schema_valid_pydantic, not as a field"
  - "grade_run returns the summary dict and no longer writes files; write_summary (called by the grade command) writes summary.json and summary.md"

patterns-established:
  - "Typed failures (refused, truncated, infrastructure_failure, harness_error) carry no field grades at all, so they cannot be counted wrong; schema_invalid is graded wrong on every field"

requirements-completed: [DOM-07, DOM-08, EVAL-01, EVAL-02]

coverage:
  - id: D1
    description: "Pydantic models generated from the canonical schema accept a valid invoice and reject extra fields, bad money, a 43-character key and a dotted CNPJ; regenerating reproduces the committed file byte for byte and the default suite fails when it is stale"
    requirement: "DOM-07"
    verification:
      - kind: unit
        ref: "python/tests/test_models.py#test_generated_models_are_fresh, #test_valid_invoice_is_accepted_in_strict_json_mode, #test_malformed_money_is_rejected, #test_extra_top_level_field_is_rejected"
        status: pass
    human_judgment: false
  - id: D2
    description: "Runner dispatch is bounded by --concurrency, stops before the first case that would exceed the Decimal cost cap (tested at exactly the cap and one cent over), exits 3, and sums costs without float"
    requirement: "EVAL-01"
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_dispatch_continues_when_spent_plus_reserve_equals_the_cap, #test_dispatch_stops_when_spent_plus_reserve_is_one_cent_over_the_cap, #test_costs_are_summed_as_decimal_never_float, #test_in_flight_requests_never_exceed_concurrency, #test_cli_exits_3_when_stopped_at_the_cost_cap"
        status: pass
    human_judgment: false
  - id: D3
    description: "Interrupted runs leave only complete JSONL lines; --resume re-runs harness errors, skips completed cases and never writes a second completed record; the eval key never reaches cases.jsonl or run.json"
    requirement: "EVAL-01"
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_cancelling_a_run_leaves_only_complete_json_lines, #test_resume_reruns_only_harness_errors_and_keeps_one_completed_record, #test_resume_repairs_a_torn_final_line, #test_key_never_reaches_disk_and_traceparents_are_unique"
        status: pass
    human_judgment: false
  - id: D4
    description: "Grader scores every D-04 field, normalizes printed CNPJs and pt-BR names, applies the Decimal tolerance inclusively, and grades schema validity independently with jsonschema and strict Pydantic"
    requirement: "EVAL-02"
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_matching_record_has_every_field_and_both_schema_grades_correct, #test_corrupted_fields_are_graded_wrong_and_only_that_field, #test_printed_cnpj_form_is_graded_equal_to_the_plain_form, #test_names_differing_only_in_form_case_spacing_or_punctuation_are_equal, #test_total_one_cent_off_is_correct_two_cents_off_is_wrong_and_zero_tolerance_is_exact"
        status: pass
    human_judgment: false
  - id: D5
    description: "Refusals, truncations, infrastructure failures and harness errors are counted but never graded as wrong; schema_invalid counts as wrong on every field (prohibition EVAL-02)"
    requirement: "EVAL-02"
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_typed_failures_are_counted_but_never_graded_as_wrong"
        status: pass
    human_judgment: false
  - id: D6
    description: "carimbo-evals grade re-grades a stored run offline with no network access and writes a deterministic summary.json and summary.md with field grades, tokens, cost and latency"
    requirement: "EVAL-02"
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_grading_a_stored_run_needs_no_network_and_imports_no_http_client, #test_summary_files_are_deterministic_apart_from_the_meta_block, #test_summary_carries_config_totals_latency_and_dataset, #test_grade_command_writes_both_files_and_prints_the_markdown"
        status: pass
    human_judgment: false
  - id: D7
    description: "The committed skeleton cases run end to end through the documented run and grade commands against the real ASP.NET host with a scripted model"
    requirement: "EVAL-01"
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#test_skeleton_cases_over_http_through_the_documented_commands"
        status: pass
    human_judgment: false
---

# Phase 1 Plan 09: Generated models, a budgeted resumable runner and an offline grader Summary

**Pydantic models generated from the canonical schema with a byte-freshness gate, a Decimal-capped resumable `carimbo-evals run`, and an offline `carimbo-evals grade` that scores every D-04 field plus independent jsonschema and strict-Pydantic validity grades.**

## Performance

- **Duration:** 11 min (18:41 to 18:52 UTC)
- **Tasks:** 3 of 3
- **Files:** 7 created, 3 modified

## Accomplishments

- `generated.py` comes from the research's codegen command through the locked environment: `Party` once (shared by issuer and recipient), `extra="forbid"` on both models, `total_amount` as an annotated `str` with the money pattern (the Money node needed no exporter change). A second codegen run leaves `git diff` empty, and `test_generated_models_are_fresh` regenerates into a temp file from the repo root and compares bytes.
- `run_cases` now dispatches sequentially with at most `--concurrency` requests in flight and a Decimal cost cap. The boundary tests: three 0.10 costs against a 0.30 cap dispatch exactly three cases; against 0.29 they dispatch two and report `cost_cap` (CLI exit 3). Null costs are charged at the reserve, resume counts prior spend against the cap, and a torn last line is repaired before appending.
- The grader scores `access_key`, `number`, `series`, `issue_date`, both CNPJs, both names and `total_amount`, and adds `schema_valid_jsonschema` and `schema_valid_pydantic` from `raw_output`. `schema_invalid` is wrong on every field; the four typed failure statuses carry no grades, so they stay out of the accuracy denominators while remaining in `counts`.
- `summary.json` is deterministic once `meta` is removed (config lists, counts, field accuracy, schema validity, token and cost totals, latency min/median/max, dataset block from the manifest), and `summary.md` has a header and a per-case table.
- The e2e test now drives the committed `data/skeleton` cases through `carimbo-evals run` and `carimbo-evals grade` as subprocesses against the scripted ASP.NET host: case-001 all nine fields correct, case-002 only `total_amount` wrong with delta 1.00, case-003 refused, and the key absent from every file in the run directory.

## Task Commits

1. **Task 1 RED: model and freshness tests** - `daea0a9` (test)
2. **Task 1 GREEN: generated models** - `abf1dbd` (feat)
3. **Task 2 RED: runner and run-command tests** - `386c3a2` (test)
4. **Task 2 GREEN: cost cap, resume, run command** - `6ebc4a8` (feat)
5. **Task 3 RED: grader, summary and offline tests** - `da71d5a` (test)
6. **Task 3 GREEN: grader, summary, grade command** - `ca32c98` (feat)
7. **Task 3: e2e over the committed cases through the CLI** - `4e1001f` (test)

## TDD Gate Compliance

Plan type is `execute` with `tdd="true"` tasks, so there is no plan-level gate. Every task has a `test(01-09)` commit before its `feat(01-09)` commit. pytest output has no tdd-red-evidence adapter, so there is no machine `RED_EVIDENCE_OK` verdict; the semantic assessments are below.

**Task 1.** RED (`daea0a9`): 13 of 13 failed because `carimbo_models.generated` did not exist (ImportError inside each behaviour test, and the freshness test's own "generated.py is missing" assertion). The module is the planned feature, so the cause is the intended one, but the behaviour tests fail by import rather than by an assertion on validation behaviour. GREEN (`abf1dbd`): 13 of 13 passed.

**Task 2.** RED (`386c3a2`): 20 of 24 failed. To keep the file collectable, the `carimbo_evals.cli` import was made lazy for the RED commit only (a top-level import would have been a collection error, which is INVALID_RED). The failures: unexpected keyword arguments (`max_cost_usd`, `resume`), missing `spent_usd`/`stopped_reason`/`run.json`, and a missing `cli` module for the CLI tests. The 4 that passed (concurrency bound, cancellation leaves whole lines, transport/non-200 become harness errors, `discover_cases`) already held in the tracer runner and are characterization. GREEN (`6ebc4a8`): 24 of 24, with the normal imports restored.

**Task 3.** RED (`da71d5a`): 22 of 26 failed. `normalize_name` and `carimbo_evals.summary` did not exist, so those two imports were wrapped in lazy shims for the RED commit only. The failures are on the planned behaviour: the tracer graded two fields and had no `schema_valid_*`, `total_delta`, `counts.total`, `summary.md` or `grade` command. The 4 passing tests (access-key corruption, transposed-key sanity, and two parametrized rows the tracer already handled) are characterization. GREEN (`ca32c98`): 111 of 111 in the default suite. No REFACTOR commits.

## Files Created/Modified

- `python/src/carimbo_models/generated.py` - generated; never hand-edited
- `python/src/carimbo_evals/runner.py` - cap, reserve, resume, `on_record`, atomic `run.json`
- `python/src/carimbo_evals/cli.py` - `run` and `grade` commands, exit codes, key from env only
- `python/src/carimbo_evals/grader.py` - full grade set, independent schema grades, `grade_run(..., schema_path)`
- `python/src/carimbo_evals/summary.py` - `build_summary`, `write_summary`, markdown renderer, shared constants
- `python/src/carimbo_evals/money.py` - `parse_cost`, Decimal-only, importable by the grader side without an HTTP client
- `python/tests/test_models.py`, `test_runner.py`, `test_grader.py` - 13, 24 and 26 tests
- `python/tests/test_e2e_fake.py` - skeleton cases through the CLI

## Decisions Made

See `key-decisions` above. One more worth stating: the cap check runs after a slot frees, so it always sees every finished case's cost, but costs of requests still in flight are unknown. The docstring states the resulting overshoot bound (`concurrency` times the largest case cost).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added `carimbo_evals/money.py`**
- **Found during:** Task 3
- **Issue:** The summary needs the runner's Decimal cost parsing, but `runner.py` imports `httpx2`, and the plan forbids the grader and summary from importing an HTTP client (tested in a socket-disabled subprocess).
- **Fix:** Moved `parse_cost` into a tiny dependency-free module that both import. It is not in the plan's `files_modified`.
- **Files modified:** `python/src/carimbo_evals/money.py`, `runner.py`, `summary.py`
- **Commit:** `ca32c98`

**2. [Rule 3 - Blocking] `@app.callback()` on the Typer app**
- **Found during:** Task 2
- **Issue:** With one command Typer collapses the app to that command, so `carimbo-evals run ...` failed with "unexpected extra argument (run)" until the second command existed in Task 3.
- **Fix:** Added a no-op callback so `run` and `grade` are subcommands from Task 2 on.
- **Files modified:** `python/src/carimbo_evals/cli.py`
- **Commit:** `6ebc4a8`

**3. [Rule 2 - Missing critical] Refuse a used run directory without `--resume`**
- **Found during:** Task 2
- **Issue:** The tracer opened `cases.jsonl` in append mode unconditionally, so a rerun into the same `--out` would silently write a second record per case, contradicting the plan's "never a second record for a completed case".
- **Fix:** `run_cases` raises `FileExistsError` (CLI exit 2, message names `--resume`) when `cases.jsonl` is non-empty and `resume` is false.
- **Files modified:** `python/src/carimbo_evals/runner.py`, `cli.py`, `python/tests/test_runner.py`
- **Commit:** `6ebc4a8`

**4. [Rule 2 - Missing critical] Torn-line repair on resume and tolerant grading**
- **Found during:** Task 2
- **Issue:** A process killed mid-write can leave a partial last line; `--resume` and `grade` would then fail to parse the file, defeating the purpose of resume.
- **Fix:** Resume truncates an unterminated final line before appending; `grade_run` ignores an unterminated final line.
- **Files modified:** `python/src/carimbo_evals/runner.py`, `grader.py`
- **Commit:** `6ebc4a8`, `ca32c98`

**Total deviations:** 4 auto-fixed (2 blocking, 2 missing-critical). **Impact:** no scope change; one small extra module.

## Verification

- `pytest python/tests -q` (default markers): 111 passed, 3 deselected
- `pytest python/tests/test_e2e_fake.py -m e2e` inside `nix shell nixpkgs#dotnet-sdk_10 ...`: 3 passed
- `ruff check python`, `ruff format --check python`, `pyright -p python`: all clean (0 errors)
- A second codegen run: `git diff --exit-code -- python/src/carimbo_models` clean
- Acceptance greps: generated header has `filename:  invoice.schema.json` and no timestamp; `class Party` 1, `class Invoice` 1, `extra="forbid"` 2; `float(` in runner.py 0; `CARIMBO_EVAL_API_KEY` in cli.py 2 and `api-key` 0; `Draft202012Validator` 6 and `model_validate_json` 1 in grader.py; no `httpx2|httpx|requests|urllib|socket` import in grader.py or summary.py; `run --help` and `grade --help` list every required option (checked with `TERM=dumb`)

## Issues Encountered

None. No server processes were left running (checked with `pgrep` after the e2e run).

## Known Stubs

None.

## Threat Flags

None. Threat dispositions: T-01-21 mitigated and tested (no key option, key read from env only, absence asserted in `cases.jsonl`, `run.json` and, in the e2e, every file in the run directory); T-01-22 mitigated and tested (Decimal cap with reserve, boundary tests at the cap and one cent over, exit 3, default US$1.00); T-01-23 mitigated and tested (socket-disabled subprocess, `httpx2` not in `sys.modules`); T-01-SC no packages added (`uv.lock` untouched, all tools came from the 01-01 lock).

## Next Phase Readiness

Ready for 01-10 through 01-13. `just skeleton` (01-13) can call `carimbo-evals run` then `carimbo-evals grade` directly. Notes for later plans: `just schema` should run the exact codegen flag list in `python/tests/test_models.py` (`CODEGEN_ARGS`); the freshness test's failure message already names that recipe. The flagged edge assumptions stand: callers that build the generated models from non-JSON dicts must also pass `strict=True` to get the same guarantees, and "offline" is proven by the import and socket subprocess check, not by a sandbox.

## Self-Check: PASSED

- FOUND: python/src/carimbo_models/generated.py, python/src/carimbo_evals/cli.py, summary.py, money.py, python/tests/test_models.py, test_runner.py, test_grader.py
- FOUND commits: daea0a9, abf1dbd, 386c3a2, 6ebc4a8, da71d5a, ca32c98, 4e1001f (all ancestors of HEAD)
