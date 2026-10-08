---
phase: 02-validated-extraction
verified: 2026-10-09T00:30:00Z
status: passed
score: 5/5 must-haves verified
covered_files:
  - .planning/phases/02-validated-extraction/02-01-PLAN.md
  - .planning/phases/02-validated-extraction/02-01-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-02-PLAN.md
  - .planning/phases/02-validated-extraction/02-02-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-03-PLAN.md
  - .planning/phases/02-validated-extraction/02-03-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-04-PLAN.md
  - .planning/phases/02-validated-extraction/02-04-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-05-PLAN.md
  - .planning/phases/02-validated-extraction/02-05-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-06-PLAN.md
  - .planning/phases/02-validated-extraction/02-06-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-07-PLAN.md
  - .planning/phases/02-validated-extraction/02-07-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-08-PLAN.md
  - .planning/phases/02-validated-extraction/02-08-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-09-PLAN.md
  - .planning/phases/02-validated-extraction/02-09-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-10-PLAN.md
  - .planning/phases/02-validated-extraction/02-10-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-11-PLAN.md
  - .planning/phases/02-validated-extraction/02-11-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-12-PLAN.md
  - .planning/phases/02-validated-extraction/02-12-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-13-PLAN.md
  - .planning/phases/02-validated-extraction/02-13-SUMMARY.md
  - docs/DANFE-MAPPING.md
  - dotnet/src/Carimbo.Domain/Invoice.cs
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  - python/src/carimbo_models/generated.py
  - python/tests/test_danfe_labels.py
  - schema/invoice.model.schema.json
  - schema/invoice.schema.json
covered_digest: "v3:sha256:21d83fb59434da9327e6d94aa3ed73f92056d909c669e8acbac20ae68f99c599"
behavior_unverified: 0
overrides_applied: 0
re_verification:
  previous_status: passed
  previous_score: 5/5
  gaps_closed:
    - "UAT G-02-1: docs/DANFE-MAPPING.md maps recipient.name to the RECEBEMOS DE receipt stub, explains the homologation notice, and corrects the stale issuer.ie note (plan 02-13)"
    - "UAT G-02-2: prompt extract-003 and the model-facing schema descriptions quote the printed totals labels, name the receipt stub for the recipient name and describe the CST or CSOSN column header (plan 02-13)"
  gaps_remaining: []
  regressions: []
---

# Phase 2: Validated Extraction Verification Report

**Phase Goal:** As a developer, I want to have every extraction target the full DANFE-visible invoice, pass deterministic validators and be repaired within a bounded budget, so that extracted invoices carry hard guarantees before anything is measured at scale.
**Verified:** 2026-10-09T00:30:00Z
**Status:** passed
**Re-verification:** Yes, after UAT gap-closure plan 02-13 (earlier report was dated after plans 02-11 and 02-12)

## Goal Achievement

The previous report certified the five ROADMAP success criteria after the CR-01 and WR-01 fixes. This pass (a) regression-checks those five, (b) verifies plan 02-13's must-haves against the code, the committed schemas, the mapping doc and the committed skeleton PDFs, and (c) confirms UAT gaps G-02-1 and G-02-2 are closed by evidence rather than by the SUMMARY. The 02-13 delta since c21d015 touches 10 non-planning files (docs/DANFE-MAPPING.md, Invoice.cs, InvoiceExtractor.cs, the two schemas, generated.py, three test files, one new test file); nothing else in `src` changed.

### UAT gap closure (independent evidence)

I extracted the text of the three committed DANFEs (`data/skeleton/case-00{1,2,3}.pdf`) with pypdfium2 in an isolated script and compared it with the new texts.

