---
phase: 02-validated-extraction
plan: 08
subsystem: api
tags: [validation, eval-endpoint, contract-2, attempts, reference-date, aspnetcore, xunit, pytest]

requires:
  - phase: 02-validated-extraction
    provides: "02-03 extraction contract extract-002 and the v2 invoice target; 02-04 InvoiceValidator, ValidationFinding, ValidationOptions; 02-05 skeleton-002 manifest with as_of_date; 02-06 grader-002 reading findings, attempts and validation_failed"
provides:
  - "InvoiceExtractor validates every schema-valid candidate with the registered InvoiceValidator: zero error findings is success (warnings carried), any error finding is the typed outcome validation_failed with the candidate and its findings"
  - "ExtractionContext (reference date only), AttemptKind, ExtractionAttempt, ExtractionResult v2 (Findings, Attempts, ReferenceDate); the single Response member is removed"
  - "POST /eval/extractions contract_version 2: strict optional reference_date, UTC default from the registered TimeProvider, effective.reference_date, outcome.findings, attempts[], usage and cost_usd as sums over attempts"
  - "Program.cs binds Validation options with startup checks and registers InvoiceValidator and TimeProvider"
  - "Python runner contract 2 / record_version 2 sending the manifest as_of_date as reference_date; e2e shows a caught case end to end"
affects: [02-09 repair loop, 02-10, Phase 3 API-04, grading and reporting]

actuals:
  tokens: 10426
  tasks: 3
  commits: 3
plan_head_before: 17540f86a3611b0383adaaf2358869ec1d023e4a
plan_head_after: b64f1d800b04b374ffeca8e3af7c50bf7816299f
commits: 3

tech-stack:
  added: []
  patterns:
    - "Response-level usage, cost and stop reason are derived views of the attempts list (sum rules, null-not-partial cost)"
    - "Reference date enters validation only; ExtractionContext has a single property and LlmRequest has no member that could carry it"
    - "Optional request member bound as JsonElement? so a wrongly typed value is reported as a field problem, not a generic body problem"

key-files:
  created: []
  modified:
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/src/Carimbo.Extraction/Carimbo.Extraction.csproj
    - dotnet/src/Carimbo.Api/EvalEndpoint.cs
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - python/src/carimbo_evals/runner.py
    - python/src/carimbo_evals/cli.py
    - python/tests/test_runner.py
    - python/tests/test_e2e_fake.py

key-decisions:
  - "reference_date is bound as JsonElement? and parsed with TryParseExact yyyy-MM-dd, so a JSON number or boolean is a 400 keyed reference_date like any malformed string (a string-typed member would have reported them as body)"
  - "validation_failed returns outcome.failure null; the findings carry the detail, and the candidate is returned in outcome.invoice"
  - "Top-level cost_usd is null without a warning when no attempt has a response, and null with the first unpriced answered attempt's warning otherwise; never a partial sum or zero"
  - "The runner also writes reference_date into run.json so a run directory records the date it was validated against"

patterns-established:
  - "Tracer first: the tracer task changes the wire contract and the signatures together, with tests, in one production-quality commit"
  - "A test TimeProvider is a small subclass overriding GetUtcNow, registered through the TestHost services callback (last registration wins over the TryAdd default)"

requirements-completed: [API-01, EXT-04, VAL-01, VAL-05]

coverage:
  - id: D1
    description: "Every schema-valid extraction is validated by the registered InvoiceValidator: error findings give validation_failed with the candidate, warnings ride on success, typed failures are never validated"
    requirement: VAL-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#An_error_finding_is_validation_failed_and_keeps_the_candidate_the_findings_and_the_raw_text"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_refusal_whose_text_is_valid_invoice_json_is_refused_and_not_validated"
        status: pass
    human_judgment: false
  - id: D2
    description: "Eval endpoint contract 2: strict optional reference_date with UTC default, findings, attempts list, sums over attempts, null-not-partial cost"
    requirement: API-01
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_malformed_reference_date_is_a_bad_request_naming_the_field_and_never_echoing_the_value"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_unpriced_model_reports_null_cost_and_an_explicit_warning_never_zero"
        status: pass
    human_judgment: false
  - id: D3
    description: "Attempt record per model call with output, findings, usage, cost, latency and stop reason (single attempt until 02-09)"
    requirement: EXT-04
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_clean_extraction_reports_no_findings_one_initial_attempt_and_the_reference_date_used"
        status: pass
    human_judgment: false
  - id: D4
    description: "Date plausibility uses the request or dataset reference date; the runner sends the manifest as_of_date"
    requirement: VAL-05
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_payload_is_contract_2_and_carries_the_reference_date"
        status: pass
      - kind: e2e
        ref: "just e2e (test_skeleton_cases_over_http_through_the_documented_commands)"
        status: pass
    human_judgment: false

duration: 8min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 08: every extraction is validated, and the endpoint reports findings and attempts Summary

**InvoiceValidator wired into the extractor (error findings give the typed outcome validation_failed with the candidate kept), and the eval endpoint and Python runner moved to contract version 2 with a strict reference_date, an attempts list and sums over attempts; the e2e run over HTTP shows the scripted wrong total as a caught, graded case.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-10-08T15:19:03Z
- **Completed:** 2026-10-08T15:27:02Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments

