---
status: complete
phase: 02-validated-extraction
source: [02-01-SUMMARY.md, 02-02-SUMMARY.md, 02-03-SUMMARY.md, 02-04-SUMMARY.md, 02-05-SUMMARY.md, 02-06-SUMMARY.md, 02-07-SUMMARY.md, 02-08-SUMMARY.md, 02-09-SUMMARY.md, 02-10-SUMMARY.md, 02-11-SUMMARY.md, 02-12-SUMMARY.md, 02-VERIFICATION.md]
started: 2026-10-08T20:48:18Z
updated: 2026-10-09T00:19:37.362Z
---

## Current Test

[testing complete]

## Tests

### 1. DANFE mapping document is right (02-02 D4, DOM-02)
expected: docs/DANFE-MAPPING.md names, for every Invoice leaf path, the DANFE label and NF-e XML source a reviewer would agree with; the not-on-DANFE list and Limitations section are accurate
result: issue
reported: "recipient.name is mapped to the DESTINATÁRIO NOME / RAZÃO SOCIAL box, but on these homologation DANFEs that box prints \"NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL\"; the real name (dest/xNome) is printed only in the RECEBEMOS DE receipt stub. The mapping should point at the stub. Minor: the live runs extracted the name correctly from the stub in all six attempts."
severity: minor
correction: "Earlier report (superseded): recipient.name isn't printed in the DESTINATÁRIO box, and case-003 clips it. The clipping claim came from a truncated text extraction by Claude; the rendered case-003 stub prints the full name (Sophia Pereira SINTETICA). Reworded by the user's choice on 2026-10-08."
evidence: "All three DANFEs print the homologation text in DESTINATÁRIO NOME / RAZÃO SOCIAL (tpAmb 2); dest/xNome appears in full in the RECEBEMOS DE stub. Live runs skeleton-20261008T183505Z and T183611Z returned the correct recipient.name for all three cases. Also noted, not reported: issuer.ie note says the generator prints ISENTO today (only case-003 does)."

### 2. Prompt extract-002 reads well (02-01 D8)
expected: The extraction prompt (prompt version extract-002 in Carimbo.Extraction) lists every v2 field with clear DANFE copying rules and the no-fabrication instruction; nothing reads as misleading to the model
result: issue
reported: "Prompt extract-002 does not match the printed DANFE in three places: (1) the recipient name instruction \"name as printed\" doesn't say where the name is printed; on these DANFEs the DESTINATÁRIO name box shows the homologation text and the name appears only in the receipt stub. The model found it anyway in all six live attempts, but the prompt should name the stub, and the fix should be done together with G-02-1; (2) three totals labels differ from the DANFE: BASE DE CÁLCULO DO ICMS SUBST. is printed BASE DE CÁLCULO DO ICMS ST, VALOR DO ICMS SUBSTITUIÇÃO is printed VALOR DO ICMS ST, VALOR TOTAL DO IPI is printed VALOR DO IPI, and docs/DANFE-MAPPING.md has the same three wrong labels; (3) cst_csosn says \"printed in the CST column\", but Simples invoices print the header CSOSN."
reported_note: "Wording drafted by Claude at the user's request and accepted by the user; item 1 reworded after the live-run records showed the model reads the name from the stub."
severity: minor

### 3. Live provider accepts the v2 schema (02-03 D2, DOM-02)
expected: docs/spikes/02-schema-probe.md ## Result shows (a) the v2 model-facing schema accepted (HTTP 200, schema-valid invoice) and (b) a two-turn cached-PDF conversation where both calls return schema-valid invoices with cache_read > 0
result: pass
evidence: "docs/spikes/02-schema-probe.md (01f7801): (a) accepted HTTP 200, schema-valid, 0 pattern violations; (b1)/(b2) HTTP 200, schema-valid, 3 items, cache_read=6321 on the repair turn; total US$0.0151 of 0.25; model-facing schema unchanged since the probe commit."

### 4. Schema not weakened to pass the provider (02-03 D3)
expected: schema/invoice.model.schema.json and ModelSchemaProjector.cs are untouched by 02-03 (last changed by 02-01 3eacfb5 and Phase 1); git log -- those paths shows no 02-03 commit
result: pass
evidence: "git log on both paths lists only 3eacfb5 (02-01) and 01-05 commits; the five 02-03 commits touch neither file; just schema-check exit 0 with a clean tree."

