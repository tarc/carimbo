---
phase: 03-replayable-runs
plan: 04
subsystem: api
tags: [eval-contract, overrides, cache, replay, dotnet, python, cli]

requires:
  - phase: 03-replayable-runs
    provides: plan 03-01 CachingLlmGateway, LlmCacheMode, LlmCacheInfo, request cache identity fields
  - phase: 03-replayable-runs
    provides: plan 03-03 default-on cache registration (LlmCacheSettings only when the cache is on), ExtractionOverrides, scripted host with per-test cache directories
provides:
  - Eval contract "3": request overrides {cache_mode, replicate_salt}, effective.cache_mode and effective.replicate_salt, attempts[].cache {mode, status, key, stored}
  - Field-specific HTTP 400 (overrides.cache_mode, overrides.replicate_salt) that never echoes the submitted value; cache_mode other than off refused on a host with no cache
  - Runner CONTRACT_VERSION 3 and RECORD_VERSION 3, run_cases(overrides=...), request.overrides on every record and overrides in run.json
  - carimbo-evals run --cache-mode and --replicate-salt
  - RunConfigMismatchError: a resumed run keeps the overrides it started with (exit 2)
  - Replay through the documented commands proven end to end against the scripted host
affects: [03-05, 03-06, 03-07, 03-08, 03-10, 06-fixtures]

actuals:
  tokens: 19600
  tasks: 3
  commits: 3
plan_head_before: 0aadfedb4cce460ae6c71ad279ddd9c3d12c4a7c
plan_head_after: de254bc267498e6bfc236b6d57c36002288e9f8e

tech-stack:
  added: []
  patterns:
    - "Request overrides stay raw JsonElement members so a wrongly typed value is reported against its own field (the reference_date pattern)"
    - "Invalid values get a fixed message naming overrides.<field>; nothing submitted is echoed"
    - "ASCII allow-list by character check in .NET and fullmatch in Python, so a trailing newline cannot pass a $ anchor"
    - "A resumed run is compared with its existing records before anything is written"

key-files:
  created: []
  modified:
    - dotnet/src/Carimbo.Api/EvalEndpoint.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - python/src/carimbo_evals/runner.py
    - python/src/carimbo_evals/cli.py
    - python/tests/test_runner.py
    - python/tests/test_e2e_fake.py

key-decisions:
  - "A salt is accepted on a host without a cache and still echoed (D-29); only cache_mode other than off needs a host cache"
  - "The salt allow-list is a character check, not a regex, because a .NET `$` also matches before a trailing newline"
  - "Records without request.overrides (record_version 2) and an empty overrides dict both count as no overrides when resuming"
  - "The mismatch check compares every existing record, including records of cases not selected for this invocation, so one directory never holds two cache configurations"

patterns-established:
  - "EvalOverrides record with raw JsonElement members; add a new override as a member, a parse block and an effective echo"
  - "e2e replay test: run A (read_write), B (read_only), C (salted read_only), D (refresh), then a mixed resume, all through _evals"

requirements-completed: [LLM-04, LLM-05, API-02]

