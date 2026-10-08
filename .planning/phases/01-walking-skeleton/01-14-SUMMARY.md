---
phase: 01-walking-skeleton
plan: 14
subsystem: extraction
tags: [dotnet, system-text-json, decimal, json-schema-pattern, typed-outcomes, gap-closure]

requires:
  - phase: 01-walking-skeleton
    provides: "InvoiceExtractor, Wire.Options, Money, eval endpoint with typed outcomes (plans 01-03, 01-05, 01-09, 01-13)"
provides:
  - "An amount too large for a decimal is a typed schema_invalid outcome (HTTP 200, raw output and cost kept), never an HTTP 500"
  - "Money.Parse reports out-of-range amounts as FormatException; MoneyJsonConverter maps FormatException and OverflowException to JsonException"
  - "Invoice.PatternViolations() and Patterns.IsFullMatch: every schema pattern is enforced from the same Patterns constants, so extraction success means schema-valid"
  - "Schema-driven drift test that fails when a pattern in the model-facing schema is not enforced"
affects: [01-15, 01-16, phase-02-validators, phase-02-money-spec]

actuals:
  tokens: 7700
  tasks: 3
  commits: 6

plan_head_before: 053b2f0b9bd7126a41e95fa33bf96cb51be808b6
plan_head_after: 7acaac7ecda81031a7971513be1c60029ff8c413

tech-stack:
  added: []
  patterns:
    - "Parse model-controlled numbers with TryParse so overflow is a FormatException, never an escaping OverflowException"
    - "Full-length regex match (Success && Index == 0 && Length == value.Length) to match ECMA-262 anchor semantics instead of the .NET $ behaviour"
    - "Schema-walking drift test: the model-facing schema is the source of truth for which fields need enforcement"

key-files:
  created: []
  modified:
    - dotnet/src/Carimbo.Domain/Wire.cs
    - dotnet/src/Carimbo.Domain/Invoice.cs
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs

key-decisions:
  - "WR-03 resolved by enforcement, not deferral: Invoice.PatternViolations() checks access_key, issuer.cnpj and recipient.cnpj against the Patterns constants the schema is exported from; no check-digit logic (D-03), Domain stays BCL-only"
  - "The extractor catches only JsonException, FormatException and OverflowException; no catch-all, cancellation still propagates, so Carimbo bugs surface as 5xx"
  - "Patterns are matched over the full value length because the .NET $ anchor accepts a trailing newline that JSON Schema (ECMA-262) rejects"

patterns-established:
  - "Pattern-bearing schema properties are enforced by Domain code that references the same Patterns constants; the drift test lists the expected paths"

requirements-completed: [EXT-01, EXT-02]

coverage:
  - id: D1
    description: "A 32-digit total_amount goes HTTP -> extractor -> Money and returns HTTP 200 with outcome schema_invalid, raw output kept and cost_usd reported"
    requirement: EXT-02
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_amount_too_large_for_a_decimal_is_schema_invalid_with_http_200_and_its_cost"
        status: pass
    human_judgment: false
  - id: D2
    description: "Money.Parse reports out-of-range amounts as FormatException, Wire.Options as JsonException, and decimal.MaxValue still parses"
    requirement: EXT-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Money_Parse_reports_an_amount_too_large_for_a_decimal_as_a_FormatException"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Money_rejects_an_amount_too_large_for_a_decimal_as_a_json_error"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Money_accepts_the_largest_decimal_amount"
        status: pass
    human_judgment: false
  - id: D3
    description: "The extractor maps an oversized amount to SchemaInvalid and only value-conversion exceptions are caught"
    requirement: EXT-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#An_amount_too_large_for_a_decimal_is_schema_invalid_and_the_raw_text_is_kept"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Cancellation_propagates_instead_of_becoming_an_outcome"
        status: pass
    human_judgment: false
  - id: D4
    description: "access_key, issuer.cnpj and recipient.cnpj violating their schema pattern are schema_invalid naming the JSON path; alphanumeric forms still succeed"
    requirement: EXT-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#A_value_violating_its_schema_pattern_is_schema_invalid_and_names_the_field"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Alphanumeric_access_key_and_cnpj_forms_are_accepted"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Invoice_PatternViolations_names_each_violating_json_path"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Patterns_IsFullMatch_rejects_a_trailing_newline_that_the_dollar_anchor_accepts"
        status: pass
    human_judgment: false
  - id: D5
    description: "Every pattern-bearing property of the model-facing schema is enforced (schema-walking drift test); schema/*.json and generated models unchanged"
    requirement: EXT-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Every_pattern_in_the_model_facing_schema_is_enforced_by_the_extractor"
        status: pass
      - kind: other
        ref: "just schema-check"
        status: pass
    human_judgment: false

