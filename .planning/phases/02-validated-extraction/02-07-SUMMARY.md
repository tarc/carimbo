---
phase: 02-validated-extraction
plan: 07
subsystem: ground-truth
tags: [dotnet, nf-e, xml-mapper, ground-truth, validators, xxe, doc-drift, json-schema]

requires:
  - phase: 02-validated-extraction
    provides: "02-01 v2 Invoice records and Wire.Options; 02-02 docs/DANFE-MAPPING.md; 02-04 InvoiceValidator; 02-05 reworked skeleton cases and manifest skeleton-002 with as_of_date"
provides:
  - "Carimbo.GroundTruth: BCL-only NfeXmlMapper.Load/Map and NfeMappingException implementing docs/DANFE-MAPPING.md once in .NET"
  - "GroundTruthGateTests: the D-17 gate that maps every manifest case and asserts zero Error findings at the manifest as_of_date"
  - "NfeXmlMapperTests (13) pinning the mapping rules and the hostile-XML refusal"
  - "MappingDocTests: the 42 schema leaf paths equal the DANFE-MAPPING.md rows in both directions"
affects: [02-08, 02-09, 02-10, phase-04-datasets]

plan_head_before: 1cfb1e0cb078935994764bf5ac381ca5e3b4cf52
plan_head_after: 3e937710c910d95fb75b8c6f7fb0d503bcb19842

estimate:
  tokens: 60000
  raw_tokens: 60000
  tasks: 2
  confidence: low
actuals:
  tokens: 14000
  tasks: 2
  commits: 3

tech-stack:
  added: []
  patterns:
    - "The ground-truth gate iterates data/skeleton/manifest.json, so Phase 4 reuses it at dataset scale"
    - "Doc-drift test walks the canonical schema ($ref into $defs, arrays as name[].) and compares to backticked first-column table cells"
    - "XML loaded through XmlReader with DtdProcessing.Prohibit and XmlResolver null"

key-files:
  created:
    - dotnet/src/Carimbo.GroundTruth/Carimbo.GroundTruth.csproj
    - dotnet/src/Carimbo.GroundTruth/NfeXmlMapper.cs
    - dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs
    - dotnet/tests/Carimbo.Validation.Tests/NfeXmlMapperTests.cs
    - dotnet/tests/Carimbo.Validation.Tests/MappingDocTests.cs
  modified:
    - dotnet/Carimbo.slnx
    - dotnet/tests/Carimbo.Validation.Tests/Carimbo.Validation.Tests.csproj
    - dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs

key-decisions:
  - "The mapper is a src project (not test-only code) so Phase 4 DATA-04 reuses it; it references only Carimbo.Domain and adds no package"
  - "Access key is the Id attribute minus the NFe prefix only; the doc's strip-spaces/uppercase wording is for model output and the mapper does not infer or normalise (matches the Python reader)"
  - "A blank required element counts as missing, and malformed integers, decimals and dates raise NfeMappingException naming the element rather than a raw FormatException"

requirements-completed: [DOM-02, VAL-03, VAL-04]

coverage:
  - id: D1
    description: "NfeXmlMapper maps every committed skeleton XML to an Invoice by the documented rules, implemented once in .NET with DTD processing prohibited"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/NfeXmlMapperTests.cs (13 tests)"
        status: pass
    human_judgment: false
  - id: D2
    description: "The ground-truth gate finds zero Error findings from InvoiceValidator on all three manifest cases at as_of_date 2026-10-01"
    requirement: VAL-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs#Mapped_ground_truth_has_no_validator_error_at_the_manifest_as_of_date"
        status: pass
    human_judgment: false
  - id: D3
    description: "Mapped invoices match the manifest expected block and round-trip through Wire.Options with no pattern violations"
    requirement: VAL-04
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs#Mapped_ground_truth_matches_the_manifest_expected_block"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs#Mapped_ground_truth_round_trips_through_the_wire_options_without_pattern_violations"
        status: pass
    human_judgment: false
  - id: D4
    description: "The 42 leaf paths of schema/invoice.schema.json equal the table rows of docs/DANFE-MAPPING.md in both directions"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/MappingDocTests.cs#Every_schema_leaf_is_a_row_of_the_mapping_doc_and_every_row_names_a_schema_leaf"
        status: pass
    human_judgment: false
  - id: D5
    description: "A DOCTYPE is refused with XmlException and a missing nNF raises NfeMappingException naming the element"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/NfeXmlMapperTests.cs#A_document_with_a_doctype_is_refused_and_nothing_is_resolved"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/NfeXmlMapperTests.cs#A_document_without_nNF_throws_a_mapping_exception_naming_the_element"
        status: pass
    human_judgment: false

duration: 12min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 07: ground truth and validators agree before any model is involved Summary

**BCL-only `NfeXmlMapper` in the new `Carimbo.GroundTruth` project maps all three committed skeleton XMLs by the DANFE-MAPPING.md rules with zero InvoiceValidator errors at 2026-10-01, with a manifest-driven gate, 13 rule tests, an XXE-safe loader and a schema-to-doc drift test**

## Performance

- **Duration:** about 12 min
- **Completed:** 2026-10-08T15:17Z
- **Tasks:** 2 (3 commits: RED, GREEN, Task 2)
- **Files modified:** 8 (5 created, 3 modified)

