---
phase: 01-walking-skeleton
plan: 08
subsystem: api
tags: [aspnetcore, minimal-api, testserver, xunit-v3, eval-endpoint, extraction, security]
status: complete

requires:
  - phase: 01-walking-skeleton
    provides: "Tracer eval endpoint, ILlmGateway contracts and InvoiceExtractor (01-03); ExtractionContract.Default and the Extraction.Tests project (01-05)"
provides:
  - "EvalEndpoint.TryMap(WebApplication, ILogger): availability rule, static-key check, strict validation, 413 limit, response mapping"
  - "Carimbo.Api.Tests (xunit.v3 on TestServer): gating, auth, validation, size limit, outcomes at 200, traceparent, concurrency, key secrecy"
  - "ExtractorTests: every gateway and model result maps to exactly one typed outcome; provider request content pinned"
affects: [01-09, 01-10, 01-11, 01-12, 01-13]

actuals:
  tokens: 10100
  tasks: 2
  commits: 3
plan_head_before: 2cd7c1f9447cf641e6304c88c65491c885c62ca9
plan_head_after: 6408085552686f5ecf7b8a46b98814b58baca4be

tech-stack:
  added: []
  patterns:
    - "Host-per-test on TestServer through CarimboApi.CreateApp(args, configureServices, configureBuilder) with an in-memory key (an empty in-memory value overrides any CARIMBO_EVAL_API_KEY in the developer's environment)"
    - "Capturing ILoggerProvider at Trace level to assert a secret never reaches a log message"
    - "Concurrency proven by holding every request open inside the gateway until all N are in the handler at once"
    - "Mutation check as RED evidence for characterization tests (delete the stop-reason branch, watch the targeted tests fail, restore)"

key-files:
  created:
    - dotnet/src/Carimbo.Api/EvalEndpoint.cs
    - dotnet/tests/Carimbo.Api.Tests/Carimbo.Api.Tests.csproj
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  modified:
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/Carimbo.slnx

key-decisions:
  - "IInvoiceExtractor is registered by factory delegate so a host without an ILlmGateway still starts; Development's ValidateOnBuild rejected the type registration, which made the 'no gateway means 404' rule unreachable there"
  - "Validation failures return Results.ValidationProblem keyed by field name (contract_version, document.media_type, document.content_base64, body); the payload is never echoed"
  - "The 10 MB cap is enforced three ways: RequestSizeLimit endpoint metadata, the Kestrel max-body feature set in the handler, and an explicit Content-Length check returning 413 (TestServer ignores the server limit)"
  - "Stop_sequence and unknown stop reasons are parsed like end_turn (EXT-02 flagged assumption) and that is now pinned by a test"

patterns-established:
  - "Typed gateway and model failures stay HTTP 200 with outcome.status; only auth, validation, size and availability use 4xx"

requirements-completed: [API-03, EXT-01, EXT-02]

coverage:
  - id: D1
    description: "Valid model output becomes a typed Invoice; unknown, missing, truncated, prose, null, trailing-garbage and pt-BR money outputs are schema_invalid with the raw text kept"
    requirement: "EXT-01"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Valid_output_becomes_a_typed_invoice_and_keeps_the_raw_text, #Invalid_model_output_is_schema_invalid_and_the_raw_text_is_kept"
        status: pass
    human_judgment: false
  - id: D2
    description: "Refusal and max_tokens bypass parsing even with valid JSON text; every LlmFailureKind becomes infrastructure_failure with kind, status and request id; cancellation propagates"
    requirement: "EXT-02"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Refusal_is_refused_even_when_the_text_is_valid_invoice_json, #Max_tokens_is_truncated_even_when_the_text_looks_like_complete_json, #Every_gateway_failure_kind_becomes_a_typed_infrastructure_failure, #Cancellation_propagates_instead_of_becoming_an_outcome"
        status: pass
    human_judgment: false
  - id: D3
    description: "The provider request holds only the PDF, versioned prompt and model-facing schema; no member of the request types or the extractor signature can carry eval metadata"
    requirement: "EXT-01"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Provider_request_holds_only_the_document_prompt_and_schema, #Provider_request_type_has_no_member_that_could_carry_eval_metadata"
        status: pass
    human_judgment: false
  - id: D4
    description: "POST /eval/extractions does not exist (404) outside Development/Eval, without a key, or without a gateway; /healthz still answers"
    requirement: "API-03"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#Production_with_key_and_gateway_has_no_eval_route_but_still_serves_healthz, #Development_without_a_configured_key_has_no_eval_route, #Development_with_a_key_but_no_gateway_has_no_eval_route"
        status: pass
    human_judgment: false
  - id: D5
    description: "Missing or wrong key is 401 (checked before the body is read); bad base64, non-PDF bytes, wrong media type, wrong contract version, unknown or missing fields are 400 naming the field; over 10 MB is 413"
    requirement: "API-03"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#Missing_or_wrong_key_is_unauthorized_and_the_right_key_is_accepted, #Malformed_requests_are_bad_requests_that_name_the_field, #A_request_declaring_more_than_ten_megabytes_is_rejected_with_413, #The_eval_route_carries_a_ten_megabyte_server_size_limit"
        status: pass
    human_judgment: false
  - id: D6
    description: "Typed failures stay at 200; the response trace_id equals an inbound traceparent trace id; eight concurrent requests keep their own case_id and distinct trace_ids; the eval key is in no response, header or log message"
    requirement: "API-03"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_refusal_is_reported_as_refused_with_http_200, #A_gateway_failure_is_a_typed_infrastructure_failure_not_a_server_error, #The_response_trace_id_is_the_trace_id_of_the_inbound_traceparent, #Concurrent_requests_each_keep_their_case_id_and_get_their_own_trace_id, #The_eval_key_appears_in_no_response_and_no_log_message"
        status: pass
    human_judgment: false

