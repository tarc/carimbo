---
phase: 01-walking-skeleton
plan: 05
subsystem: extraction
tags: [json-schema, structured-outputs, schema-projection, budget, xunit-v3, snapshot-test]
status: complete

requires:
  - phase: 01-walking-skeleton
    provides: "CanonicalSchema.Export/Serialize and the SchemaExport tool (01-04); ExtractionContract and InvoiceExtractor (01-03)"
provides:
  - "ModelSchemaProjector.Project(JsonObject) and SchemaProjectionException: pure canonical-to-model-facing projection"
  - "SchemaBudget.{MaxOptional=24, MaxUnion=16, Counts, Count, EnsureWithin} and SchemaBudgetExceededException"
  - "schema/invoice.model.schema.json: committed model-facing schema, byte-identical to what the extractor sends"
  - "ExtractionContract.Default built from the projected schema, budget-checked, hashed over the exact serialized file"
  - "SchemaExport writes and --check verifies both schema files"
  - "Carimbo.Extraction.Tests project (xunit.v3) with SchemaProjectionTests"
affects: [01-09, 01-10, 01-12, 01-13]

actuals:
  tokens: 11500
  tasks: 2
  commits: 4
plan_head_before: 446590985f99a7f16239c4d038dd6bd31e5e17a4
plan_head_after: 2fb6648990aab806927f0b197ff9bcb7f260d060

tech-stack:
  added: []
  patterns:
    - "Projector rebuilds into new nodes (never mutates input), walks schema positions structurally so a property named like a stripped keyword is kept"
    - "Budget counter expands each $ref per use and is proven by synthetic schemas at 24/25 optional and 16/17 union"
    - "Sent bytes == committed file: contract hash is SHA-256 of the serialized projected schema, asserted against the file in a test and by SchemaExport --check"

key-files:
  created:
    - dotnet/src/Carimbo.Extraction/ModelSchemaProjector.cs
    - dotnet/src/Carimbo.Extraction/SchemaBudget.cs
    - dotnet/tests/Carimbo.Extraction.Tests/Carimbo.Extraction.Tests.csproj
    - dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs
    - schema/invoice.model.schema.json
  modified:
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/tools/SchemaExport/Program.cs
    - dotnet/tools/SchemaExport/SchemaExport.csproj
    - dotnet/Carimbo.slnx

key-decisions:
  - "title, description, pattern and format: date are kept in the model-facing schema; their acceptance by the live API stays [ASSUMED] until the 01-10 spike, with the fallback owned by 01-12"
  - "Projector rejects dangling $ref, non-local $ref, recursive $defs (direct or mutual) and allOf containing $ref, all with SchemaProjectionException"
  - "A schema carrying both oneOf and anyOf on one node is rejected rather than merged"
  - "Budget counts only definitions reachable from the root, expanded per use; unreferenced $defs entries do not count"

patterns-established:
  - "New schema output files are one more entry in the outputs array of SchemaExport/Program.cs"

requirements-completed: [DOM-06, DOM-08]

coverage:
  - id: D1
    description: "Projector strips every unsupported keyword (nested and in $defs), keeps minItems 0/1 only, rewrites oneOf to anyOf, closes every object, removes root $schema, never mutates its input, and rejects bad $ref, recursion and allOf with $ref"
    requirement: DOM-06
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs"
        status: pass
    human_judgment: false
  - id: D2
    description: "Budget counter passes at exactly 24 optional and 16 union properties, fails at 25 and 17, counts a shared $ref per use, treats type arrays and anyOf as unions, and counts the Phase 1 Invoice as 0/0"
    requirement: DOM-06
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs"
        status: pass
    human_judgment: false
  - id: D3
    description: "The extractor sends exactly schema/invoice.model.schema.json (bytes and SHA-256), and a stale committed file fails both dotnet test and SchemaExport --check"
    requirement: DOM-08
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs#The_contract_sends_exactly_the_committed_model_schema_bytes"
        status: pass
      - kind: command
        ref: "dotnet run --project dotnet/tools/SchemaExport -- --check"
        status: pass
    human_judgment: false
  - id: D4
    description: "The extraction contract change does not break the end-to-end tracer"
    requirement: DOM-08
    verification:
      - kind: command
        ref: "pytest python/tests/test_e2e_fake.py -m e2e"
        status: pass
    human_judgment: false

duration: 6 min
completed: 2026-10-05
---

# Phase 1 Plan 05: Model-facing schema projection within the structured-output budget Summary

**A pure `ModelSchemaProjector` derives the schema sent to the model from the canonical one, a proven-able-to-fail `SchemaBudget` guards the 24 optional / 16 union limits, and `schema/invoice.model.schema.json` is committed byte-for-byte equal to what `ExtractionContract.Default` sends.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-10-05T18:19:58Z
- **Completed:** 2026-10-05T18:26Z
- **Tasks:** 2 (both TDD)
- **Files:** 9 (5 created, 4 modified)