### 5. Live skeleton runs with and without repair (02-10)
expected: The recorded live runs (`just skeleton 1.00 2` and `just skeleton 1.00 0`, claude-haiku-4-5, three cases) completed under the US$1.00 cap, wrote graded summaries, and their findings (0 cases repaired; case-002 access-key misread) are an acceptable phase outcome. Re-running is optional and paid.
result: pass
evidence: "02-10-SUMMARY.md and evals/runs/skeleton-20261008T183505Z (max_repairs 2, US$0.0843) and skeleton-20261008T183611Z (max_repairs 0, US$0.0642): 0 harness errors, 0 unpriced cases, 0 repaired; every wrong access key caught as validation_failed by KEY_* rules; recipient.name correct in all six extractions."

### 6. Invoice v2 is the full DANFE-visible target; every field required, only issuer.ie and recipient.ie nullable; canonical and model-facing schemas and generated Pydantic models regenerate reproducibly
expected: Invoice v2 is the full DANFE-visible target; every field required, only issuer.ie and recipient.ie nullable; canonical and model-facing schemas and generated Pydantic models regenerate reproducibly
result: pass
source: automated
coverage_id: 02-01 D1

### 7. The model-facing schema fits the structured-output budget with 0 optional and 2 union properties
expected: The model-facing schema fits the structured-output budget with 0 optional and 2 union properties
result: pass
source: automated
coverage_id: 02-01 D2

### 8. Numeric and alphanumeric CNPJ and access-key forms and an 11-digit CPF recipient parse; empty, whitespace, trailing-newline, lowercase and non-ASCII-digit identifiers are schema_invalid naming the path
expected: Numeric and alphanumeric CNPJ and access-key forms and an 11-digit CPF recipient parse; empty, whitespace, trailing-newline, lowercase and non-ASCII-digit identifiers are schema_invalid naming the path
result: pass
source: automated
coverage_id: 02-01 D3

### 9. Money, Decimal4 and Rate keep their printed precision and reject pt-BR separators, signs, exponents, padding and wrong fraction counts; Money.RoundHalfUp rounds half away from zero with no negative zero
expected: Money, Decimal4 and Rate keep their printed precision and reject pt-BR separators, signs, exponents, padding and wrong fraction counts; Money.RoundHalfUp rounds half away from zero with no negative zero
result: pass
source: automated
coverage_id: 02-01 D4

### 10. Every pattern in the model-facing schema (28 paths through $ref and array items) is enforced by the extractor
expected: Every pattern in the model-facing schema (28 paths through $ref and array items) is enforced by the extractor
result: pass
source: automated
coverage_id: 02-01 D5

### 11. The shared fixture parses strictly in .NET and validates strictly against the generated Pydantic Invoice
expected: The shared fixture parses strictly in .NET and validates strictly against the generated Pydantic Invoice
result: pass
source: automated
coverage_id: 02-01 D6

### 12. Python reads the XML ground truth and grades 27 header, party, totals and count fields; scripted v2 invoices go over real HTTP and are graded end to end
expected: Python reads the XML ground truth and grades 27 header, party, totals and count fields; scripted v2 invoices go over real HTTP and are graded end to end
result: pass
source: automated
coverage_id: 02-01 D7

### 13. Shared validator vector file checked by pytest for CNPJ, CPF and access-key check digits and half-up rounding
expected: Shared validator vector file checked by pytest for CNPJ, CPF and access-key check digits and half-up rounding
result: pass
source: automated
coverage_id: 02-02 D1

### 14. Python identifier helpers and round_half_up (CPF, access key, stricter CNPJ, no negative zero)
expected: Python identifier helpers and round_half_up (CPF, access key, stricter CNPJ, no negative zero)
result: pass
source: automated
coverage_id: 02-02 D2

### 15. D-22, D-23, D-24 recorded append-only and enforced by just docs-check
expected: D-22, D-23, D-24 recorded append-only and enforced by just docs-check
result: pass
source: automated
coverage_id: 02-02 D3

### 16. Gateway expresses follow-up repair turns and a document cache breakpoint, refuses malformed conversations before any HTTP request
expected: Gateway expresses follow-up repair turns and a document cache breakpoint, refuses malformed conversations before any HTTP request
result: pass
source: automated
coverage_id: 02-03 D1

