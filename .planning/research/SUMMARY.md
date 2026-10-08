# Project Research Summary

**Project:** carimbo — LLM-powered Brazilian NF-e (DANFE PDF) extraction pipeline with reproducible eval harness
**Domain:** LLM extraction pipeline for regulated financial documents with production-grade measurement
**Researched:** 2026-10-03
**Confidence:** MEDIUM-HIGH overall

## Executive Summary

Carimbo is a portfolio project demonstrating production discipline applied to LLM agents: extracting Brazilian electronic invoices (DANFE PDFs) into strictly typed records via Claude, with a reproducible eval suite proving measurable quality. The project splits cleanly: .NET 10 for the production pipeline (validators, extraction, gateway, API), Python for synthetic data generation and evaluation harness.

The research validates the core approach: Anthropic's structured outputs work for the invoice schema (with careful union/optional budgeting), cost accounting flows through the gateway, and a robust validation-and-repair loop with deterministic validators catches real errors. The critical path is: domain types → JSON Schema → validators + Pydantic models → synthetic dataset → extraction gateway → eval harness, each with cross-language validation.

Key risks are domain-specific (alphanumeric CNPJs now live, DANFE is a lossy PDF rendering of the XML, rounding and precision edge cases) and architectural (cache invalidation on schema changes, repair loops that teach the model to cheat, grader false positives inflating cost). Research identifies mitigations for all critical pitfalls, but **four architectural decisions conflict across research documents and require explicit resolution in planning** (see Conflicts Requiring Decision below).

## Key Findings

### Recommended Stack

**Core: .NET 10 production pipeline**
- **Anthropic SDK 12.53.0** (official, GA, IChatClient adapter) — structured outputs, document blocks, cache-token usage, and testing seams in one package
- **Microsoft.Extensions.AI 10.10.0** — IChatClient abstraction and built-in OpenTelemetry `gen_ai.*` spans
- **System.Text.Json.Schema** (built in) — exports JSON Schema from Domain records; same serialization contract as the model. Never use `JsonSerializerDefaults.Web` for export (numbers become `["string","number"]` unions)
- **PDFtoImage 5.4.0 + ZXingCpp 0.5.3** — rasterize PDF, decode Code 128 barcode (hybrid 128 for alphanumeric keys)
- **OpenTelemetry 1.19.1** — one span per gateway call, GenAI semconv, custom `carimbo.*` cost/cache attributes
- **xUnit v3 4.0.1** — requires `global.json` runner opt-in for Microsoft.Testing.Platform (verify)

**Python: eval and data (never reimplements pipeline logic)**
- **uv 0.12.23** — lock all versions; codegen and degradation reproducibility depend on exact pins
- **datamodel-code-generator 0.83.0** — Pydantic v2 models from schema; output format varies by version
- **BrazilFiscalReport 1.2.0 + nfelib 3.0.0** — DANFE PDF + Code 128 barcode from NF-e XML; deterministic with pinned date/ID metadata
- **Pillow 12.3.0 + numpy 2.5.3 + pypdfium2 + reportlab 5.0.1** — seeded degradation (rotation, blur, low DPI, JPEG) → image-only PDFs
- **httpx2 2.13.1** — async HTTP client to .NET eval endpoint (**unverified: confirm package identity and maturity**)

**Infrastructure**
- **docker compose** + **Jaeger v2** — canonical one-command local services; OTel OTLP receiver. .NET Aspire rejected as orchestrator
- **devenv** (NixOS-WSL) + **Makefile** — toolchain only; single command surface. Postgres not needed until M2

See STACK.md for full coverage, version compatibility, and verified spikes.

### Expected Features

**MVP (Phases 1–4)**

1. **Validators with alphanumeric-CNPJ support** — new `[A-Z0-9]{12}[0-9]{2}` and legacy numeric, DV via ASCII−48 mod-11, cross-field checks against the 44-char access key. Shared test vectors used by both C# and Python.
2. **Domain records and JSON Schema export** — C# records as source of truth, exported schema committed, Pydantic generated, CI fails on staleness.
3. **DANFE-visible field projection** — Invoice target represents only fields printed on the DANFE, not every XML field. Mapping documented.
4. **Deterministic validators + bounded repair loop** — validators return `{field, rule, expected, actual, severity}`, never throw. Extraction retries with errors fed back (max 2). Repair prompt forbids fabricating values to satisfy validators.
5. **Seeded synthetic dataset, 150+ cases** — paired `{case}.xml` and `{case}.pdf`, versioned, byte-stable. Coverage: single/multi-page, tax regimes, degradation (image-only), alphanumeric CNPJ (≥10%), adversarial cases. Ground truth passes schema and validators.
6. **LLM gateway** — single retry policy, cost from versioned pricing table, request-hash cache (dev/eval only, with salt), one OTel span per call.
7. **Extraction with schema-constrained output** — versioned prompts, model-facing schema projected from canonical, typed outcomes (incl. refusal / max_tokens), per-attempt detail.
8. **Eval endpoint** — `POST /eval/extractions` returns result, validator outcomes, attempts, tokens, cost, latency, trace ID; accepts per-request overrides.
9. **Python eval harness** — async runner, JSONL output, graders (schema validity, exact match, numeric tolerance, line-item assignment alignment), summary with CIs.
10. **Comparison and CI gating** — `compare` with deltas and regressed cases; fixed stratified CI subset; replay-only tier on PRs (no secrets), live tier on model/prompt changes.
11. **Results table** — two models compared with n, confidence intervals, cost per correct invoice, p95 latency.