coverage:
  - id: D1
    description: "Through carimbo-evals run, a read_only rerun into a new directory replays case-001 and case-002 with every attempt cache.status hit, http_attempts 0 and the first run's latency_ms and cost_usd, and case-003 (refused, never cached) is infrastructure_failure / cache_miss; the cache directory holds exactly 4 entries"
    requirement: "LLM-05"
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#test_a_read_only_rerun_replays_the_recorded_run_with_zero_model_calls"
        status: pass
    human_judgment: false
  - id: D2
    description: "A different replicate salt misses every case in read_only mode, and refresh calls the scripted model again (http_attempts 1, cache.status refresh) without adding entries"
    requirement: "LLM-04"
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#test_a_read_only_rerun_replays_the_recorded_run_with_zero_model_calls"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_replicate_salt_changes_the_cache_key_and_a_repeated_salt_hits"
        status: pass
    human_judgment: false
  - id: D3
    description: "The endpoint answers contract_version 3 only (2 and 1 are HTTP 400 naming contract_version); effective echoes cache_mode and replicate_salt; each attempt carries its cache block or null"
    requirement: "API-02"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#Malformed_requests_are_bad_requests_that_name_the_field (contract_version_1, contract_version_2)"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#The_response_echoes_the_effective_cache_mode_and_each_attempt_carries_its_cache_block"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_host_with_the_cache_off_reports_cache_mode_off_and_no_cache_block"
        status: pass
    human_judgment: false
  - id: D4
    description: "Override validation: replicate_salt 0/1/64/65 characters, disallowed characters, non-strings; cache_mode misspellings and non-strings; cache_mode other than off on a cache-off host; unknown member of overrides; every 400 names overrides.<field> and never echoes the value"
    requirement: "API-02"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_invalid_replicate_salt_is_a_bad_request_naming_the_field_and_never_echoing_the_value"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_sixty_five_character_replicate_salt_is_a_bad_request"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_valid_replicate_salt_reaches_every_attempt_and_is_echoed"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_cache_mode_other_than_off_is_refused_on_a_host_whose_cache_is_off"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_invalid_cache_mode_is_a_bad_request_naming_the_field_and_never_echoing_the_value"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_unknown_member_of_overrides_is_a_bad_request_naming_the_body"
        status: pass
    human_judgment: false
  - id: D5
    description: "The runner sends contract 3 and overrides only when a flag is given, writes record_version 3 with request.overrides and run.json overrides, validates --cache-mode and --replicate-salt before any request, and refuses to resume a run with different overrides (exit 2, file unchanged)"
    requirement: "LLM-05"
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_overrides_are_sent_with_every_request_and_recorded"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_cli_rejects_a_bad_override_before_any_request"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_resume_with_different_overrides_is_refused_and_leaves_the_file_unchanged"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_cli_resume_with_different_overrides_exits_2"
        status: pass
    human_judgment: false
  - id: D6
    description: "Prohibition API-02 (an override must never switch on a cache the host lacks or reach the cache outside Development or Eval) holds by construction: cache_mode other than off is a 400 without a registered LlmCacheSettings, which exists only in Development or Eval"
    requirement: "API-02"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_cache_mode_other_than_off_is_refused_on_a_host_whose_cache_is_off"
        status: pass
    human_judgment: true
    rationale: "The prohibition is a safety judgment spanning the 03-03 startup guard and this plan's endpoint check; tests pin the cache-off host case, but whether the combined guard is sufficient is a reviewer's call"

duration: 8min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 04: eval contract 3 and replay through the documented commands Summary

**Eval contract "3" adds the validated `overrides.cache_mode` and `overrides.replicate_salt` request members, echoes them in `effective`, and reports a per-attempt `cache` block; `carimbo-evals run --cache-mode read_only` then replays a recorded run with zero model calls and the original latency and cost, a different salt misses, `refresh` calls the model again, and a resumed run cannot change its overrides.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-10-15T08:04:36Z
- **Completed:** 2026-10-15T08:12:24Z
- **Tasks:** 3
- **Files modified:** 6 (0 created, 6 modified)

## Accomplishments

- Tracer: `ContractVersion` is "3" (2 and 1 are HTTP 400 naming `contract_version`); `overrides.cache_mode` is parsed with `LlmCacheModes.TryParse`, gated on a registered `LlmCacheSettings` (T-03-09), carried into `ExtractionContext.Overrides`, and echoed as `effective.cache_mode` (override, else host default, else `off`). Each attempt gains `cache {mode, status, key, stored}` or null.
- Through the real commands against the scripted host: run A (host default read_write) stores 4 entries (case-001: 1, case-002: 3 repair-chain attempts; the refusal is never stored); run B (`--cache-mode read_only`) replays case-001/002 with `cache.status` hit, `http_attempts` 0, equal `latency_ms` (1234) and `cost_usd`, and case-003 is `infrastructure_failure` / `cache_miss`.
- Run C (`--cache-mode read_only --replicate-salt r2`) misses every case; run D (`--cache-mode refresh`) calls the model again (`http_attempts` 1, `cache.status` refresh) with the entry count still 4.
- `overrides.replicate_salt` is an ASCII allow-list of 1 to 64 characters of `A-Z a-z 0-9 . _ -`; boundaries 0/1/64/65 are tested, trailing newline and non-ASCII are rejected, and the 400 body never contains the submitted value (sentinel test).
- The runner sends `overrides` only when a flag was given, records it in `request.overrides` and `run.json`, and `RunConfigMismatchError` refuses a resume whose existing records carry other overrides (exit 2, `cases.jsonl` unchanged, both values named).

## Task Commits

1. **Task 1 (tracer): contract 3 with overrides.cache_mode and a read_only replay** - `16d4238` (feat)
2. **Task 2: overrides.replicate_salt and field-specific validation** - `997e22b` (feat)
3. **Task 3: --replicate-salt, refresh, resume keeps a run's overrides** - `de254bc` (feat)

