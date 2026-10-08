---
phase: 01-walking-skeleton
plan: 12
subsystem: llm-gateway
tags: [dotnet, anthropic-sdk, ilmgateway, d-21, d-10, live-smoke, llm-02, llm-06, ext-01, ext-02]
status: complete

requires:
  - phase: 01-walking-skeleton
    provides: "ILlmGateway and the Llm records (01-03), the eval endpoint and CarimboApi.CreateApp (01-08), the direct-sdk decision and recorded live bodies (01-10), CostAccountingLlmGateway and the pricing table (01-11)"
provides:
  - "AnthropicLlmGateway and AnthropicLlmGatewayOptions in Carimbo.Llm: the single bottom adapter over the official SDK, MaxRetries configurable and 0 by default"
  - "ProviderKey.Resolve (CARIMBO_ANTHROPIC_API_KEY, then ANTHROPIC_API_KEY) and registration of the adapter in CarimboApi.CreateApp only when the host claimed no ILlmGateway"
  - "Config keys Llm:Anthropic:BaseUrl, Llm:Anthropic:TimeoutSeconds (120), Llm:Anthropic:MaxRetries (0)"
  - "docs/DECISIONS.md D-21 (gateway shape, retry ownership, model-id pinning, schema keywords)"
  - "dotnet/tools/LlmSpike now in Carimbo.slnx (built and format-checked)"
  - "One recorded live extraction through the real endpoint (evals/runs/smoke-01-12, git-ignored)"
affects: [01-13, 03-extraction-hardening]

actuals:
  tokens: 12415
  tasks: 2
  commits: 5
plan_head_before: a595b9c527d57c0ee8b36eb4351228eac0805cc7
plan_head_after: aee4fbf828ea575b6ee10aa750c3f0c42c5914de

tech-stack:
  added: []
  patterns:
    - "Per-call attempt counting through a DelegatingHandler that mutates a call-scoped state object held in an AsyncLocal, so concurrent calls never share a counter and SDK retries stay visible without Polly"
    - "Provider failures become LlmGatewayException with a message built from exception type name and status only; the request id comes from the response header the handler saw, because the SDK exception does not carry it"
    - "A host-registered ILlmGateway always wins; the Anthropic registration is a fallback decided by key presence, then wrapped by the cost decorator"
    - "Test hosts blank both provider-key variables through in-memory configuration so a developer shell or secretspec session can never change which gateway a test registers"

key-files:
  created:
    - dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs
    - dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs
  modified:
    - docs/DECISIONS.md
    - dotnet/src/Carimbo.Llm/Carimbo.Llm.csproj
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - dotnet/Carimbo.slnx

key-decisions:
  - "D-21 written from the 01-10 decision record: direct SDK behind ILlmGateway, MaxRetries 0 in Phase 1, Phase 3 (LLM-01) owns the single retry policy, alias in development and dated snapshot pinned for published runs, no schema keyword fallback needed."
  - "The test seam is an optional HttpMessageHandler, not an HttpClient: the attempt counter must sit under the SDK, and a caller-supplied HttpClient cannot be wrapped."
  - "AnthropicLlmGatewayOptions overrides the record ToString so the generated one can never print the key."
  - "Other SDK exceptions (undecodable response) map to ServerError, since LlmFailureKind has no Other member and the provider did answer with something unusable."
  - "The startup line has a third state, 'model gateway: host-registered', for the case where tests or the ScriptedHost supply their own gateway."

patterns-established:
  - "Live smoke runs inside secretspec run with an ephemeral eval key generated in the script and never echoed; the script also greps the API log and run output for both keys"

requirements-completed: [LLM-02, LLM-06, EXT-01, EXT-02]