### 17. InvoiceValidator returns structured findings with stable catalogue rule ids; the shared fixture validates clean at reference 2026-10-01
expected: InvoiceValidator returns structured findings with stable catalogue rule ids; the shared fixture validates clean at reference 2026-10-01
result: pass
source: automated
coverage_id: 02-04 D1

### 18. Validators never throw: 2000 seeded hostile mutations plus every hostile value at every fixture leaf return catalogued findings, and an overflow becomes ARITH_OVERFLOW on its field
expected: Validators never throw: 2000 seeded hostile mutations plus every hostile value at every fixture leaf return catalogued findings, and an overflow becomes ARITH_OVERFLOW on its field
result: pass
source: automated
coverage_id: 02-04 D2

### 19. CNPJ (numeric, alphanumeric, lowercase, identical characters), CPF and access-key check digits, tax id kind and UF rules, driven by the shared vector file
expected: CNPJ (numeric, alphanumeric, lowercase, identical characters), CPF and access-key check digits, tax id kind and UF rules, driven by the shared vector file
result: pass
source: automated
coverage_id: 02-04 D3

### 20. The access key is cross-checked against UF, issuer CNPJ, local year-month, model 55, series and number with one rule id each
expected: The access key is cross-checked against UF, issuer CNPJ, local year-month, model 55, series and number with one rule id each
result: pass
source: automated
coverage_id: 02-04 D4

### 21. Items are checked against totals and taxes against bases and rates per inferred regime family, with inclusive tolerances and the vNF formula
expected: Items are checked against totals and taxes against bases and rates per inferred regime family, with inclusive tolerances and the vNF formula
result: pass
source: automated
coverage_id: 02-04 D5

### 22. Issue-date plausibility against an injected reference date and installment due dates on or after the issue date
expected: Issue-date plausibility against an injected reference date and installment due dates on or after the issue date
result: pass
source: automated
coverage_id: 02-04 D6

### 23. xUnit runs every section of data/vectors/validator-vectors.json (CNPJ, CPF, access key, rounding, tolerance, sum tolerance) and the options defaults equal the file
expected: xUnit runs every section of data/vectors/validator-vectors.json (CNPJ, CPF, access key, rounding, tolerance, sum tolerance) and the options defaults equal the file
result: pass
source: automated
coverage_id: 02-04 D7

### 24. case-002 is a Regime Normal invoice (CRT 3) with ICMS00 and ICMS20 items, IPI on every item, freight 25.00, discount 10.00 and two installments summing to the invoice total
expected: case-002 is a Regime Normal invoice (CRT 3) with ICMS00 and ICMS20 items, IPI on every item, freight 25.00, discount 10.00 and two installments summing to the invoice total
result: pass
source: automated
coverage_id: 02-05 D1

### 25. case-001 is Simples (CRT 1) on CSOSN 101 with pCredSN 3.10, zero ICMS columns and no installments
expected: case-001 is Simples (CRT 1) on CSOSN 101 with pCredSN 3.10, zero ICMS columns and no installments
result: pass
source: automated
coverage_id: 02-05 D2

### 26. case-003 has an alphanumeric issuer CNPJ with IE ISENTO, a CPF recipient with no IE, CSOSN 102/400 items, renders to 2 pages and its Code 128 barcode decodes to the alphanumeric key
expected: case-003 has an alphanumeric issuer CNPJ with IE ISENTO, a CPF recipient with no IE, CSOSN 102/400 items, renders to 2 pages and its Code 128 barcode decodes to the alphanumeric key
result: pass
source: automated
coverage_id: 02-05 D3

### 27. Every generated XML obeys the vNF formula and the item arithmetic the validators check; regeneration from the seed is byte-identical
expected: Every generated XML obeys the vNF formula and the item arithmetic the validators check; regeneration from the seed is byte-identical
result: pass
source: automated
coverage_id: 02-05 D4

### 28. Manifest skeleton-002 records as_of_date 2026-10-01, tags and a spec-derived expected block; the Python reader agrees with it and validates strictly on all three cases
expected: Manifest skeleton-002 records as_of_date 2026-10-01, tags and a spec-derived expected block; the Python reader agrees with it and validates strictly on all three cases
result: pass
source: automated
coverage_id: 02-05 D5

### 29. A CPF recipient and an alphanumeric issuer round-trip over HTTP through the .NET parser and the grader on the reworked cases
expected: A CPF recipient and an alphanumeric issuer round-trip over HTTP through the .NET parser and the grader on the reworked cases
result: pass
source: automated
coverage_id: 02-05 D6

