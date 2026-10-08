---
phase: 02-validated-extraction
plan: 05
subsystem: datagen
tags: [datagen, nf-e, danfe, regime-normal, simples-nacional, ipi, installments, cpf, alphanumeric-cnpj, manifest]

requires:
  - phase: 02-validated-extraction
    provides: "02-01 v2 Invoice contract and carimbo_evals.ground_truth.invoice_from_xml; 02-02 make_cpf, is_valid_cpf, DANFE-MAPPING.md"
provides:
  - "Three reworked skeleton cases: case-001 Simples CSOSN 101, case-002 Regime Normal (ICMS00/ICMS20, IPI, freight, discount, 2 installments), case-003 multi-page (alphanumeric issuer, CPF recipient, IE ISENTO)"
  - "Datagen spec and XML writer for CRT, natOp, IE, recipient tax-id kind, ICMS00/20/SN101/SN102, IPITrib, dest CPF/IE, cobr/fat/dup, computed totals"
  - "Manifest skeleton-002 with as_of_date 2026-10-01 and a spec-derived expected block per case"
  - "python/tests/test_ground_truth.py: Python reader vs manifest, plus mapping-rule unit tests"
affects: [02-06, 02-07, 02-08, 02-09, 02-10, live runs, response-cache fixtures]

plan_head_before: 77aa59f3d81ab042f4be61811950ede344e0b889
plan_head_after: c9c19b4ccc0da59a493c5555b30bc67111ca3504

estimate:
  tokens: 85000
  raw_tokens: 85000
  tasks: 3
  confidence: low
actuals:
  tokens: 66000
  tasks: 3
  commits: 3

tech-stack:
  added: []
  patterns:
    - "Totals are computed once in CaseSpec with the formulas the validators check (vNF formula, half-up item taxes); the XML writer only formats them"
    - "Manifest expected blocks come from the CaseSpec, never from reading the XML back, so the independent Python reader is tested against them"
    - "Profile table (_Profile) drives per-case regime, tax-code cycle, IPI cycle, freight/discount, installments and payment code"

key-files:
  created:
    - python/tests/test_ground_truth.py
  modified:
    - python/src/carimbo_datagen/spec.py
    - python/src/carimbo_datagen/nfe_xml.py
    - python/src/carimbo_datagen/cli.py
    - python/tests/test_synthetic_only.py
    - python/tests/test_datagen.py
    - python/tests/test_grader.py
    - python/tests/test_e2e_fake.py
    - data/skeleton/case-001.xml
    - data/skeleton/case-001.pdf
    - data/skeleton/case-002.xml
    - data/skeleton/case-002.pdf
    - data/skeleton/case-003.xml
    - data/skeleton/case-003.pdf
    - data/skeleton/manifest.json

key-decisions:
  - "PartySpec.cnpj became PartySpec.tax_id (the recipient of case-003 is a CPF); CaseSpec.recipient_tax_id_kind says which"
  - "Freight and discount live on item 1 (prod/vFrete, prod/vDesc); CaseSpec.freight and discount are sums of the item values, so totals cannot drift from the items"
  - "Installments are computed from invoice_total: first is half-up(vNF/2), the last is the remainder, so they always sum exactly"
  - "MULTIPAGE_ITEMS = 50 rendered 2 pages on the first try, so it was not raised"
  - "indFinal is 1 for the CPF recipient (consumer), 0 otherwise; idDest and the CFOP first digit follow the issuer and recipient UFs"

requirements-completed: [DOM-03, DOM-02]