coverage:
  - id: D1
    description: "The request on the wire carries the committed model-facing schema (JSON deep-equal), a base64 PDF document block, the prompt and the requested model and max_tokens, and none of temperature, top_p, top_k, tool_choice, tools, thinking, system or cache_control"
    requirement: "LLM-06"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#The_request_body_carries_the_committed_schema_the_pdf_and_the_prompt, #The_request_body_has_no_sampling_tool_thinking_system_or_cache_control_fields"
        status: pass
    human_judgment: false
  - id: D2
    description: "Recorded live response bodies map to LlmUsage with uncached input, cache read and cache write 5m/1h kept separate (no breakdown means all creation tokens are 5m), and the cache-usage fixture prices to the hand-computed 0.0136685 USD"
    requirement: "LLM-02"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#The_haiku_end_turn_body_maps_to_a_typed_response, #The_cache_usage_body_keeps_every_token_class_separate_and_prices_exactly, #Cache_read_tokens_are_not_part_of_the_uncached_input_count, #The_five_minute_and_one_hour_cache_writes_come_from_the_breakdown, #Without_a_breakdown_all_cache_creation_tokens_count_as_five_minute_writes"
        status: pass
    human_judgment: false
  - id: D3
    description: "end_turn, max_tokens, refusal (synthetic body, with stop details) and unknown stop reasons map to distinct LlmStopReason values; each HTTP failure class (401, 403, 400, 404, 413, 422, 429, 529, 500, 503), timeout and network failure map to distinct LlmFailureKind values; no exception text contains the key or the response body"
    requirement: "LLM-06"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#The_max_tokens_body_maps_to_a_max_tokens_stop, #A_synthetic_refusal_body_maps_to_a_refusal_with_its_stop_details, #Each_http_failure_class_maps_to_its_own_kind_without_leaking_the_key, #A_connection_failure_maps_to_network, #A_call_that_never_answers_maps_to_timeout, #Caller_cancellation_propagates_instead_of_becoming_a_gateway_failure"
        status: pass
    human_judgment: false
  - id: D4
    description: "HttpAttempts is 1 per call with MaxRetries 0 (a 529 is attempted once), 3 for a 529, 529, 200 sequence at MaxRetries 2, and concurrent calls do not share a counter"
    requirement: "LLM-06"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs#With_no_retries_a_529_is_attempted_exactly_once, #Attempts_are_counted_per_call_when_retries_are_enabled, #Concurrent_calls_do_not_share_an_attempt_counter"
        status: pass
    human_judgment: false
  - id: D5
    description: "The provider key resolves from CARIMBO_ANTHROPIC_API_KEY first, then ANTHROPIC_API_KEY, blank counts as unset, the route is absent without a key (D-10), a host-registered gateway wins, the registered gateway is cost-wrapped, and the key appears in no response or log"
    requirement: "EXT-01"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_provider_key_registers_the_cost_priced_anthropic_gateway_and_the_route_exists, #Without_any_provider_key_and_no_override_the_eval_route_is_unavailable, #A_gateway_registered_by_the_host_wins_over_the_anthropic_registration, #The_provider_key_resolves_from_the_carimbo_variable_first_and_only_its_source_is_logged, #A_blank_provider_key_counts_as_missing, #The_provider_key_appears_in_no_response_and_no_log_message"
        status: pass
    human_judgment: false
  - id: D6
    description: "A real DANFE (case-001) extracted by Claude through the running Api and the Python runner yields a completed record with a schema-valid invoice, the returned model snapshot, usage and a priced cost"
    requirement: "EXT-02"
    verification:
      - kind: command
        ref: "uv run --project python python -c <plan verify one-liner> over evals/runs/smoke-01-12/cases.jsonl prints: live smoke ok success claude-haiku-4-5-20251001 0.00415400"
        status: pass
    human_judgment: false
  - id: D7
    description: "D-21 is on record with the four required headings, cites docs/spikes/01-llm-gateway.md and was added with insertions only"
    requirement: "LLM-06"
    verification:
      - kind: command
        ref: "grep -c '^## D-21 ' docs/DECISIONS.md prints 1; git diff --stat shows 47 insertions, 0 deletions"
        status: pass
    human_judgment: false