duration: 7 min
completed: 2026-10-05
---

# Phase 1 Plan 08: Typed outcomes and a locked-down eval endpoint, under test Summary

**The eval endpoint now lives in its own `EvalEndpoint` class with strict validation, a tested 10 MB limit and field-naming 400s, and both it and the extractor's outcome mapping are pinned by 45 new xunit.v3 test cases (24 extractor, 21 endpoint on TestServer).**

## Performance

- **Duration:** 7 min (18:31 to 18:38 UTC)
- **Tasks:** 2 of 2
- **Files:** 4 created, 2 modified

## Accomplishments

- `ExtractorTests` (24 cases including Theory rows) shows each model and gateway result maps to exactly one typed outcome. Refusal and `max_tokens` are never parsed, even when the text is valid invoice JSON. Every `LlmFailureKind` keeps its kind, HTTP status and request id. A cancelled token escapes as `OperationCanceledException`.
- The provider request is pinned two ways: a capturing gateway asserts the exact model, token cap, prompt, schema and PDF bytes, and a reflection test asserts `LlmRequest`, `LlmDocument` and `IInvoiceExtractor.ExtractAsync` expose no member that could carry a case id or ground truth (prohibition EXT-01, status resolved by test).
- `EvalEndpoint.TryMap` owns the three-part availability rule and logs only which condition failed. The route is gone (404) in Production, without a key, or without a gateway.
- The handler authenticates first (SHA-256 of both sides, `CryptographicOperations.FixedTimeEquals`, no state), then validates version, media type, base64 and the `%PDF-` magic. Failures return `ValidationProblem` naming the field and never echoing the payload.
- `Carimbo.Api.Tests` proves 401/400/413/404 and 200-for-typed-failures on TestServer, trace-id propagation from `traceparent`, eight genuinely overlapping requests with distinct trace ids, and that the key appears in no response, header or Trace-level log message.

## Task Commits

1. **Task 1: extractor outcome mapping** - `838e125` (test)
2. **Task 2 RED: endpoint tests** - `f834ad7` (test)
3. **Task 2 GREEN: EvalEndpoint extraction and hardening** - `6408085` (feat)

## TDD Gate Compliance

Plan type is `execute` with `tdd="true"` tasks, so there is no plan-level gate. Per task:

**Task 1 (characterization).** The tracer's `InvoiceExtractor` already implemented every behaviour, so the new tests passed on first run (the Extraction.Tests project ran 66/66, 24 of them new). That is a legitimate GREEN-first state, not an unexpected-green defect: the plan said to fix the extractor only if a test failed, and none did. To prove the tests bite, a mutation check deleted the `Refusal` and `MaxTokens` branches of the stop-reason switch: `Refusal_is_refused_even_when_the_text_is_valid_invoice_json` and `Max_tokens_is_truncated_even_when_the_text_looks_like_complete_json` failed (2 failed, 64 passed). The mutation was reverted with `git checkout -- InvoiceExtractor.cs` and never committed. No `feat` commit exists for Task 1 because no production change was needed.

**Task 2 (true RED then GREEN).** RED commit `f834ad7` ran against the old inline endpoint: 10 of 21 failed. Microsoft.Testing.Platform output has no tdd-red-evidence adapter, so no machine `RED_EVIDENCE_OK` verdict exists. Semantic assessment: each failure was on the planned assertion for the intended reason. `not_a_pdf` got 200 where 400 was required (no magic check). `png_media_type`, `contract_version_2`, `invalid_base64`, `unknown_request_field`, `unknown_document_field` and `missing_case_id` returned a bare 400 without naming the field. The oversized body got 400 instead of 413. The route carried no `IRequestSizeLimitMetadata`. The ten failures all trace to behaviour the plan asked for. The tenth, `Development_with_a_key_but_no_gateway...`, failed in host construction rather than on an HTTP assertion; that was a real startup defect (see Deviations), not a broken fixture. GREEN commit `6408085` brought the suite to 119/119 with 3 repeated runs of the endpoint project stable at 21/21. No REFACTOR commit.