coverage:
  - id: D1
    description: "case-002 is a Regime Normal invoice (CRT 3) with ICMS00 and ICMS20 items, IPI on every item, freight 25.00, discount 10.00 and two installments summing to the invoice total"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_synthetic_only.py#test_case_002_is_a_regime_normal_invoice_with_ipi_freight_discount_and_installments"
        status: pass
      - kind: unit
        ref: "python/tests/test_datagen.py#test_case_002_xml_carries_regime_normal_groups_ipi_and_duplicates"
        status: pass
      - kind: unit
        ref: "python/tests/test_datagen.py#test_case_002_maps_to_a_strictly_valid_v2_invoice"
        status: pass
    human_judgment: false
  - id: D2
    description: "case-001 is Simples (CRT 1) on CSOSN 101 with pCredSN 3.10, zero ICMS columns and no installments"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_datagen.py#test_case_001_xml_has_icmssn101_with_a_half_up_credit_and_no_icms_totals"
        status: pass
      - kind: unit
        ref: "python/tests/test_datagen.py#test_case_001_maps_to_cst_0101_and_zero_icms_columns"
        status: pass
    human_judgment: false
  - id: D3
    description: "case-003 has an alphanumeric issuer CNPJ with IE ISENTO, a CPF recipient with no IE, CSOSN 102/400 items, renders to 2 pages and its Code 128 barcode decodes to the alphanumeric key"
    requirement: DOM-03
    verification:
      - kind: unit
        ref: "python/tests/test_synthetic_only.py#test_case_003_is_the_multi_page_alphanumeric_issuer_and_cpf_recipient_case"
        status: pass
      - kind: unit
        ref: "python/tests/test_datagen.py#test_danfe_code128_barcode_decodes_to_the_access_key"
        status: pass
      - kind: unit
        ref: "python/tests/test_datagen.py#test_page_counts_single_page_cases_and_multipage_overflow"
        status: pass
    human_judgment: false
  - id: D4
    description: "Every generated XML obeys the vNF formula and the item arithmetic the validators check; regeneration from the seed is byte-identical"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_datagen.py#test_xml_totals_obey_the_vnf_formula_and_item_arithmetic"
        status: pass
      - kind: other
        ref: "devenv shell -- just datagen-check"
        status: pass
    human_judgment: false
  - id: D5
    description: "Manifest skeleton-002 records as_of_date 2026-10-01, tags and a spec-derived expected block; the Python reader agrees with it and validates strictly on all three cases"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_ground_truth.py#test_mapped_invoice_agrees_with_the_manifest_expected_block"
        status: pass
      - kind: unit
        ref: "python/tests/test_ground_truth.py#test_manifest_expected_blocks_are_the_spec_computation"
        status: pass
      - kind: unit
        ref: "python/tests/test_ground_truth.py#test_as_of_date_is_later_than_every_issue_date"
        status: pass
    human_judgment: false
  - id: D6
    description: "A CPF recipient and an alphanumeric issuer round-trip over HTTP through the .NET parser and the grader on the reworked cases"
    requirement: DOM-03
    verification:
      - kind: e2e
        ref: "devenv shell -- just e2e"
        status: pass
    human_judgment: false
  - id: D7
    description: "All party data is synthetic: valid check digits, absent from nfelib samples, SINTETICA on every name, tpAmb 2 and the SEM VALOR FISCAL note"
    requirement: DOM-03
    verification:
      - kind: unit
        ref: "python/tests/test_synthetic_only.py#test_generated_identifiers_are_valid_and_absent_from_nfelib_samples"
        status: pass
      - kind: unit
        ref: "python/tests/test_synthetic_only.py#test_every_party_name_is_non_empty_and_marked_synthetic"
        status: pass
      - kind: unit
        ref: "python/tests/test_datagen.py#test_xml_is_utf8_with_declaration_homologation_and_no_signature"
        status: pass
    human_judgment: false

duration: 11min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 05: the skeleton cases become a Simples, a Regime Normal and a multi-page CPF case Summary

**Datagen reworked in place: case-001 Simples on CSOSN 101, case-002 Regime Normal with ICMS00/ICMS20, IPI, freight, discount and two installments, case-003 a 50-item multi-page invoice with an alphanumeric issuer CNPJ and a CPF recipient, all byte-reproducible with a skeleton-002 manifest carrying as_of_date and spec-derived expectations**

