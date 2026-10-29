---
phase: 04-synthetic-dataset
plan: 05
subsystem: testing
tags: [dataset, quota, strata, tax-codes, party-traits, mei, discount-freight, installments, determinism]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-04 quota.py, dataset_spec.py, dataset.py, dataset-check; plan 04-09 ground-truth gates over every manifest case
provides:
  - quota.py: eight strata (normal and simples x short, medium, many_items, multi_page), 90 bases, pinned edge counts, literal trait slot tuples, the declared item band, stricter validation, MEI as CRT 4
  - dataset_spec.py: tax-code pools per regime family, mixed and ST-deferral assignment, party traits, MEI codes, IPI, per-item discount and freight spread to the header, installments
  - spec.py: public installment_plan(count, issue_date, invoice_total, offsets)
  - python/tests/test_dataset_spec.py and dataset tests in test_synthetic_only.py
  - data/dataset regenerated: nfe-0001..nfe-0090 plus the rotation-mild twin nfe-0091
affects: [04-06 long text and confusable values, 04-07 full quota twins and CI subset, 04-08 DECISIONS and README]

actuals:
  tokens: 13000
  tasks: 2
  commits: 5
plan_head_before: 18625d4460968d836e0ee747839a92950ea3b9ea
plan_head_after: c1b6b3dbab80bca8a3aefbf2246d1e78d9baa88b

tech-stack:
  added: []
  patterns: [literal slot tables asserted by tests so a quota change is a reviewed test change, declared per-base item band, spread-in-cents with caps for header sums, page band asserted never predicted]

key-files:
  created:
    - python/tests/test_dataset_spec.py
  modified:
    - python/src/carimbo_datagen/quota.py
    - python/src/carimbo_datagen/dataset_spec.py
    - python/src/carimbo_datagen/spec.py
    - python/tests/test_synthetic_only.py
    - data/dataset

key-decisions:
  - "Dataset item magnitudes are capped (quantity up to 60, unit price up to 499, so an item total stays under about R$ 31,000) because the DANFE item table wraps a value from about R$ 60,000 onto a second line and a 40-item single-page base then spills onto page 2; the page assertion stayed, the goods changed"
  - "A mixed_tax_codes slot gets a declared band with its low end raised to 2; a band that cannot hold two items is rejected by plan_cases"
  - "Header discount is at most a tenth of products_total and at least 5.00 (none when a tenth is below 5.00); freight 10.00 to 500.00; both are spread in cents over item subsets, so ICMSTot vDesc and vFrete always equal the item sums (SEFAZ 535 and 537)"

patterns-established:
  - "Trait slot tuples live in the quota table and again as literals in test_dataset_spec.py"
  - "Items are drawn with the case RNG first, then IPI, then freight and discount, then installments, so each stage is reproducible alone"

requirements-completed: []  # DATA-02 needs 150 or more cases (plans 04-06/04-07 widen the set; this plan makes 91); DATA-05 is closed by the manifest and CI subset of 04-07/04-08

coverage:
  - id: D1
    description: "Eight strata hold 14, 14, 7, 10, 14, 14, 7, 10 bases with pinned 1, 9, 25, 26, 40, 50 and 80 item edges, every one inside its page band"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_quota_has_the_eight_strata_in_order"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_manifest_pages_lie_in_the_stratum_page_band"
        status: pass
    human_judgment: false
  - id: D2
    description: "Tax-code traits: 14 ST/deferral bases, at least 30 mixed bases, one regime family per invoice, MEI codes only 102, 103, 400"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_normal_bases_use_only_cst_and_simples_bases_only_csosn"
        status: pass
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_manifest_trait_tags_have_the_exact_base_counts"
        status: pass
    human_judgment: false
  - id: D3
    description: "Party traits: 24 alphanumeric issuer CNPJ (26.7 percent), 4 alphanumeric recipient CNPJ, 18 CPF recipients, 4 MEI"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_party_trait_counts_hold_exactly"
        status: pass
    human_judgment: false
  - id: D4
    description: "Discount and freight spread over items sum exactly to the ICMSTot header in all 91 committed XML; IPI on Normal bases only; installments sum to vNF"
    requirement: DATA-02
    verification:
      - kind: unit
        ref: "python/tests/test_dataset_spec.py#test_icmstot_vdesc_and_vfrete_equal_the_item_sums"
        status: pass
      - kind: command
        ref: "plan Task 2 committed-XML header-sum script (prints 'header vDesc and vFrete equal the item sums in 91 XML; 33 with a per-item vDesc')"
        status: pass
    human_judgment: false
  - id: D5
    description: "Synthetic-only evidence over the 90 dataset specs and the stratified split (30 dev, both splits per stratum); dataset-check, datagen-check and both ground-truth gates stay green over 91 cases"
    requirement: DATA-05
    verification:
      - kind: unit
        ref: "python/tests/test_synthetic_only.py#test_no_dataset_cnpj_cpf_or_access_key_repeats_across_bases"
        status: pass
      - kind: command
        ref: "just dataset-check && just datagen-check; just py-check (1485 passed); just dotnet-check (1795 passed)"
        status: pass
    human_judgment: false

duration: 17min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 05: 90 base invoices across the regime and length strata, with tax-code and party traits Summary

