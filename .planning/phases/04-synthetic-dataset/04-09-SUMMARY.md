---
phase: 04-synthetic-dataset
plan: 09
subsystem: testing
tags: [ground-truth, gate, dotnet-validators, json-schema, manifest, ci]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-04 data/dataset tracer cases, manifest.json, quota.plan_cases, dataset_spec.build_dataset_spec, cli._expected
provides:
  - GroundTruthManifest: shared manifest reader (For, Name, AsOfDate, Cases, CaseOf, XmlPathOf, CaseIds) for the skeleton and dataset gates
  - DatasetGroundTruthGateTests: zero Error findings, expected block, Wire round trip, ST warning rule, file set, hashes and twin bytes per manifest case
  - test_dataset_ground_truth.py: Draft 2020-12 schema, strict model, expected agreement, quota-plan recomputation, file set, hashes, twin bytes, as_of_date
affects: [04-05, 04-06, 04-07 (every regeneration is gated), 04-08 README and verification]

actuals:
  tokens: 4500
  tasks: 2
  commits: 2
plan_head_before: 72a4f84e8c4d790650018213d608fb23c11be97e
plan_head_after: 69f8990d0548784cd4772e08979f687d3803b88a

tech-stack:
  added: []
  patterns: [manifest-driven TheoryData and pytest parameters so a failure names the case id, expected blocks recomputed from the quota plan and never from the XML]

key-files:
  created:
    - dotnet/tests/Carimbo.Validation.Tests/GroundTruthManifest.cs
    - dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs
    - python/tests/test_dataset_ground_truth.py
  modified:
    - dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs

key-decisions:
  - "Both gates iterate data/dataset/manifest.json, so the 154-case dataset of plans 04-05 to 04-07 is gated with no further test change"
  - "The Python expected-block test asserts the manifest case set equals the quota plan's case set before comparing, so a case cannot be skipped or unlisted"

patterns-established:
  - "A gate failure is a generator bug: the oracle (dotnet/src, schema) is never adjusted; no file under dotnet/src changed"

requirements-completed: []  # DATA-04 is also declared by plans 04-06 and 04-08 and completes once they finish

coverage:
  - id: D1
    description: "Every dataset case is mapped by NfeXmlMapper and has zero Error findings at the manifest as_of_date, one test per case id"
    requirement: DATA-04
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs#Mapped_ground_truth_has_no_validator_error_at_the_manifest_as_of_date"
        status: pass
    human_judgment: false
  - id: D2
    description: "Every dataset case validates against schema/invoice.schema.json (Draft 2020-12) and strictly against the generated Pydantic model"
    requirement: DATA-04
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_ground_truth.py#test_mapped_ground_truth_validates_against_the_exported_json_schema"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_ground_truth.py#test_mapped_ground_truth_validates_strictly_against_the_generated_model"
        status: pass
    human_judgment: false
  - id: D3
    description: "Expected blocks, Wire round trip, ST warning rule, file set, hashes and twin bytes are checked independently of the verdict; expected blocks come from the quota plan"
    requirement: DATA-04
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_ground_truth.py#test_expected_blocks_are_recomputed_from_the_quota_plan_never_from_the_xml"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs#Only_st_deferral_cases_carry_the_unsupported_tax_code_warning"
        status: pass
    human_judgment: false
  - id: D4
    description: "The gates run in CI through just dotnet-check and just py-check with no workflow change"
    requirement: DATA-04
    verification:
      - kind: command
        ref: "just py-check && just dotnet-check; grep -c 'run: just dotnet-check' and 'run: just py-check' in ci.yml each print 1"
        status: pass
    human_judgment: false

duration: 15min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 09: the DATA-04 ground-truth gates over every manifest case Summary

**The .NET mapper plus validators and the Python JSON Schema plus strict model now gate every case in data/dataset's manifest, with expected blocks recomputed from the quota plan, and both run in CI through the existing `just dotnet-check` and `just py-check` steps.**

## Accomplishments

- `GroundTruthManifest` reads `data/<dir>/manifest.json` once per directory and feeds `TheoryData` in manifest order; the skeleton `GroundTruthGateTests` moved onto it with every test name and fact unchanged.
- `DatasetGroundTruthGateTests` (Validation.Tests): zero Error findings at `as_of_date` with rule, field, expected and actual listed on failure; expected block; Wire round trip with no pattern violation; `st_deferral` cases carry a `TAX_CODE_UNSUPPORTED` warning and others carry no warning; case ids unique and `nfe-NNNN`; manifest set equals the XML and PDF stems on disk; XML SHA-256 matches; twin XML equals its base's bytes.
- `test_dataset_ground_truth.py`: Draft 2020-12 schema errors listed by path and message, strict `Invoice` model, expected agreement with the code-length rule, manifest set equals the quota plan, base expected equals `_expected(build_dataset_spec(DATASET_SEED, base))` and twins equal their base's, hashes, file set, twin bytes, `as_of_date` later than every issue date.
- Mutation check: changing `vNF` in nfe-0001.xml made the .NET gate fail with `TOTAL_VNF_FORMULA ... expected '416872.91' actual '416872.99'` naming the case; the file was restored with `git checkout -- <file>`.

## Task Commits

1. **Task 1 (tracer): verdict gates over every manifest case** - `864c466`
2. **Task 2: independent oracles and skeleton gate on the shared helper** - `69f8990`

## Verification

- Task verifies: Validation.Tests filtered classes passed (26 tests); pytest `test_dataset_ground_truth.py` and `test_ground_truth.py` 31 passed.
- Plan-end: `just py-check` (729 passed) and `just dotnet-check` (1261 passed, 0 failed) exit 0.
- Acceptance greps: `GroundTruthManifest.For("skeleton")` 2, `plan_cases` 2, `TAX_CODE_UNSUPPORTED` 1, ci.yml `just dotnet-check` 1 and `just py-check` 1. `git log --name-only` over the plan's commits lists no file under `dotnet/src`.

## Deviations from Plan

None - plan executed exactly as written. TDD note: the gates are test-only and the data already satisfied them, so there was no failing RED step to commit; the mutation check above shows the gate fails when the ground truth is wrong.

## Known Stubs

None.

## Threat Flags

None.

## Self-Check: PASSED

- Created files exist: GroundTruthManifest.cs, DatasetGroundTruthGateTests.cs, test_dataset_ground_truth.py.
- Commits 864c466 and 69f8990 are ancestors of HEAD.
