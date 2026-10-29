---
phase: 04-synthetic-dataset
plan: 06
subsystem: testing
tags: [dataset, long-text, confusable, d-05, exact-arithmetic, quota, determinism]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-05 eight strata, 90 bases, literal slot tables, declared item band; plan 04-09 ground-truth gates over every manifest case
provides:
  - quota.py: Cell.long_text_item_band (26, 30) on both multi cells, 18 long_text slots and 18 confusable slots, validation of both traits
  - dataset_spec.py: long party names, natOp and 100-120 code point descriptions built from whole words; confusable digit-swap pairs, a many-digit line and similar parties
  - python/tests/test_dataset_text_equals_xml.py: the D-05 test over every text-layer PDF plus the Decimal pt-BR formatter
  - exact header-sum, caps and mapped-pair tests; the literal slot table extended
  - data/dataset regenerated (91 cases; 18 long_text and 18 confusable bases changed)
affects: [04-07 full quota twins and CI subset, 04-08 DECISIONS and README, Phase 5 transposition and party-swap taxonomy]

actuals:
  tokens: 13500
  tasks: 2
  commits: 4
plan_head_before: 2e21732f324de14d1b1228c5ebc4addfa4f1dd62
plan_head_after: 3507ea10b7cd821a511232098c6b4264628d8a12

tech-stack:
  added: []
  patterns: [text-shaping traits draw from their own seeded RNG so other slots keep their bytes, placement search that preserves the invoice tax-code set, expected strings formatted from Decimal independent of the renderer]

key-files:
  created:
    - python/tests/test_dataset_text_equals_xml.py
  modified:
    - python/src/carimbo_datagen/quota.py
    - python/src/carimbo_datagen/dataset_spec.py
    - python/tests/test_dataset_spec.py
    - python/tests/test_dataset_ground_truth.py
    - data/dataset

key-decisions:
  - "long_text and confusable draw from a separate per-case text RNG (seed string master:case_id:text): the 72 other bases kept their bytes, and only the 36 trait bases changed"
  - "A confusable pair placement is accepted only if the invoice's set of tax codes is unchanged, so ST-deferral and mixed-code bases keep their traits; the many-digit line is an item outside the pairs, or the first row of the first pair when the invoice has four items"
  - "The mapped-pair test recognises a pair by the same rule as the generator (equal row, one value with two adjacent digits swapped, distinct codes), so the negative check over non-confusable bases is meaningful and holds for all 72"

patterns-established:
  - "Trait counts are asserted twice: literally in the quota table test and as manifest tag counts (28, 28, 34, 70, 20, 18, 18)"
  - "A graded string is checked as strip(NFC(value)) in strip(NFC(printed text)); amounts are formatted from the XML Decimal with integer grouping, never through float"

requirements-completed: []  # DATA-02 needs 150 or more cases and the final dataset-001 bytes (plan 04-07 widens the set); DATA-04 is proven here dataset-wide but the dataset is pre-publication until 04-07, so neither is closed by this plan

coverage:
  - id: D1
    description: "18 long_text bases (3+3 short, 6+6 multi) with party names, natOp and descriptions at the caps, whole words only; the 12 multi ones have 26 to 30 items and 2 or more pages"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_long_text_bases_have_long_names_natures_and_descriptions"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_long_text_multi_page_bases_have_26_to_30_items_and_two_or_more_pages"
        status: pass
    human_judgment: false
  - id: D2
    description: "D-05: every issuer and recipient name, natOp, item code, description, NCM, unit, quantity, unit price and total, the invoice total and the access key of all 90 text-layer PDFs is printed exactly as the XML says"
    requirement: DATA-04
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_text_equals_xml.py#test_every_graded_string_is_printed_exactly_as_the_xml_says"
        status: pass
    human_judgment: false
  - id: D3
    description: "18 confusable bases: two adjacent digit-swap pairs, an invoice total of at least 10^9, issuer and recipient in one UF sharing a name word with different tax ids; mapped pairs stay two consecutive items"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_confusable_bases_have_two_pairs_a_many_digit_total_and_similar_parties"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_ground_truth.py#test_confusable_pairs_map_to_two_consecutive_items_with_equal_descriptions"
        status: pass
    human_judgment: false
  - id: D4
    description: "Value caps (amounts below 10^13, 4-decimal values below 10^9) over all 90 specs, and exact header sums, vNF and installments over all 90 base XMLs"
    requirement: DATA-04
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_every_amount_stays_below_1e13_and_every_four_decimal_value_below_1e9"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_ground_truth.py#test_header_totals_equal_the_exact_item_sums_and_vnf_and_installments_are_exact"
        status: pass
    human_judgment: false
  - id: D5
    description: "Gates stay green: just dataset-check, just datagen-check, DatasetGroundTruthGateTests (548), just py-check (1689 passed), just dotnet-check (1795 passed)"
    verification:
      - kind: command
        ref: "just dataset-check && just datagen-check; just py-check; just dotnet-check"
        status: pass
    human_judgment: false

duration: 15min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 06: long text that wraps but never truncates, and confusable values, checked against the printed DANFE Summary

**Eighteen long_text and eighteen confusable synthetic invoices join the dataset under code-point and value caps, and a D-05 test proves every graded string of all 90 text-layer DANFEs is printed exactly as its XML says.**

## Performance

- **Duration:** about 15 min
- **Started:** 2026-10-29T14:33:00Z (approximate; the start time was not recorded at launch)
- **Completed:** 2026-10-29T14:47:07Z
- **Tasks:** 2 (1 tracer, 1 auto, both TDD)
- **Files modified:** 1 created, 5 modified (plus data/dataset regenerated: 36 trait bases and the manifest)

