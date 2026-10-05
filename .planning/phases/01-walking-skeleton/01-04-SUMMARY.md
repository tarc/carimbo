---
phase: 01-walking-skeleton
plan: 04
subsystem: domain
tags: [json-schema, schemaexporter, system-text-json, xunit-v3, snapshot-test, decisions]

requires:
  - phase: 01-walking-skeleton
    provides: "Domain records, Money and Wire.Options (01-02); Api, Llm, Extraction and ScriptedHost projects (01-03)"
provides:
  - "CanonicalSchema: pure exporter with Draft, Export(), ExportJson(), Serialize(JsonNode)"
  - "schema/invoice.schema.json: committed canonical JSON Schema (Draft 2020-12), Party hoisted into $defs"
  - "dotnet/tools/SchemaExport: writes schema files (LF, no BOM) and a --check staleness mode"
  - "Carimbo.Domain.Tests: purity, Money and strict-wire tests plus the snapshot staleness gate, run by solution-wide dotnet test"
  - "docs/DECISIONS.md entries D-18, D-19, D-20 with pointer lines under D-03, D-07, D-15"
affects: [01-05, 01-09, 01-12, 01-13]

actuals:
  tokens: 8400
  tasks: 3
  commits: 3
plan_head_before: d547435e268452772500c468aacc50909dc391d0
plan_head_after: 3e750f837bda0582e5cdad73e89f40f3b35a15e2

tech-stack:
  added: []
  patterns:
    - "JsonSchemaExporter over Wire.Options, so the schema describes exactly what the deserializer accepts"
    - "Schema files are byte-compared: committed file vs fresh export in a test, and via SchemaExport --check for CI"
    - "Hoist post-pass builds fresh nodes and DeepClones leaves; never re-parents"

key-files:
  created:
    - dotnet/src/Carimbo.Domain/CanonicalSchema.cs
    - dotnet/tools/SchemaExport/SchemaExport.csproj
    - dotnet/tools/SchemaExport/Program.cs
    - schema/invoice.schema.json
    - dotnet/tests/Carimbo.Domain.Tests/Carimbo.Domain.Tests.csproj
    - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
    - dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs
  modified:
    - dotnet/Carimbo.slnx
    - docs/DECISIONS.md

key-decisions:
  - "Money schema node is built in TransformSchemaNode before any object guard, and then still receives property attributes; it carries no title"
  - "A shared hoisted title with two different object shapes throws instead of silently overwriting"
  - "SchemaExport keeps a list of outputs so plan 01-05 can add the model-facing schema without changing the tool's shape"

patterns-established:
  - "RepoRoot.Find() (walk up to dotnet/Carimbo.slnx) is shared by all tests that read repo files"

requirements-completed: [DOM-01, DOM-05, RES-03]

coverage:
  - id: D1
    description: "A test fails if the Domain assembly references anything outside the BCL or its csproj gains a package or project reference"
    requirement: DOM-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Domain_assembly_references_only_the_base_class_library"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Domain_project_file_has_no_package_or_project_references"
        status: pass
    human_judgment: false
  - id: D2
    description: "Money rejects pt-BR and malformed amounts, round-trips 1234.50; strict parsing rejects unknown, missing, null and truncated JSON"
    requirement: DOM-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs"
        status: pass
    human_judgment: false
  - id: D3
    description: "Exporting twice is byte-identical and equals the committed schema/invoice.schema.json; the schema has Party in $defs, pattern-constrained total_amount, access_key and cnpj, closed objects and no Decision"
    requirement: DOM-05
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs"
        status: pass
      - kind: other
        ref: "dotnet run --project dotnet/tools/SchemaExport -- --check exits 0; running the export twice leaves git status schema clean"
        status: pass
    human_judgment: false
  - id: D4
    description: "docs/DECISIONS.md carries D-18, D-19, D-20 and pointer lines; the diff is 67 insertions and 0 deletions"
    requirement: RES-03
    verification:
      - kind: other
        ref: "grep acceptance checks and git diff --numstat docs/DECISIONS.md (67 0)"
        status: pass
    human_judgment: false

duration: 5min
completed: 2026-10-05
status: complete
---

# Phase 1 Plan 04: Canonical JSON Schema exported from the C# records

**A pure `CanonicalSchema` exporter turns the Domain records into a committed, byte-stable Draft 2020-12 schema, guarded by a snapshot test and a `--check` tool mode; Domain purity and strict wire behaviour are now tests; D-18..D-20 are recorded.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-10-05T18:04:30Z
- **Completed:** 2026-10-05T18:10Z
- **Tasks:** 3
- **Files modified:** 9 (7 created, 2 modified)

## Accomplishments

