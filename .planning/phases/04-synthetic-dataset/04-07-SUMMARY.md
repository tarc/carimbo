---
phase: 04-synthetic-dataset
plan: 07
subsystem: testing
tags: [dataset, image-only-twins, degradation-curve, ci-subset, manifest-invariants, size-budget, determinism]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-03 scan.py (RENDERINGS, render_twin, is_image_only, barcode_readable); plan 04-04 build_dataset_files and the manifest; plans 04-05 and 04-06 the 90 text-layer bases and their locked slot tuples
provides:
  - quota.py: eight explicit curve slots, TWIN_RENDERINGS = scan.RENDERINGS, the 20 CI cells in D-16 order with one predicate each, validate_quota rejects mei/long_text/confusable curve slots and too many explicit dev slots
  - dataset.py: build_case_files(seed, case_ids, out_dir), the single-case builder; build_dataset_files reuses the same per-case writers
  - data/dataset: 154 cases (90 text-layer bases, 64 image-only twins nfe-0091..nfe-0154 on eight curve bases), 19,368,019 bytes, 20-case dev-only ci_subset
  - python/tests/test_dataset_scans.py and test_dataset_manifest.py over the committed files
affects: [04-08 DECISIONS D-40, AGENTS.md and README dataset docs, Phase 5 degradation curves, Phase 6 CI subset and cache fixtures]

actuals:
  tokens: 11000
  tasks: 2
  commits: 4
plan_head_before: 97bb9509a19d04d9f0774917937327990c71cf08
plan_head_after: b41594b3bb05f778e394042f172945a4d39680a7