**The quota table now fills eight regime-by-length strata with 90 synthetic bases (plus the tracer twin), exact ST, mixed-code, alphanumeric-CNPJ, CPF-recipient and MEI counts, and discount and freight spread over items so the header sums match what SEFAZ requires.**

## Performance

- **Duration:** about 17 min
- **Tasks:** 2 (1 tracer, 1 auto, both TDD)
- **Files:** 1 created, 5 modified (plus data/dataset regenerated to 91 cases)

## Accomplishments

- `quota.py` holds the eight Cells with the literal trait slot tuples of the plan, pinned edge counts (1; 9 and 25; 26 and 40; 50 and 80), `_declared_band` (mixed slots raised to 2 items) and `validate` rules for st_deferral outside Normal, mixed on a 1-item pin, mei outside simples-short and simples-medium, cpf and alphanumeric recipient on one slot, and cells under 2 bases. MEI slots are built with CRT 4.
- `dataset_spec.py` assigns one code family per invoice (CST pool, ST pool only through st_deferral, CSOSN pool, MEI pool 102/103/400), parties by trait, IPI on Normal bases at 40 percent, freight and discount at 35 percent spread in cents with per-item caps, and 1 to 3 installments at 40 percent via `installment_plan` (offsets 30, 60, 90).
- Results over the 90 bases: 14 ST/deferral, 40 with two or more distinct codes, 24 alphanumeric issuer, 4 alphanumeric recipient, 18 CPF recipient, 4 MEI, 15 with IPI, 33 with freight or discount, 37 with installments, 70 one-page and 20 two-page PDFs, 30 dev bases (5, 5, 2, 3 per regime).
- `test_dataset_spec.py` asserts the slot tables as literals, bands, code families, trait counts from specs and the committed manifest, header sums in every committed XML and PDF page counts; `test_synthetic_only.py` extends the identifier, uniqueness, marker, order-independence and seed checks to the 90 dataset specs.

## Task Commits

1. **Task 1 RED:** strata, band, code-family and split tests - `8be5079`
2. **Task 1 GREEN:** eight strata with tax-code traits, data/dataset regenerated - `f07a32f`
3. **Task 2 RED:** party, MEI, commercial and synthetic-only tests - `810697c`
4. **Task 2 GREEN:** party traits, MEI, per-item discount and freight, IPI, installments, magnitude cap, data/dataset regenerated - `cd1e930`
5. **Pyright annotation fix:** pools typed as `tuple[str, ...]` - `c1b6b3d`

## Verification

- `just dataset-check` and `just datagen-check` print their ok lines (skeleton bytes unchanged after the `installment_plan` refactor).
- test_dataset_spec.py, test_synthetic_only.py, test_dataset_ground_truth.py: 795 passed; DatasetGroundTruthGateTests: 548 passed over the 91 cases.
- Committed-XML check: "header vDesc and vFrete equal the item sums in 91 XML; 33 with a per-item vDesc".
- Plan end: `just py-check` 1485 passed; `just dotnet-check` 1795 passed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Single-page 40-item bases spilled onto a second page**
- **Found during:** Task 2 (`just dataset` stopped at nfe-0031, then nfe-0033, exit 2 on the page band)
- **Issue:** Two causes, found by measurement. A 1 to 3 row installment block lowers the one-page capacity from 40 to 39 items; and independently, an item total of about R$ 60,000 or more wraps in the DANFE item table, each wrapped row costing a line, so a 40-item invoice with eight such rows left the page. The first is cured by the second, as the cap leaves headroom for the installment block.
- **Fix:** Per the plan ("fix the band or the goods, never the assertion"), `_item` in dataset_spec.py draws quantity up to 60 and unit price up to 499 (`_MAX_QUANTITY`, `_MAX_UNIT_PRICE`). The page assertion and the 26-40 band are unchanged. No installment gating was needed.
- **Files modified:** python/src/carimbo_datagen/dataset_spec.py
- **Commit:** cd1e930

**2. [Rule 1 - Bug] Pyright rejected the literal-typed code pools**
- **Found during:** plan-end `just py-check`
- **Fix:** annotated the pool tuples as `tuple[str, ...]`; output bytes unchanged (`just dataset-check` ok).
- **Commit:** c1b6b3d

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug). **Impact:** the dataset's item values are smaller than the plan's 04-04 draw (no band, count or trait changed).

## Known Stubs

None.

## Issues Encountered

- The CI subset of the regenerated manifest is nfe-0038 (a two-page normal-multi ST base, st_warning cell) and nfe-0091 (rotation mild twin of nfe-0001). The `st_warning` cell now picks from all dev ST bases, including multi-page ones; plan 04-07 owns the CI subset cells and the curve slots.
- Neither requirement is closed here: DATA-02 needs 150 or more cases and DATA-05 needs the final manifest and CI subset (plans 04-06 to 04-08).

## Next Phase Readiness

Ready for 04-06 (long-text band (26, 30) for the multi cells amends only the band-equality assertion in test_dataset_spec.py, as planned) and 04-07. The dataset-001 label stays pre-publication until 04-07 commits the final bytes.

## Self-Check: PASSED

- test_dataset_spec.py and data/dataset/manifest.json exist; 91 XML and 91 PDF files in data/dataset.
- Commits 8be5079, f07a32f, 810697c, cd1e930, c1b6b3d are on gsd/phase-04-synthetic-dataset.
