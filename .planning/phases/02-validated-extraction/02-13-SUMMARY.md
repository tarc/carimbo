---
phase: 02-validated-extraction
plan: 13
subsystem: extraction
tags: [prompt, schema-descriptions, danfe, mapping-doc, gap-closure, pytest, sha256-pin]

requires:
  - phase: 02-validated-extraction
    provides: ExtractionContract, model-facing schema projection, eval endpoint contract 2, DANFE-MAPPING.md (plans 02-01 to 02-12)
provides:
  - Prompt extract-003 quoting the labels BrazilFiscalReport 1.2.0 prints, the column headed CST or CSOSN, and where the recipient name is printed
  - Description-only schema corrections (canonical, model-facing, generated Pydantic models)
  - SHA-256 pin per prompt text, so a text change without a new version fails CI
  - python/tests/test_danfe_labels.py, which checks the schema descriptions and the mapping doc against the committed skeleton DANFEs
  - Corrected docs/DANFE-MAPPING.md rows, CRT note and receipt-stub limitation
affects: [phase-03-replay, phase-04-eval-runs]

actuals:
  tokens: 13900
  tasks: 3
  commits: 3

plan_head_before: bc7970ccabe26cb7c5d8e7ea04ea80b4a144732f
plan_head_after: 1fe61fe3744b5ee0ee27584fefa2bfb092fd774e

tech-stack:
  added: []
  patterns:
    - "Each prompt text is pinned by SHA-256 to its version in ExtractorTests until Phase 3 owns prompt versioning"
    - "Documentation and schema descriptions are checked against the printed text layer of the committed DANFEs"

key-files:
  created:
    - python/tests/test_danfe_labels.py
  modified:
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/src/Carimbo.Domain/Invoice.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - python/tests/test_e2e_fake.py
    - schema/invoice.schema.json
    - schema/invoice.model.schema.json
    - python/src/carimbo_models/generated.py
    - docs/DANFE-MAPPING.md

key-decisions:
  - "The model-facing schema descriptions are corrected together with the prompt, because the model reads both and one box must not carry two labels"
  - "The prompt version is bumped to extract-003 and each prompt text is pinned by SHA-256; no new decision record (D-19 and D-23 already cover versioning)"
  - "test_grader.py synthetic records, docs/spikes/02-schema-probe.md and earlier SUMMARYs keep extract-002 as history"

patterns-established:
  - "Printed-label facts live in one pytest table that guards the prompt-adjacent schema descriptions and the mapping doc"

requirements-completed: [DOM-02, API-01]

coverage:
  - id: D1
    description: "Prompt extract-003 quotes the 11 totals labels as printed (including BASE DE CÁLCULO DO ICMS ST, VALOR DO ICMS ST, VALOR DO IPI), says the items column is headed CST or CSOSN, and tells the model to read the recipient name from the RECEBEMOS DE stub when the name box shows the homologation notice"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_prompt_quotes_each_totals_label_as_the_danfe_prints_it"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_prompt_says_where_the_recipient_name_is_printed"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_prompt_names_the_cst_or_csosn_column_header"
        status: pass
    human_judgment: false
  - id: D2
    description: "The version is extract-003 in the contract and over HTTP (effective.prompt_version, attempts[].prompt_version); each prompt text is pinned to its version by SHA-256"
    requirement: API-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_prompt_texts_are_pinned_to_their_versions"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_clean_extraction_reports_no_findings_one_initial_attempt_and_the_reference_date_used"
        status: pass
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#TestRepairOverHttp.test_repaired_and_exhausted_cases_are_recorded_and_graded"
        status: pass
    human_judgment: false
  - id: D3
    description: "The canonical and model-facing schema descriptions carry the printed labels, the CST or CSOSN header and the recipient name location, and the schemas differ from c21d015 only in description strings"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_danfe_labels.py#test_schema_descriptions_quote_the_printed_labels"
        status: pass
      - kind: other
        ref: "description-stripped comparison against c21d015 printed 'constraints unchanged'; just schema-check"
        status: pass
    human_judgment: false
  - id: D4
    description: "The printed facts the doc and schema rely on hold on the committed DANFEs (11 totals labels, homologation notice in the name box, stub name, CST or CSOSN header by CRT, issuer IE as in the XML)"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_danfe_labels.py"
        status: pass
    human_judgment: false
  - id: D5
    description: "docs/DANFE-MAPPING.md maps recipient.name to the receipt stub, quotes the printed totals labels, names the CSOSN header and the CRT role, and states the IE forms and the receipt-stub limitation"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_danfe_labels.py#test_the_mapping_doc_points_the_recipient_name_at_the_receipt_stub"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/MappingDocTests.cs"
        status: pass
    human_judgment: false
  - id: D6
    description: "The whole offline gate passes and no live paid recipe was run"
    requirement: DOM-02
    verification:
      - kind: other
        ref: "devenv shell -- just check (exit 0)"
        status: pass
    human_judgment: false

