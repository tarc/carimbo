---
phase: 03-replayable-runs
plan: 06
subsystem: evals
tags: [cost-accounting, incurred-cost, notional-cost, cache-hit-rate, summary, runner, dotnet, python]

requires:
  - phase: 03-replayable-runs
    provides: plan 03-04 eval contract 3 (attempts[].cache, overrides.cache_mode, read_only replay through the documented commands)
  - phase: 03-replayable-runs
    provides: plan 03-05 the SDK as the single retry owner (worst case (max_repairs+1) x (MaxRetries+1) x 300 s per case)
provides:
  - "attempts[].incurred_cost_usd and top-level incurred_cost_usd on the eval response (D-29); cost_usd stays the notional cost"
  - "Runner cap and spent_usd on incurred cost, notional_usd beside it in RunReport, run.json and the CLI; client read timeout 2700 s (D-28)"
  - "Grader per-case incurred_cost_usd, cache_hits, answered_attempts, model_latency_ms, cache_mode, replicate_salt"
  - "summary_version 3: totals.incurred_cost_usd, totals.unpriced_incurred_cases, cache {hits, answered_attempts, hit_rate}, model_latency_ms, config.cache_modes and replicate_salts, markdown cost and cache lines and a hits column"
affects: [03-07, 03-08, 03-10, 06-fixtures]

actuals:
  tokens: 10100
  tasks: 2
  commits: 2
plan_head_before: 8c86b05d0c1729296e0b550ff287550c22d75c59
plan_head_after: b154216e0aa9a0c4ccfc63b623ecadc8cc57f222

tech-stack:
  added: []
  patterns:
    - "Unknown is null, never zero: incurred is 0 only when no provider call was provably made (cache hit, cache_miss)"
    - "A key present with an explicit null means unknown and is not replaced by a fallback; an absent key falls back (contract 2 records)"
    - "Decimal money is written with format(x, 'f') so a scaled zero never prints as 0E-8"

key-files:
  created: []
  modified:
    - dotnet/src/Carimbo.Api/EvalEndpoint.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - python/src/carimbo_evals/runner.py
    - python/src/carimbo_evals/cli.py
    - python/src/carimbo_evals/grader.py
    - python/src/carimbo_evals/summary.py
    - python/tests/test_runner.py
    - python/tests/test_grader.py
    - python/tests/test_e2e_fake.py

key-decisions:
  - "The top-level incurred_cost_usd is null as soon as any attempt's is null, so a provider failure after an answered attempt is charged the reserve (T-03-15); with every attempt known it is the sum, which equals the sum over answered attempts"
  - "The markdown '(n unpriced)' next to the notional cost counts totals.unpriced_cases (notional); the incurred unknowns are in totals.unpriced_incurred_cases in summary.json"
  - "Money in run.json, the CLI run line and totals is rendered with format(x, 'f') (plain decimal, scale kept), so a replay prints spent_usd 0.00000000, not 0E-8"
  - "GRADER_VERSION stays grader-002: field grading is untouched, only added evidence fields"

patterns-established:
  - "Endpoint derives both the attempt and the response incurred value from one function (EvalAttempt.IncurredAmount), so the two levels cannot disagree"
  - "cache_counts(response) in grader.py is the one definition of a hit and of an answered attempt, shared by the grader and the CLI line"

requirements-completed: [LLM-05, LLM-04]

