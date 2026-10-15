---
phase: 03-replayable-runs
plan: 05
subsystem: llm
tags: [retry, anthropic-sdk, max-retries, backoff, resilience, dotnet, static-guard]

requires:
  - phase: 03-replayable-runs
    provides: plan 03-01 CachingLlmGateway, LlmCacheStore, CostAccountingLlmGateway and the cache entry format (original_http_attempts)
  - phase: 03-replayable-runs
    provides: plan 03-03 Program.cs DecorateGateway and the Llm:Cache startup block; TestHost helpers in EvalEndpointTests
provides:
  - "Llm:Anthropic:MaxRetries defaults to 2 (DefaultMaxRetries), bounded 0..5, out-of-range values fail startup with 'Llm:Anthropic:MaxRetries must be between 0 and 5.'"
  - "The gateway startup line ends with ', max retries <n>'"
  - "Composed-chain proof (Caching over CostAccounting over Anthropic) that a persistent 529 is attempted 1 + MaxRetries times, 529/529/200 is one cached response with HttpAttempts 3 whose replay makes no attempt"
  - "SDK retry matrix, backoff, retry-after-ms, x-should-retry, connection-failure, identical-body, ordering and cancellation tests against a failing fake transport"
  - "Static guard test_one_retry_owner_no_resilience_package_and_one_delegating_handler"
affects: [03-06, 03-07, 03-08, 03-10]

actuals:
  tokens: 11550
  tasks: 2
  commits: 2
plan_head_before: 149055b273e38548a494859da21fd600e4b30f4f
plan_head_after: e2acecb363e32ec530ef391ca84a8ea6db9027f4

tech-stack:
  added: []
  patterns:
    - "One retry owner: the Anthropic SDK's MaxRetries; the attempt-counting DelegatingHandler is visibility only"
    - "RecordingHandler ScriptStep(Status, Body, Headers) plus SentAt and OnAttempt for timing and cancellation tests"
    - "Static repo guards as pytest tests over dotnet/ manifests and sources, with build output (bin, obj) excluded"

key-files:
  created:
    - dotnet/tests/Carimbo.Llm.Tests/RetryOwnershipTests.cs
    - dotnet/tests/Carimbo.Api.Tests/RetryConfigurationTests.cs
  modified:
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs
    - dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs
    - python/tests/test_repo_layout.py

key-decisions:
  - "D-28 applied: the SDK is the single retry owner with MaxRetries 2 by default; no custom retry, no resilience package"
  - "The startup bound message carries the literal 5 (not an interpolated constant) so the contract text is greppable, matching the plan's acceptance criterion"
  - "RecordingHandler gains a ScriptStep overload instead of changing the existing tuple constructors, so every earlier test compiles unchanged"
  - "The DelegatingHandler guard strips // comments before matching and scans only dotnet/src; tools/LlmSpike is outside the guarded surface"

patterns-established:
  - "Retry behavior is pinned by characterization tests against the SDK at the gateway level and by chain tests at the decorator level"
  - "Host configuration bounds are tested with a Theory of in-range values (log line) and out-of-range values (startup InvalidOperationException naming the key)"

requirements-completed: [LLM-01]

