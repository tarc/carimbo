---
phase: 03-replayable-runs
plan: 03
subsystem: api
tags: [cache, composition-root, dotnet, aspnet, config-guard, replay, extraction]

requires:
  - phase: 03-replayable-runs
    provides: plan 03-01 CachingLlmGateway, LlmCacheStore, LlmCacheSettings, request cache identity fields
  - phase: 03-replayable-runs
    provides: plan 03-02 decision record D-27 (cache on by default in Development and Eval)
provides:
  - Api composition root registers the response cache (read_write at .cache/llm under the content root) in Development and Eval, off and refused elsewhere
  - Caching(CostAccounting(original)) gateway composition, so a replay carries its original cost
  - Startup guard and mode validation for Llm:Cache:Directory and Llm:Cache:Mode
  - ExtractionOverrides (CacheMode, ReplicateSalt) on ExtractionContext, stamped with PromptVersion and RepairPromptVersion on every LlmRequest
  - Scripted host reporting HttpAttempts 1 and an optional scripted latency_ms
  - Hermetic TestHost default (cache off) and per-test cache directory for every e2e scripted host
affects: [03-04, 03-05, 03-07, 03-10, 06-fixtures]

actuals:
  tokens: 16400
  tasks: 3
  commits: 3
plan_head_before: f000560ad1a1a4ff91103ef7021efb4733fe8c0d
plan_head_after: eb8b5edf42d720a622fc8211f9ce6df680497b55

tech-stack:
  added: []
  patterns:
    - "Environment-gated feature: registered only in Development and Eval, startup throws if configured elsewhere"
    - "Hermetic test host seeds risky defaults off before the test's own config so later values win"
    - "Per-test cache directory under tmp_path for out-of-process hosts"

key-files:
  created:
    - dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs
  modified:
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.ScriptedHost/Program.cs
    - python/tests/test_e2e_fake.py

key-decisions:
  - "The cache decision (registered or not) is made once in Program.cs ResolveCache and logged as one startup line; a blank setting counts as unset"
  - "Environment check precedes the off shortcut: Production with Llm:Cache:Directory set fails even when Mode is off, since a directory there signals a misconfiguration"
  - "The default directory is never created at startup; LlmCacheStore creates it on the first stored answer, so a host that only starts leaves no trace on disk"
  - "TestHost treats either Llm:Cache:Directory or Llm:Cache:Mode in the test's config as opting into the cache and then seeds Mode blank, so a directory alone defaults to read_write"
  - "The scripted host reports HttpAttempts 1 per call: a replay (0) is then distinguishable from a call on the wire"

patterns-established:
  - "Config guard messages name the offending key (Llm:Cache:Directory / Llm:Cache:Mode) and the environment"
  - "EvalEndpointTests helpers are internal and shared by other test classes through using static"

requirements-completed: [LLM-04, LLM-05, EXT-05]

