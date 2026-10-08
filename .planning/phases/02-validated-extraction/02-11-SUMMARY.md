---
phase: 02-validated-extraction
plan: 11
subsystem: extraction
tags: [dotnet, validation, schema-invalid, nullability, reflection, gap-closure]

requires:
  - phase: 02-validated-extraction
    provides: InvoiceExtractor repair loop, InvoiceValidator rule catalogue, eval endpoint contract 2 (plans 02-04, 02-08, 02-09, 02-10)
provides:
  - Invoice.NullViolations(): reflection walker over nullability annotations and list elements
  - InvoiceExtractor.Parse rejects null where the schema requires a value as schema_invalid
  - RuleIds.NULL_VALUE, the InvoiceValidator entry guard and its fixed repair sentence
  - Regression tests for a null list element at validator, extractor, repair-loop and endpoint level
affects: [02-12, phase-03-replay, phase-05-eval-tables]

actuals:
  tokens: 12342
  tasks: 3
  commits: 5

plan_head_before: 0367e28bb5f5cd2e71f64b2cd7757d8e6e521868
plan_head_after: c456e3a8c8300cb1a2bd71876ba585d096b803ef

tech-stack:
  added: []
  patterns:
    - "Schema-validity checks live in the Domain as reflection walkers (PatternViolations, NullViolations) sharing one ordered-property enumeration"
    - "Defence in depth: the parse boundary rejects, the validator entry guard reports an error instead of throwing; no catch-all anywhere"

key-files:
  created: []
  modified:
    - dotnet/src/Carimbo.Domain/Invoice.cs
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/src/Carimbo.Extraction/RepairFeedback.cs
    - dotnet/src/Carimbo.Validation/Findings.cs
    - dotnet/src/Carimbo.Validation/InvoiceValidator.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs
    - dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs
    - dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs
    - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs

key-decisions:
  - "The null check is a Domain walker (Invoice.NullViolations) driven by NullabilityInfoContext, not a two-list check in Parse, so a list member added later is covered automatically"
  - "NULL_VALUE is an error-severity rule id emitted alone from the validator entry guard; ArithmeticRules.cs and KeyAndDateRules.cs are untouched and no catch block was added"
  - "NULL_VALUE gets a fixed repair sentence that states no value and is not in the reveal set"

patterns-established:
  - "Parse rejects before the validator runs, so a null element is schema_invalid with no findings and cannot be masked by a NULL_VALUE validation_failed"

requirements-completed: [VAL-01, EXT-04, API-01]

coverage:
  - id: D1
    description: "A model answer with a null list element in items or installments is schema_invalid at the parse boundary, naming the null path, with no findings"
    requirement: VAL-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#A_null_list_element_is_schema_invalid_names_its_path_and_is_never_validated"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#A_null_items_element_is_a_NullViolation_and_not_a_PatternViolation"
        status: pass
    human_judgment: false
  - id: D2
    description: "InvoiceValidator.Validate never throws on a null list element, null list, null nested record or null required string: one NULL_VALUE error per null path, nothing else"
    requirement: VAL-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs#Nulls_at_every_string_and_list_leaf_never_throw_and_yield_only_NULL_VALUE_at_exactly_that_path"
        status: pass
    human_judgment: false
  - id: D3
    description: "POST /eval/extractions answers HTTP 200 schema_invalid for a null list element and keeps every paid attempt, including a null in a repair attempt"
    requirement: EXT-04
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_null_items_element_is_schema_invalid_with_http_200_and_its_paid_attempt"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_null_element_in_a_repair_attempt_keeps_every_paid_attempt_in_the_response"
        status: pass
    human_judgment: false
  - id: D4
    description: "A schema_invalid repair answer consumes one unit of repair budget and the next turn tells the model the previous answer did not match the schema"
    requirement: API-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs#A_null_element_in_a_repair_answer_is_schema_invalid_consumes_budget_and_keeps_its_usage"
        status: pass
    human_judgment: false

duration: 15min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 11: a null list element is a typed schema_invalid, never a validator crash Summary

**`Invoice.NullViolations()` reflection walker rejects `items: [null]` / `installments: [null]` as schema_invalid at parse, and a `NULL_VALUE` validator entry guard makes `Validate` total on nulls, closing review finding CR-01 (HTTP 500 and lost paid attempts).**

## Performance

- **Duration:** about 15 min (start time was not captured; approximate)
- **Completed:** 2026-10-08T20:21Z
- **Tasks:** 3
- **Files modified:** 11 (5 production, 6 test)

## Accomplishments