duration: 5min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 13: The prompt, schema descriptions and DANFE mapping quote what the DANFE prints Summary

**Prompt extract-003, the model-facing schema descriptions and docs/DANFE-MAPPING.md now quote the labels BrazilFiscalReport 1.2.0 prints (ICMS ST and IPI totals, the column headed CST or CSOSN, the recipient name in the RECEBEMOS DE stub on homologation DANFEs), with a SHA-256 pin per prompt text and a pytest that checks the facts against the committed PDFs.**

UAT gaps G-02-1 and G-02-2 are closed by this plan.

## Performance

- **Duration:** about 5 min
- **Started:** 2026-10-08T23:42Z
- **Completed:** 2026-10-08
- **Tasks:** 3
- **Files modified:** 10 (1 created)

## Accomplishments

- The recipient line of the prompt names the NOME / RAZÃO SOCIAL box of DESTINATÁRIO / REMETENTE, and for the homologation notice `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL` it points to the text after `DESTINATARIO:` in the RECEBEMOS DE stub, and forbids returning the notice as the name. The no-fabrication and data-only lines are unchanged.
- Totals labels for `icms_st_base`, `icms_st_amount` and `ipi_amount` match the print; the items line says the column is headed CST or CSOSN. The Description attributes in `Invoice.cs` say the same, so the model never sees two labels for one box. `just schema` regenerated both schemas and `generated.py`; a description-stripped comparison with c21d015 shows no constraint changed.
- `ExtractorTests.The_prompt_texts_are_pinned_to_their_versions` pins extract-003 and repair-001 by SHA-256. The version reaches `effective.prompt_version` and `attempts[].prompt_version` over HTTP.
- `python/tests/test_danfe_labels.py` (28 tests) reads the manifest cases, the committed PDFs (pypdfium2, whitespace collapsed) and XMLs, and checks the schema descriptions and the mapping doc rows against the print.
- `docs/DANFE-MAPPING.md`: recipient.name row, issuer IE forms (digits or `ISENTO`), cst_csosn header by CRT, three totals labels, CRT note and a receipt-stub limitation.

## Task Commits

1. **Task 1 (tracer): prompt extract-003 and version pins** - `44c4323` (feat)
2. **Task 2: schema descriptions with the printed labels, label test** - `90ee408` (fix)
3. **Task 3: DANFE-MAPPING.md and its tests** - `1fe61fe` (docs)

**Plan metadata:** docs commit (this SUMMARY with STATE, ROADMAP and REQUIREMENTS)

## TDD evidence

- Task 1: the label, recipient, header and pin tests were written before the prompt change; the pin test printed the actual hashes on its first runs, which were then recorded as the constants. The extraction (148) and Api (71) test projects and the e2e repair test pass.
- Task 2: before the `Invoice.cs` change only `test_schema_descriptions_quote_the_printed_labels` failed (2 of 15), the other 13 pinned existing facts. After `just schema` all 15 pass.
- Task 3: before the doc edit exactly the 5 doc tests failed (3 totals labels, recipient stub, CSOSN header); after it all 28 pass and `MappingDocTests` (3 tests) passes.

## Decisions Made

- Scope includes the model-facing schema descriptions (see key-decisions): fixing the prompt alone would leave the model with two labels for one box.
- Versioning is enforced mechanically by the hash pin until Phase 3 (EXT-05) formalises it; no decision record was added.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Ruff E501 in test_danfe_labels.py**
- **Found during:** Task 3 (`just check`)
- **Issue:** three lines exceeded the 100-column limit in the new test file, so `py-check` failed.
- **Fix:** rewrapped the module docstring, split one statement and moved a trailing comment to its own line.
- **Files modified:** `python/tests/test_danfe_labels.py`
- **Committed in:** `1fe61fe`

---

**Total deviations:** 1 auto-fixed (lint). **Impact on plan:** none on behavior.

## Evidence references

- The 02-01-SUMMARY D8 evidence ref now points to the renamed test `The_contract_is_prompt_version_extract_003_and_the_prompt_names_every_v2_field` (the 02-01 SUMMARY text itself is history and is not rewritten).
- The model-facing schema SHA-256 changed (description-only) after the 02-03 live probe. The probe accepted the earlier bytes; descriptions carry no constraint and the structured-output budget does not count them. No live run was added.
- extract-003 has not been measured live. The next paid run (`just skeleton` or Phase 3) will be its first. `docs/spikes/02-schema-probe.md`, `evals/runs` and `test_grader.py` still record extract-002 as history.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. The prompt now points the model at document content in the receipt stub; the data-only line stays and a test asserts it (T-02-45).

## Self-Check: PASSED

- Created file present: `python/tests/test_danfe_labels.py`.
- Commits `44c4323`, `90ee408`, `1fe61fe` are ancestors of HEAD.
- All task acceptance criteria re-run and passing; `devenv shell -- just check` exits 0; `git diff --exit-code c21d015 -- data/skeleton python/tests/test_grader.py docs/spikes` exits 0; the description-stripped schema comparison printed "constraints unchanged".