## Files Created/Modified

- `dotnet/src/Carimbo.Api/EvalEndpoint.cs` - availability policy, key check, validation, size limit, DTOs and response mapping (moved out of Program.cs)
- `dotnet/src/Carimbo.Api/Program.cs` - composition root only; calls `EvalEndpoint.TryMap`; extractor registered by factory
- `dotnet/tests/Carimbo.Api.Tests/` - new xunit.v3 project and `EvalEndpointTests` (21 cases)
- `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` - outcome-mapping tests
- `dotnet/Carimbo.slnx` - adds Carimbo.Api.Tests

## Decisions Made

See `key-decisions` above. The 413 path deliberately keeps both the endpoint metadata and the explicit `Content-Length` check: the metadata is asserted present, the explicit check is what TestServer exercises, and the handler also sets the Kestrel max-body feature for chunked bodies without a declared length (that last path is not exercised under TestServer).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Development host without an ILlmGateway failed to start**
- **Found during:** Task 2 (RED run of `Development_with_a_key_but_no_gateway_has_no_eval_route`)
- **Issue:** `AddSingleton<IInvoiceExtractor, InvoiceExtractor>()` is validated at build time in the Development environment, so `builder.Build()` threw `InvalidOperationException` (cannot resolve `ILlmGateway`). The plan truth "without a model gateway, POST /eval/extractions returns 404 because the route does not exist" was unreachable in Development, and `dotnet run` in Development with no gateway would crash instead of serving `/healthz`.
- **Fix:** Register `IInvoiceExtractor` with a factory delegate that resolves the gateway lazily; the eval route is only mapped when a gateway exists, so the factory is never invoked otherwise.
- **Files modified:** `dotnet/src/Carimbo.Api/Program.cs`
- **Verification:** the test passes; full suite 119/119; e2e tracer 3 passed
- **Commit:** `6408085`

**2. [Rule 3 - Blocking] Ambiguous `JsonOptions` after adding `Microsoft.AspNetCore.Mvc`**
- **Found during:** Task 2 (first GREEN build)
- **Issue:** `RequestSizeLimitAttribute` needs the Mvc namespace, which also exports `JsonOptions`, so the existing `IOptions<JsonOptions>` lookup became ambiguous (CS0104).
- **Fix:** `using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;`
- **Files modified:** `dotnet/src/Carimbo.Api/EvalEndpoint.cs`
- **Commit:** `6408085`

**3. [Rule 3 - Blocking] Import ordering rejected by `dotnet format --verify-no-changes`**
- **Fix:** ran `dotnet format` once; the alias using moved below the namespace usings. No behaviour change.
- **Commit:** `6408085`

**Total deviations:** 3 auto-fixed (1 bug, 2 blocking). **Impact:** the bug fix is a real hardening of the host; the other two are mechanical. No scope creep.

## Verification

- `cd dotnet && dotnet test`: 119 total, 0 failed (Domain, Extraction, Api test projects all ran)
- `dotnet format dotnet/Carimbo.slnx --verify-no-changes`: exit 0
- `uv run --project python pytest python/tests/test_e2e_fake.py -m e2e -q`: 3 passed (tracer unchanged)
- Acceptance greps: `FixedTimeEquals` 1, `%PDF-` 2 (code and comment), `EvalEndpoint.TryMap` 1 in Program.cs, `Production` and `Eval` and `traceparent` tests present, concurrency test with `count = 8`, `UseTestServer` present, `dotnet sln list` includes Carimbo.Api.Tests; Task 1 greps 1/1/4/1

## Issues Encountered

None beyond the deviations above. No server processes were left running.

## Known Stubs

None.

## Threat Flags

None. The only new surface is the ten-megabyte metadata and field-naming 400 bodies, both inside the plan's threat model (T-01-17, T-01-18). Threat dispositions: T-01-17 mitigated and tested; T-01-18 mitigated and tested; T-01-19 mitigated and tested (Production, no key, no gateway all 404); T-01-20 mitigated and tested; T-01-36 mitigated and tested (key absent from responses, headers and Trace-level logs); T-01-SC no packages added.

## Next Phase Readiness

Ready for the remaining Phase 1 plans. Open item carried forward unchanged: the live-API acceptance of the model-facing schema is still unverified until the 01-10 spike. The Content-Length-less chunked-body path of the size limit relies on Kestrel and is not covered under TestServer.

## Self-Check: PASSED

- FOUND: dotnet/src/Carimbo.Api/EvalEndpoint.cs
- FOUND: dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
- FOUND: dotnet/tests/Carimbo.Api.Tests/Carimbo.Api.Tests.csproj
- FOUND: dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
- FOUND commits: 838e125, f834ad7, 6408085 (all ancestors of HEAD)
