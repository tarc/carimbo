---
phase: 03-replayable-runs
plan: 07
subsystem: observability
tags: [opentelemetry, tracing, genai-semconv, activity-source, otlp, sampler, dotnet]

requires:
  - phase: 03-replayable-runs
    provides: plan 03-01 the cost-accounted gateway chain and LlmCost
  - phase: 03-replayable-runs
    provides: plan 03-03 the response cache decorator (hit and miss status on LlmResponse.Cache)
  - phase: 03-replayable-runs
    provides: plan 03-04 the eval contract that reports the request trace_id
  - phase: 03-replayable-runs
    provides: plan 03-05 the SDK as the single retry owner (http_attempts)
provides:
  - "TracingLlmGateway: outermost decorator, one `chat <model>` client span per CompleteAsync call (hits included) from the Carimbo.Llm ActivitySource"
  - "LlmTelemetry: source name, attribute-name constants and the AllowedTags allowlist (the GenAI convention pin, D-31)"
  - "Api OpenTelemetry registration: AlwaysOnSampler in code, ASP.NET Core server instrumentation, Carimbo.Llm source, OTLP exporter only when OTEL_EXPORTER_OTLP_ENDPOINT is set; startup log line says which case applies"
  - "Real-pipeline wiring tests (in-memory exporter) and gateway unit tests (BCL ActivityListener)"
affects: [03-08, 03-10, 08-observability]

actuals:
  tokens: 10800
  tasks: 2
  commits: 3
plan_head_before: f229bd651689951747f419322083d33e5c56097a
plan_head_after: e49d4c78af0025212a1d4e9baa3aaf8429537d90

tech-stack:
  added:
    - "OpenTelemetry.Extensions.Hosting 1.19.1"
    - "OpenTelemetry.Exporter.OpenTelemetryProtocol 1.19.1"
    - "OpenTelemetry.Instrumentation.AspNetCore 1.19.0"
    - "OpenTelemetry.Exporter.InMemory 1.19.1 (Carimbo.Api.Tests only)"
  patterns:
    - "Observe-only decorator: tracing sits outside caching and cost accounting, adds no retry and returns the inner response untouched"
    - "Attribute allowlist owned as constants plus a test that every emitted key is a member; content-bearing GenAI attributes are absent by construction"
    - "AlwaysOn sampler set in code so the trace id a response reports always names a recorded trace; the exporter switch alone decides whether spans leave the process"
    - "Tests share process-wide ActivitySources, so each filters captured spans by its own trace id"

key-files:
  created:
    - dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs
    - dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs
    - dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs
    - dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs
  modified:
    - dotnet/Directory.Packages.props
    - dotnet/src/Carimbo.Api/Carimbo.Api.csproj
    - dotnet/tests/Carimbo.Api.Tests/Carimbo.Api.Tests.csproj
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs

key-decisions:
  - "Tracing is the outermost decorator, so a cache hit is its own span with carimbo.llm.cache.status hit and http_attempts 0, never merged with the miss that stored it"
  - "AlwaysOnSampler is set in code (D-31); OTEL_TRACES_SAMPLER becomes ineffective by design, and an inbound traceparent with flags 00 is still continued and recorded"
  - "OTLP exporter registered only when OTEL_EXPORTER_OTLP_ENDPOINT is non-blank; no default endpoint, so no silent telemetry egress (T-03-18)"
  - "A non-gateway cancellation also marks the span Error with error.type cancelled (one addition beyond the plan's LlmGatewayException case); no exception events and no status description, so provider text cannot leak"
  - "No new GenAI attribute names were needed: the semantic-conventions-genai repository read on 2026-10-15 matches RESEARCH Pattern 4 for every name used"

patterns-established:
  - "Pin a Development-status semantic convention by owning its names as constants and testing the emitted key set against the allowlist"
  - "Span unit tests: ActivityListener over the source name plus a test-local parent activity; listen by source name, not by reference to a static source (static-init recursion inside ShouldListenTo left Parent null)"

requirements-completed: [LLM-03, REPO-05]