| Gap item | What the DANFEs print | What the code and doc now say | Status |
| -------- | --------------------- | ----------------------------- | ------ |
| G-02-1 / G-02-2.1 recipient name | All three PDFs print `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL` (homologation notice); the name appears after `DESTINATARIO:` in the stub: "Cassiano CONSTRUÇÕES SINTETICA LTDA", "Jesus DISTRIBUIÇÃO SINTETICA LTDA", "Sophia Pereira SINTETICA", each equal to the `dest/xNome` in the XML | Prompt line 55 of `InvoiceExtractor.cs`, the `Recipient.Name` description (and both schemas) and the mapping row name the RECEBEMOS DE stub, quote the notice, and forbid returning the notice as the name | CLOSED |
| G-02-2.2 totals labels | `BASE DE CÁLCULO DO ICMS ST`, `VALOR DO ICMS ST`, `VALOR DO IPI` present on all three; `VALOR TOTAL DO IPI` absent on all three | Prompt, `Invoice.cs` descriptions, both schemas, `generated.py` and the mapping rows use the printed labels. A grep for `ICMS SUBST.`, `ICMS S.T`, `SUBSTITUIÇÃO`, `VALOR TOTAL DO IPI` over `dotnet/src`, `python/src`, `docs`, `schema` finds nothing | CLOSED |
| G-02-2.3 CST or CSOSN | Header `CSOSN` on case-001 and case-003, `CST` on case-002 | Prompt, `CstCsosn` description, schemas and mapping row say "column headed CST or CSOSN"; the CRT note says CRT selects the header though its value is not printed | CLOSED |
| G-02-1 issuer.ie note | `ISENTO` only on case-003 | Mapping row now says digits on case-001 and case-002, `ISENTO` on case-003 | CLOSED |
| Prompt version | n/a | `ExtractionContract.Default.PromptVersion` is `extract-003`; `repair-001` unchanged; version history in the XML doc; a SHA-256 pin test covers both prompt texts. `extract-003` is asserted in `ExtractorTests` (two tests), `EvalEndpointTests` (attempt `prompt_version` over HTTP) and `test_e2e_fake.py` (real host) | CLOSED |

Prohibition check (02-13 prohibitions): `git diff c21d015 HEAD` over `schema/*.json` shows only changes to `"description"` lines (six per file); no type, pattern, enum, required or structure change. `just schema-check` ran a fresh regeneration with no diff against the working tree. The live probe record `docs/spikes/02-schema-probe.md` and the synthetic `test_grader.py` records still say `extract-002`; that is historical evidence, correctly not rewritten, and no live paid recipe was run (judgment-tier prohibition: honoured, no measured claim for extract-003 appears anywhere).

### Observable Truths (ROADMAP success criteria)

| #   | Truth | Status | Evidence |
| --- | ----- | ------ | -------- |
| 1 | `Invoice` holds exactly the DANFE-visible fields; XML-to-DANFE mapping documented; CNPJ/key accept numeric and alphanumeric; money is pattern-constrained decimal strings; half-up rounding identical in C# and Python | VERIFIED | `Invoice.cs`, `docs/DANFE-MAPPING.md` (now matching the printed DANFE; `MappingDocTests` in the Validation project: 191 passed), `schema-check` clean, shared rounding vectors |
| 2 | One hand-curated vector file passes under both xUnit and pytest | VERIFIED (regression check) | `data/vectors/validator-vectors.json` read by `VectorTests.cs` and `test_vectors.py`; both in the green `just check` run reported by the orchestrator |
| 3 | For known-bad invoices validators never throw; they return structured errors for the listed rule families | VERIFIED (regression check) | Unchanged by 02-13 (no edits under `Carimbo.Validation`); `NeverThrowsTests` green within the 191 Validation tests I re-ran |
| 4 | Failed attempts retry with structured errors under a no-fabrication prompt; bounded by max (default 2); typed failure; scripted tests for first-try, repair, exhaustion | VERIFIED (regression check) | `repair-001` text and hash pinned unchanged; `RepairLoopTests` inside the 148 passing Extraction tests I re-ran |
| 5 | `POST /eval/extractions` returns validator outcomes and every attempt with output, validator results, tokens, cost, latency, result and trace ID | VERIFIED (regression check) | `EvalEndpointTests` asserts `attempts[0].prompt_version == "extract-003"`; pytest e2e over a real scripted host asserts `["extract-003", "repair-001"]` (orchestrator run: 5 e2e passed) |

**Score:** 5/5 truths verified (0 present, behavior-unverified)

Plan 02-13 must-have truths (all VERIFIED): recipient-name location in prompt (code line read); printed totals labels in prompt, schemas and doc; CST or CSOSN wording; version `extract-003` reaching HTTP records and a hash pin; mapping doc rows; `test_danfe_labels.py` reads the committed PDFs and XMLs (28 tests, I re-ran: 28 passed); description-only schema diff with byte-identical regeneration.

### Deferred Items

None.

### Advisory (New Scope, Unevidenced)