coverage:
  - id: D1
    description: "Through Caching over CostAccounting over AnthropicLlmGateway with MaxRetries 2, a persistent 529 is attempted exactly 3 times, ends in LlmGatewayException Overloaded and caches nothing; 529, 529, 200 yields one response with HttpAttempts 3, stored with original_http_attempts 3, and its replay makes no further HTTP attempt; a 400 is attempted once"
    requirement: "LLM-01"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/RetryOwnershipTests.cs#A_persistent_529_is_attempted_one_plus_max_retries_times_through_the_chain_and_caches_nothing"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/RetryOwnershipTests.cs#Two_529s_then_a_200_is_one_cached_response_with_three_attempts_and_its_replay_makes_no_attempt"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/RetryOwnershipTests.cs#A_400_through_the_chain_is_attempted_once_and_maps_to_bad_request"
        status: pass
    human_judgment: false
  - id: D2
    description: "The host defaults Llm:Anthropic:MaxRetries to 2 and logs 'max retries 2'; 0, 1 and 5 start and are logged; 6, -1 and 100 fail startup with an InvalidOperationException naming Llm:Anthropic:MaxRetries; no log line or message contains the provider key"
    requirement: "LLM-01"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/RetryConfigurationTests.cs#With_the_setting_unset_the_host_uses_two_retries_and_logs_it"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/RetryConfigurationTests.cs#A_configured_value_inside_the_bound_is_used_and_logged"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/RetryConfigurationTests.cs#A_value_outside_zero_to_five_fails_startup_naming_the_setting"
        status: pass
    human_judgment: false
  - id: D3
    description: "The SDK retries 408, 409, 429, 500, 503, 529 up to MaxRetries and attempts 400, 401, 404 once; without retry headers the second attempt starts at least 100 ms later; retry-after-ms 300 delays it at least 250 ms; x-should-retry false stops a 500; MaxRetries 0 ignores x-should-retry true; a connection failure is retried and maps to Network"
    requirement: "LLM-01"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#The_sdk_retries_transient_statuses_up_to_max_retries_and_never_retries_the_others"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#Without_retry_headers_the_second_attempt_starts_at_least_100_ms_after_the_first"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#Retry_after_ms_is_honoured_between_attempts"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#X_should_retry_false_stops_retrying_a_500"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#With_zero_retries_even_x_should_retry_true_gives_one_attempt"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#A_connection_failure_is_retried_and_still_maps_to_network"
        status: pass
    human_judgment: false
  - id: D4
    description: "Every retry of one call sends a byte-identical body, attempts are strictly ordered and HttpAttempts equals what the transport saw; a caller cancellation during backoff propagates as OperationCanceledException and starts no further attempt"
    requirement: "LLM-01"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#Every_retry_of_one_call_sends_a_byte_identical_body_and_attempts_run_strictly_in_order"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#A_cancellation_during_backoff_propagates_and_starts_no_further_attempt"
        status: pass
    human_judgment: false
  - id: D5
    description: "No project in dotnet/ references Polly, Microsoft.Extensions.Http.Resilience, Microsoft.Extensions.Resilience or Microsoft.Extensions.Http.Polly, and the attempt counter in AnthropicLlmGateway.cs is the only DelegatingHandler subclass in dotnet/src"
    requirement: "LLM-01"
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_one_retry_owner_no_resilience_package_and_one_delegating_handler"
        status: pass
    human_judgment: false
  - id: D6
    description: "Prohibition LLM-01 (never retry a refused, truncated or validation-failing answer into a score: only transport and provider failures are retried) holds because retries live in the SDK, which sees only HTTP statuses and connection errors; a refusal or truncation is a 200 and reaches the typed outcome or the bounded repair loop"
    requirement: "LLM-01"
    verification: []
    human_judgment: true
    rationale: "The prohibition is a transparency judgment about how quality failures are routed. No test here sends a 200 refusal through a retrying gateway; the 03-01 cache tests and the existing refusal tests cover the typed outcome separately, and whether that is sufficient is a reviewer's call."

duration: 5min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 05: one retry owner, the SDK, with backoff Summary

**Anthropic SDK MaxRetries is now the only retry policy for model calls (default 2, bounded 0..5, shown as "max retries <n>" in the startup line), proven by a failing fake transport through the composed Caching, CostAccounting, Anthropic chain to make exactly 1 + MaxRetries attempts, with a pytest guard that forbids resilience packages and any second DelegatingHandler.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-10-15T08:14:03Z
- **Completed:** 2026-10-15T08:19:30Z
- **Tasks:** 2
- **Files modified:** 6 (2 created, 4 modified)

## Accomplishments

- Tracer: `DefaultMaxRetries = 2` with `MaxRetriesLimit = 5`; a value outside 0..5 throws "Llm:Anthropic:MaxRetries must be between 0 and 5." and the gateway line ends "timeout <n>s per attempt, max retries <n>", so the existing timeout test still passes. `AnthropicLlmGateway` documents that the SDK retry is the single policy (408, 409, 429, 5xx and connection errors with backoff, `retry-after`, `retry-after-ms`, `x-should-retry`) and that nothing may wrap it.
- Through the real decorator chain with a scripted 529 transport and MaxRetries 2: a persistent 529 gives exactly 3 attempts, Kind Overloaded and an empty cache directory; 529, 529, 200 gives one response with HttpAttempts 3, a stored entry with `response.original_http_attempts` 3, and a replay with HttpAttempts 0 while the handler still counts 3; a 400 is attempted once as BadRequest.
- The SDK behavior is pinned at the gateway: the status matrix (529, 500, 503, 429, 408, 409 retried; 400, 401, 404 once), the backoff floor (at least 100 ms), `retry-after-ms: 300` honoured, `x-should-retry: false` stopping a 500, MaxRetries 0 ignoring `x-should-retry: true`, a connection failure retried 3 times and mapped to Network, byte-identical bodies and strictly increasing send times across one call's attempts, and cancellation during backoff starting no further attempt.
- `test_one_retry_owner_no_resilience_package_and_one_delegating_handler` scans `Directory.Packages.props` and every `dotnet/**/*.csproj` for the four resilience packages (case-insensitive) and every `.cs` under `dotnet/src` for `DelegatingHandler` subclasses; I confirmed by mutation that a planted `polly` PackageVersion and a second handler class each fail it.

