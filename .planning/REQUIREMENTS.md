# Requirements: carimbo

**Defined:** 2026-10-03
**Core Value:** Measured quality — a reproducible eval suite that exercises the real pipeline, compares runs, gates CI on regressions and publishes a results table.

Scope: Milestone 1 "Extraction, measured" (brief Phases 1–4). "Developer" = the author; "reviewer" = an external person cloning the repo.

## v1 Requirements

### Repository & Dev Environment

- [x] **REPO-01**: Developer can build and test the .NET solution (.NET 10, `global.json`, Central Package Management, xUnit v3) with one documented command
- [x] **REPO-02**: Developer can build, lint (ruff), type-check (pyright) and test (pytest) the single uv-managed Python project with one documented command
- [x] **REPO-03**: Developer gets the full toolchain via devenv on NixOS-WSL, and a reviewer without Nix gets it via a documented non-Nix path
- [x] **REPO-04**: Repo works out of the box under both Claude Code and OpenCode (one canonical agent instruction file)
- [ ] **REPO-05**: Reviewer can start local services (trace viewer) with one command via docker compose

### Domain & Schema

- [x] **DOM-01**: C# domain records (`Invoice`, `Party`, `LineItem`, `Taxes`, `Decision`) are pure — no I/O, model calls or framework dependencies (D-04)
- [ ] **DOM-02**: The `Invoice` extraction target covers only DANFE-visible fields; the XML→DANFE field mapping is documented
- [x] **DOM-03**: CNPJ and access key are string value objects accepting both numeric and alphanumeric forms (`[A-Z0-9]{12}[0-9]{2}`, `[0-9]{6}[A-Z0-9]{12}[0-9]{26}`)
- [x] **DOM-04**: Money is a decimal string on the wire (pattern-constrained) and `decimal`/`Decimal` in code; rounding is half-up in both languages, specified by shared vectors
- [x] **DOM-05**: Canonical JSON Schema is exported deterministically from the C# records and committed
- [x] **DOM-06**: A model-facing schema is derived by a pure projector (unsupported keywords stripped, `additionalProperties:false`) and a test asserts it stays within the structured-output budget (≤24 optional, ≤16 union properties)
- [x] **DOM-07**: Pydantic models are generated from the committed schema with pinned codegen and committed
- [x] **DOM-08**: CI fails if the committed schema or generated Python models are stale

### Validators

- [ ] **VAL-01**: Validators return a list of structured errors (field, rule ID, expected, actual, severity) and never throw (D-06)
- [x] **VAL-02**: CNPJ check digits validate for numeric and alphanumeric forms (ASCII−48 mod 11)
- [ ] **VAL-03**: Access-key check digit validates, and the key's embedded issuer CNPJ, year-month, model, series and number are cross-checked against extracted fields
- [ ] **VAL-04**: Line items are checked against totals, and tax amounts against bases and rates (regime-aware)
- [ ] **VAL-05**: Dates are checked for plausibility
- [x] **VAL-06**: A shared, hand-curated test-vector file (known-valid and known-invalid CNPJs, keys, rounding cases) is exercised by both xUnit and pytest

### Synthetic Dataset

- [x] **DATA-01**: Generator produces paired `{case}.xml` ground truth and `{case}.pdf` DANFE (with Code 128 access-key barcode) from a seed, byte-reproducibly
- [ ] **DATA-02**: Dataset has ≥150 cases covering single/multi-page, many line items, several tax regimes, and ≥10% alphanumeric-CNPJ cases
- [ ] **DATA-03**: Degraded variants (rotation, blur, low DPI) and barcode-unreadable variants are image-only PDFs (no text layer)
- [ ] **DATA-04**: Every ground-truth record validates against the exported schema and passes the .NET validators (checked in CI)
- [ ] **DATA-05**: A versioned manifest records per-case tags, split, content hashes, seed and generator version, and defines a fixed stratified CI subset
- [ ] **DATA-06**: The generated dataset is committed when ≤ ~20 MB; otherwise manifest + CI subset are committed and regeneration is a new dataset version
- [x] **DATA-07**: All data is synthetic — no real CNPJs, names, addresses or invoices (D-14)

### LLM Gateway

- [ ] **LLM-01**: All model calls go through a gateway with a single (non-stacked) retry policy with backoff
- [x] **LLM-02**: Gateway records input, output, cache-read and cache-write tokens per call and computes cost from a versioned pricing table
- [ ] **LLM-03**: Each call emits one OpenTelemetry span carrying model, tokens and cost, with no prompt/response/PDF content
- [ ] **LLM-04**: A request-hash response cache (SHA-256 over the canonical final request, including a replicate salt) is enabled in dev/eval and off in production (D-07 refined)
- [ ] **LLM-05**: Cache supports read-write, read-only (replay) and refresh modes; hits report the original latency and cost; truncated or refused responses are never cached
- [x] **LLM-06**: Gateway shape (IChatClient vs direct SDK) and retry ownership are decided by a spike confirming PDF document blocks, raw output schema, cache-token usage and per-attempt visibility