coverage:
  - id: D1
    description: "One eval request with no inbound traceparent exports exactly one `chat <model>` Client span per model call on the trace id the response reports, with a parent span, model, tokens and cost equal to the attempt cost"
    requirement: LLM-03
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#One_request_with_no_traceparent_exports_one_chat_span_on_the_response_trace_id"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#An_answer_that_fails_validation_every_time_exports_one_span_per_attempt"
        status: pass
    human_judgment: false
  - id: D2
    description: "The trace id an eval response reports always names a recorded trace holding its model-call spans: inbound sampled and unsampled traceparents, and a configured traceidratio 0 sampler"
    requirement: LLM-03
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#An_inbound_sampled_traceparent_is_continued_by_the_chat_span"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#An_inbound_unsampled_traceparent_still_exports_the_chat_span_on_the_inbound_trace_id"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#A_configured_ratio_zero_sampler_does_not_drop_the_chat_span"
        status: pass
    human_judgment: false
  - id: D3
    description: "OTLP export is registered only when OTEL_EXPORTER_OTLP_ENDPOINT is set, and the host logs which case applies"
    requirement: LLM-03
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#The_host_logs_that_no_exporter_is_registered_when_the_endpoint_is_blank"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#The_host_logs_the_endpoint_when_one_is_set"
        status: pass
    human_judgment: false
  - id: D4
    description: "Span attributes are a subset of the LlmTelemetry allowlist, with input tokens summing every input class, and an unpriced zero-usage call has zero tokens, a cost warning and no cost attribute"
    requirement: LLM-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#A_priced_end_turn_span_carries_exactly_allowlisted_metadata"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#An_unpriced_model_with_zero_usage_has_zero_tokens_a_cost_warning_and_no_cost_attribute"
        status: pass
    human_judgment: false
  - id: D5
    description: "No prompt, answer, schema, document, base64 or provider text reaches a span name, tag, event or status description (T-03-17)"
    requirement: LLM-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#No_prompt_answer_schema_document_or_base64_reaches_a_name_a_tag_or_an_event"
        status: pass
    human_judgment: false
  - id: D6
    description: "Spans stay one per call through failures, cache hits, read_only misses, SDK retries (529, 529, 200 gives one span with http_attempts 3) and sequential calls under one parent"
    requirement: LLM-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#A_gateway_failure_is_one_error_span_with_the_snake_case_kind_and_the_same_exception_is_rethrown"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#A_cache_hit_and_the_miss_that_stored_it_are_two_separate_spans"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#A_read_only_miss_is_an_error_span_with_the_cache_miss_kind"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#Two_529s_then_a_200_through_the_full_chain_is_one_span_with_three_http_attempts"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs#Sequential_calls_under_one_parent_share_its_trace_and_span_ids_and_are_ordered_by_start"
        status: pass
    human_judgment: false
  - id: D7
    description: "A reviewer can find the spans of a real eval request in the Jaeger viewer (live lookup is plan 03-10, the viewer is 03-02)"
    verification: []
    human_judgment: true
    rationale: "This plan proves export through the in-memory exporter only; viewing in Jaeger with a real OTLP receiver is exercised by the live lookup in 03-10"

duration: 11min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 07: every model call is a costed span on the eval request's trace Summary

**A content-free `chat <model>` OpenTelemetry span per model call (model, GenAI usage tokens, 8-place cost, cache status, http attempts) on the eval request's trace id, sampled AlwaysOn in code so the reported trace id never names an empty trace, with OTLP export only when an endpoint is configured.**

## Performance

- **Duration:** 11 min
- **Started:** 2026-10-15T08:28:00Z (approximate; start time was not captured at spawn)
- **Completed:** 2026-10-15T08:41:00Z
- **Tasks:** 2
- **Files modified:** 9 (4 created, 5 modified)

## Accomplishments