**Differentiators (if time allows):** silent-error rate and coverage metrics; repair ablation (`maxRepairs=0` vs 2); k≥3 replicates with cache salt; separate `run`/`grade` with offline re-grading; failure taxonomy auto-tagging; validator mutation testing; barcode-first key with cross-check; committed per-case table for paired stats; committed cache fixtures for offline reviewer reproduction.

**Out of scope:** real data (D-14); LLM-as-judge in M1; agent loop, MCP tools, Temporal (M2+); full IBS/CBS tax-reform modeling.

See FEATURES.md for the full matrix and open questions.

### Architecture Approach

Monorepo with two stacks separated by HTTP and schema: the .NET pipeline calls the Anthropic SDK, exposes a sync eval endpoint, and exports schemas; the Python harness calls the endpoint, generates synthetic data, runs evals and reports.

**Critical patterns:**
1. **Gateway as decorator chain** — telemetry outermost, cache inside (file-based, canonical-JSON hash), retry innermost, single policy
2. **Cost from versioned pricing table** — computed at read time; cache hits report notional and billed cost
3. **One schema, two layers** — canonical with rich constraints, model-facing projection stripping unsupported keywords (no min/max/length/recursion, ≤24 optionals, ≤16 unions)
4. **Request-hash cache with golden key vectors** — SHA-256 over canonical JSON (model, params, messages with document bytes hashed, schema, resolved defaults)
5. **Schema-constrained extraction, repair loop, typed outcomes** — repair reuses the PDF via prompt cache; typed failures carry kind and best candidate
6. **Eval endpoint as adapter** — same `IExtractionService` M3's Temporal activity will call
7. **Separate run and grade stages** — runner streams JSONL with raw output; graders are pure and re-runnable offline

**Components:** `Domain` (pure records, Cnpj/AccessKey value objects) · `Validators` (rule codes, no exceptions) · `Llm` (gateway, pricing, cache, telemetry; domain-agnostic) · `Extraction` (prompts, repair loop) · `Api` (composition root, eval endpoints) · Python `models` (generated, diff-checked) · `datagen` · `evals` · `reports`.

See ARCHITECTURE.md for build order, data flows, and CI design.

### Critical Pitfalls & Mitigations