## Accomplishments
- `NfeXmlMapper.Load(path)` opens the file through `XmlReader` with `DtdProcessing.Prohibit` and `XmlResolver = null`; `Map(XDocument)` implements the documented rules (CSOSN for CRT 1 and 4, else CST; ISENTO verbatim; absent tax tags 0.00; four-decimal quantity and unit price half-up; issue date from the first 10 characters of `dhEmi`; installments from `cobr/dup`). `NfeMappingException` names the missing or malformed element.
- The D-17 gate maps every case listed in `manifest.json`, asserts no Error finding at `as_of_date`, checks the `expected` block (item and installment counts, invoice total, issuer CNPJ, recipient tax id and kind) and round-trips through `Wire.Options` with empty `PatternViolations()`. All three cases passed on the first run, so the datagen formulas and the .NET validators agree.
- The doc-drift test collects 42 schema leaf paths (following `$ref` into `$defs`, arrays as `items[].ncm`) and compares them with the backticked first cells of the mapping table, listing missing and extra paths on failure.

## Task Commits

1. **Task 1 (tracer): skeleton XML to Invoice with zero validator errors** - `e0277bb` (test, RED: gate plus a NotImplemented mapper stub, 11 failures), `93f5b5e` (feat, GREEN: mapper, 164 tests pass)
2. **Task 2: mapper rule tests and schema to mapping-doc drift test** - `3e93771` (test)

Tracer gate: the Task 1 verify (`dotnet test` of Carimbo.Validation.Tests plus `dotnet build -warnaserror` and `dotnet format --verify-no-changes`) passed after the GREEN commit before Task 2 started (tracer verified end-to-end, expanding).

**Plan metadata:** the docs commit that carries this file.

## Files Created/Modified
- `dotnet/src/Carimbo.GroundTruth/Carimbo.GroundTruth.csproj` - project, references Carimbo.Domain only
- `dotnet/src/Carimbo.GroundTruth/NfeXmlMapper.cs` - mapper and exception
- `dotnet/Carimbo.slnx` - project added under /src/
- `dotnet/tests/Carimbo.Validation.Tests/Carimbo.Validation.Tests.csproj` - reference to Carimbo.GroundTruth
- `dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs` - `RepoFiles.PathOf` helper
- `dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs`, `NfeXmlMapperTests.cs`, `MappingDocTests.cs` - new tests

## Decisions Made
See `key-decisions`. None needs a `docs/DECISIONS.md` record: they apply D-05 and D-17 as written.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] RepoFiles had no way to build a repo path**
- **Found during:** Task 1
- **Issue:** The gate and doc tests must read `data/skeleton/*`, `schema/` and `docs/`, but `RepoFiles` only exposed vector reads and kept `Root` private. `VectorTests.cs` is not in the plan's `files_modified`.
- **Fix:** added `RepoFiles.PathOf(params string[] segments)`.
- **Files modified:** `dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs`
- **Verification:** all 180 tests in the project pass; `just check` exits 0
- **Committed in:** `e0277bb`

### Plan wording notes

- **TDD shape.** The plan type is `execute`, so the plan-level TDD gate does not apply. Task 1 was still run RED then GREEN with a compiling NotImplemented stub (11 failures for the right reason). Task 2's tests passed immediately because the mapper already implemented the rules (the one fix needed was in the test's own schema walker, which assumed `type` is always a string).

**Total deviations:** 1 auto-fixed (Rule 3)
**Impact on plan:** none on scope.

## Issues Encountered
- The first doc-drift run failed because some schema nodes declare `type` as an array (nullable unions); the walker now uses `TryGetValue<string>`.

## Authentication Gates
None.

## Known Stubs
None. The `NotImplementedException` stub from the RED commit was replaced in the GREEN commit.

## Threat Flags
None. T-02-21 is mitigated (`DtdProcessing.Prohibit`, no resolver, DOCTYPE test expecting `XmlException`); T-02-22 is mitigated (printed-form rules only, the gate fails on any error finding or manifest mismatch); no package added (T-02-SC).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- `Carimbo.GroundTruth` is available to later plans (and Phase 4) as a project reference; the gate in `GroundTruthGateTests` already iterates the manifest.
- Handoff observed: the validators find zero errors on all three skeleton XMLs at 2026-10-01 (all interstate CFOP 6xxx; case-003 alphanumeric issuer CNPJ, IE ISENTO, CPF recipient with no IE).

## Self-Check: PASSED

- Created and modified files exist (checked with `[ -f ]`): the five created files and the three modified files listed above.
- Task commits `e0277bb`, `93f5b5e`, `3e93771` are ancestors of HEAD; `git rev-list --count` from the plan base measured 3.
- Acceptance criteria re-run: PackageReference count 0; `Carimbo.GroundTruth` in slnx 1; `DtdProcessing.Prohibit` 1; `as_of_date` in gate 4; `DANFE-MAPPING.md` in MappingDocTests 3; `NfeMappingException` in NfeXmlMapperTests 4; 13 `[Fact]` tests (at least 9); `dotnet build -warnaserror` and `dotnet format --verify-no-changes` exit 0.
- Plan verification: `dotnet test --project tests/Carimbo.Validation.Tests` passed (180 tests); `devenv shell -- just check` exited 0 (including schema-check, datagen-check, e2e, docs-check, secrets-check).

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
