---
phase: 04-synthetic-dataset
plan: 01
subsystem: testing
tags: [datagen, nfe, icms, st, csosn, xsd, nfelib, brazilfiscalreport]

requires:
  - phase: 04-synthetic-dataset
    provides: 04-02 supply-chain gate, so uv run syncs only from the approved lock
  - phase: 01-walking-skeleton
    provides: the shared generator (spec.py, nfe_xml.py), the Python mapper and the three skeleton profiles
provides:
  - ItemSpec/CaseSpec support for every CST and CSOSN the .NET validators classify, ICMS ST and CST 51 deferral included
  - ICMS10/30/40/51/60/70/90 and ICMSSN102/500/900 writers in the NF-e 4.00 XSD order
  - ICMSTot vBCST and vST, and vNF with ST, exact to the cent
  - python/tests/test_tax_groups.py, a per-code XSD, DANFE, mapper and arithmetic test
  - CRT 4 (MEI) treated as the Simples family
affects: [04-04 phase tracer, 04-05 case builder, 04-06 and 04-09 dataset gates]

actuals:
  tokens: 9000
  tasks: 2
  commits: 4
plan_head_before: 92e008ee55ee58936cf388ceaf9673b40e609a2b
plan_head_after: 7f36804be87118c407c759bab95870ff000b3b00

tech-stack:
  added: []
  patterns: [additive zero-default spec fields keep the skeleton bytes, XSD as a unit test of the writers with a dummy signature and digits-only CEP]

key-files:
  created: [python/tests/test_tax_groups.py]
  modified: [python/src/carimbo_datagen/spec.py, python/src/carimbo_datagen/nfe_xml.py]

key-decisions:
  - "ST, deferral and reduction parameters live in tables keyed by code without the origin digit (_ST_MVA, _ST_RATE, _DEFERRAL_RATE, _ICMS_REDUCTION); every new key is outside the codes the skeleton uses, so data/skeleton did not move"
  - "icms_amount is now icms_operation_amount minus deferred_amount; the two are identical for every code without deferral"
  - "CST 30 stays out of _ICMS_RATE, so it carries no vBC or vICMS of its own and its vICMSST is the full ST value"

requirements-completed: []  # DATA-02 and DATA-04 are only enabled here (tax-code coverage); they complete with the dataset and gate plans of phase 4

duration: 7min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 01: every validator-known tax code, ST and deferral, written, rendered and read back Summary

**The generator now writes CST 00/10/20/30/40/41/50/51/60/70/90 and CSOSN 101/102/103/300/400/500/900 (CRT 1, 3 and 4) as NF-e 4.00 XSD-valid XML that prints on the DANFE, maps through invoice_from_xml to a strict, schema-valid Invoice, and keeps vBCST, vST and vNF exact; data/skeleton is byte-identical.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-10-29T13:55:30Z
- **Completed:** 2026-10-29T14:02:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Task 1 (tracer): one CST 10 item with ICMS ST crosses spec, XSD-valid XML, DANFE (`<ncm> 010 <cfop>` adjacent) and the mapper; header vBCST and vST equal the item sums and vNF = vProd - vDesc + vST + vFrete + vSeg + vOutro + vIPI exactly. The tracer was verified end to end (6 tests, `just datagen-check`) before the expansion task started.
- Task 2: the remaining tables and writers. 21 single-code specs (11 CST under CRT 3, 7 CSOSN under CRT 1, 3 CSOSN under CRT 4) each pass the XSD, the printed header (CSOSN for CRT 1 and 4, CST for CRT 3), the mapper values, the strict model and the Draft 2020-12 schema, plus an exact header-sum and vNF check.
- Taxed codes (CST 00, 20, 90, CSOSN 900) satisfy vICMS = round_half_up(vBC x pICMS / 100); the not-taxed groups have no vICMS element; CST 51 writes vICMSOp, pDif, vICMSDif and the net vICMS; CST 70 puts pRedBC before vBC; CST 30 has no vBC or vICMS.
- An 11-item CRT 3 invoice cycling every CST and a 7-item CRT 1 invoice cycling every CSOSN are XSD-valid and map with one code length each (3 and 4).
- DATA-04 empty edge: items with no ICMS amount, no IPI group and no installments map to 0.00 or an empty list and still pass the model and the schema.
- The stale nfe_xml.py docstring (XSD as the Phase 4 dataset gate) now states that the gate is the JSON Schema plus the .NET validators, and the XSD is a unit-test tool.

## TDD Gate Compliance

Both tasks ran RED, GREEN, with no separate refactor commit.