## Performance

- **Duration:** 11 min
- **Started:** 2026-10-08T14:49:56Z
- **Completed:** 2026-10-08T15:01:00Z
- **Tasks:** 3
- **Files modified:** 15 (1 created, 14 modified)

## Accomplishments
- `CaseSpec` and `ItemSpec` now carry the CRT, operation nature, issuer and recipient IE, recipient tax-id kind, per-item ICMS code, IPI rate, freight, discount and installments. The totals (`products_total`, `icms_base_total`, `icms_total`, `ipi_total`, `freight`, `discount`, `invoice_total`) are properties computed with the vNF formula and half-up item taxes, so the XML cannot disagree with the formulas the .NET validators check.
- `nfe_xml.py` writes ICMS00, ICMS20, ICMSSN101 and ICMSSN102 (CSOSN 102 and 400), `IPI/IPITrib`, `prod/vFrete` and `vDesc`, `dest/CPF` or `dest/CNPJ`, `dest/IE` with indIEDest, `cobr/fat/dup` and `ICMSTot` in the official element order (confirmed against nfelib's `leiauteNFe_v4.00.xsd`).
- case-003 renders to 2 pages with `MULTIPAGE_ITEMS = 50` and its Code 128 barcode decodes to the alphanumeric access key; the CPF and CNPJ values are check-digit valid and absent from the nfelib sample texts.
- The manifest is `skeleton-002` with `as_of_date` 2026-10-01 and an `expected` block per case computed from the `CaseSpec` (never from the XML). `test_ground_truth.py` proves the independent Python reader agrees on all three cases, validates strictly against the generated Pydantic model, and covers the mapping rules on minimal documents (13 tests).
- `just check` passes in full: dotnet-check (438 tests), py-check (253 tests), schema-check, datagen-check, e2e (4 tests over HTTP on the reworked cases), docs-check, secrets-check.

## Task Commits

1. **Task 1 (tracer): case-002 Regime Normal end to end from seed to graded e2e** - `c50b176` (feat)
2. **Task 2: case-001 CSOSN 101 and case-003 CPF/alphanumeric profiles** - `52a61e4` (test)
3. **Task 3: manifest skeleton-002, as_of_date, expected blocks, Python reader vs manifest** - `c9c19b4` (feat)

Tracer gate: the Task 1 verify (two pytest files and `just datagen-check`) and the HTTP e2e were re-run after the tracer commit's content was final, and passed before Task 2 started (tracer verified end-to-end, expanding).

## Files Created/Modified
- `python/src/carimbo_datagen/spec.py` - profiles, tax parameters, computed totals, installments, CPF recipient
- `python/src/carimbo_datagen/nfe_xml.py` - ICMS/IPI/dest/cobr writers and computed `ICMSTot`
- `python/src/carimbo_datagen/cli.py` - `skeleton-002`, `AS_OF_DATE`, new tags, `_expected`
- `python/tests/test_synthetic_only.py`, `test_datagen.py` - profile facts, XML arithmetic, mapped-invoice checks, manifest assertions
- `python/tests/test_ground_truth.py` - new: reader vs manifest and mapping rules
- `python/tests/test_grader.py`, `test_e2e_fake.py` - follow the moved and reworked cases (see Deviations)
- `data/skeleton/*` - regenerated with `just datagen` (never hand-edited)

## Decisions Made
See `key-decisions` above. None needs a `docs/DECISIONS.md` record: they refine D-16, which already specifies the case profiles.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] test_grader.py and test_e2e_fake.py depended on the old case contents**
- **Found during:** Task 1 (full pytest after regenerating the dataset)
- **Issue:** `test_ground_truth_of_case_001_follows_the_documented_mapping` hardcoded Phase 1 values (IE ISENTO, issue date 2026-01-20, total 183737.44, CSOSN 0102). The e2e edge-outcome test relied on case-002 having the alphanumeric issuer CNPJ for its lowercase-CNPJ reply, and on the Phase 1 dataset version string. Neither file is in the plan's `files_modified`.
- **Fix:** updated the case-001 mapping test to the new values; moved the lowercase-issuer edge reply to case-003 (now the alphanumeric case) and swapped the success and schema_invalid expectations between case-002 and case-003; the dataset version assertion became `skeleton-002` in Task 3.
- **Files modified:** `python/tests/test_grader.py`, `python/tests/test_e2e_fake.py`
- **Verification:** `just py-check` and `just e2e` pass
- **Commit:** `c50b176`, `c9c19b4`