duration: 6min
completed: 2026-10-08
status: complete
---

# Phase 1 Plan 14: typed outcomes for unrepresentable amounts and pattern-violating fields Summary

**An oversized total_amount now ends as a typed schema_invalid (HTTP 200, cost kept) instead of an HTTP 500 after a paid call, and extraction success means schema-valid because access_key and both CNPJs are checked against the same Patterns constants the schema is exported from.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-10-08T03:10:38Z
- **Completed:** 2026-10-08T03:16:00Z
- **Tasks:** 3
- **Files modified:** 6 (no files created)

## Accomplishments

- CR-01 (BLOCKER) closed at three layers. `Money.Parse` uses `decimal.TryParse` and throws `FormatException` for an amount that does not fit; `MoneyJsonConverter.Read` also maps `OverflowException` to `JsonException`; `InvoiceExtractor.Parse` catches `FormatException` and `OverflowException` next to `JsonException`. The tracer test drives a 32-digit amount through HTTP, extractor and Money and gets HTTP 200, `schema_invalid`, the raw output and `cost_usd` `0.00200000`.
- WR-03 closed by enforcement. `Invoice.PatternViolations()` returns the JSON paths (`access_key`, `issuer.cnpj`, `recipient.cnpj`) whose values are not a full match of their schema pattern, and `InvoiceExtractor.Parse` returns `SchemaInvalid` naming them (paths only) before reporting success.
- `Patterns.IsFullMatch` closes the trailing-newline gap of the .NET `$` anchor (`"key\n"` matches `^...$` in .NET, not in ECMA-262).
- A drift test walks `ExtractionContract.Default.OutputSchemaJson` (following `$ref`), asserts the pattern paths are exactly `access_key`, `issuer.cnpj`, `recipient.cnpj`, `total_amount`, and that a violating value at each yields `schema_invalid`.
- `just dotnet-check` (188 tests, warnings as errors, format) and `just schema-check` pass; `schema/*.json`, the generated Pydantic models, `Directory.Packages.props` and `Carimbo.Domain.csproj` are unchanged.

## Task Commits

Each task was committed atomically (TDD gate commits in order):

1. **Task 1 (tracer): oversized amount over HTTP**
   - RED `d8ae54b` (test) - endpoint test fails with HTTP 500 instead of 200
   - GREEN `cc0f7a2` (feat) - `Money.Parse` TryParse, converter maps `OverflowException`
2. **Task 2: CR-01 regressions at Domain and extractor layers**
   - `609ef11` (test) - Domain `Money` overflow/boundary tests and extractor SchemaInvalid test
   - `bc6a4fb` (feat) - extractor catches `FormatException` and `OverflowException` only
3. **Task 3: enforce every schema pattern**
   - RED `cc9db28` (test) - 7 extractor tests fail with Success instead of SchemaInvalid
   - GREEN `7acaac7` (feat) - `Patterns.IsFullMatch`, `Invoice.PatternViolations()`, extractor enforcement, Wire.Options doc, Domain tests and fixture fix

**Plan metadata:** recorded in the `docs(01-14)` commit that adds this SUMMARY (hash in `git log`).

Commit hashes for plan 01-16's `01-REVIEW-DISPOSITION.md`: CR-01 = `cc0f7a2` (+ `bc6a4fb`, tests `d8ae54b`, `609ef11`); WR-03 = `7acaac7` (tests `cc9db28`).

## Files Created/Modified

