# Feature Research

**Domain:** LLM-powered Brazilian NF-e (DANFE PDF to typed record) extraction pipeline with a reproducible LLM eval harness; portfolio project for senior AI platform reviewers
**Researched:** 2026-10-03
**Scope:** Milestone 1 (brief Phases 1-4). M2/M3 noted only where they constrain M1 design.
**Confidence:** MEDIUM-HIGH. Check-digit algorithms and key structure are stable, well-known and cross-checked (HIGH). Alphanumeric CNPJ and the IBS/CBS reform were verified against web sources (MEDIUM-HIGH). Exact DANFE layout contents, CRT/CST nuances and the `vNF` formula come from domain knowledge of NF-e layout 4.00 and should be checked against the current MOC (Manual de Orientacao do Contribuinte) during Phase 1/2 planning (MEDIUM). Eval-harness patterns come from established practice (Inspect, promptfoo, Braintrust, Anthropic's error-bars paper) (MEDIUM-HIGH).

## Four findings that change M1 design

These are not features. They are facts that make several obvious designs wrong, so they come first.

1. **Alphanumeric CNPJ is live now.** The Receita Federal's alphanumeric CNPJ (IN RFB 2.229/2024) started with registrations from July 2026. Today is 2026-10-03, so real invoices now carry CNPJs such as `12ABC34501DE35`. Format is `[A-Z0-9]{12}[0-9]{2}`. The mod-11 algorithm is unchanged, but each character's value is `ASCII(c) - 48` (digits keep 0-9, `A`=17, `B`=18, and so on). The same rule applies to the access key: the 44-char key is now `[0-9]{6}[A-Z0-9]{12}[0-9]{26}` and its check digit uses the same ASCII-48 conversion. Existing numeric CNPJs stay valid indefinitely. Validators, the schema pattern, the generator and the dataset must all support both. A validator that only handles digits will look dated to any Brazilian reviewer. (Sources: NT Conjunta 2025.001 coverage, Tecnospeed, Tecnoblog, Serpro. MEDIUM-HIGH.)
2. **The DANFE does not contain everything in the XML.** The XML is exact ground truth for the document, not for what is visible on the PDF. The DANFE does not print the CRT (tax regime code), does not print per-item PIS/COFINS, and does not print `idDest`, `finNFe` or `cNF` as separate fields. Grading a model against XML fields it cannot see penalizes it for impossible work and pollutes accuracy numbers. The extraction target (`Invoice`) must be the **DANFE-visible projection** of the XML, with an explicit documented mapping. Regime is inferred from the CST vs CSOSN column format (three digits origin+CST vs four digits origin+CSOSN). PIS/COFINS are either validated only on the XML side, or the generator prints them in "Informacoes complementares" (decide in Phase 1/2; see Anti-Features and Gaps).
3. **The response cache (D-07) and variance measurement conflict.** A request-hash cache returns identical bytes on rerun, so repeated runs show zero variance and give false confidence. The cache key must include a `replicate`/salt field and the runner needs `--cache-mode use|refresh|bypass`. Cached responses also carry meaningless latency and misleading cost. Store `cache_hit`, the original latency and the original cost, and report "cost incurred" vs "cost if uncached" separately.
4. **D-15 conflicts with `compare`.** D-15 says full JSONL is not committed, only summaries. But "regressed cases between two runs" and paired statistics need per-case outcomes for the baseline. Resolution: commit `summary.json` **plus a compact per-case score table** (`case_id`, per-grader pass/fail and key scores, tens of KB) under `evals/reports/`. Keep raw outputs in gitignored JSONL and as CI artifacts.

## NF-e Domain Reference (what the extractor and validators must cover)

### Extraction target: DANFE-visible fields

| Block | Fields (XML tag) | Notes |
|-------|------------------|-------|
| Identification | access key `chNFe` (44), `nNF`, `serie`, `mod` (55), `natOp`, `tpNF`, `dhEmi`, `dhSaiEnt`, protocol `nProt` + `dhRecbto` | Key printed in groups of 4 and as Code 128C barcode. `mod` must be 55; reject NFC-e (65). Homologation DANFEs carry a "SEM VALOR FISCAL" notice. |
| Issuer `emit` | `xNome`, `CNPJ`, `IE`, address (`xLgr`, `nro`, `xBairro`, `xMun`, `UF`, `CEP`) | CNPJ may be alphanumeric. `CRT` is NOT printed. |
| Recipient `dest` | `xNome`, `CNPJ` or `CPF` (or foreign ID), `IE`, address | Individuals use CPF (11 digits, mod 11). Exports use a foreign ID. |
| Line items `det` | `nItem`, `cProd`, `xProd`, `NCM`, `CFOP`, `uCom`, `qCom`, `vUnCom`, `vProd`, `vDesc`, origin+`CST`/`CSOSN`, `vBC`, `vICMS`, `pICMS`, `vIPI`, `pIPI` | `qCom` up to 4 decimals, `vUnCom` up to 10, so `vProd = round(qCom x vUnCom, 2)` needs a rounding tolerance. Descriptions wrap across lines on the PDF. |
| Totals `ICMSTot` | `vBC`, `vICMS`, `vBCST`, `vST`, `vProd`, `vFrete`, `vSeg`, `vDesc`, `vOutro`, `vIPI`, `vNF` | `vPIS`/`vCOFINS` totals exist in XML but are not in the standard DANFE block. |
| Billing | `fat`, `dup` (`nDup`, `dVenc`, `vDup`) | Due dates matter for finance. Secondary priority. |
| Transport | `modFrete`, carrier name/CNPJ, volumes | Optional; low priority. |
| Additional info | `infCpl` free text | Fuzzy-graded at most. Prompt-injection slice lives here and in item descriptions. |

Format normalization is a core extraction concern: the DANFE prints `1.234,56` and `dd/mm/yyyy`, the XML stores `1234.56` and ISO 8601 with offset. A silent thousands-separator misparse (`1.234,56` read as `1.23456`) is a classic failure and gets its own error-taxonomy tag.

### Access key structure (44 digits)

`cUF(2) | AAMM(4) | CNPJ(14) | mod(2) | serie(3) | nNF(9) | tpEmis(1) | cNF(8) | cDV(1)`

- **cDV:** mod 11 over the first 43 characters, weights 2..9 cycling right to left; `rem = sum % 11`; `cDV = 0` if `rem` is 0 or 1, else `11 - rem`. Character value is `ASCII - 48` (matters for alphanumeric CNPJ).
- **Cross-checks (D-05):** `cUF` equals the IBGE code of the issuer UF; `AAMM` equals the year-month of `dhEmi`; embedded CNPJ equals issuer CNPJ; `mod` equals `ide.mod` (55); `serie` and `nNF` equal the zero-padded printed values; `tpEmis` equals `ide.tpEmis`.

### CNPJ (and CPF) check digits

- CNPJ DV1 uses weights 5,4,3,2,9,8,7,6,5,4,3,2 over the first 12 characters; DV2 uses 6,5,4,3,2,9,8,7,6,5,4,3,2 over the first 13. In each case `rem < 2` gives 0, else `11 - rem`. Character value is `ASCII - 48`.
- Reject all-identical-character sequences (`00000000000000`): they pass the arithmetic but are invalid.
- Mod 11 detects any single-character substitution but NOT all adjacent transpositions, and remainders 0 and 1 both map to DV 0. This is why the key-vs-fields cross-check adds value beyond checksums, and it makes a good property-test target.
- CPF is mod 11 with weights 10..2 and 11..2 (same reject-identical rule).

### Tax groups and regimes

| Group | What to model | Validation rule |
|-------|---------------|-----------------|
| ICMS, Regime Normal (CRT 3) | CST 00, 10, 20, 30, 40, 41, 50, 51, 60, 70, 90 (plus newer monophase codes) with `vBC`, `pICMS`, `vICMS`; ST variants `vBCST`, `pICMSST`, `vICMSST`; reduction `pRedBC`; deferral (51) | `vICMS = vBC x pICMS / 100` (+/- R$0.01). CST 40/41/50 carry no `vICMS`. CST 10/30/70 require ST fields. CST 60 uses retained-ST fields. |
| ICMS, Simples Nacional (CRT 1, CRT 2 sub-limit excess, CRT 4 MEI) | CSOSN 101, 102, 103, 201, 202, 203, 300, 400, 500, 900 | CSOSN 102/103/300/400 mean no ICMS amount. CSOSN 101 carries a credit (`pCredSN`/`vCredICMSSN`) that is not tax due. Normal-regime `vICMS` rules do not apply. Regime-aware validation is mandatory; applying CST rules to Simples invoices produces false errors. |
| IPI | CST 00-05 (entry), 49-55 (exit); `vBC`, `pIPI`, `vIPI`. Only industrial issuers charge it | `vIPI = vBC x pIPI / 100`. Often absent entirely (valid). |
| PIS / COFINS | CST 01/02 (ad valorem), 03 (quantity), 04-09, 49, 99. Typical rates 0.65%/3.00% (presumed profit), 1.65%/7.60% (real profit); Simples issuers typically use CST 49/99 with zero values | Item-level `vPIS = vBC x pPIS / 100`. Not on the DANFE, so XML-side validation and generator realism only (see finding 2). |
| Totals | `vNF = vProd - vDesc - vICMSDeson + vST + vFrete + vSeg + vOutro + vII + vIPI` (+ FCP-ST and similar when present) | `ICMSTot.vProd` equals the sum of item `vProd`; `vICMS`, `vBC`, `vIPI` equal item sums (+/- R$0.01 per rounding). |
| IBS / CBS / IS (tax reform, NT 2025.002) | New group UB with IBS, CBS and Imposto Seletivo; mandatory for Regime Normal issuers from 2026-08-03 per the COAD source (MEDIUM, verify). New schema version required from 2027 | Out of M1 scope (see Anti-Features) but the `Taxes` record must be extensible, and the README must state this as a known limitation. |

### Validator rule catalogue (stable rule IDs, D-06 shape `{field, rule, expected, actual}` plus severity)

| Rule ID | Check | Severity | Complexity |
|---------|-------|----------|------------|
| `CNPJ_FORMAT` / `CNPJ_CHECK_DIGIT` | Regex `[A-Z0-9]{12}[0-9]{2}`, mod 11, reject repeated chars | hard | LOW |
| `CPF_CHECK_DIGIT` | Recipient individual | hard | LOW |
| `KEY_FORMAT` / `KEY_CHECK_DIGIT` | 44 chars, alphanumeric-aware mod 11 | hard | LOW |
| `KEY_FIELD_MISMATCH` (one per component) | The cross-checks above | hard | MEDIUM |
| `ITEM_ARITH` | `vProd` vs `qCom x vUnCom` | soft (tolerance) | LOW |
| `TOTAL_SUM_*` | Item sums vs `ICMSTot`, `vNF` formula | soft (tolerance) | MEDIUM |
| `TAX_ARITH_ICMS/IPI/ST` | base x rate vs amount | soft (tolerance) | MEDIUM |
| `REGIME_CODE_MISMATCH` | CST vs CSOSN family vs inferred regime; amounts allowed per code | hard | MEDIUM |
| `CFOP_SCOPE` | First digit 5/6/7 vs issuer UF, recipient UF, destination | soft | LOW |
| `DATE_PLAUSIBLE` | Not in the future, not before 2006, `dhSaiEnt` not before `dhEmi` beyond tolerance, Brazilian UTC offset | soft | LOW |
| `ENUM_*` | UF, 8-digit NCM, `mod == 55` | hard | LOW |
| `DUP_SUM` | Sum of `dup` equals `vNF` (when present) | soft | LOW |

Validator outcomes drive three things: the repair prompt (D-06), deterministic free graders (D-05) and slice analysis. Keep rule IDs stable, because eval artifacts and committed summaries reference them.

## Feature Landscape

Complexity: LOW (hours), MEDIUM (a day or two), HIGH (multi-day or research-heavy). Phase refers to the brief: P1 Foundation, P2 Dataset, P3 Pipeline, P4 Harness.

### Table Stakes (reviewers dismiss the project without these)

#### A. Extraction pipeline

| Feature | Why Expected | Complexity | Phase | Notes |
|---------|--------------|------------|-------|-------|
| Schema-constrained output yielding a typed `Invoice` or a typed failure (never free text, never an exception) | Baseline for "production, not prototype" | MEDIUM | P3 | Typed failure carries reason enum (`SchemaInvalid`, `ValidationFailedAfterRepairs`, `ModelRefusal`, `NotAnNfe`, `InfraError`). Infra errors are distinguishable from quality failures, which the eval needs. |
| Deterministic validators with structured, never-throwing errors and known-valid/known-invalid test vectors | D-05/D-06; core thesis | MEDIUM | P1 | Include alphanumeric CNPJ and key vectors. Shared JSON test-vector file consumed by both xUnit and pytest. |
| Bounded validate-and-repair loop (default 2) feeding structured errors back | D-06 | MEDIUM | P3 | Record every attempt (prompt hash, errors, tokens, cost). Repair only on hard and arithmetic errors. |
| LLM gateway: retries with backoff, token and cost accounting, OTel span per call, request-hash cache | D-07/D-17 | MEDIUM | P3 | Versioned pricing table. Cache key includes model, params, messages, schema and a replicate salt. Include cache-read/write tokens in accounting (Anthropic prompt caching). |
| Sync eval endpoint returning result, validator outcomes, per-attempt detail, tokens, cost, latency, trace ID, `cache_hit` | D-02 | MEDIUM | P3 | Accepts per-request overrides (model, prompt version, max repairs, cache mode/salt). Returns pipeline build info (git SHA, prompt hash, schema hash). Never receives ground truth. |
| Multi-page and many-line-item handling | Real invoices have 50+ items; brief requires it | MEDIUM | P3 | Page handling and output-token limits for long item lists are the likely failure mode (see Pitfalls). |
| Eval-path vs production-path parity | D-02: evals must describe production code | LOW | P3 | Same extraction code, only intake/Temporal bypassed. |

#### B. Synthetic dataset

| Feature | Why Expected | Complexity | Phase | Notes |
|---------|--------------|------------|-------|-------|
| Seeded, reproducible generator producing paired `{case}.xml` + `{case}.pdf` | D-14; brief | HIGH | P2 | Determinism test: same seed gives byte-identical XML and identical rendered-page hashes. Pin PDF creation dates and IDs, because PDF bytes often embed timestamps. |
| 150+ cases with per-case metadata tags (manifest) | Brief; enables slicing | MEDIUM | P2 | Manifest row: `case_id`, seed, tags, split, `expected_outcome`, `variant_of`, truth path. |
| Coverage dimensions (see table below) | Brief | HIGH | P2 | Tags are overlays, not exclusive buckets. |
| Ground truth validates against exported JSON Schema AND passes the .NET validators (except deliberate negatives) | D-03/D-05 | MEDIUM | P2 | Dataset build fails otherwise. Cross-language conformance run (CLI or `/validate`) is a free consistency test of both sides. |
| Dataset version (semver + content hash over manifest and files) recorded in every run | D-14 | LOW | P2 | |
| Fixed dev/test split and a fixed stratified CI subset (case-ID list committed) | Prevents prompt overfitting; D-15 | LOW | P2 | Tune on dev; report headline on test. State this in the README. |
| Code 128C barcode of the access key on DANFE; readable and unreadable variants | Brief | MEDIUM | P2 | Only meaningful if a decoder exists (see Gaps: is barcode decoding in M1?). |

**Synthetic dataset coverage dimensions** (recommended stratification, overlapping; about 150-200 total):

| Dimension | Values | Suggested weight |
|-----------|--------|------------------|
| Tax regime | CRT 3 (CST, ST, IPI, reduction, deferral), CRT 1 Simples (CSOSN 101/102/400/500/900), CRT 2, CRT 4 MEI | about 40 / 35 / 5 / 5 percent, plus 15 percent ST and IPI heavy |
| Length | 1-5 items, 6-15, 16-40, 41-100+; single-page vs 2-4 pages | at least 25 multi-page cases |
| Money mix | Plain; discount; freight; insurance; other expenses; ICMS desonerado; rounding-edge cases | |
| Operation | CFOP 5xxx intra-state, 6xxx inter-state, 7xxx export; recipient CPF vs CNPJ vs foreign ID | |
| CNPJ type | Numeric (legacy) vs alphanumeric (new) | at least 10 percent alphanumeric |
| PDF modality | Native text-layer PDF vs image-only (rasterized) PDF | Dominant accuracy factor. Make it a tag. |
| Degradation | Rotation +/-2-5 degrees, Gaussian blur, low DPI (100/150), noise, JPEG artifacts, low contrast, crop margin, combined | about 30 cases, graded severity levels |
| Barcode | Readable, unreadable (blur/damage/cut), absent | about 10 unreadable |
| Layout variants | Item-description wrapping, long names, multi-line `infCpl`, with and without duplicatas, carrier block present or absent | |
| Adversarial / negative (with `expected_outcome`) | Invalid CNPJ DV printed; invalid key DV; key vs fields mismatch; totals do not add up; non-NF-e document; blank page; NFC-e; homologation watermark; prompt-injection text in item description or `infCpl` | about 15 cases |
| Realism | Recurring vendor pool from a seeded "world", realistic NCM/CFOP/UF/product names, plausible amounts | Needed by M2 (vendor and PO reference data) |

Negative cases need an `expected_outcome` of `success` or `typed_failure(rule)`. Ground truth for a document with a bad printed CNPJ is "what is printed", and the pipeline's correct behavior is to flag it, not to silently correct it.

#### C. Eval harness

| Feature | Why Expected | Complexity | Phase | Notes |
|---------|--------------|------------|-------|-------|
| Async runner, bounded concurrency, per-case timeout, infra-error retry, resumable (skip completed case IDs) | Brief; any real runner | MEDIUM | P4 | Infra errors (429, timeout, 5xx) are counted separately from wrong answers. Incomplete runs fail the gate. |
| Per-case JSONL record: case ref, tags, status, raw output, extracted invoice, attempts, grader scores, tokens, cost, latency, trace ID, cache flag | D-15 | MEDIUM | P4 | Schema versioned. Include `task: extraction` and an `expected` blob so M2 can add `decision` tasks without a rewrite. |
| Run manifest and summary (see Run Artifacts below) | Reproducibility is the core value | MEDIUM | P4 | |
| Graders: schema validity, exact match (IDs, CNPJ, key), numeric tolerance (amounts, taxes), line-item matching | Brief | MEDIUM-HIGH | P4 | Details below. |
| Per-field and per-slice accuracy (by tag), not a single blended number | Reviewers distrust one score | LOW | P4 | |
| `compare`: per-field deltas, per-slice deltas, regressed/improved case lists, cost and latency deltas | Brief | MEDIUM | P4 | Refuse or warn on different dataset versions. Operate on the intersection of case IDs. |
| CI eval subset on PRs with per-grader thresholds; nonzero exit on breach | Brief | MEDIUM | P4 | Config file under version control. Write the results table to `$GITHUB_STEP_SUMMARY`. |
| Results table (README-ready markdown generated from committed summaries) comparing two models or two prompt versions | Brief; the deliverable | MEDIUM | P4 | Include n and confidence intervals in the table. |
| Cost and latency reporting (total, per invoice, p50/p95) | Brief | LOW | P4 | See section below. |
| Confidence intervals on every headline number | Senior reviewers check this first | LOW-MEDIUM | P4 | Wilson for proportions; case-clustered bootstrap for field-level pooled metrics. |

**Grader specifics (table stakes level):**

| Grader | Behavior | Notes |
|--------|----------|-------|
| Schema validity | Output parses and validates against the exported JSON Schema; typed failure reported distinctly | Near 100 percent with constrained decoding; report anyway (a regression here is a hard fail). |
| Exact match | Digits/letters-only normalization for CNPJ, CPF, key, NCM, CFOP, `nNF`; case-insensitive; whitespace-trimmed | Report per field. Alphanumeric CNPJ slice reported separately. |
| Numeric tolerance | `decimal` arithmetic (never float). Default absolute tolerance R$0.01 for amounts; 0.0001 for quantities; 0.01 percentage points for rates. Report strict (exact cents) and tolerant side by side | Tolerance values live in config, versioned. |
| Date | Exact date after normalization; timezone-normalized | |
| Text fields (names, descriptions) | Normalized exact plus fuzzy ratio (for example >= 0.9); no LLM judge in M1 | Judges are deferred (D-16). |
| Line-item matching | Do not compare by index. Align predicted and truth items by assignment on a cost matrix (code + description similarity + total), order-preserving (DP/Needleman-Wunsch) or Hungarian, with documented tie-breaking | Report item precision/recall/F1, count delta, per-field accuracy over matched pairs, and "all-fields-correct item rate". Distinguish dropped, hallucinated, merged and split rows. |
| Invoice-level strict pass | All critical fields (key, issuer CNPJ, recipient ID, `vNF`, dates, item count, item totals) correct | Primary headline metric. Declare the primary metric before looking at results. |

### Differentiators (set the project apart for senior AI platform reviewers)

Ordered by value for the effort. Each aligns with the Core Value (measured quality).

| Feature | Value Proposition | Complexity | Phase | Notes |
|---------|-------------------|------------|-------|-------|
| **Silent-error rate and coverage/accuracy trade-off** (selective prediction) | In regulated finance the dangerous outcome is a wrong value that passes validators. Report coverage (fraction returned as success), accuracy among returned, and silent-error rate (returned success but any critical field wrong). Typed failure is a feature, not a miss | LOW | P4 | The most domain-relevant metric in the project. Headline it next to accuracy. |
| **Repair-loop and validator ablation** | Proves the validate-and-repair loop earns its cost: run with `maxRepairs=0` vs 2. Report first-pass accuracy vs final, repair success rate, extra tokens and latency, attempts distribution | LOW | P3-P4 | Requires the eval endpoint to accept `maxRepairs`. Cheap and unusually convincing. |
| **Separate `run` from `grade`** (offline re-grading of stored outputs) | Grader changes cost nothing and never re-call the pipeline; honors D-07's intent. Graders are versioned pure functions | LOW-MEDIUM | P4 | Store raw model output and the extracted invoice per case. `evals grade <run_id>` writes a new summary. |
| **Paired run comparison with significance** | `compare` reports per-case paired differences with a bootstrap CI or exact McNemar, not just deltas. Per Anthropic's "Adding Error Bars to Evals", paired and clustered analysis shrinks error bars substantially (clustered SEs can be 3x naive) | MEDIUM | P4 | Flag each delta as `significant`, `inconclusive` or `no change`. Clustering unit is the case. |
| **Repeated runs with variance decomposition** | k >= 3 replicates (cache bypassed or salted) on the headline comparison. Report mean and SD across runs, per-case flip rate, flaky-case list, and "noise floor" used by the gate | MEDIUM | P4 | Needs the cache salt (finding 3). Temperature 0 is not deterministic on hosted models. |
| **Two-tier CI**: PR tier with record/replay cache, plus nightly/manual live tier | PR tier is free, deterministic and catches grader/validator/schema regressions. A prompt change causes a cache miss and is flagged as "needs live run". Live tier measures the real model and is budget-capped | MEDIUM | P4 | Avoids the two common failures: flaky paid CI, and CI that silently measures nothing. Fork PRs have no secrets, which is another reason for replay. |
| **Gate design: absolute floors plus non-inferiority vs pinned baseline** | Per-grader floors, zero-tolerance hard gates (schema validity, deterministic validators), and a relative gate: fail if the CI lower bound of the paired difference is below `-delta`. Cost and p95 latency budgets as gates too | MEDIUM | P4 | Baseline lives in `evals/baselines/` and moves only through an explicit `evals baseline update` in a PR (ratchet, like snapshot updates). |
| **Honest minimum-detectable-effect statement for the CI subset** | With n=25-30, only large drops are detectable. State this and call the PR gate a tripwire; the nightly full run is the proof. Shows statistical maturity | LOW | P4 | Include a small power/MDE helper or table in the docs. |
| **Failure taxonomy auto-tagging** | Classify each miss from the diff: digit transposition in key, issuer/recipient swap, thousands-separator misparse, dropped/merged/hallucinated row, page-2 totals missed, wrong regime code. Feeds the brief's required written failure analysis | MEDIUM | P4 | Rule-based on grader diffs; no LLM. |
| **Slice heatmap and cost-accuracy plots** (accuracy by tag vs model; cost per correct invoice; latency percentiles) | Makes the results table readable at a glance; the "cost per correct invoice" figure is rare and useful | LOW-MEDIUM | P4 | Matplotlib from summaries, regenerated in CI. |
| **Input-modality as a measured axis** | Native PDF vs rasterized images vs text layer plus images, measured on the native/scanned slices; cost and accuracy trade-off | MEDIUM | P3-P4 | Pairs naturally with the "PDF modality" dataset tag. A strong first two-config comparison alongside two models. |
| **Validator fault injection (mutation testing)** | Mutation operators (flip one key digit, swap two CNPJ digits, perturb a total) with known expected rule IDs; assert detection. Reports validator recall and surfaces mod-11 transposition gaps | MEDIUM | P1-P2 | Property-based tests (FsCheck/Hypothesis) on generated valid CNPJ/keys. |
| **Cross-language conformance fixtures** | One JSON vector file used by xUnit, pytest and the dataset build. Optionally cross-check against an independent library (for example python-stdnum, if it supports alphanumeric CNPJ; verify) | LOW | P1 | Directly demonstrates D-03 in practice. |
| **Barcode-first key with cross-check** | Decode Code 128 to get the key with zero model calls (D-05), compare with the LLM-read key, and log `key_source`. Disagreement is a high-signal anomaly | MEDIUM | P3 | Decide in discuss phase whether barcode decoding is in M1 (Gaps). |
| **Dry-run cost estimate and spend cap** | `evals run --dry-run` estimates cost from token counts; `--max-cost` aborts. Reviewers value budget awareness | LOW | P4 | |
| **Provenance manifest on every run** | Git SHA and dirty flag, dataset version/hash, case-list hash, model IDs, prompt hash, schema hash, pipeline config, grader versions, thresholds, cache mode, concurrency, seed, harness and service versions | LOW | P4 | The difference between "reproducible" as a claim and as a property. |
| **Realistic world model in the generator** (recurring vendor pool, `variant_of` links: same key, different scan) | Zero extra M1 value, but M2 near-duplicate and M3 idempotency/re-scan scenarios get data for free | LOW-MEDIUM | P2 | Cheap now, expensive to retrofit. |

### Anti-Features (commonly requested, deliberately NOT built)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| LLM-as-judge graders in M1 | Feels modern and "AI eval" | Ground truth is exact, so deterministic graders are cheaper, reproducible and unbiased. An uncalibrated judge is an unmeasured metric (D-16) | Defer to M2 reason-quality grading, calibrated on a human-labeled sample |
| Generic eval framework, plugin system or YAML grader DSL | Looks extensible | Rebuilds Inspect/promptfoo badly and dilutes the thesis | Small grader registry (functions keyed by name, versioned). README states why custom: it must call the real .NET pipeline over HTTP (D-02) and run domain graders |
| Results database, dashboard UI or hosted tracking (Langfuse, W&B, MLflow) | "Real" platforms have them | Hides the artifact trail; adds infra; D-15 wants a git-visible history | JSONL plus committed summaries plus generated markdown and plots. Trace IDs link to Jaeger |
| Full NF-e schema coverage (fuel, medicines, vehicles, export documents, import declarations, traceability) | Completeness | Hundreds of fields; modeling effort without evaluation value | A documented, versioned "supported subset" with unsupported structures flagged as typed failure or ignored fields |
| State registration (IE) check-digit validation for 27 UFs | Looks thorough | 27 divergent algorithms; zero thesis value | Format-only check or none; note in limitations |
| NFC-e, CT-e, NFS-e, MDF-e, DANFE-Simplificado or contingency FS-DA support | "Brazilian invoices" is broader | Different layouts and rules | Detect and reject with typed failure (`NotAnNfe` / `UnsupportedDocument`); include as negative cases |
| SEFAZ lookup, XML signature or protocol validation | "Verify authenticity" | Out of scope per brief; requires certificates and live services | Structural key validation only; state limitation |
| IBS/CBS/IS full modeling in M1 | Reform is topical, mandatory for Regime Normal issuers from 2026-08 | Layout still moving; the DANFE print layout is not settled in sources checked; large scope | Extensible `Taxes` record; limitation and roadmap note in README; revisit after M3 |
| Custom OCR, layout models or fine-tuning | "Better extraction" | Contradicts the LLM-first thesis and out-of-scope list | Optionally one preprocessing-free baseline for comparison, not a pipeline |
| Prompt auto-optimization (DSPy-style) | Popular | Obscures what is being measured; invites overfitting to a 150-case set | Manual prompt versions compared with paired statistics |
| Agent frameworks, multi-provider model matrix | Convenience; "model-agnostic" | Locked (D-08; Anthropic decided) | Gateway abstraction only; compare two Anthropic models |
| Live (unsalted) paid API calls in every PR | "Test the real thing on every change" | Costly, flaky, impossible on fork PRs, and statistically underpowered at n about 25 | Two-tier CI (replay on PR, live nightly) |
| One blended "accuracy" number as the headline | Easy to communicate | Hides critical-field failures behind many easy fields | Invoice-level strict pass, silent-error rate, per-field table |
| Passing ground truth, or any case-identifying hint, to the pipeline | Convenience in debugging | Invalidates the eval | Eval endpoint takes only the document and config; assert this in a test |
| Counting infra errors (429, timeouts) as wrong extractions, or silently dropping them | Simpler aggregation | Biases comparisons and hides capacity problems | Separate `error_rate`; gate on completeness |

## Eval Harness Detail

### Run artifacts

```
evals/runs/<run_id>/            (gitignored, uploaded as CI artifact)
  manifest.json                 provenance (see differentiator "Provenance manifest")
  results.jsonl                 one record per case x replicate
  summary.json                  aggregates, CIs, slices, cost, latency, errors
evals/reports/<run_id>/         (committed, D-15)
  summary.json
  cases.csv                     compact per-case scores for compare and paired stats (no raw output)
  report.md                     generated results table
evals/baselines/main.json       pinned baseline pointer; updated only via explicit command
```

Per-case record fields: `run_id`, `case_id`, `replicate`, `tags`, `status` (`ok | typed_failure | infra_error | timeout`), `failure_reason`, raw model output ref, extracted invoice, `attempts[]` (prompt hash, validator errors, tokens, cost, latency), `grader_scores{}` per field and aggregate, `tokens{in,out,cache_read,cache_write}`, `cost_usd`, `latency_ms`, `latency_original_ms`, `cache_hit`, `trace_id`, `key_source` (`barcode | model`).

`summary.json` contents: metrics with n and CI; per-field and per-slice tables; silent-error rate, coverage; repair stats; failure taxonomy counts; token and cost totals; latency p50/p95/p99 (cache-miss calls only); error and incomplete counts; replicate dispersion when k > 1.

### Cost and latency reporting

- Cost computed centrally in the gateway from a **versioned pricing table** (model to $/MTok for input, output, cache read, cache write); the pricing version is in the manifest, so old runs stay interpretable when prices change.
- Report: total spend, cost per invoice (mean and p95), **cost per correct invoice** (spend / strict-pass count), repair overhead (extra cost attributable to attempts > 1), and tokens per page.
- Latency: p50/p95/p99 end-to-end and per attempt; computed on non-cached calls; note concurrency level, because rate limiting inflates latency under load.
- Cached calls: keep `cache_hit`; show both cost incurred and cost-if-uncached; never mix cached latency into percentiles.

### Run comparison and regression detection

1. Verify comparability (dataset version and case-ID intersection; grader versions; warn on config differences beyond the intended variable).
2. Per-field and per-slice deltas with paired bootstrap CI (or exact McNemar for binary invoice-level pass).
3. Case lists: regressed, improved, unchanged-failing; flaky cases flagged when replicates exist.
4. Cost and latency deltas with CIs.
5. Machine-readable output plus markdown; exit code reflects the gate.

### CI gating

- Thresholds in a versioned config (per grader): absolute floors, relative non-inferiority margin, hard zero-tolerance gates (schema validity, deterministic validator conformance), cost and p95 latency budgets.
- Fixed stratified subset (about 20-30 cases, committed ID list) covering each major tag at least once, including one alphanumeric CNPJ, one multi-page, one degraded and one negative case.
- Path-filtered trigger (pipeline, prompts, schema, graders, thresholds).
- PR tier uses replay cache; cache miss caused by a prompt/model change fails with "run live tier" guidance. Nightly/manual live tier is budget-capped.
- Output: step-summary table, uploaded artifact, trace-ID links for failing cases.
- State the MDE: a gate on about 25 cases detects only large regressions. The full dataset on nightly gives the evidence.

### Statistical rigor (proportionate to a 150-case dataset)

| Practice | Level | Notes |
|----------|-------|-------|
| Report n with every number | Table stakes | |
| Wilson interval for case-level proportions | Table stakes | Normal approximation misbehaves near 0 and 1, which is where extraction accuracy lives. |
| Cluster by case for pooled field-level metrics | Table stakes | Fields within an invoice are correlated; naive field-level SE is too small. |
| Seeded percentile bootstrap (about 10k resamples) for ratios and means | Table stakes | Seed recorded in manifest. |
| Paired comparison between runs | Differentiator | Same cases, so use paired differences. |
| k >= 3 replicates on the headline comparison, cache salted | Differentiator | Gives noise floor and flaky-case list. |
| Pre-declared primary metric (strict invoice pass); others exploratory | Differentiator | Avoids reporting 30 per-field p-values and picking winners. |
| Power/MDE note | Differentiator | One paragraph and a small helper. |
| Held-out test split reported separately from dev | Differentiator | Cheap and prevents prompt overfitting on 150 cases. |

## Feature Dependencies

```
Domain records (C#) ──requires──> JSON Schema export ──requires──> Python models (codegen) + CI staleness check
        │
        └──requires──> Validators (alphanumeric-aware CNPJ/key, regime-aware tax rules) ──> shared test vectors
                                     │
Synthetic generator (seeded world, XML truth) ──requires──> Validators (truth must pass)  [dataset build gate]
        │                            │
        └──> DANFE-visible projection of XML (defines Invoice target; Phase 1 must decide it)
        └──> DANFE renderer + Code 128 + degradation ──> manifest with tags, splits, expected_outcome
                                     │
LLM gateway (cache with replicate salt, pricing table, OTel) ──> Extraction (schema-constrained)
        └──requires──> Validators ──> Repair loop (bounded) ──> Eval endpoint (overrides: model, prompt, maxRepairs, cache)
                                     │
Eval runner (async, resumable) ──requires──> Eval endpoint + dataset manifest
        └──> results.jsonl (raw outputs stored) ──> Graders (pure, versioned) ──> summary.json (+CIs)
                                                         │
                                          compare ──requires──> compact per-case table (cases.csv)
                                          CI gate ──requires──> compare + pinned baseline + thresholds + fixed subset
                                          Results table/plots ──requires──> committed summaries
Repeated runs ──requires──> cache salt / bypass
Repair ablation ──requires──> maxRepairs override on endpoint
Barcode-first key ──requires──> decoder in .NET + barcode-unreadable dataset variants (otherwise variants have no consumer)
Failure taxonomy ──enhances──> README failure analysis
Provenance manifest ──enhances──> everything (reproducibility claim)
```

### Dependency Notes

- **Projection decision (Phase 1) blocks Phases 2-4:** if `Invoice` includes XML-only fields (CRT, per-item PIS/COFINS), the generator, prompt, graders and validators all change later.
- **Validators before generator:** the dataset build gate (truth passes validators) uses them, and it doubles as a cross-language consistency test.
- **Cache salt must exist in the gateway before the runner:** retrofitting it after results exist invalidates cached baselines.
- **Raw output storage before graders:** offline re-grading and failure taxonomy require it.
- **Compact per-case table before `compare`:** resolves the D-15 / paired-statistics tension.
- **Endpoint overrides (`model`, `promptVersion`, `maxRepairs`, `cacheMode`, `replicate`) before experiments:** every differentiator experiment needs them. Design the endpoint contract once, in Phase 3, with the harness in mind.
- **Conflict:** live paid CI on every PR vs fork-PR secrets, budget and noise. Resolved by two-tier CI.
- **Conflict:** repeated-run variance measurement vs unsalted cache.

### M2/M3 constraints on M1 design (keep cheap now)

- Result and case schemas carry `task` (`extraction | decision`) and an `expected` blob; grader registry is keyed by task. Endpoint response reserves a `tool_calls[]` array (empty in M1).
- Generator builds a seeded world (vendor pool, recurring issuers) and supports `variant_of` (same access key, different scan or bytes) for M2 near-duplicate and M3 re-scan/idempotency tests.
- Store raw outputs now; M2's judge calibration and failure analysis will want them.
- Barcode decoding is the idempotency key source in M3 (D-12); if deferred past M1, keep the interface and the dataset variants ready.

## MVP Definition

### Launch With (M1 must-have)

- [ ] Domain records, schema export, schema-staleness CI, DANFE-visible projection decided and documented (P1)
- [ ] Validators with the full rule catalogue, alphanumeric CNPJ/key support, shared test vectors (P1)
- [ ] Seeded generator, 150+ tagged cases, manifest with splits and `expected_outcome`, versioned, truth gated by schema and validators (P2)
- [ ] Gateway (cache with replicate salt, pricing table, OTel) and schema-constrained extraction with bounded repair (P3)
- [ ] Eval endpoint with config overrides and per-attempt detail (P3)
- [ ] Runner, JSONL, manifest, summary with CIs, four graders (including line-item alignment), per-field and per-slice tables (P4)
- [ ] `compare` with per-field deltas and regressed cases; CI subset gating with thresholds (P4)
- [ ] First results table with n, CIs, cost and latency (P4)

### Add Within M1 If Time Allows (high value-to-cost differentiators)

- [ ] Silent-error rate and coverage metrics (cheap; do it)
- [ ] Repair ablation (`maxRepairs=0` vs 2) as one row pair in the results table
- [ ] `run` vs `grade` separation and offline re-grade
- [ ] Paired comparison with significance, and k=3 replicates on the headline comparison
- [ ] Failure taxonomy tags feeding the README failure analysis
- [ ] Two-tier CI (replay on PR; live manual/nightly)

### Future Consideration (M2+)

- [ ] LLM-as-judge with calibration (M2, D-16)
- [ ] Decision graders: correctness, required/forbidden tool calls (M2)
- [ ] OTel-to-eval deep links and per-run cost attribution dashboards (M3)
- [ ] IBS/CBS group modeling and a dataset slice (post-M3; verify current layout and DANFE rendering first)
- [ ] Input-modality sweep beyond one comparison, model-fallback routing

## Feature Prioritization Matrix

| Feature | Reviewer Value | Implementation Cost | Priority |
|---------|----------------|---------------------|----------|
| Validators incl. alphanumeric CNPJ/key + shared vectors | HIGH | LOW-MEDIUM | P1 |
| DANFE-visible projection decision | HIGH | LOW | P1 |
| Seeded generator + manifest + tags | HIGH | HIGH | P1 |
| Gateway, repair loop, eval endpoint | HIGH | MEDIUM-HIGH | P1 |
| Graders incl. line-item alignment | HIGH | MEDIUM-HIGH | P1 |
| Runner, artifacts, provenance manifest | HIGH | MEDIUM | P1 |
| CIs (Wilson, clustered bootstrap) | HIGH | LOW-MEDIUM | P1 |
| `compare` + CI gate + results table | HIGH | MEDIUM | P1 |
| Silent-error rate and coverage | HIGH | LOW | P1 |
| Repair ablation | HIGH | LOW | P2 |
| `run`/`grade` separation | MEDIUM-HIGH | LOW-MEDIUM | P2 |
| Paired significance, replicates, flaky list | HIGH | MEDIUM | P2 |
| Two-tier CI with replay cache | MEDIUM-HIGH | MEDIUM | P2 |
| Failure taxonomy | MEDIUM-HIGH | MEDIUM | P2 |
| Validator mutation testing | MEDIUM | MEDIUM | P2 |
| Barcode-first key and cross-check | MEDIUM | MEDIUM | P2 |
| Slice heatmap, cost-accuracy plots | MEDIUM | LOW-MEDIUM | P2 |
| Modality axis comparison | MEDIUM | MEDIUM | P3 |
| Seeded world and `variant_of` | MEDIUM (M2/M3) | LOW-MEDIUM | P2 |
| IBS/CBS modeling | LOW (M1) | HIGH | P3 |

**Priority key:** P1 must have; P2 should have, add when possible; P3 later.

## Competitor / Reference Feature Analysis

| Feature | Inspect (UK AISI) | promptfoo | Braintrust / hosted | IDP products (Textract AnalyzeExpense, Azure Document Intelligence, Mindee) | Our Approach |
|---------|-------------------|-----------|---------------------|------------------------------------------------------|--------------|
| Datasets, scorers, aggregation | First-class (solvers, scorers, metrics, epochs) | YAML assertions | Datasets, scorers, experiments | Vendor-supplied accuracy claims, little published methodology | Python grader registry, domain-specific, deterministic |
| Repeated runs | Epochs built in | `repeat` option | Experiment reruns | None | Replicates with salted cache, variance reported |
| Run comparison | Log viewer, tooling | Side-by-side matrix | Diff UI, gating features | None | `compare` CLI, paired CIs, regressed-case lists |
| CI gating | DIY | Strong, config-driven | Built-in gates | n/a | Threshold config, two-tier CI, pinned baseline |
| Confidence intervals | Basic stderr | Limited | Varies | Rarely | Wilson, clustered, paired (per Miller 2024) |
| Document-extraction field accuracy | None | None | None | Field-level accuracy, usually unaudited, line items weakest | Field plus invoice-level strict, silent-error rate, slices |
| Tests the real system | Calls a solver you write | Calls a provider or script | SDK-instrumented | n/a | HTTP to the actual .NET pipeline (D-02), the stronger claim |

Published invoice-extraction benchmarks agree on two points that shape grading choices: line items and tables are the weakest area (one study found accuracy dropping from about 84 percent to about 68 percent when tables were included), and field-level, not document-level, accuracy is what matters operationally. Hybrid scoring (rule-based exact matching plus fuzzy matching for text) is the norm.

A framing risk to address in the README: Brazilian finance teams usually receive the XML alongside the DANFE, so a reviewer may ask why parse a PDF. The honest answer is that the PDF path covers suppliers who send only the PDF, scans and photos, and that the XML serves as ground truth and (in M1) as an oracle. Say so up front.

## Open Questions / Gaps for discuss phases

- **Is barcode decoding in M1?** PROJECT.md lists barcode-unreadable dataset variants but no decoder in M1. Without a decoder those variants have no consumer. Recommend including a minimal decoder and `key_source` in Phase 3.
- **PIS/COFINS:** keep in `Invoice` and print in `infCpl` (testing realism, and the generator controls this), or restrict the extraction target to DANFE-standard fields and validate PIS/COFINS only on the XML side. Recommend the latter plus an optional `infCpl` slice.
- **Regime field:** infer from CST/CSOSN format, or treat CRT as non-graded metadata. Verify the DANFE column layout against the MOC.
- **Tolerances and normalization rules:** confirm R$0.01 per-line and per-total tolerance behavior against real rounding conventions (`vProd` rounding, sums of rounded items).
- **Two models and CI subset size/thresholds** remain open (D-15, open questions); the MDE reasoning above should inform them.
- **IBS/CBS print layout** on DANFE not verified; defer.
- **python-stdnum support for alphanumeric CNPJ** unverified; check before relying on it as a cross-check.

## Sources

- NT Conjunta 2025.001 (alphanumeric CNPJ) coverage: https://blog.tecnospeed.com.br/novo-cnpj-nota-tecnica/ (search snippet; direct fetch blocked by egress proxy) and https://oobj.com.br/legislacao/cnpj-alfanumerico/ (MEDIUM-HIGH: ASCII-48 rule, `[A-Z0-9]{12}[0-9]{2}`, key regex `[0-9]{6}[A-Z0-9]{12}[0-9]{26}`)
- Alphanumeric CNPJ timeline, existing numeric CNPJs remain valid: https://tecnoblog.net/noticias/cnpj-com-letras-e-numeros-comeca-a-valer-veja-o-que-muda/ and https://serpro.gov.br/menu/noticias/noticias-2024/cnpj-alfanumerico (MEDIUM-HIGH)
- NT 2025.002 (IBS/CBS/IS in NF-e/NFC-e) and dates: https://nfe.io/blog/notas-tecnicas/nota-tecnica-2025-002-guia-estrutural-da-reforma-tributaria-para-nf-e-e-nfc-e/ and https://www.coad.com.br/home/noticias-detalhe/138635/nf-e-preenchimento-da-cbs-e-do-ibs-sera-obrigatorio-a-partir-de-agosto (MEDIUM; mandatory date and CRT 3 scope to be re-verified)
- Miller, "Adding Error Bars to Evals: A Statistical Approach to Language Model Evaluations": https://arxiv.org/abs/2411.00640 and https://anthropic.com/research/statistical-approach-to-model-evals (HIGH for the paired and clustered methodology)
- Eval framework landscape (Inspect, promptfoo, Braintrust): https://inspect.aisi.org.uk/ and https://futureagi.com/blog/best-prompt-testing-frameworks-2026 (MEDIUM, vendor-flavored comparison)
- Invoice extraction benchmarking practices: https://arxiv.org/html/2509.04469v1 and https://www.zenml.io/llmops-database/comprehensive-llm-benchmarking-for-financial-automation-tasks (MEDIUM)
- NF-e layout 4.00 field set, CST/CSOSN families, CNPJ/CPF/key check-digit algorithms, `vNF` formula, DANFE contents: domain knowledge of the MOC and NT series, not re-fetched in this session; verify against the current MOC in Phase 1 planning (MEDIUM)
- Project inputs: `/home/user/carimbo/.planning/PROJECT.md`, `/home/user/carimbo/docs/PROJECT-BRIEF.md`, `/home/user/carimbo/docs/DECISIONS.md` (D-01..D-17)

---
*Feature research for: LLM-powered NF-e extraction pipeline with eval harness*
*Researched: 2026-10-03*