- The extractor validates each schema-valid candidate with the same `InvoiceValidator` singleton the composition root registers (API-01 key link). `Success` now means schema-valid and zero error findings; any error finding is `ValidationFailed(candidate, findings)`. Refusal, truncation, schema_invalid and infrastructure failure are never validated (D-12).
- `ExtractionContext(ReferenceDate)`, `AttemptKind`, `ExtractionAttempt` and `ExtractionResult` v2 (outcome, findings, attempts, raw output, model, prompt version, schema hash, reference date). Exactly one attempt (index 0, kind initial) until 02-09; the reflection test pins `ExtractAsync(ReadOnlyMemory<byte>, ExtractionContext, CancellationToken)` and that the context has only `ReferenceDate`. A test also proves the reference date and case id never reach the provider request.
- Eval endpoint contract 2: strict `reference_date` (`yyyy-MM-dd`, 400 keyed `reference_date`, value never echoed), default from the registered `TimeProvider` UTC date, `effective.reference_date`, `outcome.findings`, `attempts[]` and top-level usage / cost as sums. An unpriced answered attempt gives `cost_usd` null with the first warning; an attempt with no response has null usage, cost and latency and adds nothing.
- Program: `Validation` section bound to `ValidationOptions` with startup `InvalidOperationException` for a non-positive tolerance or a cap below it; `InvoiceValidator` and `TimeProvider.System` (TryAdd, replaceable by hosts) registered before the host callback.
- Runner: `CONTRACT_VERSION = "2"`, `RECORD_VERSION = 2`, `read_as_of_date`, `run_cases(reference_date=...)`, `request.reference_date` on every record, CLI reads the manifest next to the cases. e2e: case-001 success (no error finding), case-002 validation_failed with TOTAL_VNF_FORMULA and graded as caught with only `totals.invoice_total` wrong, case-003 refused.

## Task Commits

1. **Task 1 (tracer): one DANFE over HTTP comes back with findings, attempt record and reference date** - `d3262e2` (feat)
2. **Task 2: runner speaks contract 2 with the dataset as-of date; e2e shows a caught case** - `261bc87` (feat)
3. **Task 3: contract 2 edge cases pinned by tests** - `b64f1d8` (test)

**Plan metadata:** committed separately (docs: complete plan).

Tracer feedback gate: auto mode off, `human_verify_mode` end-of-phase, the tracer's `<verify>` is automated only. The Extraction and Api test projects were re-run after the commit and passed (96 and 35 tests at that point); log: Tracer verified end-to-end, expanding.

## Files Created/Modified

- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` - new types, `ValidationFailed`, validator call, single initial attempt
- `dotnet/src/Carimbo.Extraction/Carimbo.Extraction.csproj` - ProjectReference to Carimbo.Validation
- `dotnet/src/Carimbo.Api/EvalEndpoint.cs` - contract 2 request parsing and response mapping (`EvalFinding`, `EvalAttempt`, sum rules)
- `dotnet/src/Carimbo.Api/Program.cs` - Validation options with startup checks, validator and TimeProvider registration
- `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` - validator-clean fixtures, reflection test, validation tests
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - tracer tests, reference date theory, `FixedTimeProvider`, sum invariant helper, config bounds
- `python/src/carimbo_evals/runner.py`, `cli.py` - contract 2, `read_as_of_date`, reference date plumbing
- `python/tests/test_runner.py`, `test_e2e_fake.py` - payload and record assertions, caught-case e2e

## Decisions Made

See `key-decisions` above. The `JsonElement?` binding for `reference_date` is the only shape that satisfies "a JSON number is a 400 keyed reference_date" with the strict body binding; the alternative (parse `JsonException.Path`) was rejected as depending on serializer internals.

## Deviations from Plan

None - plan executed exactly as written.

Notes that are not deviations:
- The tracer task is `tdd="true"`, but changing the extractor and interface signatures makes a compile-failing RED step meaningless; tests and implementation went into one production-quality tracer commit, and the new behaviors were asserted by tests that failed before the change was in place only in the sense that they could not compile. Task 3 tests passed against the existing implementation, as the plan expected (no production change needed).
- `python/src/carimbo_evals/ground_truth.py` needed no change: the mapped ground truth for case-001..003 produced zero validator errors in the e2e run.
- One of my own Task 3 tests initially failed because the date rule allows one day of slack after the reference date (D-11); the test clock was moved to two days before the fixture issue date. Test-only fix, no production change.

## Issues Encountered

None. The full multi-attempt sum invariant (several answered attempts, mixed priced and unpriced) cannot be driven through the endpoint until 02-09 adds repair; `EvalResponse.From` is internal and no `InternalsVisibleTo` exists for the Api tests, so 02-09 should extend `AssertTopLevelEqualsSums` with multi-attempt cases (the helper already sums over the attempts list).

## Known Stubs

None.

## Threat Flags

None. The new `reference_date` input and the findings/attempts disclosure are the surfaces named in the plan's threat model (T-02-23 to T-02-27); no new endpoint, auth path or schema trust boundary was added. No log line includes findings, invoices or raw output (acceptance grep prints 0).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for 02-09: `ExtractionAttempt` already has `Kind.Repair` and index ordering, `AssertTopLevelEqualsSums` is attempt-list based, and `ExtractionResult.RawOutput` is defined as the producing attempt's text.
- Handoff: `effective` currently holds model, prompt_version, schema_sha256, pricing_version, reference_date; 02-09 adds `max_repairs` and `repair_prompt_version`.

## Self-Check: PASSED

- All ten key-files exist and are committed; commits `d3262e2`, `261bc87`, `b64f1d8` are ancestors of HEAD.
- Acceptance criteria re-run: ContractVersion "2" (1), Validation csproj reference (1), `ValidationFailed(` record (1), `"validation_failed"` literal (1), TryAddSingleton TimeProvider (1), log-line grep (0), runner/cli greps (1, 1, 1, 2, 5), `FixedTimeProvider` (1), `git diff main` on uv.lock and Directory.Packages.props exits 0.
- Plan verification: `devenv shell -- just check` exits 0 (495 .NET tests, 276 Python tests, schema-check, datagen-check, 4 e2e, docs, secrets).

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