coverage:
  - id: D1
    description: "Each attempt carries incurred_cost_usd: 0 for a cache hit or a cache_miss, equal to cost_usd for another answered call (a refresh included), null when unpriced and for any other failure; the response total is null when any attempt's is null, otherwise the sum; cost_usd stays the notional cost (the original cost on a hit)"
    requirement: "LLM-05"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_miss_incurs_its_cost_and_the_hit_of_the_same_request_incurs_nothing_while_keeping_the_original_cost"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_read_only_cache_miss_made_no_provider_call_so_it_incurs_zero_and_has_no_notional_cost"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_provider_failure_after_an_answered_attempt_makes_the_total_incurred_unknown"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_unpriced_answered_miss_has_a_null_incurred_cost_at_both_levels"
        status: pass
    human_judgment: false
  - id: D2
    description: "The runner caps spend on incurred_cost_usd (cost_usd for records without it, reserve for an explicit null), so ten replayed cases at US$0.30 notional all run under a US$1.00 cap; run.json and the CLI report spent_usd and notional_usd; the client read timeout is 2700 s"
    requirement: "LLM-05"
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_replayed_cases_add_nothing_to_spend_so_ten_of_them_fit_under_the_cap"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_a_null_incurred_cost_is_charged_the_reserve_even_when_the_notional_cost_is_known"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_a_record_without_incurred_cost_is_accounted_on_cost_usd"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_the_client_read_timeout_covers_a_full_repair_chain"
        status: pass
    human_judgment: false
  - id: D3
    description: "Through the documented commands the read_only replay run has run.json spent_usd 0 and notional_usd above 0, and every replayed attempt has incurred_cost_usd 0.00000000"
    requirement: "LLM-05"
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#test_a_read_only_rerun_replays_the_recorded_run_with_zero_model_calls"
        status: pass
    human_judgment: false
  - id: D4
    description: "The graded summary (summary_version 3) reports incurred and notional totals, a cache block with hit_rate, a model_latency_ms block and per-case incurred_cost_usd, cache_hits, answered_attempts, model_latency_ms, cache_mode and replicate_salt; the replay summary shows incurred 0, 4/4 hits, hit_rate 1.0 and case-001 model_latency_ms 1234, the recording summary 0 hits"
    requirement: "LLM-04"
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_replay_summary_reports_incurred_next_to_notional_and_the_full_hit_rate"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_replayed_record_grades_zero_incurred_two_hits_and_the_original_model_latency"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_a_contract_2_record_is_graded_on_its_cost_with_no_hits_and_no_attempt_latency"
        status: pass
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#test_a_read_only_rerun_replays_the_recorded_run_with_zero_model_calls"
        status: pass
    human_judgment: false
  - id: D5
    description: "Summaries never report a replay's notional cost as money spent and never hide it: markdown and JSON show incurred and notional side by side; an unknown call is never counted as free"
    requirement: "LLM-05"
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_replay_markdown_shows_incurred_and_notional_cost_the_hit_line_and_the_hits_column"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_an_unknown_incurred_cost_is_counted_and_never_summed_as_zero"
        status: pass
    human_judgment: true
    rationale: "The transparency and safety prohibitions are judgment statements; tests pin the wire values and the rendered lines, but whether the wording reads honestly to a reviewer is a human call"

duration: 7min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 06: incurred versus notional cost, and the cache hit rate in summaries Summary

**Every attempt and the response now carry `incurred_cost_usd` (0 for a cache hit or cache_miss, the cost for a paid call, null when unknown) beside the notional `cost_usd`; the runner caps and reports spend on it with `notional_usd` alongside, so a read_only replay shows `spent_usd` 0 and a positive notional cost, and summary_version 3 reports both costs, the cache hit rate and the original model latency.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-10-15T08:23:04Z
- **Completed:** 2026-10-15T08:30:00Z
- **Tasks:** 2
- **Files modified:** 9 (0 created, 9 modified)

## Accomplishments