- `dotnet/src/Carimbo.Domain/Wire.cs` - `Money.Parse` without overflow; converter maps `OverflowException`; honest `Wire.Options` doc
- `dotnet/src/Carimbo.Domain/Invoice.cs` - `Patterns.IsFullMatch` and `Invoice.PatternViolations()`
- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` - pattern enforcement before Success; narrow value-conversion catches; Success doc comment
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - tracer test, `ValidInvoiceJson(mutate)`
- `dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs` - Money overflow/boundary, PatternViolations and IsFullMatch tests, 44-character pattern-valid fixture key
- `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` - CR-01 and WR-03 regressions, drift test, `SetPath` helper

## Decisions Made

- Enforce rather than defer WR-03: Phase 2 (VAL-01..06) owns check digits and cross-field rules, not schema conformance, so no later phase would have closed it. Enforcement adds no check-digit logic (D-03) and keeps the Domain BCL-only.
- Keep the extractor's catch list to `JsonException`, `FormatException` and `OverflowException` so a Carimbo bug or cancellation is never turned into a typed outcome.
- Error text for pattern violations names paths only; `raw_output` already carries the values.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] xUnit2008 analyzer rejected `Assert.True(Regex.IsMatch(...))`**
- **Found during:** Task 3 (`just dotnet-check`, warnings as errors)
- **Issue:** The newline test used `Assert.True(Regex.IsMatch(key + "\n", Patterns.AccessKey))`, which xunit.analyzers flags as an error.
- **Fix:** `Assert.Matches(Patterns.AccessKey, key + "\n")`, same .NET regex semantics, so the test still documents why `IsFullMatch` exists.
- **Files modified:** `dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs`
- **Verification:** `just dotnet-check` exits 0
- **Committed in:** `7acaac7`

**2. [Rule 1 - Test clarity] Tracer asserts HTTP status before parsing the body**
- **Found during:** Task 1 RED run
- **Issue:** With the plan's ordering the first RED failure was a `JsonReaderException` parsing the 500 error page, which obscured the real defect.
- **Fix:** Assert `HttpStatusCode.OK` before `ReadJsonAsync`, so RED reads `Expected: OK / Actual: InternalServerError`.
- **Files modified:** `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs`
- **Committed in:** `d8ae54b`

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 test-clarity)
**Impact on plan:** Both are test-only; no scope change.

## TDD Gate Compliance

- **RED evidence (tracer):** command `dotnet test --project tests/Carimbo.Api.Tests --filter-method "*An_amount_too_large_for_a_decimal*"`, exit 2, target test failed on `Assert.Equal(HttpStatusCode.OK, ...)`: expected OK, actual InternalServerError. Semantic assessment: the target test executed and failed on the planned assertion for the intended reason (OverflowException from `Money.Parse` becoming HTTP 500).
- **RED evidence (Task 3):** 7 of 75 extractor tests failed (6 theory rows plus the drift test), each with `Expected SchemaInvalid / Actual Success`; the drift test failed at "schema pattern at 'access_key' is not enforced". Semantic assessment: failures are the planned assertions, not compile or fixture errors.
- **Classifier note:** `gsd_run check tdd-red-evidence` was not run. xUnit v3 on Microsoft.Testing.Platform emits a console format that is not one of the classifier's supported adapters (TAP, JUnit XML, swift-testing, unittest), so RED was assessed from the real console output only (saved in the session scratchpad), not machine-classified.
- **Task 2 has no separate RED:** its Domain and extractor tests pin behaviour that Task 1's GREEN already delivered, so they passed on first run (expected, not an accidental green). The pre-fix failure of the same behaviour is demonstrated by the Task 1 RED. The Domain tests that need the new `PatternViolations` and `IsFullMatch` API were committed with their GREEN (`7acaac7`) because they cannot compile before it; the extractor-level tests carry the RED commit.
- RED (`test(01-14)`) precedes GREEN (`feat(01-14)`) for Tasks 1 and 3; no REFACTOR commit was needed.

## Issues Encountered

None.

## Residual observation (not a gap)

The JSON Schema money pattern `^-?[0-9]+\.[0-9]{2}$` has no length bound, so the Python grader's `jsonschema` and Pydantic checks still call a 32-digit amount schema-valid while .NET reports `schema_invalid` ("does not fit in a decimal"). The structured-output API rejects `maxLength`, and bounding the digit count in the pattern is a wire-contract change that belongs with Phase 2's money specification (DOM-04).

## Known Stubs

None.

## Threat Flags

None. No new endpoint, auth path, file access or trust-boundary schema change; T-01-38, T-01-39, T-01-40 and T-01-SC mitigations are implemented as planned.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 01-15 can rely on a 5xx now meaning a genuine Carimbo bug (it counts against the runner's cost cap); model-controlled values no longer produce one.
- Plan 01-16 can cite the commit hashes above in `01-REVIEW-DISPOSITION.md`.

## Self-Check: PASSED

- All six modified files exist; no files were created.
- Commits `d8ae54b`, `cc0f7a2`, `609ef11`, `bc6a4fb`, `cc9db28`, `7acaac7` are ancestors of HEAD.
- Acceptance greps and the three verify commands (`dotnet test`, `just dotnet-check`, `just schema-check`) pass; no change under `schema/`, `python/src/carimbo_models`, `dotnet/Directory.Packages.props` or `Carimbo.Domain.csproj`.

---
*Phase: 01-walking-skeleton*
*Completed: 2026-10-08*
