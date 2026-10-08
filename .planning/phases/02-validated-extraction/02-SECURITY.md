---
phase: "02"
slug: "validated-extraction"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
created: "2026-10-08"
---

# Phase 02 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| Model output → Domain/validators | Untrusted structured output parsed by Wire.Options and checked by InvoiceValidator | JSON invoice candidate (synthetic) |
| Validator findings → repair turn | Feedback sent back to the provider under the disclosure policy | Rule ids, field paths, code-built numeric/date strings |
| Runner → eval endpoint | Local HTTP with an ephemeral eval key, fixed-time check before body read | PDF, reference date, results |
| Api → Anthropic API | Provider key from env/secretspec, never logged | Synthetic DANFE PDF, prompts |
| Ground-truth XML → NfeXmlMapper | Committed synthetic NF-e XML parsed with DTDs prohibited | Synthetic fiscal data |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-02-01 | Tampering | Invoice.PatternViolations over the v2 records | high | mitigate | Reflection walker over every `[RegularExpression]` member including list elements; schema-driven drift test in ExtractorTests fails when a patterned path is not enforced; extractor returns SchemaInvalid naming paths, never values | closed |
| T-02-02 | Denial of service | regex matching of model-controlled strings | low | mitigate | All Patterns are linear (no nested quantifiers; TaxId alternation has two fixed lengths); full-length match via IsFullMatch | closed |
| T-02-03 | Tampering | Decimal4 and Rate wire parsing | high | mitigate | Pattern check before an invariant `decimal.TryParse` without sign or thousands styles; FormatException and OverflowException wrapped in JsonException; tests reject pt-BR, sign, exponent, padding and wrong fraction counts | closed |
| T-02-04 | Information disclosure | data/vectors/valid-invoice.json | medium | mitigate | Only the textbook CNPJ 11222333000181, the NT 2025.001 example 12ABC34501DE35, keys built from them and SINTETICA names (D-14) | closed |
| T-02-SC | Tampering | package installs | low | mitigate | No package added or version changed; Task 3 acceptance runs `git diff --exit-code main -- python/uv.lock dotnet/Directory.Packages.props` | closed |
| T-02-05 | Information disclosure | data/vectors/validator-vectors.json | medium | mitigate | Only published examples (11222333000181, 12ABC34501DE35, 52998224725) and keys built from them; no key copied from nfelib sample XMLs (they can embed real CNPJs); test_synthetic_only keeps using nfelib samples at test time only | closed |
| T-02-06 | Tampering | test oracle | medium | mitigate | Expected values are copied from the plan (published examples and hand-computed weighted sums recorded in notes), never produced by running ids.py; xUnit (02-04) runs the same file against an independent C# port | closed |
| T-02-07 | Repudiation | docs/DECISIONS.md history | low | mitigate | Append-only records plus pointer lines; verify checks 0 deleted lines against main | closed |
| T-02-08 | Information disclosure | LlmSpike --schema-probe output | high | mitigate | Key passed explicitly from the environment and never printed; doc records shapes, counts and parse outcomes only; Task 3 verify greps for key shapes, auth header names and base64 PDF bytes; `just secrets-check` | closed |
| T-02-09 | Denial of service (wallet) | live probe spend | medium | mitigate | `--budget-usd 0.25` with pre-call reservation and skip; one run; spend recorded against the US$5 phase cap | closed |
| T-02-10 | Tampering | conversation shape | medium | mitigate | Gateway refuses non-alternating or assistant-final follow-ups with ArgumentException before any HTTP request; tested with a recording handler | closed |
| T-02-11 | Information disclosure | provider prompt cache of the PDF block | low | accept | The cache is ephemeral (5 minutes) and every document is synthetic (D-14); it is set only when repairs are enabled | closed |
| T-02-12 | Denial of service | InvoiceValidator on overflowing or hostile input | high | mitigate | SafeMath around every decimal operation yields ARITH_OVERFLOW; check-digit routines run only after IsFullMatch; DayNumber integer date comparisons; NeverThrowsTests (2000 seeded mutations) | closed |
| T-02-13 | Tampering (integrity) | silent pass when a check cannot run | high | mitigate | FORMAT findings for malformed identifiers and ARITH_OVERFLOW for overflows; no top-level catch; tests assert the finding appears | closed |
| T-02-14 | Denial of service | regex matching of 100000-character strings | low | mitigate | Linear patterns matched once per field; included in the never-throw loop | closed |
| T-02-15 | Tampering | non-ASCII digits accepted as digits | medium | mitigate | Explicit [0-9] patterns and ordinal character ranges; no char.IsDigit; Arabic-Indic digits are in the hostile set | closed |
| T-02-16 | Information disclosure | generated CPF and CNPJ values | medium | mitigate | Values come from the project's own seeded generators, are checked absent from nfelib sample texts, every name carries SINTETICA, documents are tpAmb 2 with the SEM VALOR FISCAL note. Residual: a check-digit-valid synthetic CPF could coincide with a real person's; the markers make the documents unmistakably synthetic (documented limitation, as T-01-13) | closed |
| T-02-17 | Tampering | data/skeleton bytes | medium | mitigate | Per-case RNG, fixed creation date, `just datagen-check` byte comparison in CI and in this plan | closed |
| T-02-18 | Tampering (integrity) | inconsistent synthetic ground truth | medium | mitigate | Totals computed by the same formulas the validators check; manifest expected blocks from the spec; the Python reader asserted against them; the .NET gate (02-07) asserts zero validator errors | closed |
| T-02-19 | Tampering (measurement integrity) | grade_case status handling | medium | mitigate | validation_failed graded on its candidate and counted as caught, never as success; typed failures carry no grades; tests assert each case | closed |
| T-02-20 | Information disclosure | grader network access | low | mitigate | Grader imports no HTTP client; the existing offline subprocess test keeps passing | closed |
| T-02-21 | Information disclosure / Denial of service | NfeXmlMapper XML loading (external entities, entity expansion) | medium | mitigate | XmlReader with DtdProcessing.Prohibit and XmlResolver null; a test feeds a DOCTYPE and expects XmlException | closed |
| T-02-22 | Tampering (integrity) | mapper hiding generator bugs | medium | mitigate | Printed-form rules only (no inference or correction); the gate fails on any error finding and on a mismatch with the spec-derived manifest expectations | closed |
| T-02-23 | Tampering | reference_date request field | medium | mitigate | Strict `TryParseExact("yyyy-MM-dd")` after authentication and strict body binding; 400 keyed by field without echoing the value; theory over malformed inputs | closed |
| T-02-24 | Information disclosure | logs | medium | mitigate | No log line includes findings, invoices or raw output (acceptance grep); the existing key-secrecy tests keep passing | closed |
| T-02-25 | Elevation of privilege | eval endpoint availability and auth order | high | mitigate | Unchanged order: availability gate, fixed-time key check before any body read; the existing 401/404 tests run against contract 2 | closed |
| T-02-26 | Repudiation (integrity of cost) | cost and usage sums across attempts | medium | mitigate | Null-not-partial rule with warning; invariant test that top-level values equal the sums over attempts | closed |
| T-02-27 | Denial of service | response size with attempts | low | accept | One attempt in this plan; bounded by MaxTokens × attempts later; request size cap (10 MB) unchanged | closed |
| T-02-28 | Tampering | prompt injection from DANFE text into the repair turn | high | mitigate | RepairFeedback builds every detail in code from rule ids, field paths and validator-built numeric or date strings; previous model output travels only in the assistant role; test with an injected operation_nature phrase | closed |
| T-02-29 | Tampering (integrity) | model fabricating values to satisfy validators | high | mitigate | Disclosure policy (no expected values for identifier, check-digit and key rules), repair-001 anti-fabrication sentences asserted by test, every attempt recorded so later phases can measure repair damage | closed |
| T-02-30 | Denial of service (wallet) | unbounded repair | high | mitigate | MaxRepairs bounded 0..5 at startup (tests at -1, 0, 5, 6); configuration only, no per-request override; runner cost cap and reserve unchanged | closed |
| T-02-31 | Denial of service | latency amplification | medium | mitigate | Per-attempt provider timeout 300 s; at most MaxRepairs + 1 sequential calls; runner read timeout raised in 02-10 | closed |
| T-02-32 | Elevation of privilege | ScriptedHost attempts[] | low | accept | Test-only host under dotnet/tests, never referenced by Carimbo.Api, reads files only by PDF digest from an explicit directory | closed |
| T-02-33 | Denial of service (wallet) | live skeleton runs with repair | high | mitigate | Key precondition; runner cap 1.00 USD per run with a 0.25 USD per-case reserve; MaxRepairs bounded at startup; two runs only; cumulative spend checked against the US$5 phase cap before running | closed |
| T-02-34 | Information disclosure | provider or eval key in run files | high | mitigate | Ephemeral eval key minted inside the recipe; runner never serializes headers; verify greps evals/runs for key shapes and auth header names | closed |
| T-02-35 | Tampering | committing run output | low | mitigate | evals/runs is git-ignored; acceptance requires an empty `git status --porcelain evals` | closed |