### Extraction

- [x] **EXT-01**: Extraction sends the PDF with the model-facing schema and returns a schema-valid `Invoice` or a typed failure
- [x] **EXT-02**: Refusals, `max_tokens` truncation and infrastructure errors are distinct typed outcomes, not quality failures
- [ ] **EXT-03**: On validator errors, extraction retries with the structured errors fed back, bounded by a configured maximum (default 2); the repair prompt forbids fabricating values
- [ ] **EXT-04**: Every attempt is recorded (output, validator results, tokens, cost, latency)
- [ ] **EXT-05**: Prompts are versioned and the prompt version is part of the cache key and the result metadata

### Eval Endpoint

- [ ] **API-01**: `POST /eval/extractions` runs the same extraction and validation code as production and returns result, validator outcomes, attempts, tokens, cost, latency and trace ID (D-02)
- [ ] **API-02**: The endpoint accepts per-request overrides (model, prompt version, max repairs, cache mode, replicate salt) and echoes the effective configuration
- [x] **API-03**: The endpoint is disabled outside dev/eval and protected by a static API key
- [ ] **API-04**: Golden response fixtures emitted by .NET tests are parsed by pytest (cross-stack contract test)

### Eval Harness

- [x] **EVAL-01**: Runner executes a dataset (or subset) against the eval endpoint with bounded concurrency, a cost cap and resume, writing one JSONL record per case including raw output
- [x] **EVAL-02**: Grading is a separate stage that can re-grade stored raw outputs offline without model calls
- [ ] **EVAL-03**: Graders cover schema validity, exact match (IDs, CNPJ, access key), numeric tolerance (amounts, taxes; tolerance configurable) and line-item matching by optimal assignment (not index)
- [ ] **EVAL-04**: Graders have self-tests (ground truth scores perfect; known corruptions score wrong) and infra failures stay distinct from wrong answers
- [ ] **EVAL-05**: Run summary reports per-field and per-slice accuracy with n and confidence intervals, cost, cache hit rate and latency percentiles
- [ ] **EVAL-06**: Summary reports silent-error rate: values that passed validators but are wrong
- [ ] **EVAL-07**: Failures are auto-tagged with a failure taxonomy (e.g. digit transposition, party swap, dropped/merged row) feeding the failure analysis
- [ ] **EVAL-08**: `compare` lists per-field deltas and regressed cases between two runs and refuses runs with mismatched dataset or grader versions
- [ ] **EVAL-09**: `summary.json` and a compact per-case score table are committed per published run under `evals/reports/`; raw JSONL is not committed (D-15 refined)

### CI

- [x] **CI-01**: GitHub Actions builds and tests both stacks on every PR
- [ ] **CI-02**: PRs run the eval CI subset in cache-replay mode from committed fixtures with no secrets (fork-safe) and fail below configured per-grader thresholds
- [ ] **CI-03**: A live eval tier runs only for same-repo changes or manual dispatch, with a budget abort
- [ ] **CI-04**: Cache fixtures for the published run are committed so a reviewer can replay the eval suite with no API key (D-15 refined)

### Results

- [ ] **RES-01**: First results table compares two models (or two prompt versions) with n, confidence intervals, cost per correct invoice and p95 latency
- [ ] **RES-02**: A written failure analysis explains the main failure categories using the taxonomy
- [x] **RES-03**: `docs/DECISIONS.md` gains superseding entries for the accepted D-03, D-07 and D-15 refinements

## v2 Requirements

Deferred. Milestone 2/3 scope or differentiators not selected for v1.

### Measurement differentiators

- **MEAS-01**: Repair ablation (`maxRepairs=0` vs 2) with first-pass vs final grades and a repair-damage metric
- **MEAS-02**: k≥3 replicates per case with noise floor and flaky-case list
- **MEAS-03**: Paired significance testing in `compare`
- **MEAS-04**: Validator mutation and property tests

### Data differentiators

- **DGEN-01**: Barcode-first access key decoded in .NET at extraction with cross-check (`key_source`)
- **DGEN-02**: 2–3 DANFE layout variants

### Milestone 2: The agent

- **AGNT-01**: MCP tool server (vendor lookup, PO match, duplicate/near-duplicate check, categorization) backed by Postgres
- **AGNT-02**: Hand-written agent loop reaching approve/reject/escalate with reason and evidence within a step budget
- **AGNT-03**: Decision evals incl. calibrated LLM-as-judge