coverage:
  - id: D1
    description: "Over HTTP in process, a read_write host stores one entry and a fresh read_only host on the same directory answers the same eval request with zero model calls, http_attempts 0, the original latency_ms and cost_usd; a read_only host on an empty directory reports infrastructure_failure / cache_miss"
    requirement: "LLM-05"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_read_write_request_is_replayed_by_a_fresh_read_only_host_with_zero_model_calls"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_read_only_host_on_an_empty_directory_reports_a_cache_miss_without_calling_the_model"
        status: pass
    human_judgment: false
  - id: D2
    description: "The cache is on by default (read_write, .cache/llm under the content root) in Development and Eval, can be turned off or relocated, creates no directory at startup, and is refused with a startup error in every other environment"
    requirement: "LLM-04"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs#The_cache_defaults_to_read_write_at_the_default_directory_under_the_content_root"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs#A_cache_directory_outside_development_and_eval_fails_startup"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs#A_cache_mode_other_than_off_outside_development_and_eval_fails_startup"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs#Mode_off_registers_no_cache_even_with_a_directory"
        status: pass
    human_judgment: false
  - id: D3
    description: "Misspelled modes (readonly, READ_ONLY, read-only) fail startup in Development and Production; a relative directory resolves against the content root"
    requirement: "LLM-04"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs#A_misspelled_mode_fails_startup_in_every_environment"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs#A_relative_directory_resolves_against_the_content_root"
        status: pass
    human_judgment: false
  - id: D4
    description: "Every LlmRequest of the repair loop carries PromptVersion, RepairPromptVersion only on repair attempts, and CacheMode and ReplicateSalt from ExtractionContext.Overrides"
    requirement: "EXT-05"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_initial_request_carries_the_prompt_version_and_each_repair_adds_the_repair_prompt_version"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_context_overrides_reach_every_request_of_the_repair_loop"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Provider_request_type_has_no_member_that_could_carry_eval_metadata"
        status: pass
    human_judgment: false
  - id: D5
    description: "No hermetic test shares a cache: TestHost runs with the cache off unless a test passes its own directory or mode, and every e2e scripted host has its own Llm__Cache__Directory"
    requirement: "LLM-04"
    verification:
      - kind: e2e
        ref: "just e2e (5 passed)"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_test_host_without_cache_settings_runs_with_the_cache_off"
        status: pass
    human_judgment: false
  - id: D6
    description: "Review of the LLM-04 per-environment default (the probe left it unclassified): whether refusing a directory even with mode off in Production is the intended strictness"
    requirement: "LLM-04"
    verification: []
    human_judgment: true
    rationale: "The plan flagged LLM-04's per-environment default as unclassified and asked for manual review at verification; tests pin the chosen behavior but cannot say whether it is the desired policy"

duration: 5min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 03: the response cache on by default in Development and Eval, proven by a read_only replay over HTTP Summary

**The Api composition root now wraps CostAccounting in CachingLlmGateway by default in Development and Eval (read_write at .cache/llm under the content root), refuses any cache configuration elsewhere at startup, and the extractor stamps prompt version, cache mode and replicate salt on every request; a read_only host replays an eval request over HTTP with zero model calls and the original latency and cost.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-10-15T07:56:27Z
- **Completed:** 2026-10-15T08:01:25Z
- **Tasks:** 3
- **Files modified:** 7 (1 created, 6 modified)

## Accomplishments