- RED 1 (`f7a1917`): 6 tests failed on `AttributeError: st_base_total` and `KeyError: '10'`, i.e. the planned ST support was missing; no import, syntax or fixture fault. Semantic assessment: the target tests executed and failed because the behavior did not exist.
- GREEN 1 (`c79f2af`): 6 passed, `just datagen-check` ok.
- RED 2 (`253ffb0`): 70 of 117 tests failed on `KeyError` for each missing code (`'103'`, `'30'`, `'51'`, `'900'`, `'90'`, `'70'`, `'60'`, `'500'`, `'50'`, `'41'`); the 47 already-green ones are the CST 00/10/20 and CSOSN 101/102/400 cases the tables already covered. Semantic assessment: same as above, the failures are the planned missing codes.
- GREEN 2 (`7f36804`): 117 passed; the XSD accepted the planned child order of every group on the first run, so RESEARCH A6 is resolved by test and no order fix was needed.

`gsd_run check tdd-red-evidence` was not run: the plans are `type: execute` and `workflow.tdd_mode` is not enabled in config.

## Task Commits

1. **Task 1 (tracer): CST 10 with ICMS ST** - `f7a1917` (test), `c79f2af` (feat)
2. **Task 2: remaining CSTs and CSOSNs, deferral, MEI** - `253ffb0` (test), `7f36804` (feat)

**Plan metadata:** recorded by the docs(04-01) commit that follows.

## Files Created/Modified

- `python/src/carimbo_datagen/spec.py` - ST/deferral/reduction tables, full `_ICMS_GROUP`, ItemSpec `st_mva`, `st_rate`, `st_base`, `st_amount`, `deferral_rate`, `icms_operation_amount`, `deferred_amount`, CaseSpec `st_base_total`, `st_total`, vNF with ST, `regime` treating CRT 4 as Simples
- `python/src/carimbo_datagen/nfe_xml.py` - `_icms_own`/`_icms_st` helpers and a dispatch over every group, ICMSTot vBCST and vST, `_MOD_BC_ST_MVA`, corrected docstring
- `python/tests/test_tax_groups.py` - `_xsd_errors` (dummy Signature), `_spec_with`, the tracer tests, the parametrized per-code tests and the mixed-code invoices

## Decisions Made

See key-decisions. Nothing contradicts a DECISIONS.md record; CONTEXT D-04 and D-18 are followed as written.

## Deviations from Plan

None - plan executed exactly as written.

Two small adjustments inside the planned files, neither a deviation from behavior: the `_spec_with` helper gained an optional `count` argument so one spec can cycle 11 codes over the 4-item case-002, and the tests type mapped invoices as `dict[str, Any]` to satisfy pyright.

## Issues Encountered

- `just py-check` first failed pyright on the new test (`dict[str, object]` passed to jsonschema); fixed by typing the mapped invoice as `dict[str, Any]`, then green (674 passed, 13 deselected).

## Verification

- `uv run --project python pytest python/tests/test_tax_groups.py -q`: 117 passed.
- `just datagen-check`: `ok: data/skeleton matches a regeneration from seed 20261004`.
- Existing skeleton modules (test_datagen, test_ground_truth, test_synthetic_only, test_danfe_labels): 109 passed.
- Plan-end gate `just py-check`: exit 0.
- Acceptance greps: `"ICMS10"` in spec.py 1, `st_total` in spec.py 2, `"vST": spec.st_total` in nfe_xml.py 1, ICMSSN900/ICMS51/ICMS70/ICMS30 lines in nfe_xml.py 4, `DATA-04 in Phase 4` 0, `_DEFERRAL_RATE` 2.

## Known Stubs

None.

## Threat Flags

None. The change adds writers to an offline generator; T-04-01 (skeleton bytes) and T-04-02 (oracle integrity) are mitigated by `just datagen-check` and the per-group XSD test.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- The case builder can draw mixed per-item codes of one regime family, including CST 10/30/51/70/90 and CSOSN 103/300/500/900, and the TAX_CODE_UNSUPPORTED warning path.
- Not yet covered here (later plans): the 150-case dataset, the dataset-wide .NET gate and the README task. The XSD check is deliberately a unit test, not a dataset gate, because the generator's CEP keeps its hyphen.

---
*Phase: 04-synthetic-dataset*
*Completed: 2026-10-29*

## Self-Check: PASSED

- `python/tests/test_tax_groups.py`, `python/src/carimbo_datagen/spec.py`, `python/src/carimbo_datagen/nfe_xml.py` exist.
- Commits `f7a1917`, `c79f2af`, `253ffb0`, `7f36804` are ancestors of HEAD.