- Tracer: `EvalAttempt.IncurredAmount` derives the attempt value (0 for `Cache.Status == Hit`, `Cost.AmountUsd` for any other response, 0 for a response-less `InfrastructureFailure` of kind `CacheMiss`, null otherwise) and `EvalResponse` sums those; `cost_usd` and `cost_warning` are untouched.
- The runner's `account()` charges `incurred_cost_usd` when the key is present (a null is unknown and costs the reserve), `cost_usd` when absent, and tracks `notional_usd` from `cost_usd`; ten replayed cases at US$0.30 notional now run under a US$1.00 cap. The client read timeout is `httpx2.Timeout(2700.0, connect=5.0)` with the D-28 formula in a comment.
- Through the documented commands (scripted host): the recording run has incurred equal to cost everywhere; the `--cache-mode read_only` replay has `spent_usd` 0.00000000, `assumed_usd` 0, `notional_usd` equal to case-001 plus case-002's recorded cost, every replayed attempt incurred `0.00000000`, and the case-003 `cache_miss` incurred `0.00000000` with `cost_usd` null.
- Grading (summary_version 3): replay summary shows `incurred_cost_usd` 0, `cache {hits: 4, answered_attempts: 4, hit_rate: 1.0}`, case-001 `model_latency_ms` 1234 and case-002 3702 (the original latencies); the recording summary shows `{hits: 0, answered_attempts: 5, hit_rate: 0.0}`. `hit_rate` is `None` when no attempt was answered, which differs from 0.0.
- Markdown: `- cost: US$<incurred> incurred, US$<notional> notional (<n> unpriced)`, `- cache: <h>/<n> answered attempts from the cache`, a separate tokens line, and a `hits` column after `attempts`. The CLI prints `cost= incurred= hits=h/n` per case and `spent= notional= assumed=` per run.

## Task Commits

1. **Task 1 (tracer): incurred_cost_usd on the wire and in the runner cap; a read_only replay spends 0** - `f99158c` (feat)
2. **Task 2: summaries report incurred and notional cost, the hit rate and the original model latency** - `b154216` (feat)

**Plan metadata:** committed separately as `docs(03-06): complete ... plan`.

_TDD note: each task followed RED then GREEN locally and was committed once as the task unit, matching 03-01, 03-03 and 03-04 (the plan is `type: execute`, so no `test(...)`/`feat(...)` split or TDD gate section applies). RED evidence: Task 1 failed 5 of 140 .NET tests on the planned assertions (`incurred_cost_usd` null where `"0.00000000"` or the cost was expected) and 7 Python runner tests (the cap stopped at 3 of 10 replayed cases, `RunReport.notional_usd` absent, read timeout 900.0 != 2700.0); Task 2 failed 15 grader/summary tests on missing keys and the old `summary_version`/column layout. Two of my own test arithmetic mistakes (a median, a malformed-cache expectation) surfaced in the GREEN run and were corrected in the tests, not the code._

## Files Created/Modified

- `dotnet/src/Carimbo.Api/EvalEndpoint.cs` - `EvalAttempt.IncurredCostUsd` / `IncurredAmount`, `EvalResponse.IncurredCostUsd` (appended last, so existing member order is kept)
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - "contract 3: incurred versus notional cost" region (7 tests); `AssertTopLevelEqualsSums` checks the incurred invariant on every cost test
- `python/src/carimbo_evals/runner.py` - `account()` on incurred, `RunReport.notional_usd`, `run.json` `notional_usd`, 2700 s timeout, plain-decimal money in `run.json`
- `python/src/carimbo_evals/cli.py` - per-case and run lines
- `python/src/carimbo_evals/grader.py` - `cache_counts`, per-case incurred/cache/latency/config fields
- `python/src/carimbo_evals/summary.py` - `SUMMARY_VERSION` 3, totals, `_cache`, `model_latency_ms`, config keys, markdown
- `python/tests/test_runner.py`, `python/tests/test_grader.py`, `python/tests/test_e2e_fake.py` - behavior tests, `_v3_record` builder, replay and recording summary assertions

## Decisions Made