### 30. All party data is synthetic: valid check digits, absent from nfelib samples, SINTETICA on every name, tpAmb 2 and the SEM VALOR FISCAL note
expected: All party data is synthetic: valid check digits, absent from nfelib samples, SINTETICA on every name, tpAmb 2 and the SEM VALOR FISCAL note
result: pass
source: automated
coverage_id: 02-05 D7

### 31. A validation_failed record is graded on its candidate (27 fields), counted under validation_failed and as caught, never as success
expected: A validation_failed record is graded on its candidate (27 fields), counted under validation_failed and as caught, never as success
result: pass
source: automated
coverage_id: 02-06 D1

### 32. Every graded case records validator outcome and attempt count and statuses; contract 1 records grade with attempt_count null; malformed members never crash the grader
expected: Every graded case records validator outcome and attempt count and statuses; contract 1 records grade with attempt_count null; malformed members never crash the grader
result: pass
source: automated
coverage_id: 02-06 D2

### 33. Refusals, truncations, infrastructure failures and harness errors still carry no field grades
expected: Refusals, truncations, infrastructure failures and harness errors still carry no field grades
result: pass
source: automated
coverage_id: 02-06 D3

### 34. summary.json validation and attempts blocks and summary.md attempts/findings columns and caught line
expected: summary.json validation and attempts blocks and summary.md attempts/findings columns and caught line
result: pass
source: automated
coverage_id: 02-06 D4

### 35. NfeXmlMapper maps every committed skeleton XML to an Invoice by the documented rules, implemented once in .NET with DTD processing prohibited
expected: NfeXmlMapper maps every committed skeleton XML to an Invoice by the documented rules, implemented once in .NET with DTD processing prohibited
result: pass
source: automated
coverage_id: 02-07 D1

### 36. The ground-truth gate finds zero Error findings from InvoiceValidator on all three manifest cases at as_of_date 2026-10-01
expected: The ground-truth gate finds zero Error findings from InvoiceValidator on all three manifest cases at as_of_date 2026-10-01
result: pass
source: automated
coverage_id: 02-07 D2

### 37. Mapped invoices match the manifest expected block and round-trip through Wire.Options with no pattern violations
expected: Mapped invoices match the manifest expected block and round-trip through Wire.Options with no pattern violations
result: pass
source: automated
coverage_id: 02-07 D3

### 38. The 42 leaf paths of schema/invoice.schema.json equal the table rows of docs/DANFE-MAPPING.md in both directions
expected: The 42 leaf paths of schema/invoice.schema.json equal the table rows of docs/DANFE-MAPPING.md in both directions
result: pass
source: automated
coverage_id: 02-07 D4

### 39. A DOCTYPE is refused with XmlException and a missing nNF raises NfeMappingException naming the element
expected: A DOCTYPE is refused with XmlException and a missing nNF raises NfeMappingException naming the element
result: pass
source: automated
coverage_id: 02-07 D5

### 40. Every schema-valid extraction is validated by the registered InvoiceValidator: error findings give validation_failed with the candidate, warnings ride on success, typed failures are never validated
expected: Every schema-valid extraction is validated by the registered InvoiceValidator: error findings give validation_failed with the candidate, warnings ride on success, typed failures are never validated
result: pass
source: automated
coverage_id: 02-08 D1

### 41. Eval endpoint contract 2: strict optional reference_date with UTC default, findings, attempts list, sums over attempts, null-not-partial cost
expected: Eval endpoint contract 2: strict optional reference_date with UTC default, findings, attempts list, sums over attempts, null-not-partial cost
result: pass
source: automated
coverage_id: 02-08 D2

### 42. Attempt record per model call with output, findings, usage, cost, latency and stop reason (single attempt until 02-09)
expected: Attempt record per model call with output, findings, usage, cost, latency and stop reason (single attempt until 02-09)
result: pass
source: automated
coverage_id: 02-08 D3

### 43. Date plausibility uses the request or dataset reference date; the runner sends the manifest as_of_date
expected: Date plausibility uses the request or dataset reference date; the runner sends the manifest as_of_date
result: pass
source: automated
coverage_id: 02-08 D4

### 44. Validator errors are sent back with structured, redacted findings under a no-fabrication prompt and stop at Extraction:MaxRepairs, ending in validation_failed with the last candidate
expected: Validator errors are sent back with structured, redacted findings under a no-fabrication prompt and stop at Extraction:MaxRepairs, ending in validation_failed with the last candidate
result: pass
source: automated
coverage_id: 02-09 D1