1. **Alphanumeric CNPJ is live (July 2026).** Validators, vectors, schema patterns, generators and barcode codecs must support both forms with ASCII−48 mod-11; ≥10% alphanumeric cases; hybrid Code 128 round-trip test. Use `[0-9]`, not `\d`, in .NET regex.
2. **Money, decimals, rounding.** `decimal` in both languages; half-up rounding (both defaults are banker's); shared JSON vectors; R$0.01 tolerance as a parameter.
3. **The DANFE is a lossy projection of the XML.** Define the target from the DANFE (MOC Annex II), not the XSD; CI check that every graded field is visible.
4. **Synthetic DANFEs too clean / uniform.** Mature renderer plus 2–3 layouts; degraded variants rasterized to image-only PDFs (the provider also reads the text layer).
5. **Request-hash cache can hide regressions.** Key includes model, params, schema hash, prompt version, PDF hash; store original latency/cost; never cache truncated or refused responses; runner cache modes; salt for variance runs.
6. **No sampling control on current models.** Sonnet 5.5 / Opus 5.5 reject non-default temperature/top_p/top_k and prefill — the cache is the only reproducibility mechanism; report run-to-run variance.

See PITFALLS.md for 30+ pitfalls with phase mapping.

## Conflicts Requiring Decision

### Conflict 1: Money representation — decimal strings vs JSON numbers

- **A (STACK, PITFALLS):** decimal strings on the wire with a pattern; parse as `Decimal` in Python, `decimal` in C#.
- **B (ARCHITECTURE):** JSON `number`; graders parse as `Decimal` and compare with tolerance.
- **Recommendation:** A — no float precision risk anywhere, explicit on the wire.

### Conflict 2: Retries — own policy vs SDK built-in

- **A (ARCHITECTURE):** SDK `MaxRetries = 0`; own policy; one span and cost record per attempt.
- **B (STACK):** SDK built-in retries (honour `Retry-After`); no Polly.
- **Recommendation:** Both agree on a single, non-stacked policy. Decide after a Phase 3 spike that checks per-attempt visibility.

### Conflict 3: Dataset storage — commit vs regenerate

- **A (ARCHITECTURE):** commit if under ~25 MB; cache keys depend on PDF bytes; "clone and run" matches the Definition of Done.
- **B (STACK):** don't commit PDFs; regenerate from seed + `uv.lock`; manifest commits hashes.
- **Recommendation:** A, with a ~20 MB cap; if cross-machine byte reproduction fails, commit manifest + CI subset and treat regeneration as a new dataset version.

### Conflict 4: Gateway shape — direct SDK vs IChatClient

- **A (ARCHITECTURE):** direct `AnthropicClient` behind our own decorators; `IChatClient` adapter later.
- **B (STACK):** SDK through `AsIChatClient()` with a `DelegatingChatClient` cache; test seam and OTel for free.
- **Recommendation:** B, provided a Phase 1/3 spike confirms raw `output_config.format`, document blocks and cache-token usage pass through.

## Proposed Refinements to Locked Decisions

These are proposals, not changes. Each needs explicit approval and a superseding entry in `docs/DECISIONS.md`.

- **D-03 — model-facing schema projection.** Canonical schema exported from C#; a pure projector derives the model-facing schema (strip unsupported keywords, `additionalProperties:false`, assert ≤24 optionals / ≤16 unions); extraction target is the DANFE-visible projection.
- **D-07 — cache salt and modes.** Key includes a `replicate` salt; runner `--cache-mode {read-write, read-only, refresh}`; hits report original latency and cost; summaries show incurred vs uncached cost and hit rate.
- **D-15 — committed compact per-case scores and replay fixtures.** Commit a compact per-case score table next to `summary.json` for paired stats; raw JSONL stays out of git (CI artifact). Optionally commit cache fixtures for the published run (~5 MB) so PR CI and reviewers can replay at $0 with no API key.

## Implications for Roadmap

The brief's four phases need no reordering:

1. **Foundation** — Domain records, validators (alphanumeric CNPJ/CPF/key), shared vectors, schema export + Pydantic codegen with staleness checks, CI, dev environment. Spikes: exporter hoisting, codegen determinism, union budget.
2. **Synthetic dataset** — sampler, XML writer, DANFE renderer, image-only degradation, manifest, conformance against .NET validators. Spikes: renderer fidelity, hybrid Code 128.
3. **Extraction pipeline** — gateway, SDK adapter, schema projector, extraction + repair, eval endpoint, contract fixtures. Can start in parallel with Phase 2. Critical live-call spike first.
4. **Eval harness** — graders, runner, summarize, compare, thresholds, CI tiers, first results table. Graders can start once generated models exist.

## Research Flags for Planning

| Phase | Research needed? | Scope |
|-------|------------------|-------|
| Foundation | Yes (2–3 spikes) | Schema export + hoisting; codegen determinism; complexity budget |
| Dataset | Yes | DANFE renderer fidelity; hybrid Code 128; token-cost distribution |
| Extraction | Yes (critical) | SDK structured-output + PDF + cache_control contract; model capability |
| Eval harness | No | Established patterns; calibrate CI subset from first baseline |

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Versions from registries 2026-10-03; key integrations spiked; httpx2 unverified |
| Features | MEDIUM-HIGH | Alphanumeric CNPJ from secondary sources; NT/MOC to verify |
| Architecture | MEDIUM-HIGH | Vendor facts from official docs; conflicts identified |
| Pitfalls | MEDIUM | Domain specifics from secondary sources and domain knowledge |
| **Overall** | **MEDIUM-HIGH** | Critical unknowns identified as spikes |

## Gaps to Address

1. Primary-source verification of NT 2025.001, NT 2026.004 and the current MOC (DANFE printed fields, `vNF` formula, tolerances) — Foundation.
2. Anthropic SDK capabilities (raw output schema, PDF document blocks, `cache_control`, cache-token usage, request ID) — Foundation/Extraction spike.
3. DANFE rendering fidelity against reference samples — Dataset.
4. Hybrid Code 128 encode/decode round trip — Dataset.
5. `httpx2` identity and maturity — before Eval harness.
6. Dataset byte reproduction across NixOS-WSL, CI and a reviewer machine — Dataset.
7. xUnit v3 + `dotnet test` runner opt-in — Foundation.
8. Model pair (suggested Sonnet 5.5 vs Haiku 4.5) and capability check — Extraction/Eval.
9. CI subset size and thresholds — Eval, after first baseline.
10. Failure taxonomy rules — Eval design.

## Sources

**Primary (HIGH):** Anthropic C# SDK docs and releases; Anthropic structured outputs and PDF support docs; NuGet and PyPI registries (2026-10-03); project inputs (PROJECT.md, PROJECT-BRIEF.md, DECISIONS.md).

**Secondary (MEDIUM):** alphanumeric CNPJ coverage (Tecnospeed, oobj, tecnoblog, Serpro); NF-e domain knowledge (MOC, CST/CSOSN, access key, `vNF`); eval practice (Miller "Adding Error Bars to Evals", Inspect, promptfoo, Braintrust); OpenTelemetry GenAI semconv (Development status).

**Tertiary (LOW):** devenv/nixpkgs Jaeger availability; httpx2.

---
*Research completed: 2026-10-03*
*Synthesized from STACK, FEATURES, ARCHITECTURE, PITFALLS*