- `TracingLlmGateway` is the outermost decorator (Tracing over Caching over CostAccounting over the adapter). One span per `CompleteAsync` call, hit or miss, success or failure, with the allowlisted attributes only; `LlmGatewayException` gives status Error plus `error.type` as the snake_case kind and the same exception is rethrown.
- The Api registers OpenTelemetry tracing (service `carimbo-api`, ASP.NET Core instrumentation, `Carimbo.Llm` source). `SetSampler(new AlwaysOnSampler())` guarantees that every eval response's `trace_id` names a recorded trace, including an inbound `traceparent` with flags 00 and a host configured with `OTEL_TRACES_SAMPLER=traceidratio` at 0.
- The OTLP exporter exists only when `OTEL_EXPORTER_OTLP_ENDPOINT` is non-blank; startup logs `tracing: OTLP export to <endpoint>` or `tracing: no exporter (OTEL_EXPORTER_OTLP_ENDPOINT unset)`.
- 7 wiring tests (real SDK pipeline, in-memory exporter) and 8 gateway tests (BCL `ActivityListener`). `just check` passes (dotnet 147 + 138 tests, 353 pytest, e2e 6, schema, datagen, docs, secrets).

## Task Commits

1. **Task 1 (tracer): one eval request exports one chat span per model call on the response's trace id**
   - RED `1c0ba3d` (test): pinned packages, `LlmTelemetry`, 7 failing wiring tests
   - GREEN `8f3487c` (feat): `TracingLlmGateway`, `Program.cs` tracing registration and outermost decoration
2. **Task 2: the span attribute allowlist, content-free guarantee, error spans and hit spans** - `e49d4c7` (test)

**Plan metadata:** the docs commit that carries this file, STATE.md, ROADMAP.md and REQUIREMENTS.md.

## TDD Gate Compliance

- **RED (Task 1):** `1c0ba3d`. Command: `dotnet test --project tests/Carimbo.Api.Tests -- --filter-class Carimbo.Api.Tests.TracingWiringTests`, exit code 2, 7 of 7 failed. Target failures: `Assert.Single() Failure: The collection was empty` on the exported `Carimbo.Llm` spans (five cases) and `Assert.Contains() Failure: Filter not matched` on the tracing log line (two cases); the three-attempt case failed with `Expected: 3, Actual: 0`. `semanticAssessment`: the target tests compiled, executed and failed on the planned assertions for the intended reason (no tracer provider and no gateway decorator existed). Microsoft.Testing.Platform output is not one of the report formats `gsd_run check tdd-red-evidence` accepts (TAP, JUnit XML, swift-testing, unittest), so no classifier record was produced; the assessment rests on the unmodified console output above. `LlmTelemetry` (constants only) was committed with the RED tests because the tests reference it to compile; it contains no behavior.
- **GREEN (Task 1):** `8f3487c`, all 7 wiring tests and the full Api suite (147) pass.
- **Sampler red-first check (required by the plan):** with `SetSampler(new AlwaysOnSampler())` commented out and everything else in place, 2 of the 7 wiring tests failed with `The collection was empty`: the unsampled (flags 00) traceparent case and the `OTEL_TRACES_SAMPLER=traceidratio` / `OTEL_TRACES_SAMPLER_ARG=0` case. The other five passed. With the line restored all 7 pass. The span guarantee therefore rests on the AlwaysOn sampler set in code, not on SDK defaults.
- **Task 2:** the implementation already existed from Task 1, so these tests passed on first compile-clean run (one test-helper fault was fixed first, see Issues). To show they are not vacuous, a mutation adding `gen_ai.input.messages = request.Prompt` to the gateway was applied and reverted: 4 tests failed (allowlist, content sentinels, hit/miss, error span). No REFACTOR commit.

## Files Created/Modified

- `dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs` - `Carimbo.Llm` source, attribute constants, `AllowedTags`; header comment cites the semconv repository, status Development, and the read date
- `dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs` - the per-call span decorator
- `dotnet/src/Carimbo.Api/Program.cs` - `RegisterTracing`, tracing log line, `TracingLlmGateway` wrapping the decorated chain
- `dotnet/Directory.Packages.props`, `Carimbo.Api.csproj`, `Carimbo.Api.Tests.csproj` - four exact-pinned OpenTelemetry packages (restore resolves exactly 1.19.1 / 1.19.1 / 1.19.0 for the Api)
- `dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs` - real-pipeline export, sampler cases, exporter log lines
- `dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs` - allowlist, sentinels, unpriced, error, hit/miss, retry chain, ordering
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - one assertion updated: the registered gateway is now `TracingLlmGateway` (see Deviations)