### 45. Disclosure policy: no expected identifier, check digit or key component, no document text, in any repair turn
expected: Disclosure policy: no expected identifier, check digit or key component, no document text, in any repair turn
result: pass
source: automated
coverage_id: 02-09 D2

### 46. Every attempt is recorded with its own output, findings, usage, cost and latency; response totals are exact sums
expected: Every attempt is recorded with its own output, findings, usage, cost and latency; response totals are exact sums
result: pass
source: automated
coverage_id: 02-09 D3

### 47. Repair budget and per-attempt timeout are bounded by configuration checked at startup
expected: Repair budget and per-attempt timeout are bounded by configuration checked at startup
result: pass
source: automated
coverage_id: 02-09 D4

### 48. A model answer with a null list element in items or installments is schema_invalid at the parse boundary, naming the null path, with no findings
expected: A model answer with a null list element in items or installments is schema_invalid at the parse boundary, naming the null path, with no findings
result: pass
source: automated
coverage_id: 02-11 D1

### 49. InvoiceValidator.Validate never throws on a null list element, null list, null nested record or null required string: one NULL_VALUE error per null path, nothing else
expected: InvoiceValidator.Validate never throws on a null list element, null list, null nested record or null required string: one NULL_VALUE error per null path, nothing else
result: pass
source: automated
coverage_id: 02-11 D2

### 50. POST /eval/extractions answers HTTP 200 schema_invalid for a null list element and keeps every paid attempt, including a null in a repair attempt
expected: POST /eval/extractions answers HTTP 200 schema_invalid for a null list element and keeps every paid attempt, including a null in a repair attempt
result: pass
source: automated
coverage_id: 02-11 D3

### 51. A schema_invalid repair answer consumes one unit of repair budget and the next turn tells the model the previous answer did not match the schema
expected: A schema_invalid repair answer consumes one unit of repair budget and the next turn tells the model the previous answer did not match the schema
result: pass
source: automated
coverage_id: 02-11 D4

### 52. recipient.tax_id_kind outside the exact strings cnpj and cpf (integers, numeric strings, wrong case, padding, comma lists, true, null, {}) raises JsonException at Wire.Options and is schema_invalid in the extractor
expected: recipient.tax_id_kind outside the exact strings cnpj and cpf (integers, numeric strings, wrong case, padding, comma lists, true, null, {}) raises JsonException at Wire.Options and is schema_invalid in the extractor
result: pass
source: automated
coverage_id: 02-12 D1

### 53. Every public Domain enum round-trips by wire name only; an escaped spelling parses; an undefined value is never written; the error names the JSON path and not the value
expected: Every public Domain enum round-trips by wire name only; an escaped spelling parses; an undefined value is never written; the error names the JSON path and not the value
result: pass
source: automated
coverage_id: 02-12 D2

### 54. POST /eval/extractions answers HTTP 200 schema_invalid with no invoice for a tax_id_kind of CNPJ or 0
expected: POST /eval/extractions answers HTTP 200 schema_invalid with no invoice for a tax_id_kind of CNPJ or 0
result: pass
source: automated
coverage_id: 02-12 D3

### 55. The committed canonical and model-facing schemas and generated Pydantic models are byte-identical after the change
expected: The committed canonical and model-facing schemas and generated Pydantic models are byte-identical after the change
result: pass
source: automated
coverage_id: 02-12 D4

### 56. D-25 records both gap fixes and docs-check requires it
expected: D-25 records both gap fixes and docs-check requires it
result: pass
source: automated
coverage_id: 02-12 D5

## Summary

total: 56
passed: 54
issues: 2
pending: 0
skipped: 0
blocked: 0

## Gaps

