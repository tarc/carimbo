---
phase: 04-synthetic-dataset
plan: 04
subsystem: testing
tags: [dataset, quota, manifest, byte-identical-check, ci, determinism]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-01 tax groups and ST in spec.py/nfe_xml.py; plan 04-03 scan.py (render_twin, barcode_readable, scan_parameters)
provides:
  - quota.py: the quota table, case planner, seeded dev/test split and CI-subset selection, quota_sha256
  - dataset_spec.py: build_dataset_spec and the 32-entry DATASET_GOODS catalogue
  - dataset.py: build_dataset_files, check_dataset and the extended manifest (split, base_case, rendering, severity, barcode_readable, ci_subset, quota_sha256, generator.scan)
  - cli commands dataset-build and dataset-check, the shared _compare_directories, recipes dataset and dataset-check, a CI python-job step
  - data/dataset: nfe-0001 (Regime Normal ST invoice, text layer) and nfe-0002 (rotation-mild image-only twin, XML byte copy)
affects: [04-05 commercial details, 04-06 trait cells, 04-07 full quota and twins, 04-08 DECISIONS D-40 and README, 04-09 ground-truth gates]

actuals:
  tokens: 14000
  tasks: 2
  commits: 2
plan_head_before: 5d554f87d1d7b85f168fa8ce22980af5509dc8cc
plan_head_after: 92265842b3b2742589c223b637bee8d4a8627139

tech-stack:
  added: []
  patterns: [quota table as the single source of dataset shape, ids that name neither rendering nor base, spec-derived expected blocks, lazy import of the scan stack in dataset commands, regenerate-and-compare in a temp directory]

key-files:
  created:
    - python/src/carimbo_datagen/quota.py
    - python/src/carimbo_datagen/dataset_spec.py
    - python/src/carimbo_datagen/dataset.py
    - python/tests/test_dataset_check.py
    - data/dataset/manifest.json
    - data/dataset/nfe-0001.xml
    - data/dataset/nfe-0001.pdf
    - data/dataset/nfe-0002.xml
    - data/dataset/nfe-0002.pdf
  modified:
    - python/src/carimbo_datagen/cli.py
    - justfile
    - .github/workflows/ci.yml

key-decisions:
  - "The comparison loop of the skeleton check became _compare_directories(fresh_dir, directory), shared by check and dataset-check with the same messages and order"
  - "With st_deferral and two or more items the ST codes go on a proper non-empty subset, so the invoice always mixes tax codes (the base-family code stays on at least one item)"
  - "Simples cells draw their one code from 101, 102, 400 for now; plan 04-05/04-06 widen the code set"

patterns-established:
  - "Twins reuse the base's _expected(spec) and a byte copy of its XML; the manifest alone records base_case and rendering"
  - "build_dataset_files deletes unlisted *.xml and *.pdf so a quota change never leaves stale files"

requirements-completed: []  # DATA-05 and DATA-06 are completed when the full quota lands (plan 04-07) and the gates (plan 04-09); this plan delivers the tracer pipeline only

coverage:
  - id: D1
    description: "just dataset writes nfe-0001 (Normal, ST items, text layer) and nfe-0002 (rotation-mild image-only twin, byte-copy XML) and an extended manifest"
    requirement: DATA-05
    verification:
      - kind: command
        ref: "just dataset; manifest assertion script in the plan (prints 'manifest ok')"
        status: pass
    human_judgment: false
  - id: D2
    description: "just dataset-check regenerates into a temp directory and is byte-identical; it runs in just check and the CI python job"
    requirement: DATA-06
    verification:
      - kind: command
        ref: "just dataset-check && just datagen-check"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_check.py"
        status: pass
    human_judgment: false
  - id: D3
    description: "The comparison reports missing, extra and changed files by name and the command exits 1 on any difference"
    requirement: DATA-06
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_check.py#test_mixed_differences_come_in_sorted_name_order"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_check.py#test_dataset_check_exits_1_and_writes_the_differences_to_stderr"
        status: pass
    human_judgment: false
  - id: D4
    description: "The first green CI run of the new dataset-check step on the GitHub runner confirms cross-machine byte identity"
    requirement: DATA-06
    verification: []
    human_judgment: true
    rationale: "Local runs cannot prove byte identity on the runner (RESEARCH A2); the CI run after push is the arbiter, tracked as a backstop truth of plan 04-08"

duration: 5min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 04: tracer dataset, quota plan to CI-checked bytes Summary

**A quota table and seed now generate data/dataset (one ST Regime Normal invoice plus its rotation-mild image twin) with the extended manifest, and `just dataset-check` proves the bytes reproducible in `just check` and the CI python job.**

## Performance

- **Duration:** about 5 min
- **Tasks:** 2 (1 tracer, 1 auto)
- **Files:** 9 created, 3 modified

## Accomplishments

- `quota.py` plans nfe-0001 upward (bases in cell then slot order, twins after), splits each cell with seeded SHA-256 orderings, selects the CI subset from the dev cases, and hashes the quota document for the manifest.
- `dataset_spec.py` draws each base from a per-case `random.Random(f"{seed}:{case_id}")`, with eight-digit CEPs and the ST/deferral trait; nfe-0001 has 5 items mixing codes and CST 10/30/51/70.
- `dataset.py` writes XML, text DANFE and twin, computes `barcode_readable` from the final PDF bytes, and records `expected` from the spec (twin reuses its base's block). Manifest keeps the skeleton keys and adds split, base_case, rendering, severity, barcode_readable, ci_subset, quota_sha256 and `generator.scan`.
- `just dataset-check` regenerates into a temp directory and compares through `_compare_directories`; `check` ordering in the justfile and the CI step follow `datagen-check`.
- `test_dataset_check.py` proves identical, missing, extra, changed, absent-directory and mixed ordering cases plus the command's exit codes.

## Task Commits

1. **Task 1 (tracer): quota plan, spec, manifest, recipes, CI step, data/dataset** - `c974e3c`
2. **Task 2: comparison and command tests** - `9226584`

## Verification

- Task 1: `just dataset` printed "wrote 2 case(s) and manifest.json to data/dataset"; `just datagen-check` ok; test_repo_layout passes; manifest assertion script prints "manifest ok"; `cmp` of the twin XML is clean; grep criteria all 1.
- Tracer gate: `just dataset-check` and the Task 1 checks re-run green before Task 2 ("Tracer verified end-to-end").
- Task 2: 8 tests pass; `just dataset-check && just datagen-check` print their ok lines.
- Plan end: `just py-check` 715 passed; `just dotnet-check` 1247 passed.

## Deviations from Plan

None - plan executed exactly as written.

## Known Stubs

None.

## Next Phase Readiness

Ready for 04-05. The dataset-001 label is pre-publication per the D-17 qualification; no eval run should be recorded against it until 04-07 commits the final bytes. CI byte identity on the runner is unconfirmed until the first CI run.

## Self-Check: PASSED

All created files exist; commits c974e3c and 9226584 are on the branch.