### Plan wording notes

- **Task split.** The whole generator (ICMSSN101, CPF recipient, case-003 profile) was implemented in the Task 1 commit because the tracer regenerates all three cases and a half-built profile table would have left the dataset inconsistent. Task 2's commit therefore carries the case-001/case-003 tests and acceptance checks only; its acceptance criteria were re-run and pass.
- **TDD shape.** The `tdd="true"` tasks shipped tests together with (Task 1) or after (Task 2) the implementation, not as a separate failing RED commit; the plan type is `execute`, so the plan-level TDD gate does not apply. No RED evidence record was produced.
- **`ground_truth.py` unchanged.** Listed in `files_modified`, but the reworked cases exposed no mapping gap, so it was not touched.

**Total deviations:** 1 auto-fixed (Rule 3), 3 plan-wording notes
**Impact on plan:** none on scope; the extra test edits were required for a green `just check`.

## Issues Encountered
- The first draft of the case-001 XML test navigated to a parent element, which `xml.etree` does not support; rewritten to iterate `det` elements.
- Both generated cross-UF pairs for case-001 and case-002 (and case-003) came out interstate, so the CFOP first digit is 6 and idDest 2 in all three committed cases. The same-UF branch (5 / idDest 1) is covered by `test_cfop_first_digit_follows_the_party_ufs` only by construction, not by a committed case.

## Authentication Gates
None.

## Known Stubs
None. `installments == []` and the absent IPI and ICMS groups are the legitimate content of the Simples cases.

## Threat Flags
None. T-02-16 to T-02-18 are mitigated as planned (own generators, nfelib collision check extended to CPFs, SINTETICA names, tpAmb 2; byte-identity check; expected blocks from the spec). Residual risk, as in T-01-13: a check-digit-valid synthetic CPF could coincide with a real person's; the markers make the documents unmistakably synthetic. No package or lockfile change (T-02-SC).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The committed PDF bytes changed (D-16). Any live probe in 02-03 or later cache fixtures keyed on the old case PDF SHA-256 must be regenerated; Phase 3 had not yet keyed a cache on them.
- 02-07 can run the .NET validators over the three reworked XMLs and assert zero errors at reference date 2026-10-01 (`manifest.as_of_date`); the totals follow the same formulas by construction.
- 02-06 edits `grader.py` and `test_grader.py`; `test_grader.py` now holds the new case-001 values.

## Self-Check: PASSED

- Created and modified files exist: `python/tests/test_ground_truth.py`, `spec.py`, `nfe_xml.py`, `cli.py`, `data/skeleton/manifest.json` and the three regenerated PDFs (checked with `[ -f ]`).
- Task commits `c50b176`, `52a61e4`, `c9c19b4` are ancestors of HEAD; `check evaluation-scope --plan 02-05 --commits-only` resolves all three.
- Acceptance criteria of all three tasks re-run: PASS (ICMS20 2, IPITrib 4, dup 2, CRT 3 1, tpAmb 2, SEM VALOR FISCAL 1; CPF 1, ICMSSN101 3, IE ISENTO 1, MULTIPAGE_ITEMS 1; skeleton-002 1, as_of_date 1, expected 3, 13 test functions; `just e2e` exits 0).
- Plan verification: `devenv shell -- just datagen-check` and `devenv shell -- just check` exit 0.

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
