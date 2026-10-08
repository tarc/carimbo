---
phase: 02-validated-extraction
plan: 12
subsystem: domain
tags: [dotnet, system-text-json, enum, strict-parsing, schema-export, decision-record, gap-closure]

requires:
  - phase: 02-validated-extraction
    provides: Invoice.NullViolations and RuleIds.NULL_VALUE (plan 02-11), Wire.Options and CanonicalSchema (phase 1), eval endpoint contract 2 (02-09)
provides:
  - StrictEnumJsonConverter: Wire.Options reads an enum only from a JSON string equal ordinally to its snake_case wire name
  - Wire.EnumNames(Type): one name table shared by the converter and the exported schema enum node
  - Regression tests for every non-exact enum spelling at wire, extractor and HTTP level
  - Decision record D-25 and the docs-check update
affects: [phase-03-replay, phase-05-eval-tables]

actuals:
  tokens: 6324
  tasks: 3
  commits: 4

plan_head_before: ce21d4de1116af2734b301160cd3d82e600036ee
plan_head_after: 749262769a3dbec62ff8a0c1974a00bd68fb2a02

tech-stack:
  added: []
  patterns:
    - "A custom converter factory owns an enum's wire names and the schema node is built from the same table, so parser and schema cannot drift"
    - "Converter failures throw a message-less JsonException so System.Text.Json reports only the type and JSON path, never the model's value"

key-files:
  created: []
  modified:
    - dotnet/src/Carimbo.Domain/Wire.cs
    - dotnet/src/Carimbo.Domain/CanonicalSchema.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - docs/DECISIONS.md
    - justfile
    - AGENTS.md

key-decisions:
  - "Strictness is added on the parser side only: the committed schemas and generated Pydantic models are byte-identical (D-25)"
  - "Enum equality is ordinal on the decoded JSON string, the definition a schema validator uses, so a JSON-escaped spelling of cnpj still parses"
  - "D-25 records NULL_VALUE, null-element rejection and exact-name enums as one superseding entry; D-23 gets a Refined by line"

patterns-established:
  - "Enum schema nodes come from Wire.EnumNames in CanonicalSchema.TransformSchemaNode, next to the Money, Decimal4 and Rate branches"

requirements-completed: [DOM-02, API-01, VAL-01]

coverage:
  - id: D1
    description: "recipient.tax_id_kind outside the exact strings cnpj and cpf (integers, numeric strings, wrong case, padding, comma lists, true, null, {}) raises JsonException at Wire.Options and is schema_invalid in the extractor"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Strict_parsing_rejects_a_tax_id_kind_that_is_not_an_exact_wire_name"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#A_tax_id_kind_that_is_not_an_exact_wire_name_is_schema_invalid"
        status: pass
    human_judgment: false
  - id: D2
    description: "Every public Domain enum round-trips by wire name only; an escaped spelling parses; an undefined value is never written; the error names the JSON path and not the value"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Every_public_domain_enum_is_read_and_written_by_exact_wire_name_only"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#The_enum_parse_error_names_the_json_path_and_never_echoes_the_value"
        status: pass
    human_judgment: false
  - id: D3
    description: "POST /eval/extractions answers HTTP 200 schema_invalid with no invoice for a tax_id_kind of CNPJ or 0"
    requirement: API-01
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_tax_id_kind_outside_the_schema_is_schema_invalid_with_http_200"
        status: pass
    human_judgment: false
  - id: D4
    description: "The committed canonical and model-facing schemas and generated Pydantic models are byte-identical after the change"
    requirement: DOM-02
    verification:
      - kind: other
        ref: "just schema-check; dotnet run --project dotnet/tools/SchemaExport -- --check; git diff --exit-code 6b12d86 -- schema python/src/carimbo_models data/skeleton"
        status: pass
    human_judgment: false
  - id: D5
    description: "D-25 records both gap fixes and docs-check requires it"
    requirement: VAL-01
    verification:
      - kind: other
        ref: "just docs-check; git diff --numstat 6b12d86 -- docs/DECISIONS.md (0 deleted lines)"
        status: pass
    human_judgment: false

duration: 5min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 12: Wire.Options reads enums by exact wire name, and D-25 records both fixes Summary

**`StrictEnumJsonConverter` makes `Wire.Options` accept an enum only from a JSON string equal to its snake_case wire name, so integer and wrong-case `tax_id_kind` values are `schema_invalid` instead of `success`, with the committed schema byte-identical and D-25 recording both gap-closure fixes.**

## Performance

- **Duration:** about 5 min
- **Started:** 2026-10-08T20:22:51Z
- **Completed:** 2026-10-08T20:28Z
- **Tasks:** 3
- **Files modified:** 8 (2 production, 3 test, 3 docs/config)

## Accomplishments