*Status: open · closed · open — below high threshold (non-blocking)*
*Severity: critical > high > medium > low — only open threats at or above workflow.security_block_on count toward threats_open*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

Evidence (L1, grep depth, 2026-10-08): reflection pattern walker in Carimbo.Domain (T-02-01); FormatException/OverflowException wrapping (T-02-03); gateway ArgumentException on bad conversation shape (T-02-10); ARITH_OVERFLOW findings and NeverThrowsTests (T-02-12); no `char.IsDigit` in dotnet/src (T-02-15); `DtdProcessing.Prohibit` in the mapper (T-02-21); `TryParseExact` for reference_date (T-02-23); fixed-time key check (T-02-25); RepairFeedback disclosure policy (T-02-28/29); MaxRepairs 0..5 startup bound (T-02-30); DefaultTimeoutSeconds = 300 (T-02-31); no key shapes in evals/runs and evals/runs/ git-ignored (T-02-34/35); uv.lock and Directory.Packages.props unchanged against main (T-02-SC); grader imports no HTTP client (T-02-20); no log call carries findings, invoices or raw output (T-02-24). `just check` green, including secrets-check.

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-02-01 | T-02-11 | Provider prompt cache is ephemeral (5 min), documents are synthetic (D-14), set only when repairs are enabled | plan 02-03 | 2026-10-08 |
| AR-02-02 | T-02-27 | Response size bounded by MaxTokens × (MaxRepairs + 1); 10 MB request cap unchanged | plan 02-08 | 2026-10-08 |
| AR-02-03 | T-02-32 | ScriptedHost is test-only under dotnet/tests, never referenced by Carimbo.Api | plan 02-09 | 2026-10-08 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-10-08 | 36 | 36 | 0 | secure-phase (orchestrator, L1) |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-10-08