## Accomplishments

- `Cell.long_text_item_band` (26, 30) on normal-multi and simples-multi, 18 long_text slots (normal-short 5-7, simples-short 6-8, both multi 3-8) and 18 confusable slots (1, 1, 3, 3, 3, 3, 2, 2), all asserted literally and validated (long_text only in short or multi cells and never on a pinned multi slot; confusable never on a long_text slot, a 1-item pin or a curve slot).
- Long text: party names filled word by word to 55-60 code points including the SINTETICA suffix, natOp from six natures of 51-55 code points, descriptions of 100-120 code points from the catalogue description plus qualifier words of at most 20 code points. Every one of the 12 long-text multi bases renders 2 or more pages.
- Confusable: two non-overlapping adjacent pairs per base (same description, NCM, unit, CFOP and tax code, distinct product code, one value with two adjacent different digits swapped), one line with quantity 2000-9999 and unit price 500000-999999 so vNF reaches 10 integer digits (1.50e9 to 7.77e9 in the manifest), recipient in the issuer's UF with the issuer's first name word and a different business word.
- `test_dataset_text_equals_xml.py` checks 90 text-layer PDFs, five header strings plus seven per item, with a Decimal-only pt-BR formatter. All 90 pass on the first regeneration, so the caps keep BrazilFiscalReport's float formatting exact.
- Manifest tag counts: short 28, medium 28, many_items 34, single_page 70, multi_page 20, long_text 18, confusable 18.

## Task Commits

1. **Task 1 (tracer): long_text invoices and the D-05 test**
   - RED `c6c29ee` (test): 8 target tests failed on the planned assertions (slot table, band, validation, 18 long_text tags, multi pages, tag counts)
   - GREEN `efa95d9` (feat): quota and dataset_spec changes, data/dataset regenerated, lint fix of the D-05 test file
2. **Task 2: confusable rows, many-digit totals and similar parties**
   - RED `d24335a` (test): 7 target tests failed (slot table, two validation rules, 18 confusable tags, pair and total checks, mapped pairs)
   - GREEN `3507ea1` (feat): confusable generation and validation, data/dataset regenerated

**Plan metadata:** the docs commit that follows this file (summary, STATE, ROADMAP).

## Files Created/Modified

- `python/src/carimbo_datagen/quota.py` - `long_text_item_band`, the long_text and confusable slot tuples, their validation, `quota_document` now lists the band
- `python/src/carimbo_datagen/dataset_spec.py` - `_long_name`, `_long_description`, the long natOp list, `_similar_recipient`, `_swapped`, `_with_confusable_rows`
- `python/tests/test_dataset_text_equals_xml.py` - the D-05 test
- `python/tests/test_dataset_spec.py` - literal long_text and confusable tables, amended band test, length-tag and tag-count tests, caps, pair and party checks
- `python/tests/test_dataset_ground_truth.py` - exact-arithmetic test over 90 XML, mapped confusable pairs
- `data/dataset/` - 18 + 18 regenerated bases and the manifest (quota_sha256 changed)

## Decisions Made

See key-decisions. The quota change is a new dataset version in intent: the label stays `dataset-001` pre-publication until plan 04-07 commits the final bytes (CONTEXT D-17).

## Deviations from Plan

None - plan executed exactly as written. Two notes that are not deviations:

- The plan names `validate_quota`; the code's validator is `quota._validate`, called by `plan_cases`, and the new rules went there.
- The plan keeps the 04-05 band test "unchanged"; it is renamed to `test_declared_band_follows_the_long_text_band_then_the_mixed_rule` with the amended equality, and the item-count-inside-band test is untouched.

## Issues Encountered

- The RED commit of Task 1 (`c6c29ee`) carried one ruff RUF001 finding (literal no-break space in a test string) because the lint command was chained with `;` before the commit. It was fixed with `\N{...}` escapes in the GREEN commit `efa95d9`; no later commit was affected and `just py-check` is green.
- A first draft of the negative mapped-pair test (no pair in non-confusable bases) failed because it matched any adjacent same-description rows. It was tightened to the real rule (one value with two adjacent digits swapped) before the RED commit of Task 2.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for 04-07 (full quota twins, CI subset, widening towards 150 cases); the long_text and confusable slots are fixed, so 04-07 must keep curve bases off confusable slots (validation already enforces it).
- Plan 04-08 should record the digit-swap rule and the caps (names 60, natOp 60, descriptions 100-120, words 20, codes 12, amounts under 10^13, 4-decimal values under 10^9) in DECISIONS.

## Self-Check: PASSED

- Files: test_dataset_text_equals_xml.py, quota.py, dataset_spec.py, test_dataset_spec.py, test_dataset_ground_truth.py present; data/dataset holds 91 XML and 91 PDF plus manifest.json.
- Commits: c6c29ee, efa95d9, d24335a, 3507ea1 are ancestors of HEAD; `git rev-list --count` from the ledger base is 4.
- Acceptance: `grep -c long_text_item_band quota.py` is 13 and `grep -c '(26, 30)'` is 2; 90 D-05 cases pass; manifest has 18 long_text and 18 confusable text cases, each confusable total at least 1000000000.00; 90 exact-arithmetic cases pass.
- Plan gates: `just dataset-check` and `just datagen-check` print ok; DatasetGroundTruthGateTests 548 succeeded; `just py-check` 1689 passed; `just dotnet-check` 1795 passed.

---
*Phase: 04-synthetic-dataset*
*Completed: 2026-10-29*