- gap_id: G-02-1
  truth: "docs/DANFE-MAPPING.md names, for every Invoice leaf path, the DANFE label and NF-e XML source a reviewer would agree with; the not-on-DANFE list and Limitations section are accurate"
  status: resolved
  resolved_by: 02-13-PLAN.md
  resolved_at: 2026-10-09
  retest: "pass, re-checked line by line with the user on 2026-10-09 against the committed skeleton PDFs and XMLs"
  reason: "User reported: recipient.name is mapped to the DESTINATÁRIO NOME / RAZÃO SOCIAL box, but on these homologation DANFEs that box prints \"NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL\"; the real name (dest/xNome) is printed only in the RECEBEMOS DE receipt stub. The mapping should point at the stub. Minor: the live runs extracted the name correctly from the stub in all six attempts."
  severity: minor
  test: 1
  related: [G-02-2]
  note: "Fix together with G-02-2: point the mapping doc and the prompt's recipient name instruction at the receipt stub."
  root_cause: "BrazilFiscalReport prints the homologation notice in the DESTINATÁRIO NOME / RAZÃO SOCIAL box for tpAmb 2 documents (every synthetic case is homologation by D-14), so dest/xNome is printed only in the RECEBEMOS DE receipt stub. docs/DANFE-MAPPING.md was written against the label, not the rendered PDFs. Diagnosed in-session by rendering all three skeleton PDFs; no debug agent spawned."
  artifacts:
    - path: "docs/DANFE-MAPPING.md"
      issue: "line 31: recipient.name DANFE label points at the DESTINATÁRIO name box instead of the RECEBEMOS DE receipt stub; line 25: issuer.ie note says the generator prints ISENTO today (only case-003 does)"
  missing:
    - "Point recipient.name at the RECEBEMOS DE receipt stub (text after DESTINATARIO:) and explain the homologation notice in the DESTINATÁRIO box"
    - "Correct the stale issuer.ie note (IE is numeric or ISENTO depending on the case)"
    - "Do together with G-02-2 item 1 (prompt)"
  debug_session: ""
- gap_id: G-02-2
  truth: "The extraction prompt (prompt version extract-002 in Carimbo.Extraction) lists every v2 field with clear DANFE copying rules and the no-fabrication instruction; nothing reads as misleading to the model"
  status: resolved
  resolved_by: 02-13-PLAN.md
  resolved_at: 2026-10-09
  retest: "pass, re-checked line by line with the user on 2026-10-09 against the committed skeleton PDFs and XMLs"
  reason: "User reported: Prompt extract-002 does not match the printed DANFE in three places: (1) the recipient name instruction \"name as printed\" doesn't say where the name is printed; on these DANFEs the DESTINATÁRIO name box shows the homologation text and the name appears only in the receipt stub. The model found it anyway in all six live attempts, but the prompt should name the stub, and the fix should be done together with G-02-1; (2) three totals labels differ from the DANFE: BASE DE CÁLCULO DO ICMS SUBST. is printed BASE DE CÁLCULO DO ICMS ST, VALOR DO ICMS SUBSTITUIÇÃO is printed VALOR DO ICMS ST, VALOR TOTAL DO IPI is printed VALOR DO IPI, and docs/DANFE-MAPPING.md has the same three wrong labels; (3) cst_csosn says \"printed in the CST column\", but Simples invoices print the header CSOSN."
  severity: minor
  test: 2
  related: [G-02-1]
  root_cause: "Prompt extract-002 and docs/DANFE-MAPPING.md were written from DANFE label conventions rather than from the labels BrazilFiscalReport 1.2.0 prints; the recipient name instruction does not say where the name is printed (homologation notice in the DESTINATÁRIO box). Diagnosed in-session from the text of the three rendered skeleton PDFs; no debug agent spawned."
  artifacts:
    - path: "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
      issue: "line 51: recipient name as printed, with no location; line 53: cst_csosn says CST column; line 55: three totals labels (BASE DE CÁLCULO DO ICMS SUBST., VALOR DO ICMS SUBSTITUIÇÃO, VALOR TOTAL DO IPI) differ from the printed BASE DE CÁLCULO DO ICMS ST, VALOR DO ICMS ST, VALOR DO IPI"
    - path: "docs/DANFE-MAPPING.md"
      issue: "lines 48, 49, 55: the same three totals labels (BASE DE CÁLC. ICMS S.T., VALOR DO ICMS SUBST., VALOR TOTAL DO IPI); line 35: CST column label"
  missing:
    - "Prompt: tell the model the recipient name is in the RECEBEMOS DE receipt stub (the DESTINATÁRIO name box may show a homologation notice); use the printed totals labels; describe the column as CST or CSOSN"
    - "Bump the prompt version (extract-002 to extract-003) because the prompt text changes; update tests that pin the version and record the change where prompt versions are documented"
    - "Mapping doc: use the printed totals labels and the CST/CSOSN header wording"
  debug_session: ""