### Milestone 3: Production hardening

- **HARD-01**: Temporal workflow with all model/tool calls as activities
- **HARD-02**: Async intake API with three idempotency layers and side-effect keys
- **HARD-03**: End-to-end OpenTelemetry tracing and per-invoice cost attribution; full README publication

## Out of Scope

| Feature | Reason |
|---------|--------|
| Real customer/personal data | Synthetic only (D-14) |
| Real payments, ERP or SEFAZ integration | Side effects simulated |
| UI beyond API + CLI + reports | Not what the project demonstrates |
| Multi-tenant auth | Single static API key suffices |
| Model fine-tuning | Not part of the thesis |
| Agent frameworks | Hide the loop being demonstrated (D-08) |
| LLM-as-judge in M1 | Only deterministic graders until calibrated (D-16, M2) |
| Generic eval framework / DSL / results DB / UI | Harness stays purpose-built |
| Full NF-e schema, NFC-e/CT-e/NFS-e, IE validation for all UFs, signature checks | Beyond DANFE-visible extraction |
| Full IBS/CBS tax-reform modeling | Layout still moving; `Taxes` stays extensible, noted as a limitation |
| Live paid evals on every PR | Cost; PRs replay from fixtures |

## Traceability

Each v1 requirement maps to exactly one phase in `.planning/ROADMAP.md`, the phase where it is fully delivered.

| Requirement | Phase | Status |
|-------------|-------|--------|
| REPO-01 | Phase 1 | Complete |
| REPO-02 | Phase 1 | Complete |
| REPO-03 | Phase 1 | Complete |
| REPO-04 | Phase 1 | Complete |
| REPO-05 | Phase 3 | Pending |
| DOM-01 | Phase 1 | Complete |
| DOM-02 | Phase 2 | Pending |
| DOM-03 | Phase 2 | Complete |
| DOM-04 | Phase 2 | Complete |
| DOM-05 | Phase 1 | Complete |
| DOM-06 | Phase 1 | Complete |
| DOM-07 | Phase 1 | Complete |
| DOM-08 | Phase 1 | Complete |
| VAL-01 | Phase 2 | Pending |
| VAL-02 | Phase 2 | Complete |
| VAL-03 | Phase 2 | Pending |
| VAL-04 | Phase 2 | Pending |
| VAL-05 | Phase 2 | Pending |
| VAL-06 | Phase 2 | Complete |
| DATA-01 | Phase 1 | Complete |
| DATA-02 | Phase 4 | Pending |
| DATA-03 | Phase 4 | Pending |
| DATA-04 | Phase 4 | Pending |
| DATA-05 | Phase 4 | Pending |
| DATA-06 | Phase 4 | Pending |
| DATA-07 | Phase 1 | Complete |
| LLM-01 | Phase 3 | Pending |
| LLM-02 | Phase 1 | Complete |
| LLM-03 | Phase 3 | Pending |
| LLM-04 | Phase 3 | Pending |
| LLM-05 | Phase 3 | Pending |
| LLM-06 | Phase 1 | Complete |
| EXT-01 | Phase 1 | Complete |
| EXT-02 | Phase 1 | Complete |
| EXT-03 | Phase 2 | Pending |
| EXT-04 | Phase 2 | Pending |
| EXT-05 | Phase 3 | Pending |
| API-01 | Phase 2 | Pending |
| API-02 | Phase 3 | Pending |
| API-03 | Phase 1 | Complete |
| API-04 | Phase 3 | Pending |
| EVAL-01 | Phase 1 | Complete |
| EVAL-02 | Phase 1 | Complete |
| EVAL-03 | Phase 5 | Pending |
| EVAL-04 | Phase 5 | Pending |
| EVAL-05 | Phase 5 | Pending |
| EVAL-06 | Phase 5 | Pending |
| EVAL-07 | Phase 5 | Pending |
| EVAL-08 | Phase 5 | Pending |
| EVAL-09 | Phase 6 | Pending |
| CI-01 | Phase 1 | Complete |
| CI-02 | Phase 6 | Pending |
| CI-03 | Phase 6 | Pending |
| CI-04 | Phase 6 | Pending |
| RES-01 | Phase 6 | Pending |
| RES-02 | Phase 6 | Pending |
| RES-03 | Phase 1 | Complete |

**Coverage:**
- v1 requirements: 57 total
- Mapped to phases: 57
- Unmapped: 0 ✓

---
*Requirements defined: 2026-10-03*
*Last updated: 2026-10-03 after roadmap creation (traceability filled)*