- Tracer: host A (read_write) answers an eval post with one model call and stores one `<64-hex>.json`; fresh host B (read_only, same directory) answers the same post with 0 gateway calls, `http_attempts` 0, `latency_ms` 1234 and equal `cost_usd`. A read_only host on an empty directory answers `infrastructure_failure` / `cache_miss` over HTTP 200.
- `Program.cs` `ResolveCache` implements D-27 exactly: on by default in Development and Eval, `Llm:Cache:Directory` and `Llm:Cache:Mode` override, mode `off` registers nothing, any other environment is cache-free and throws `InvalidOperationException` naming the key when a directory or a non-off mode is configured, invalid mode spellings throw everywhere. One startup line states the outcome (`response cache: read_write at <path>`, `off (Llm:Cache:Mode off)` or `off (environment <name>)`).
- `InvoiceExtractor` gained `ExtractionOverrides` and stamps `PromptVersion`, `RepairPromptVersion` (repair attempts only), `CacheMode` and `ReplicateSalt` on every request (EXT-05), keeping the case id out of the request.
- Hermetic by construction: `TestHost` seeds the cache off (overriding the developer's environment) unless the test names its own directory or mode, and the e2e fixture gives each scripted host its own `Llm__Cache__Directory` under `tmp_path`.
- 20 new Api tests in `CacheConfigurationTests` plus the tracer trio; a mutation of the environment guard and the default directory failed 10 of them.

## Task Commits

1. **Task 1 (tracer): default-on cache, HTTP replay, hermetic hosts** - `5d5b32e` (feat)
2. **Task 2: extractor stamps cache identity on every request** - `e52375b` (feat)
3. **Task 3: configuration pinned by tests, scripted host countable** - `eb8b5ed` (test)

**Plan metadata:** committed separately as `docs(03-03): complete ... plan`.

_TDD note: Task 1 and Task 2 followed RED then GREEN locally (Task 1: 3 tests failing on the intended assertions, 2 model calls instead of 1, success instead of cache_miss, log line absent; Task 2: types added without stamping so the 2 stamping tests failed on null versus "extract-003" / ReadOnly) and were committed once each as the task unit, matching 03-01's convention. Task 3's tests passed on first run because Task 1 already implemented the behavior; the mutation check above confirms they are not vacuous._

## Files Created/Modified

- `dotnet/src/Carimbo.Api/Program.cs` - `DefaultCacheDirectory`, `ResolveCache`, `DecorateGateway(services, LlmCacheSettings?)` building Caching(CostAccounting(original)); settings registered only when the cache is on
- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` - `ExtractionOverrides`, `ExtractionContext.Overrides`, per-request stamping, doc comments
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - cache region (tracer, empty-directory read_only, default-off), TestHost cache seeding, helpers made internal
- `dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs` - default directory, directory-only mode, relative directory, off, Production/Staging refusal, mode validation
- `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` - updated request-property guard, stamping tests
- `dotnet/tests/Carimbo.ScriptedHost/Program.cs` - HttpAttempts 1, optional scripted `latency_ms`
- `python/tests/test_e2e_fake.py` - per-test `Llm__Cache__Directory` and `Llm__Cache__Mode` in the host fixture

## Decisions Made

- The environment test runs before the `off` shortcut, so a configured directory in Production fails even with mode `off` (a directory there signals a misconfiguration; the plan says "a non-blank Llm:Cache:Directory ... fails startup"). Covered by `Mode_off_with_a_directory_outside_development_and_eval_still_fails_startup`.
- The default directory is resolved but not created at startup; `LlmCacheStore` creates it on the first stored answer.
- `Counter`, `CacheConfig` and `CountingGateway` test helpers were made `internal` alongside the listed ones so `CacheConfigurationTests` reuses them.

## Deviations from Plan

None - plan executed exactly as written. (The `Counter`/`CacheConfig`/`CountingGateway` helpers being internal is an addition to the plan's list of members to expose, not a behavior change.)

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-03-03 (cache outside Development/Eval) is mitigated and asserted for Production and Staging; T-03-29 is covered by the `git check-ignore` verify for the repo root, the Api project directory and a test output directory; T-03-30 by the per-test directories and the startup line. No package was added (T-03-SC).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Later plans (03-04 contract 3, 03-05 retries, 03-07 spans, 03-10 skeleton recipe) start from this composition: the cache sits between the extractor and `CostAccountingLlmGateway`; plan 03-10 should pin the skeleton recipe's directory explicitly.
- Requirements LLM-04, LLM-05 and EXT-05 are declared by sibling plans in this phase; they are marked complete here only if the shared-ID gate lets them through.
- The default directory is under the repo's ignored `.cache/`; no cache entry was created by tests or `just e2e` in the repo tree.

## Self-Check: PASSED

- Created file exists: `dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs` (FOUND).
- Commits `5d5b32e`, `e52375b`, `eb8b5ed` are ancestors of HEAD.
- Acceptance criteria re-run: `".cache/llm"` 1, `new CachingLlmGateway` 1, `Llm:Cache:Directory` in Program.cs 3, `Llm:Cache:Mode` in EvalEndpointTests 4, `Llm__Cache__Directory` 1, `RepairPromptVersion = index` 1, `sealed record ExtractionOverrides` 1, `HttpAttempts: 1` 1, `internal sealed class TestHost` 1, `git check-ignore` for the three default paths exits 0.
- `just dotnet-check`: build with warnings as errors, format verification and 691 tests passed. `just e2e`: 5 passed.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