| # | Finding | Category | Why Advisory |
| - | ------- | -------- | ------------ |
| 1 | `RepairFeedback.cs:60` (rule `REGIME_CODE_MISMATCH`) still says "Re-read the CST column". It is repair feedback, not the extraction prompt, the schema or the doc, so it is outside 02-13's stated must-haves, but it is the same CST-only wording G-02-2 item 3 corrected elsewhere. A fix would change the repair text, needing a new `repair-` version under the pin test | other | wording only; the model in a repair turn also has the corrected extract-003 prompt and schema in the same conversation |
| 2 | 02-REVIEW.md WR-01 (two totals-label assertions are substring-weak: `VALOR DO ICMS` and `BASE DE CÁLCULO DO ICMS` match inside the `ST` labels, `DESCONTO` is a bare word) | other | test strength; my own PDF check shows the labels are in fact present and the `ST` labels are exact |
| 3 | 02-REVIEW.md WR-02 (a name containing ` - ` would be truncated by the "up to the ` - `" rule; synthetic names never contain it) | other | latent, no current case affected |
| 4 | 02-REVIEW.md IN-01..IN-03 (duplicated label table, pin does not couple version to hash, `tpAmb` 1 branch untested) | other | info, advisory as the orchestrator noted |

Carried forward from earlier reviews and open in `02-REVIEW-DISPOSITION.md` (WR-02 to WR-06 of the first review: `EX` recipient UF, runner timeout, unpriced infrastructure failures, gateway disposal, `schema-check` index comparison) are unchanged and outside the must-haves.

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` | prompt `extract-003`, version history | VERIFIED | Substantive; raw-string prompt lines read; version literal `"extract-003"` at the `ExtractionContract` constructor call; consumed by the eval endpoint |
| `dotnet/src/Carimbo.Domain/Invoice.cs` | Description attributes with printed labels | VERIFIED | Six description changes; exported to schemas; no constraint touched |
| `schema/invoice.schema.json`, `schema/invoice.model.schema.json`, `python/src/carimbo_models/generated.py` | Regenerated, description-only change | VERIFIED | Diff vs c21d015 is description lines only; regeneration clean |
| `docs/DANFE-MAPPING.md` | Corrected rows, CRT note, receipt-stub limitation | VERIFIED | `recipient.name`, `issuer.ie`, `cst_csosn`, `icms_st_base`, `icms_st_amount`, `ipi_amount` rows and the new limitation bullet read; `docs-check` ok |
| `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` | version, label, header and SHA-256 pins | VERIFIED | `The_prompt_texts_are_pinned_to_their_versions` present and passing |
| `python/tests/test_danfe_labels.py` | printed facts from committed PDFs vs schema and doc | VERIFIED | 179 lines, reads `manifest.json`, PDFs and XMLs; 28 passed |
| Earlier-plan artifacts (Validation, GroundTruth, Api, vectors, skeleton data) | unchanged | VERIFIED (regression) | untouched by 02-13 per `git diff --stat`; Extraction (148) and Validation (191) test projects re-run green |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | -- | --- | ------ | ------- |
| `InvoiceExtractor.cs` | `EvalEndpoint.cs` | `ExtractionContract.Default.PromptVersion` into `effective.prompt_version` and `attempts[].prompt_version` | WIRED | `extract-003` asserted over HTTP in C# and in the Python e2e |
| `Invoice.cs` | `schema/invoice.model.schema.json` | Description attributes, SchemaExport, model-facing projection | WIRED | `schema-check` regeneration produces the committed bytes |
| `test_danfe_labels.py` | `data/skeleton/manifest.json` | reads PDFs and XMLs listed in the manifest | WIRED | test file reads manifest and cases |
| `test_danfe_labels.py` | `docs/DANFE-MAPPING.md` | parses mapping rows and compares label cells | WIRED | 28 tests pass |
| Prior links (parse boundary, validator, strict enum, eval endpoint, repair feedback, schema to Pydantic) | | | WIRED (regression) | no change |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
| -------- | ------------- | ------ | ------------------ | ------ |
| Prompt and schema descriptions sent to the model | `ExtractionContract.Default.Prompt`, `OutputSchemaJson` | Built from the literal prompt and the exported model-facing schema; hash echoed as `effective.schema_sha256` | Yes | FLOWING |
| EvalResponse.Attempts | `result.Attempts` | real gateway responses (live record in 02-10) | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Printed labels and recipient stub in the committed PDFs | isolated pypdfium2 script over case-001..003 | notice printed on all three; stub name equals XML `dest/xNome`; `ICMS ST` and `VALOR DO IPI` labels present, `VALOR TOTAL DO IPI` absent; CSOSN header on 001 and 003, CST on 002 | PASS |
| Label tests | `devenv shell -- uv run --project python pytest python/tests/test_danfe_labels.py -q` | 28 passed | PASS |
| Extraction project | `dotnet test --project tests/Carimbo.Extraction.Tests` | 148 passed, 0 failed | PASS |
| Validation project (includes `MappingDocTests`) | `dotnet test --project tests/Carimbo.Validation.Tests` | 191 passed, 0 failed | PASS |
| Schema regeneration | `devenv shell -- just schema-check` | exit 0, no diff | PASS |
| Docs gate | `devenv shell -- just docs-check` | `docs ok` | PASS |
| Whole offline gate | `just check` | exit 0, run by the orchestrator after the last source commit (308 pytest, 5 e2e, .NET build/format/tests, schema-check, datagen-check, docs, secrets); not re-run by me | PASS (reported) |

### Probe Execution

No `probe-*.sh` probes declared or present; SKIPPED.

### Requirements Coverage

All 12 IDs of the phase appear in at least one PLAN `requirements` field and in REQUIREMENTS.md, where all are `[x]` and "Complete" in the traceability table. No orphaned Phase 2 IDs. 02-13 declares DOM-02 and API-01.

| Requirement | Source Plan(s) | Description | Status | Evidence |
| ----------- | -------------- | ----------- | ------ | -------- |
| DOM-02 | 02-01, 02-02, 02-03, 02-05, 02-07, 02-12, 02-13 | DANFE-visible target, mapping documented | SATISFIED | `Invoice.cs`, mapping doc now matching the printed DANFE, `MappingDocTests`, `test_danfe_labels.py` |
| DOM-03 | 02-01, 02-05 | Alphanumeric CNPJ and key forms | SATISFIED | Patterns, vectors, alphanumeric-issuer case-003 |
| DOM-04 | 02-01, 02-02 | Money string on wire, half-up in both languages | SATISFIED | Shared vectors |
| VAL-01 | 02-04, 02-08, 02-11, 02-12 | Structured errors, never throws | SATISFIED | `NeverThrowsTests`, unchanged |
| VAL-02 | 02-02, 02-04 | CNPJ check digits | SATISFIED | Vectors |
| VAL-03 | 02-04, 02-07 | Key check digit and cross-checks | SATISFIED | `KEY_*` rules |
| VAL-04 | 02-04, 02-07 | Items vs totals, taxes, regime-aware | SATISFIED | `ArithmeticRules` tests |
| VAL-05 | 02-04, 02-08 | Date plausibility | SATISFIED | `DATE_PLAUSIBLE`, `DUE_DATE_ORDER` |
| VAL-06 | 02-02, 02-04 | Shared vector file in xUnit and pytest | SATISFIED | `VectorTests.cs`, `test_vectors.py` |
| API-01 | 02-06, 02-08, 02-10, 02-11, 02-12, 02-13 | Eval endpoint returns result, outcomes, attempts, cost, trace ID | SATISFIED | `EvalEndpoint.cs`; `prompt_version` extract-003 over HTTP |
| EXT-03 | 02-03, 02-09, 02-10 | Bounded repair, structured errors, no fabrication | SATISFIED | `repair-001` unchanged and hash-pinned; `RepairLoopTests` |
| EXT-04 | 02-06, 02-08, 02-09, 02-10, 02-11 | Every attempt recorded | SATISFIED | Attempts list over HTTP |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| 02-13 changed files | n/a | `TBD`, `FIXME`, `XXX` debt markers | none found | No blocker |
| `dotnet/src/Carimbo.Extraction/RepairFeedback.cs` | 60 | "CST column" wording left in repair feedback | Info | See Advisory 1; outside must-haves |
| `python/tests/test_danfe_labels.py` | 79-82 | substring-weak totals assertions (review WR-01) | Warning | Test strength only; labels verified independently above |

### Human Verification Required

None. The phase's guarantees are deterministic code paths covered by automated tests, and the 02-13 changes are text and documentation checked against the committed PDFs. Whether extract-003 changes live model accuracy is deliberately not claimed (no live run was permitted, and the plan prohibits presenting it as measured); a live `just skeleton` run in a later phase would measure it.

### Gaps Summary

No gaps. UAT gaps G-02-1 and G-02-2 are closed: every label, location and header statement in the prompt, the schemas and the mapping doc matches what the committed skeleton DANFEs print, as checked directly against the PDFs and XMLs; the prompt version is bumped and pinned; the schema change is description-only. The five ROADMAP success criteria show no regression. Remaining items are advisory (repair-feedback wording and the 02-REVIEW test-strength notes).

---

_Verified: 2026-10-09T00:30:00Z_
_Verifier: Claude (gsd-verifier)_