## Accomplishments

- Projector: removes `minimum`, `maximum`, `exclusiveMinimum/Maximum`, `multipleOf`, `minLength`, `maxLength`, `maxItems`, `uniqueItems`, `minProperties`, `maxProperties` everywhere, keeps `minItems` only for 0/1, rewrites `oneOf` to `anyOf`, forces `additionalProperties: false` on every object, drops the root `$schema`, preserves key order and leaves its input untouched.
- Budget counter: counts optional (not in `required`) and union (`anyOf` or type array) properties, expanding each `$ref` per use. Boundary tests prove it passes at 24/16 and fails at 25/17, and its exception message states both counts and both limits.
- The Phase 1 Invoice projection counts 0 optional and 0 union properties. The projection equals the canonical schema minus `$schema`.
- `ExtractionContract.Default` now builds from `ModelSchemaProjector.Project(CanonicalSchema.Export())`, calls `SchemaBudget.EnsureWithin`, serializes with `CanonicalSchema.Serialize` and hashes those bytes. This resolves the 01-04 handoff.
- `SchemaExport` writes and `--check`s `schema/invoice.model.schema.json`; I confirmed a one-byte change to the file makes `--check` exit 1 and the snapshot test fail, and restoring it returns both to green.

## Task Commits

1. **Task 1 RED:** `35d3401` test(01-05): failing tests for projector and budget counter (32 assertion failures against stubs)
2. **Task 1 GREEN:** `079c2c8` feat(01-05): pure schema projector and budget counter
3. **Task 2 RED:** `b82e83e` test(01-05): failing snapshot tests for the model-facing schema (4 failures, file absent)
4. **Task 2 GREEN:** `2fb6648` feat(01-05): send the committed model-facing schema, export it with SchemaExport

## TDD Gate Compliance

RED and GREEN commits exist for both tasks, in order. No REFACTOR commits were needed.

RED evidence is the console output of `dotnet test` (Microsoft.Testing.Platform), a format `gsd check tdd-red-evidence` has no adapter for, so the classifier was not run and this is self-assessed. Semantic assessment: Task 1 RED, 32 of 74 tests failed with `Assert.Equal` / `Assert.Throws` failures on the planned behaviour (stub projector returned a plain clone, stub counter returned 0/0, stub EnsureWithin never threw), and the Domain tests stayed green. Task 2 RED, 4 tests failed on the explicit assertion that `schema/invoice.model.schema.json` exists, so they failed for the intended reason (the file did not exist yet), not on a crash.

## Files Created/Modified

- `dotnet/src/Carimbo.Extraction/ModelSchemaProjector.cs` - projector and `SchemaProjectionException`
- `dotnet/src/Carimbo.Extraction/SchemaBudget.cs` - budget counter and `SchemaBudgetExceededException`
- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` - schema source of `ExtractionContract.Default` only
- `dotnet/tools/SchemaExport/{Program.cs,SchemaExport.csproj}` - second output file, reference to Extraction
- `schema/invoice.model.schema.json` - committed model-facing schema
- `dotnet/tests/Carimbo.Extraction.Tests/*` and `dotnet/Carimbo.slnx` - new test project in the solution

## Decisions Made

See `key-decisions` above. The notable one: `title`, `description`, `pattern` and `format: date` are intentionally left in the sent schema. Whether the live API accepts them is unconfirmed until the 01-10 spike; if it does not, 01-12 adds the documented fallback inside the projector.

## Deviations from Plan

None - plan executed exactly as written.

Notes that are not deviations: the project-level `.claude/CLAUDE.md` GSD workflow rule was satisfied by running inside `/gsd-execute-phase`. `RepoRoot.Find()` is `internal` to the Domain test assembly, so the Extraction tests carry their own small repo-root walk instead of a shared test-support project (a new project for one helper would be more than the plan asks for).

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-01-12 is mitigated as planned: pure projector, budget enforced at contract construction and in tests, and the snapshot test asserts the committed file equals the sent bytes. Wire-level equality on the real adapter remains for 01-12.

## Next Phase Readiness

Ready for 01-06 onward and for the 01-10 spike, which should send `ExtractionContract.Default.OutputSchemaJson` to the real API and record whether `pattern`, `format: date`, `title` and `description` are accepted. Edge-probe assumption carried forward: the budget is counted over the single extraction schema; if a later phase sends several strict schemas in one request their counts must be summed.

## Self-Check: PASSED

- Files present: ModelSchemaProjector.cs, SchemaBudget.cs, Carimbo.Extraction.Tests.csproj, SchemaProjectionTests.cs, invoice.model.schema.json
- Commits present: 35d3401, 079c2c8, b82e83e, 2fb6648
- `dotnet test` 74/74, `SchemaExport --check` exit 0, `dotnet format --verify-no-changes` exit 0, e2e tracer 3 passed