duration: 15min
completed: 2026-10-07
---

# Phase 1 Plan 12: Live extraction through the chosen Anthropic adapter Summary

**The official Anthropic SDK now sits behind `ILlmGateway` as `AnthropicLlmGateway` (direct SDK, MaxRetries 0, committed schema sent verbatim), proven offline on the recorded live bodies, registered through D-10 key resolution, and exercised once live: case-001 came back `success` from `claude-haiku-4-5-20251001` for US$0.00415400.**

## Performance

- **Duration:** about 15 min (commit timestamps; the start time was not captured separately)
- **Completed:** 2026-10-07
- **Tasks:** 2 of 2
- **Files:** 2 created, 5 modified (49.7 KB of diff, about 12.4k tokens on the chars/4 scale)

## Accomplishments

- **D-21 recorded** in `docs/DECISIONS.md` from the 01-10 decision record, in house format, insertions only: direct SDK, `MaxRetries` 0 in Phase 1 with Phase 3 (LLM-01) owning the one retry policy, alias in development and the dated snapshot pinned for published runs, no schema keyword moved or stripped.
- **`AnthropicLlmGateway`** builds one `AnthropicClient` with an explicit `ApiKey` (never the SDK's environment default), per-attempt timeout and `MaxRetries` from options. It sends the committed schema as `output_config.format`, the PDF as a base64 document block and the prompt as a text block. No `CacheControl`, sampling, tool-choice, thinking or system fields.
- **Usage is never double-counted.** `InputTokens` is the SDK's `input_tokens` (already uncached), cache reads and 5m/1h writes are separate fields, and a body without a `cache_creation` breakdown puts all creation tokens in 5m. The cache-usage fixture prices to 0.0136685 USD through `LlmPricingTable`, matching the hand computation.
- **Typed failures.** 401/403 Auth, 400/404/413/422 BadRequest, 429 RateLimit, 529 Overloaded, other 5xx ServerError, SDK timeout Timeout, connection failure Network, caller cancellation propagates. Messages are `"<ExceptionType> status <code>"` only; tests feed a body that echoes the key and assert nothing leaks. The request id is read from the `request-id` response header by the attempt-counting handler.
- **Attempts counted per call** (`AsyncLocal` plus a `DelegatingHandler`), including SDK retries: 1 at `MaxRetries` 0, 3 for 529, 529, 200 at `MaxRetries` 2, and concurrent calls get independent counters.
- **Composition (D-10).** `ProviderKey.Resolve` reads `CARIMBO_ANTHROPIC_API_KEY` then `ANTHROPIC_API_KEY`, blank counting as unset. `CarimboApi.CreateApp` registers the adapter only when the host registered no `ILlmGateway` and a key resolves; the 01-11 decorator then wraps it. Without a key the eval route does not exist. One startup line names the source variable or "missing".
- **`dotnet/tools/LlmSpike`** added to `Carimbo.slnx`; it builds under `TreatWarningsAsErrors` and passes `dotnet format --verify-no-changes`.
- **Full suite:** 170 tests green (40 in `Carimbo.Llm.Tests`, of which 27 are new gateway tests and 13 are the 01-11 pricing tests; 32 in `Carimbo.Api.Tests`); the Api tests also pass with the real key exported by secretspec, so a developer shell cannot change a test host's gateway.

## Live smoke (counts toward the US$5 cap, D-09)

Run through the real Api (Development, ephemeral eval key, key from secretspec) and `carimbo-evals run --cases data/skeleton --case case-001 --out evals/runs/smoke-01-12 --max-cost-usd 0.10`:

| Field | Value |
|-------|-------|
| Startup line | `model gateway: anthropic (key from CARIMBO_ANTHROPIC_API_KEY)` |
| Record status / outcome | `completed` / `success` (schema-valid invoice, access key and totals parsed) |
| model_requested / model_returned | `claude-haiku-4-5` / `claude-haiku-4-5-20251001` |
| stop_reason | `end_turn` |
| Usage | input 3574, output 116, cache read 0, cache write 5m 0, cache write 1h 0 |
| cost_usd | 0.00415400 (3574 x 1.00 + 116 x 5.00 per MTok, pricing version anthropic-2026-10-04) |
| Latency | 3.8 s, HTTP 200 |

Usage and cost match the 01-10 Step 2 figures for the same case exactly (input 3574, output 116), which cross-checks the adapter against the spike tool. The script also grepped the API log and the run output for both the eval key and the provider key: no hit.

**Spend:** spike US$0.2551 + this smoke US$0.0042 = **US$0.2593**, under the US$5.00 phase cap (US$4.7407 left). `evals/runs/` is git-ignored, so the run artifact is not committed.

## Task Commits

1. **D-21 record** (Task 1, docs first per the plan) - `5296b1e` (docs)
2. **Task 1 RED: failing gateway tests with a stub** - `acf8fb3` (test)
3. **Task 1 GREEN: `AnthropicLlmGateway` and `LlmSpike` in the solution** - `e0a1e6f` (feat)
4. **Task 2 RED: failing provider-key and registration tests** - `4c5d8ae` (test)
5. **Task 2 GREEN: `ProviderKey` and gateway registration** - `aee4fbf` (feat)

**Plan metadata:** committed separately after this file (docs: complete plan).

## Files Created/Modified

- `dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs` - options record, the adapter, the attempt-counting handler
- `dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs` - 27 tests on a recording handler, including the recorded fixtures from 01-10
- `dotnet/src/Carimbo.Llm/Carimbo.Llm.csproj` - `Anthropic` package reference (version from CPM, 12.53.0); no other src project references it
- `dotnet/src/Carimbo.Api/Program.cs` - `ProviderKey`, `RegisterAnthropicGatewayWhenUnclaimed`, startup log line
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - provider-key and registration tests; test host blanks both key variables by default and accepts extra config
- `dotnet/Carimbo.slnx` - `tools/LlmSpike`
- `docs/DECISIONS.md` - D-21

## Decisions Made

See `key-decisions`. In short: D-21 as decided at the 01-10 checkpoint, plus four small implementation choices (handler seam, key-safe options `ToString`, ServerError for unusable responses, a `host-registered` log state).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Test seam is an `HttpMessageHandler`, not an `HttpClient`**
- **Found during:** Task 1 (design of the attempt counter)
- **Issue:** The plan says the constructor takes "an optional HttpClient (the test seam)" and also requires an internal handler that counts attempts. A caller-supplied `HttpClient` cannot be wrapped, so the counter could not sit under the SDK.
- **Fix:** The constructor takes an optional inner `HttpMessageHandler`; the gateway builds its own `HttpClient` around the counting handler (with the `HttpClient` timeout infinite so the SDK timeout is the only one).
- **Files modified:** `dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs`
- **Commit:** `e0a1e6f`

**2. [Rule 2 - Missing critical] Key-safe `ToString` on the options record**
- **Found during:** Task 1 (T-01-28)
- **Issue:** A positional record's generated `ToString` prints every member, including `ApiKey`. Any accidental log or exception interpolation of the options would leak the key.
- **Fix:** `AnthropicLlmGatewayOptions.ToString` is overridden to omit the key.
- **Files modified:** `dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs`
- **Commit:** `e0a1e6f`

**3. [Rule 2 - Missing critical] Test hosts blank the provider-key variables**
- **Found during:** Task 2 (before the first run under secretspec)
- **Issue:** `CreateApp` reads provider keys from configuration, which includes real environment variables. With a key exported in the developer shell, the existing test "Development with a key but no gateway has no eval route" would silently register the real adapter and fail (or worse, spend).
- **Fix:** `TestHost.StartAsync` adds in-memory empty values for `CARIMBO_ANTHROPIC_API_KEY` and `ANTHROPIC_API_KEY` after the environment provider, and accepts extra config for the new tests. Verified by running the Api tests under `secretspec run`.
- **Files modified:** `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs`
- **Commit:** `4c5d8ae`

**4. [Rule 2 - Missing critical] Config validation and a third startup state**
- **Found during:** Task 2
- **Issue:** `Llm:Anthropic:TimeoutSeconds` or `MaxRetries` set to nonsense would reach the SDK unchecked, and when a host supplies its own gateway the "anthropic" or "none" line would be misleading.
- **Fix:** Non-positive timeout and negative retries throw at startup with a message naming the setting; the startup line reads `model gateway: host-registered` when the host claimed the slot.
- **Files modified:** `dotnet/src/Carimbo.Api/Program.cs`
- **Commit:** `aee4fbf`

**5. Environment adaptations (not plan deviations):** `python3` is not on PATH, so the ephemeral eval key is generated with `/dev/urandom` and `base64` inside the smoke script rather than with `python3 -c`; the Api is started from the built DLL (after `dotnet build`) instead of `dotnet run`, for a clean process id to stop. Same behaviour, same environment variables.

**Not applicable, as the plan anticipated:** no schema-keyword fallback was applied (the live API accepted the schema unchanged, see D-21), so no schema files were regenerated; the `MaxRetries` 2 attempt test was added anyway because the option is configurable.

**Total deviations:** 4 auto-fixed (1 blocking, 3 missing-critical). **Impact:** none on scope; all strengthen the key-safety and test-isolation requirements of the plan.

## Authentication Gates

None. The provider key resolved through secretspec (`CARIMBO_ANTHROPIC_API_KEY` set, `ANTHROPIC_API_KEY` unset), so the Task 2 precondition was met.

## Issues Encountered

None blocking. The offline timeout test needed no workaround: with `HttpClient.Timeout` infinite and the SDK `Timeout` of 1 second, the SDK cancels the attempt and the adapter classifies it as `Timeout` (and distinguishes it from caller cancellation by the caller's token).

## Known Stubs

None.

## Threat Flags

None beyond the plan's register. T-01-SC mitigated (only `Carimbo.Llm.csproj` among src projects names `Anthropic`, pinned 12.53.0 in CPM); T-01-28 mitigated (explicit key, messages from type name and status, options `ToString` omits the key, tests and the live run's grep show no leak); T-01-29 mitigated (the client always receives `ApiKey`; resolver order tested, including a blank value and the CARIMBO-first case); T-01-30 mitigated (one retry owner, `MaxRetries` 0 and attempts reported); T-01-31 mitigated (wire test deep-compares the sent schema with `schema/invoice.model.schema.json`).

## Next Phase Readiness

Plan 01-13 can run the Python runner against the live endpoint for the whole skeleton set; the endpoint is available whenever `CARIMBO_ANTHROPIC_API_KEY` resolves (for example via `secretspec run`). Phase 3 can add cache and telemetry decorators around `ILlmGateway`, and owns the single retry policy (the `Llm:Anthropic:MaxRetries` setting and the per-call attempt count are already in place). Remaining Phase 1 budget: US$4.7407.

## Self-Check: PASSED

- FOUND: dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs, dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs, docs/DECISIONS.md (D-21), evals/runs/smoke-01-12/cases.jsonl
- FOUND commits (ancestors of HEAD): 5296b1e, acf8fb3, e0a1e6f, 4c5d8ae, aee4fbf
- Acceptance criteria re-run: Anthropic reference count 1 and only Carimbo.Llm.csproj; AsyncLocal count 3; D-21 heading count 1; `git diff` for D-21 insertions only; `test-key-not-real` count 0 in `dotnet/src`; `CARIMBO_ANTHROPIC_API_KEY` read before `ANTHROPIC_API_KEY` in the resolver
- Plan verification: `dotnet test` 170 passed, 0 failed; `dotnet format dotnet/Carimbo.slnx --verify-no-changes` exit 0; live smoke line printed