## Task Commits

1. **Task 1 (tracer): the SDK alone retries through the composed chain, host default MaxRetries 2** - `d389eab` (feat)
2. **Task 2: SDK retry matrix, backoff evidence, static single-owner guard** - `e2acecb` (test)

**Plan metadata:** committed separately as `docs(03-05): complete ... plan`.

_TDD note: the plan is `type: execute`, so no `## TDD Gate Compliance` section applies. Task 1's RED run failed 6 of the 13 new Api tests on the intended assertions (no "max retries" in the log, 6 and 100 not rejected); the "-1" case already threw under the old negative check. The 3 new chain tests in RetryOwnershipTests passed on the first run, correctly: the SDK already owned retries, so those tests characterize behavior that was already true and prove the chain adds none. Task 2's tests are likewise characterization of the SDK and of the repo layout, so they could not be RED against unchanged code; instead the static guard was mutation-checked (planted Polly reference, planted second DelegatingHandler) and the Llm tests were run three more times with no flake. One test bug of my own (reading `original_http_attempts` at the wrong JSON level, it sits under `response`) was fixed before any commit._

## Files Created/Modified

- `dotnet/src/Carimbo.Api/Program.cs` - DefaultMaxRetries 2, MaxRetriesLimit 5, bound check and message, "max retries <n>" in the gateway log line
- `dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs` - D-28 docs on the MaxRetries parameter and the class: one retry owner, never wrap it
- `dotnet/tests/Carimbo.Llm.Tests/RetryOwnershipTests.cs` - composed-chain attempt counts, cached HttpAttempts 3 and its zero-attempt replay
- `dotnet/tests/Carimbo.Api.Tests/RetryConfigurationTests.cs` - default 2, accepted 0/1/5, rejected 6/-1/100, key never in the log
- `dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs` - `ScriptStep`, RecordingHandler `SentAt` and `OnAttempt`, retry matrix and timing tests
- `python/tests/test_repo_layout.py` - the static single-retry-owner guard

## Decisions Made

- The SDK stays the sole retry layer; the only change in production behavior is MaxRetries 0 to 2 (D-28), so no custom retry exists to keep in sync with cost accounting.
- The bound message uses a literal "5" because the plan's acceptance criterion greps for it; `MaxRetriesLimit` still drives the check.
- The RecordingHandler keeps its two tuple constructors untouched and gains a `params ScriptStep[]` constructor, so no existing test changed.
- The DelegatingHandler guard covers `dotnet/src` only. `dotnet/tools/LlmSpike` also mentions `DelegatingChatClient`/`DelegatingHandler` machinery, but tools are not on a production call path.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The Api host tests that reach the unreachable provider now wait for up to two SDK retries (about 1.5 s each) before failing, as the plan predicted; their assertions are unchanged and the Api suite still finishes in under 5 s.
- The connection-failure retry test and the backoff tests use real SDK backoff (about 1.5 s and 0.1 s); the status matrix uses `retry-after-ms: 1` to stay fast.

## Known Stubs

None.

## Threat Flags

None. T-03-12 (single owner, 0..5 bound, composed-chain attempt count, static guard), T-03-13 (matrix pins 400/401/404 at one attempt), T-03-14 (RetryConfigurationTests and the existing Classify tests assert the dummy key never reaches a log line or message) are mitigated and asserted. No package was added (T-03-SC).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- A worst-case provider outage now costs up to (max_repairs + 1) x (MaxRetries + 1) x 300 s per case, 2700 s with defaults; plan 03-06 raises the runner read timeout to match (D-28), as flagged in the plan.
- Retried attempts may be billed by the provider but are not separately costed: `HttpAttempts` reports them and cost accounting prices the final response only. Plan 03-06's incurred-cost fields should keep that in mind.

## Self-Check: PASSED

- Created files exist: `RetryOwnershipTests.cs` and `RetryConfigurationTests.cs`; the four modified files exist.
- Commits `d389eab` and `e2acecb` are ancestors of HEAD; `git rev-list --count` from the recorded base `149055b` gives 2.
- Acceptance criteria re-run: `DefaultMaxRetries = 2` 1, `must be between 0 and 5` 1, `def test_one_retry_owner` 1, `SentAt` 8 in AnthropicGatewayTests.cs.
- `just check` (dotnet-check 749 tests, py-check 333 passed, schema-check, datagen-check, e2e 6 passed, docs-check, secrets-check) exits 0; the Llm suite ran three more times, 130 passed each.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