- Review finding CR-01 and VERIFICATION gap 1 are fixed by this plan: a null element in `items` or `installments` no longer reaches the validator as a success, so `POST /eval/extractions` answers HTTP 200 `schema_invalid` and the paid attempt keeps its raw output, usage, cost and latency.
- `Invoice.NullViolations()` reports null members whose annotation is not nullable (the two `ie` members stay allowed) and null list elements (`items[0]`), in schema property order, sharing `SchemaProperties` with `PatternViolations`.
- `InvoiceValidator.Validate` returns one `NULL_VALUE` error per null path and runs no other rule; a null invoice reference stays an `ArgumentNullException`. `ArithmeticRules.cs` and `KeyAndDateRules.cs` are unchanged and no catch block exists.
- Regression coverage at four levels: Domain walker, validator never-throw (every string and list leaf, first/middle/last position), extractor and repair loop (initial, repair and last-attempt nulls), and HTTP (both lists, middle position, repair chain with sums 0.00300000).
- `EvalEndpoint.cs` unchanged: the typed outcome flows through the existing loop.

## Task Commits

TDD gate sequence (RED then GREEN per task):

1. **Task 1 (tracer): null items element over HTTP** - `6e8a7bc` (test, RED: 500 instead of 200) then `644a23d` (feat, GREEN)
2. **Task 2: validator stays total on nulls** - `3e40d7d` (test, RED: NullReferenceException) then `4d40ce9` (feat, GREEN)
3. **Task 3: [null] regressions at extractor, repair-loop and endpoint level** - `c456e3a` (test; tests only, passed on first run because Tasks 1 and 2 shipped the behavior)

**Plan metadata:** docs commit (this SUMMARY with STATE and ROADMAP)

## TDD evidence

- RED 1: `A_null_items_element_is_schema_invalid_with_http_200_and_its_paid_attempt` failed on `Assert.Equal(OK, InternalServerError)`, the planned assertion; the cause was the NullReferenceException the verifier reproduced. semanticAssessment: target test executed and failed for the intended reason.
- RED 2: eight `NeverThrowsTests` failed with "the validator threw NullReferenceException" (and ArgumentNullException for a null list); the pre-existing null-reference test and the `ie`/empty-list facts passed, as designed. The DomainTests `NullViolations` facts passed in the RED commit because Task 1 already shipped the walker.
- GREEN: all suites pass; `just dotnet-check` exits 0 (560 tests).

## Files Created/Modified

- `dotnet/src/Carimbo.Domain/Invoice.cs` - `NullViolations()`, shared `SchemaProperties` enumeration
- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` - Parse rejects null before the pattern check; Success doc updated
- `dotnet/src/Carimbo.Validation/Findings.cs` - `RuleIds.NULL_VALUE`
- `dotnet/src/Carimbo.Validation/InvoiceValidator.cs` - entry guard
- `dotnet/src/Carimbo.Extraction/RepairFeedback.cs` - fixed sentence for NULL_VALUE
- Tests: `EvalEndpointTests.cs`, `ExtractorTests.cs`, `RepairLoopTests.cs`, `NeverThrowsTests.cs`, `DomainTests.cs`, `VectorTests.cs`

## Decisions Made

- Walker in the Domain rather than a two-list check in Parse (covers future list members, usable by any Domain consumer).
- NULL_VALUE findings come alone and the validator guard never masks the schema rejection: the extractor tests prove Parse rejects first, with empty findings.
- The decision record for NULL_VALUE and the null rejection (D-25) is written by plan 02-12, as planned.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Rule-id catalogue count assertion in VectorTests**
- **Found during:** Task 2
- **Issue:** `Rule_ids_are_unique_and_each_value_equals_its_name` hard-codes 30 rule ids; adding NULL_VALUE makes 31.
- **Fix:** updated both counts to 31 (the test is not in the plan's `files_modified`, but it asserts the catalogue size this task intentionally changes).
- **Files modified:** `dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs`
- **Verification:** Validation tests pass (191).
- **Committed in:** `4d40ce9`

**2. [Rule 1 - Test threshold] Exhaustive null-leaf sanity threshold**
- **Found during:** Task 2
- **Issue:** my first sanity bound (at least 30 checked leaves) was above the real count of 28 string and list leaf cases.
- **Fix:** bound set to 25; the test still fails if the walker finds nothing.
- **Committed in:** `4d40ce9`

---

**Total deviations:** 2 auto-fixed (both test-only). **Impact on plan:** none on production behavior; no scope creep.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. Threats T-02-36 to T-02-39 are mitigated as registered (no catch-all, paths-only message, endpoint tests assert HTTP 200 and attempts).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for 02-12 (decision record D-25 for NULL_VALUE and the null rejection; WR-01 enum strictness is the remaining VERIFICATION gap 2).
- Generated artifacts (`schema/`, `python/src/carimbo_models/`, `data/skeleton/`) are untouched (verified by `git diff` against 6b12d86).

## Self-Check: PASSED

- Files modified exist and commits `6e8a7bc`, `644a23d`, `3e40d7d`, `4d40ce9`, `c456e3a` are ancestors of HEAD.
- All acceptance criteria greps pass; `just dotnet-check` exit 0.

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