- Review finding WR-01 (VERIFICATION gap 2) is fixed by this plan; CR-01 was fixed by plan 02-11. `tax_id_kind` set to `0`, `7`, `"CNPJ"`, `" cnpj"` and the rest no longer parses as a success while the Python grader marks the same output schema-invalid.
- `Wire.EnumNames(Type)` is the single name table: the converter reads and writes from it and `CanonicalSchema` builds the enum node (`type` string first, then `enum`) from it. `SchemaExport --check`, `SchemaSnapshotTests`, `just schema-check` and `git diff 6b12d86 -- schema python/src/carimbo_models data/skeleton` are all clean.
- Failures throw a message-less `JsonException`, so the message holds the type and `$.recipient.tax_id_kind` and never the model's value.
- D-25 added after D-24, D-23 carries `Refined by: D-25 (2026-10-08)` (38 lines added, 0 deleted in DECISIONS.md), `just docs-check` loops D-18 to D-25, and `just check` exits 0.

## Task Commits

TDD gate sequence:

1. **Task 1 (tracer): exact-name enum parsing** - `e3d73c9` (test, RED) then `8bd69da` (feat, GREEN)
2. **Task 2: enum spelling matrix at wire and HTTP level** - `365994f` (test; tests only, passed on first run because Task 1 shipped the behavior)
3. **Task 3: D-25 and docs-check** - `7492627` (docs)

**Plan metadata:** docs commit (this SUMMARY with STATE, ROADMAP and REQUIREMENTS)

## TDD evidence

- RED: both rows of `A_tax_id_kind_that_is_not_an_exact_wire_name_is_schema_invalid` (`"CNPJ"` and `0`) failed on `Assert.IsType` with expected `SchemaInvalid`, actual `Success`, the planned assertion. semanticAssessment: the target test executed and failed for the intended reason (the lax built-in converter). Run output was the MTP console report with exit code 2.
- GREEN: Domain 120, Extraction 134 tests pass after the converter; after Task 2 Domain 143 and Api 71 pass.
- No REFACTOR commit.

## Files Created/Modified

- `dotnet/src/Carimbo.Domain/Wire.cs` - `StrictEnumJsonConverter` (factory with a private per-enum converter), `Wire.EnumNames`, registration in `Wire.Options`, rewritten XML doc
- `dotnet/src/Carimbo.Domain/CanonicalSchema.cs` - enum branch built from `Wire.EnumNames`, replacing the type-first reordering block
- `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` - `A_tax_id_kind_that_is_not_an_exact_wire_name_is_schema_invalid`
- `dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs` - spelling matrix (17 values), round trip, escaped spelling, every-Domain-enum, undefined write, message tests
- `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - `A_tax_id_kind_outside_the_schema_is_schema_invalid_with_http_200`
- `docs/DECISIONS.md` - D-25 and the D-23 Refined by line
- `justfile` - docs-check requires D-25
- `AGENTS.md` - the docs-check row now says D-18 to D-25

## Decisions Made

- Strict converter in the Domain rather than integers disallowed plus a post-parse check: the built-in converter folds case and trims even with integers off, so the check would be hand-listed per enum field.
- Equality is ordinal after JSON unescaping, the same definition a schema validator uses (a `cnpj` spelling parses as `Cnpj`).
- Recorded as D-25 together with the plan 02-11 decisions (NULL_VALUE, null element rejection).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Consistency] AGENTS.md docs-check description**
- **Found during:** Task 3
- **Issue:** `AGENTS.md` still said `just docs-check` verifies "D-18 to D-24" after the recipe began requiring D-25 (the plan lists only DECISIONS.md and the justfile).
- **Fix:** changed the row to "D-18 to D-25"; the justfile comment line was updated the same way.
- **Files modified:** `AGENTS.md`, `justfile`
- **Committed in:** `7492627`

---

**Total deviations:** 1 auto-fixed (documentation consistency). **Impact on plan:** none on behavior.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-02-40 to T-02-42 are mitigated as registered: exact ordinal string-only enum reads with the spelling matrix, extractor and endpoint tests; schema node from the shared name table with byte-identity checks; message-less `JsonException` with a test that the value is absent.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Both VERIFICATION gaps are closed (null list elements in 02-11, lax enums here); `just check` exits 0. Ready for phase verification.
- Generated artifacts (`schema/`, `python/src/carimbo_models/`, `data/skeleton/`) are untouched relative to 6b12d86.

## TDD Gate Compliance

RED `e3d73c9` (test) precedes GREEN `8bd69da` (feat). No REFACTOR commit. Task 2 is a tests-only task whose behavior shipped in Task 1.

## Self-Check: PASSED

- Modified files exist; commits `e3d73c9`, `8bd69da`, `365994f`, `7492627` are ancestors of HEAD.
- All acceptance-criteria greps pass; `just check` exit 0; schema and Pydantic models byte-identical.

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