**Plan metadata:** committed separately as `docs(03-04): complete ... plan`.

_TDD note: each task followed RED then GREEN locally and was committed once as the task unit, matching 03-01 and 03-03. RED evidence: Task 1 failed on the intended assertions (contract_version "3" not accepted, `overrides` an unknown member -> HTTP 400 instead of 200, `run_cases()` rejecting the `overrides` keyword); Task 2 failed 14 tests on the missing `replicate_salt` handling (no `overrides.replicate_salt` key in 400 bodies, no echo, no propagation) while the `cache_mode` validation tests already passed from Task 1; Task 3 first failed at collection on the missing `RunConfigMismatchError`, so a stub class was added and the run repeated, giving 9 assertion failures (help text, salt payloads, the four mismatch cases, mixed-resume exit code). The `test(...)` / `feat(...)` split of `tdd.md` was therefore not made; the plan is `type: execute`, so no TDD gate section applies._

## Files Created/Modified

- `dotnet/src/Carimbo.Api/EvalEndpoint.cs` - contract 3, `EvalOverrides`, cache_mode and salt parsing with field-named 400s, `EvalCache`, `effective.cache_mode` and `effective.replicate_salt`
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - contract literals, `contract_version_2` case, region "contract 3: cache overrides" (echo, override of the host default, salt boundaries, no-echo sentinel, cache-off host, unknown member, key and hit/miss with salts)
- `python/src/carimbo_evals/runner.py` - `CONTRACT_VERSION` 3, `RECORD_VERSION` 3, `overrides` through `_run_one` and `run_cases`, `RunConfigMismatchError`, `_require_same_overrides`
- `python/src/carimbo_evals/cli.py` - `--cache-mode`, `--replicate-salt`, exit 2 on a bad flag value or a resume mismatch
- `python/tests/test_runner.py` - contract 3 payloads, overrides recording, CLI flag validation, resume mismatch matrix
- `python/tests/test_e2e_fake.py` - scripted `latency_ms`, contract 3 literals, the four-run replay test with a mixed resume

## Decisions Made

- A salt is accepted on a host without a cache and still echoed (D-29); only a `cache_mode` other than `off` needs a host cache.
- The salt check is a character loop in .NET and `re.fullmatch` in Python rather than a `$`-anchored regex, which would accept a trailing newline.
- When resuming, records without `request.overrides` (record_version 2), null and an empty dict all mean "no overrides"; every existing record is compared, including cases not selected for this invocation.

## Deviations from Plan

None - plan executed exactly as written.

(Test-only note: the first draft of `test_cli_accepts_the_salt_length_limits` wrote to a relative `--out` and left `out-1/` and `out-64/` in the repo root; the second `just check` failed on it. The directories were deleted before any commit and the test now uses `tmp_path`.)

## Issues Encountered

- Task 3's first `just check` run failed on the test above (a repeated run into the stray directory); fixed within the task, nothing committed in that state.

## Known Stubs

None.

## Threat Flags

None. T-03-08 (salt validated at the endpoint and the CLI, the cache file name is the key digest only), T-03-09 (any mode but off is 400 without a registered `LlmCacheSettings`), T-03-10 (fixed messages, sentinel test) and T-03-11 (per-record `request.overrides`, resume refusal) are mitigated and asserted. No package was added (T-03-SC).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Contract 3 now has the cache half of D-29. Plans that add `model`, `prompt_version`, `max_repairs` overrides (03-08), `incurred_cost_usd` and the cost-cap switch (03-06) extend `EvalOverrides` and `EvalAttempt` the same way.
- The runner's cost cap still charges notional cost on a replay until 03-06 introduces `incurred_cost_usd`; a three-case skeleton replay stays far below the default US$1.00 cap.
- `run.json` and `request.overrides` are the record that summaries (03-06) should read to label cached runs.

## Self-Check: PASSED

- No file was created; the six modified files exist.
- Commits `16d4238`, `997e22b`, `de254bc` are ancestors of HEAD; `git rev-list --count` from the recorded base gives 3.
- Acceptance criteria re-run: `ContractVersion = "3"` 1, `CONTRACT_VERSION = "3"` 1, `RECORD_VERSION = 3` 1, replay test name 1, `overrides.replicate_salt` 1, `overrides.cache_mode` 2, `class RunConfigMismatchError` 1, `--replicate-salt` in cli.py 2.
- `just check` (dotnet-check 723 tests, py-check 332 passed, schema-check, datagen-check, e2e 6 passed, docs-check, secrets-check) exits 0.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