## Decisions Made

- GenAI semantic conventions re-read on 2026-10-15 (`open-telemetry/semantic-conventions-genai`, `docs/gen-ai/gen-ai-spans.md` and `docs/registry/attributes/gen-ai.md`, status Development, no release). Every attribute name in RESEARCH Pattern 4 is spelled identically in the registry (`gen_ai.request.max_tokens`, `gen_ai.usage.output_tokens`, `gen_ai.usage.cache_read.input_tokens`, `gen_ai.usage.cache_write.input_tokens`, `gen_ai.response.finish_reasons`, provider well-known value `anthropic`), and the convention that `gen_ai.usage.input_tokens` includes cached tokens still holds. No difference to record. The registry now also defines per-modality and `gen_ai.usage.reasoning.output_tokens` attributes; none are used because the provider reports no such breakdown.
- Cancellation also yields an Error span (`error.type` `cancelled`), a small addition to the plan so an aborted request never leaves a span with unset status.
- Status descriptions are never set; the exception message could carry provider text.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Existing test asserted the registered gateway type**
- **Found during:** Task 1 (full Api suite after GREEN)
- **Issue:** `A_provider_key_registers_the_cost_priced_anthropic_gateway_and_the_route_exists` asserted the resolved `ILlmGateway` is exactly `CostAccountingLlmGateway`; with tracing outermost (the plan's own design) it is `TracingLlmGateway`.
- **Fix:** assertion changed to `TracingLlmGateway`, with a comment that tracing is outermost and the cache is off in a test host. The test still proves the route and the real adapter's `network` failure.
- **Files modified:** `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs`
- **Verification:** full Api suite passes (147)
- **Committed in:** `8f3487c`

**2. [Rule 1 - Bug] Test-local parent source raced its own listener**
- **Found during:** Task 2
- **Issue:** `SpanScope` decided what to listen to with `source == ParentSource`; the static `ParentSource` was initialized inside the `ShouldListenTo` callback, so the first scope in a process got a null parent activity (NullReferenceException in `Dispose`).
- **Fix:** listen by source name constants instead of by reference.
- **Files modified:** `dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs`
- **Verification:** three consecutive full Llm.Tests runs and the test alone, all green
- **Committed in:** `e49d4c7`

---

**Total deviations:** 2 auto-fixed (2 Rule 1)
**Impact on plan:** Both were consequences of the planned design (outermost tracing) and test-harness mechanics. No scope creep.

## Issues Encountered

- The first attempt at `Assert.Contains(key, LlmTelemetry.AllowedTags)` did not compile (xUnit has an `ISet<T>` and an `IReadOnlySet<T>` overload and `FrozenSet<T>` implements both); replaced with an `AssertAllowlisted` helper using `Assert.True(set.Contains(key), message)`.
- Plan start time was not captured at spawn, so the start timestamp and the 11 min duration are approximate.

## Known Stubs

None.

## Threat Flags

None. The only new outbound surface is the OTLP exporter, already covered by T-03-18 and gated on `OTEL_EXPORTER_OTLP_ENDPOINT`.

## User Setup Required

None - no external service configuration required. Traces leave the process only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (the local Jaeger from `compose.yaml` listens on loopback).

## Next Phase Readiness

- Ready for 03-08. Eval responses carry a `trace_id` that now always has `chat <model>` spans behind it once an endpoint is configured; 03-10 can do the live lookup in Jaeger.
- Nothing in this plan was exercised against a real OTLP receiver (the in-memory exporter stands in); that is 03-10's job.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*

## Self-Check: PASSED

- FOUND: dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs
- FOUND: dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs
- FOUND: dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs
- FOUND: dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs
- FOUND commits: 1c0ba3d, 8f3487c, e49d4c7 (ancestors of HEAD)
- Acceptance greps: PackageVersion count 7, `1.19.0` x1, `1.19.1` x3, `new TracingLlmGateway` x1, `SetSampler(new AlwaysOnSampler())` x1, `OTEL_EXPORTER_OTLP_ENDPOINT` x3
- `just check` exit 0