- `Carimbo.Domain.Tests` (xUnit v3) enforces BCL-only references, a reference-free Domain csproj, Money's two-decimal invariant form (rejects `12,34`, `1.234,56`, `12.3`, `12.345`, `1e3`, leading space, empty, bare JSON number) and strict parsing (unknown, missing, null name, truncated).
- `CanonicalSchema.Export()` runs `JsonSchemaExporter` over `Wire.Options`, with Money as `{"type":"string","pattern":...}` (no title), attribute `Description` and `RegularExpression` copied, objects closed, and `Party` hoisted to `$defs`. The output matches the research Pattern 2 sample.
- `schema/invoice.schema.json` is committed with LF endings, no BOM and a single trailing newline. `SchemaExport --check` and `Export_equals_the_committed_schema_file` both fail when it drifts, and the failure message names the regeneration command.
- Solution-wide `dotnet test` now runs 32 tests; `dotnet format --verify-no-changes` is clean. `Carimbo.slnx` lists Api, Domain, Extraction, Llm, Domain.Tests, ScriptedHost and SchemaExport.
- D-18, D-19 and D-20 appended before "Open questions"; one pointer line under each original D-03, D-07 and D-15 heading; 67 insertions, 0 deletions.

## Task Commits

1. **Task 1: Domain test project proving purity and strict wire behaviour** - `55b6d28` (test)
2. **Task 2: Pure canonical schema exporter, SchemaExport tool and committed snapshot** - `8e7478f` (feat)
3. **Task 3: Superseding decision entries D-18, D-19, D-20** - `3e750f8` (docs)

**Plan metadata:** committed with this SUMMARY (docs: complete plan)

## TDD Notes

- **Task 1 (characterization):** the tests exercise 01-02 code that already existed, so they passed on the first run (20 of 20). This is an unexpected-green by construction, not a feature RED; no Domain code needed fixing. The plan's own "fix Wire.cs if a test fails" branch was not triggered.
- **Task 2:** the tests were written before the exporter. After the exporter and tool existed but before `schema/invoice.schema.json` was generated, the run showed 31 passed and 1 failed (`Export_equals_the_committed_schema_file`, `DirectoryNotFoundException` for the missing file), which is the RED for the snapshot gate. The exporter's own structural tests passed on the first implementation. A compile-failing RED commit was not made, because it would have broken the solution build at that commit; red and green are in the same task commit per the plan's one-commit-per-task shape.

## Files Created/Modified

- `dotnet/src/Carimbo.Domain/CanonicalSchema.cs` - pure exporter (BCL only; Domain csproj still has no references)
- `dotnet/tools/SchemaExport/*` - console tool, default writes, `--check` verifies
- `schema/invoice.schema.json` - committed canonical schema
- `dotnet/tests/Carimbo.Domain.Tests/*` - project, DomainTests, SchemaSnapshotTests (shared `RepoRoot` helper)
- `dotnet/Carimbo.slnx` - Domain.Tests and SchemaExport added (Api and ScriptedHost were already added by 01-03)
- `docs/DECISIONS.md` - D-18, D-19, D-20 and three pointer lines

## Decisions Made

See `key-decisions` in the frontmatter. No change to any locked decision beyond the accepted D-18..D-20 records.

## Deviations from Plan

### Auto-fixed Issues

None - the plan executed as written. Two notes that are not deviations:

- The plan asked to add Api and ScriptedHost to `Carimbo.slnx`; plan 01-03 had already done so (its Deviation 1), so only Domain.Tests and SchemaExport were added.
- `dotnet format --verify-no-changes` passed without a one-off `dotnet format` run, so no whitespace fixes to 01-02 or 01-03 files were needed.

## Issues Encountered

- `git diff --exit-code -- schema` is vacuous while `schema/` is untracked; it was re-run after the Task 2 commit and is clean.

## Handoff Notes for Later Plans

- `InvoiceExtractor.ExtractionContract.Default` (01-03) still exports its own schema with a bare `JsonSchemaExporter.GetJsonSchemaAsNode(Wire.Options, typeof(Invoice))` call and plain `ToJsonString`. That is not the canonical schema and not the model-facing projection. Plan 01-05 (projector) should replace it with `CanonicalSchema.Serialize` over the projected node so the hash covers the exact file sent to the model.
- `CanonicalSchema.Serialize(JsonNode)` is the single serializer for committed schema files; add new files to the `outputs` array in `SchemaExport/Program.cs`.
- DOM-08 is only partly delivered (canonical schema staleness gate). The generated-Pydantic staleness check belongs to plans 01-09 and 01-13, so DOM-08 is not marked complete here.

## Known Stubs

None.

## Threat Flags

None. No new network endpoints, auth paths or trust-boundary file access; the tool only writes `schema/*.json` under the repo root.

## Self-Check: PASSED

- FOUND: dotnet/src/Carimbo.Domain/CanonicalSchema.cs, dotnet/tools/SchemaExport/Program.cs, schema/invoice.schema.json, dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs
- FOUND commits: 55b6d28, 8e7478f, 3e750f8