tech-stack:
  added: []
  patterns: [one dev case per CI cell chosen by the smallest seeded SHA-256 among the matches, text cells match the text rendering only because a twin repeats its base's tags, the decode DPI equals the scan DPI so a twin is never resampled before it is read, the single-case builder renders a twin's base in a temporary directory so a case's bytes never depend on what else was built]

key-files:
  created:
    - python/tests/test_dataset_scans.py
    - python/tests/test_dataset_manifest.py
  modified:
    - python/src/carimbo_datagen/quota.py
    - python/src/carimbo_datagen/dataset.py
    - python/src/carimbo_datagen/scan.py
    - python/tests/test_dataset_spec.py
    - python/tests/test_scan.py
    - data/dataset

key-decisions:
  - "SCAN_DPI and BARCODE_DECODE_DPI move from 200 to 220 together: the DANFE Code 128 is 0.29 mm per module, 2.3 pixels at 200 DPI, and eight of the 90 text DANFEs (nfe-0011, 0015, 0030, 0046, 0049, 0058, 0060, 0066) did not decode there while a sweep of 17 DPIs decoded all 90 at 204, 216, 220, 224, 232, 290 and 300; 220 is the centre of the 216-224 band and the only one where all eight image_clean twins also decode at the scan DPI"
  - "The decode DPI stays equal to the scan DPI: decoding an image_clean twin at 225, 250 or 300 DPI resampled the embedded raster and failed for most bases, so a separate higher decode DPI was not an option"
  - "Blur sigmas (1.2 and 2.4 pixels) and rotation angles keep their values; the blur sigma is defined in pixels at SCAN_DPI, so the physical blur is 9 percent weaker at 220 DPI and every blur twin is still barcode-unreadable"

patterns-established:
  - "A text or image_clean case that fails to decode is a generator bug fixed in scan.py constants, never by editing the flag; the tests assert text and image_clean decode and barcode_damaged does not, over the committed bytes"
  - "The size gate is a test: a warning above 18,000,000 bytes and a failure above 20,000,000, with the total taken from the files on disk"

requirements-completed: [DATA-02, DATA-03]  # DATA-05 and DATA-06 are also declared by plan 04-08, which has no SUMMARY yet, so requirements.ready-ids holds them back until it finishes

coverage:
  - id: D1
    description: "154 cases: 90 text-layer bases and 64 image-only twins, eight per curve base in RENDERINGS order, n = 8 per rendering slice, at least 10 percent alphanumeric issuer CNPJ, both regimes, single_page, many_items and multi_page present"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_the_dataset_has_154_cases_ninety_bases_and_sixty_four_twins"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_each_of_the_eight_twin_renderings_has_eight_cases"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_at_least_ten_percent_have_an_alphanumeric_issuer_cnpj"
        status: pass
    human_judgment: false
  - id: D2
    description: "Every non-text PDF has zero text objects, an empty text range, one image object per page and its base's page count; no twin leaks its case id, base id or access key, uses DCTDecode without ASCII85 and carries the invariant 1980 date"
    requirement: DATA-03
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_scans.py#test_twin_is_image_only_with_one_image_per_page_and_the_base_page_count"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_scans.py#test_twin_bytes_leak_no_identifier_and_carry_the_invariant_metadata"
        status: pass
    human_judgment: false
  - id: D3
    description: "barcode_readable recomputed from the committed bytes equals the manifest for all 154 cases; every text and image_clean case decodes, every barcode_damaged case does not; barcode_unreadable is tagged exactly when the flag is false"
    requirement: DATA-03
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_scans.py#test_barcode_flag_equals_a_decode_of_the_committed_bytes"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_barcode_unreadable_is_tagged_exactly_when_the_flag_is_false"
        status: pass
    human_judgment: false
  - id: D4
    description: "Splits: 30 of 90 bases and 54 of 154 cases are dev, every stratum has both splits, every twin has its base's split and a byte-identical XML; the 20-case ci_subset is dev only, sorted, recomputed equal and covers every cell"
    requirement: DATA-05
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_ci_subset_is_the_recomputed_selection_over_the_manifest_entries"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_no_ci_member_or_its_base_is_a_test_case"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_thirty_of_ninety_bases_and_fifty_four_of_all_cases_are_dev"
        status: pass
    human_judgment: false
  - id: D5
    description: "Ordering and order independence: sorted cases, tags and ci_subset; a separate process that builds nfe-0154, a multi-page base and nfe-0001 in that order writes the committed bytes; hashes, quota_sha256 and manifest hygiene hold"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_a_separate_process_building_cases_in_reverse_order_writes_the_committed_bytes"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_quota_sha256_equals_the_code"
        status: pass
    human_judgment: false
  - id: D6
    description: "data/dataset is 19,368,019 bytes (limit 20,000,000, warning 18,000,000), every file committed, a regeneration is byte-identical, and no evals/results file cites dataset-001"
    requirement: DATA-06
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_manifest.py#test_the_dataset_stays_within_the_size_budget"
        status: pass
      - kind: other
        ref: "devenv shell -- just dataset-check (ok: data/dataset matches a regeneration from seed 20261022)"
        status: pass
    human_judgment: false

duration: 27min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 07: 64 image-only twins on eight curve bases, the CI subset and the manifest invariants Summary

**154 committed cases (90 text-layer bases plus 64 image-only twins, eight renderings on each of eight single-page curve bases) in 19.37 MB, a 20-cell dev-only CI subset, and tests that hold the committed files to the manifest; the scan and barcode-decode DPI moved from 200 to 220 because eight text DANFEs did not decode at 200**

## Performance

- **Duration:** 27 min
- **Started:** 2026-10-29T14:49:00Z
- **Completed:** 2026-10-29T15:16:00Z
- **Tasks:** 2 (both TDD; 4 commits: 2 RED, 2 GREEN)
- **Files modified:** 7 source and test files, 2 created, 135 files in data/dataset touched (64 twin PDFs plus 64 twin XML copies, nfe-0091 rewritten, manifest)

## Accomplishments

- Eight curve bases fixed in quota.py exactly as the plan states (normal-short slots 0 dev and 2 test, normal-medium 4 and 5 test, simples-short 2 dev and 3 test, simples-medium 4 dev and 5 test): nfe-0001 (dev), 0003, 0019, 0020 (test), 0048 (dev), 0049 (test), 0064 (dev), 0065 (test). All single-page; none MEI, long_text or confusable. Their 64 twins are nfe-0091..nfe-0154, eight per base in RENDERINGS order. 64 of 154 cases (41.6 percent) are image-only; 54 of 154 are dev (30 bases plus 24 twins of the three dev curve bases).
- The 20 CI cells of CONTEXT D-16 with one predicate each; `select_ci_subset` found a dev case for every cell on the first run, so the CI-SUBSET-EMPTY-CELL stop never triggered and no locked trait slot tuple changed. The subset, by cell: regime_simples nfe-0057, regime_normal nfe-0029, multi_page nfe-0088, many_items nfe-0079, alphanumeric_cnpj nfe-0048, cpf_recipient nfe-0002, st_warning nfe-0038, image_clean nfe-0123, rotation_mild nfe-0140, rotation_severe nfe-0125, blur_mild nfe-0094, blur_severe nfe-0127, low_dpi_mild nfe-0096, low_dpi_severe nfe-0097, barcode_damaged nfe-0146, short nfe-0011, medium nfe-0017, mixed_tax_codes nfe-0037, long_text nfe-0053, confusable nfe-0077 (confusable had only 2 dev candidates, the tightest cell). Sorted: nfe-0002 0011 0017 0029 0037 0038 0048 0053 0057 0077 0079 0088 0094 0096 0097 0123 0125 0127 0140 0146.
- `build_case_files(seed, case_ids, out_dir)` writes only the named cases and `build_dataset_files` reuses the same writers; a separate process building nfe-0154, a multi-page base and nfe-0001 in that order writes bytes equal to the committed files.
- test_dataset_scans.py (over the committed bytes) and test_dataset_manifest.py (counts, slices, splits, CI subset recomputation, hashes, hygiene, size, ordering, order independence).

## Size budget (CONTEXT D-11, D-12)

`du -sb data/dataset` = **19,368,019 bytes** (limit 20,000,000, so no DATA-06-SIZE stop; above the 18,000,000 warning line, so the size test emits a pytest warning on every run, by design). The RESEARCH projection of about 15 MB (assumption A1) was too low: the twins alone come to 15.4 MB, because the 64 XML copies add 3.0 MB and the twins are larger than the 1.4 MB per set the research measured. Headroom to the cap is 632 KB.

| Part | Bytes |
|------|-------|
| text-layer PDFs (90) | 817,669 |
| XML (154, 64 of them byte copies) | 3,040,114 |
| manifest.json | 142,775 |
| image_clean (8) | 2,683,408 |
| rotation mild (8) | 2,754,939 |
| rotation severe (8) | 2,908,873 |
| blur mild (8) | 1,723,723 |
| blur severe (8) | 1,208,192 |
| low_dpi mild (8) | 944,473 |
| low_dpi severe (8) | 469,084 |
| barcode_damaged (8) | 2,674,769 |
| **Twin PDFs, 64** | **15,367,461** |
| **Total** | **19,368,019** |

## Measured wall times (CONTEXT D-13 threshold of roughly 3 minutes)

On the final 154-case dataset, `devenv shell` included:

| Command | Wall time |
|---------|-----------|
| `just dataset` (regenerate) | 52.6 s |
| `just dataset-check` | 50.9 s |
| `just dataset-check && just datagen-check` | 51.0 s |
| `just py-check` | 49.5 s (2805 passed, 13 deselected, 1 size warning) |
| `just dotnet-check` | 44.6 s (2173 passed) |

All well under 3 minutes. The size of the dataset, not its build time, is now the constraint plan 04-08 should record in D-40.

## Task Commits

1. **Task 1 (tracer, TDD): eight curve bases times eight renderings**
   - RED `1aa1727` test(04-07): failing curve-twin, image-only, barcode-flag and leak tests
   - GREEN `38d0ac7` feat(04-07): quota curve slots, scan DPI 220, data/dataset regenerated to 154 cases
2. **Task 2 (TDD): the 20-cell CI subset and the manifest invariants**
   - RED `6d66850` test(04-07): failing manifest invariant, CI subset and order-independence tests
   - GREEN `b41594b` feat(04-07): CI_CELLS and predicates, build_case_files, ci_subset in the manifest (only manifest.json changed under data/dataset)

**Plan metadata:** the docs(04-07) commit that adds this file.

## TDD Gate Compliance

`workflow.tdd_mode` is not enabled, so no RED evidence record was run through `gsd_run check tdd-red-evidence` (pytest has no supported report format). The gate order holds: `test(04-07)` precedes `feat(04-07)` twice.

- **RED 1 (Task 1):** `pytest python/tests/test_dataset_scans.py python/tests/test_dataset_spec.py`: 15 failed, 495 passed. The first failure was `test_the_dataset_has_154_cases_and_64_of_them_are_image_only` with `assert 91 == 154`; the others were the eight-per-rendering slices, the eight-per-base ordering, the 64-twin plan, the literal curve table, the 54-dev split, the 154-case manifest list, and eight `test_barcode_flag_equals_a_decode_of_the_committed_bytes[...]` cases. Semantic assessment: every failure was an assertion on planned behavior (case counts, curve slots, the decode invariant) against the 91-case dataset, none was an import, collection or fixture fault. The eight barcode failures were a real finding, not a wrong test: they were text-layer bases that do not decode at 200 DPI (see Deviations).
- **RED 2 (Task 2):** `pytest python/tests/test_dataset_manifest.py`: 3 failed, 233 passed. The failures were `test_ci_cells_are_the_twenty_cells_of_decision_d_16_in_order` (`'st_warning' != 'regime_simples'`), `test_ci_subset_has_twenty_sorted_unique_dev_cases` and the subprocess order-independence test (`CalledProcessError`, `build_case_files` did not exist). Semantic assessment: all three fail on the planned behavior; the other 233 pass because they hold already for the Task 1 dataset.
- **GREEN:** Task 1 `1788 passed` over scans, spec, scan and ground truth plus the .NET gate (926 passed, 154 cases); Task 2 `1991 passed` over the four dataset test files.
- **REFACTOR:** none.

## Files Created/Modified

- `python/src/carimbo_datagen/quota.py` - eight curve slots, `TWIN_RENDERINGS = RENDERINGS`, 20 `CI_CELLS` with predicates, curve validation
- `python/src/carimbo_datagen/dataset.py` - `_write_base`, `_write_twin`, `build_case_files`; `build_dataset_files` reuses them
- `python/src/carimbo_datagen/scan.py` - `SCAN_DPI` and `BARCODE_DECODE_DPI` 220 with the reason in a comment
- `python/tests/test_dataset_scans.py` - no-text-layer, one-image-per-page, barcode-flag, leak and metadata tests
- `python/tests/test_dataset_manifest.py` - DATA-02, DATA-05 and DATA-06 invariants, size, order independence
- `python/tests/test_dataset_spec.py`, `python/tests/test_scan.py` - literal curve table, 64-twin plan, 154 cases, 54 dev, DPI 220
- `data/dataset/` - 154 cases and the final manifest of dataset-001

## Decisions Made

See key-decisions. D-40 (plan 04-08) should record the eight curve bases, the 220 DPI decision with its sweep, the 19.37 MB size and the measured wall times.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Eight text DANFEs did not decode at 200 DPI; scan and decode DPI raised to 220**
- **Found during:** Task 1 RED (the new `test_barcode_flag_equals_a_decode_of_the_committed_bytes` failed for nfe-0011, 0015, 0030, 0046, 0049, 0058, 0060 and 0066, all text-layer bases the manifest had already flagged `barcode_unreadable`)
- **Issue:** The plan's invariant is that every text and image_clean case decodes, and that a failure is a generator bug fixed in scan.py constants or the DANFE writer, never in the flag. The DANFE Code 128 is about 0.29 mm per module (80.6 mm for 277 modules), 2.3 pixels per module at 200 DPI, and eight bases fell on the wrong side of the anti-aliasing. zxing-cpp binarizer, downscale and invert options did not help. A sweep over the 90 text PDFs found 8 failures at 200 DPI, 0 at 204, 66 at 208, 3 at 210, 2 at 212, 0 at 216, 220 and 224, 8 at 228, 0 at 232, 1 at 240, 250, 260 and 270, 5 at 280, 0 at 290 and 300. A higher decode DPI alone was no fix, because decoding an image_clean twin at 225, 250 or 300 DPI resamples the embedded 200 DPI raster and failed for most bases. A simulation of all 64 twins at 220 DPI had every image_clean twin readable and every barcode_damaged twin unreadable, and at 200 DPI nfe-0049 could not even be damaged (its barcode was not found).
- **Fix:** `SCAN_DPI = BARCODE_DECODE_DPI = 220` in scan.py (the centre of the 216-224 band), with the evidence in a comment. The DANFE writer is shared with data/skeleton and was left alone. The flag logic is unchanged; the 90 text bases and 8 image_clean twins now decode, the 8 barcode_damaged twins do not.
- **Files modified:** python/src/carimbo_datagen/scan.py, python/tests/test_scan.py (the two parameter assertions), data/dataset (regenerated)
- **Verification:** test_dataset_scans.py passes for all 154 cases; `just dataset-check` and the .NET gate pass
- **Committed in:** 38d0ac7
- **Consequences:** This changes parameters frozen by plan 04-03 (`generator.scan.scan_dpi` and `barcode_decode_dpi` in the manifest now read 220), raises twin sizes by about 17 percent (twins 13.2 MB at 200 DPI in simulation, 15.4 MB at 220), and leaves the blur sigma at the same pixel values, so blur is physically 9 percent weaker. Rotation severe is now barcode-unreadable for some bases and readable for others, which is what D-09 means by a general degradation that happens to kill the barcode. Plan 04-03's summary and the README still say 200 DPI where they mention it; 04-08 should correct them.

**2. [Rule 1 - Bug] test_dataset_spec.py had to change although the acceptance criterion says its diff is empty**
- **Found during:** Task 1 (tests first)
- **Issue:** The criterion protects the locked trait slot tuples. The same file asserts literally that only normal-short has a curve (`{0: "dev"}`), that the plan has one twin, that the manifest lists 91 cases and that nfe-0091 shares nfe-0001's split. Those assertions are the state this plan replaces and cannot stay.
- **Fix:** Replaced exactly those assertions with the eight-slot curve table, the 64-twin plan, 154 cases, 54 dev cases and the curve bases' splits. No trait slot tuple (st_deferral, mixed_tax_codes, alphanumeric, cpf, mei, long_text, confusable, pinned items) was touched; the CI-SUBSET-EMPTY-CELL condition that would have allowed it did not occur.
- **Files modified:** python/tests/test_dataset_spec.py
- **Committed in:** 1aa1727

**3. [Rule 3 - Blocking] CI_CELLS and its predicates moved from Task 1 to Task 2**
- **Found during:** Task 1
- **Issue:** The plan lists the CI cells under Task 2's behavior; keeping the two-cell tuple through Task 1 kept the Task 1 regeneration and its commit self-consistent (`select_ci_subset` needs a predicate for every cell).
- **Fix:** Task 1 touched only the curve and DPI constants; Task 2 introduced the 20 cells.

---

**Total deviations:** 3 (2 Rule 1, 1 Rule 3)
**Impact on plan:** Deviation 1 is the only one with consequences beyond this plan (frozen scan parameters, 17 percent larger twins). The invariant it protects is the plan's own, and the fix is in the place the plan names.

## Issues Encountered

- A first background sweep took about eight minutes because it decoded 90 PDFs at 17 DPIs; the final checks never re-run it.
- The size test emits a `UserWarning` on every pytest run (19.37 MB is above 18 MB). That is the designed behaviour, not a failure.

## Known Stubs

None.

## Threat Flags

None. No new network endpoint, auth path or file access beyond reading and writing under data/dataset.

## Threat model dispositions

- T-04-16 (dev/test leakage): mitigated; twins inherit the base split, the CI subset is dev only and recomputed, and each CI member's base is dev (`test_no_ci_member_or_its_base_is_a_test_case`).
- T-04-17 (identifiers inside twins): mitigated; the leak test runs over all 64 committed twins.
- T-04-18 (size): mitigated; 19,368,019 bytes, gate passed, warning at 18 MB.
- T-04-19 (byte drift): `just dataset-check` passes locally; the first green CI run remains plan 04-08's end-of-phase check.
- T-04-23 (locked tuples): no tuple changed; no stop needed.
- T-04-24 (eval run against dataset-001): `git grep -l 'dataset-001' -- evals/results` prints nothing at the data commit.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 04-08 (DECISIONS D-40, AGENTS.md and README dataset docs) can start; it should record the eight curve bases, DPI 220 and its sweep, the 19.37 MB size with 632 KB of headroom, the measured wall times above, and correct the "200 DPI" mentions in the 04-03 material.
- Adding more cases or raising DPI again would cross the 20 MB cap; the D-12 fallback is not planned.
- DATA-05 and DATA-06 are left unmarked until plan 04-08 finishes (shared IDs).

## Self-Check: PASSED

All created and modified files exist (checked with `[ -f ]`): test_dataset_scans.py, test_dataset_manifest.py, quota.py, dataset.py, scan.py, data/dataset/nfe-0154.pdf, manifest.json; 154 PDFs and 154 XML files on disk. Commits 1aa1727, 38d0ac7, 6d66850 and b41594b are ancestors of HEAD. Plan-level gates: `just py-check` and `just dotnet-check` exit 0, `just dataset-check && just datagen-check` print both ok lines, `du -sb data/dataset` is 19,368,019 (at most 20,000,000), `git grep -l 'dataset-001' -- evals/results` prints nothing.

---
*Phase: 04-synthetic-dataset*
*Completed: 2026-10-29*