- The response-level incurred value is null as soon as one attempt's is (see deviation 1), so the runner charges the reserve for it.
- The `(n unpriced)` in the markdown cost line is the notional unpriced count; `unpriced_incurred_cases` is in `summary.json`.
- Money strings use `format(x, "f")`: a Decimal sum of `0.00000000` strings would otherwise print as `0E-8`. This also changes `totals.cost_usd` and `run.json` to plain form; the values already tested (`"0.3"`) are unchanged.
- `GRADER_VERSION` stays `grader-002`; committed `evals/results/*/summary.json` (versions 1 and 2) are verbatim copies and were not touched.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Top-level incurred is null when any attempt's incurred is unknown, not only any answered attempt's**
- **Found during:** Task 1 (T-03-15 mitigation check before writing EvalResponse)
- **Issue:** The plan text sums incurred "over answered attempts". An answered attempt followed by a provider failure (a repair call that fails with `overloaded`) would then report the first attempt's cost as the total and the runner would not charge any reserve for the failed call, which the threat register (T-03-15: "unknown failures stay null and are charged the reserve") forbids.
- **Fix:** The response total is the F8 sum of every attempt's incurred amount, null when any is null. A cache-miss attempt contributes 0, so in every case the plan describes (all attempts answered or hits, or a lone cache_miss) the result is identical to the plan's rule; it differs only when a non-cache_miss failure is among the attempts.
- **Files modified:** `dotnet/src/Carimbo.Api/EvalEndpoint.cs`, `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` (test `A_provider_failure_after_an_answered_attempt_makes_the_total_incurred_unknown`)
- **Verification:** `dotnet test --project tests/Carimbo.Api.Tests` 140 passed
- **Commit:** f99158c

**2. [Rule 1 - Bug] Scaled zero printed as `0E-8`**
- **Found during:** Task 2 (CLI test of the run line, and the replay summary test)
- **Issue:** `str()` of `Decimal(0) + Decimal("0.00000000")` is `0E-8`, which would have landed in `run.json`, the CLI line and `summary.json` for every replay.
- **Fix:** `format(x, "f")` for `spent_usd`, `notional_usd`, `assumed_usd` and the summary totals. Existing assertions (`"0.3"`, `"0.30"`) still hold.
- **Files modified:** `python/src/carimbo_evals/runner.py`, `python/src/carimbo_evals/cli.py`, `python/src/carimbo_evals/summary.py`
- **Verification:** `just check` exits 0
- **Commit:** b154216

**Total deviations:** 2 auto-fixed (1 missing critical, 1 bug). **Impact:** both strengthen honesty of the figures; no scope creep, and no change to the planned wire fields.

## Issues Encountered

- `git diff` here uses an external difftool that dies on the runner; `git --no-pager diff --no-ext-diff` was used for the token count.
- pyright rejected an inferred `list[tuple[str, int, str]]` passed where `list[tuple[str, int, str | None]]` is declared; fixed with an annotation in the test.

## Known Stubs

None.

## Threat Flags

None. T-03-15 is mitigated and asserted (incurred 0 only for a hit or a cache_miss; unpriced and unknown failures stay null and cost the reserve; records without the field fall back to `cost_usd`); T-03-16 is mitigated (incurred and notional side by side, hit rate, per-case `cache_hits` and `cache_mode`, field grading unchanged). No package was added (T-03-SC).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plans that add overrides (03-08) extend `EvalAttempt`/`EvalResponse` the same way; new wire fields are appended last.
- Note for plans reading `run.json`: `spent_usd`, `notional_usd` and `assumed_usd` are now plain-decimal strings with the scale of their inputs (for example `0.00000000`).
- Worst-case per-case wall time is 2700 s at the defaults; the runner timeout matches it, so a chain of three attempts each retried twice does not become a harness error.

## Self-Check: PASSED

- The nine modified files exist; no file was created.
- Commits `f99158c` and `b154216` are ancestors of HEAD; `git rev-list --count` from `plan_head_before` gives 2.
- Acceptance criteria re-run: `IncurredCostUsd` in EvalEndpoint.cs 2, `Timeout(2700.0, connect=5.0)` 1, `notional_usd` in runner.py 5, `SUMMARY_VERSION = 3` 1, `GRADER_VERSION = "grader-002"` 1, `hit_rate` in summary.py 2; the e2e replay run.json has `spent_usd` 0.00000000 and a positive `notional_usd`, the replay summary 4/4 hits and hit_rate 1.0.
- `just check` (dotnet-check 756 tests, py-check 339+ passed, schema-check, datagen-check, e2e 6 passed, docs-check, secrets-check) exits 0.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
