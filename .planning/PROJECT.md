# carimbo

*invoice-agent: LLM-powered processing of Brazilian NF-e invoices, built with production discipline*

## What This Is

carimbo turns Brazilian electronic invoices (NF-e), received as DANFE PDFs, into strictly typed invoice records, and later into typed approve / reject / escalate decisions. It is a portfolio project. It shows a payments engineer applying production rigor to LLM agents in a regulated fintech setting, and backs that with evidence: structured outputs, deterministic validators, a reproducible eval suite, durable execution, idempotency, tracing and cost attribution.

The audience is reviewers hiring for senior AI platform roles (e.g. an "Operational AI / Agents Platform" team). They should be able to clone the repo, start local services with one command, process a sample invoice, run the eval suite and read measured results.

## Core Value

**Measured quality.** A reproducible eval suite that exercises the real pipeline, compares runs, gates CI on regressions and publishes a results table. The evidence is the product. When tradeoffs arise, choose whatever keeps quality measurable and reproducible.

## Requirements

### Validated

- ✓ .NET solution and Python project scaffolded in one repo; both build and test in GitHub Actions CI — Phase 1 (UAT: PR #2 CI green; fresh clone `just check` exit 0 with devenv and without Nix)
- ✓ Repo works out of the box under both Claude Code and OpenCode — Phase 1 (UAT: both answered `just check` from AGENTS.md)
- ✓ Deterministic validators (CNPJ check digits, access-key check digit, access key vs extracted fields, line items vs totals, tax consistency, date plausibility) return structured errors, with unit tests over known-valid and known-invalid inputs — Phase 2 (shared cross-stack vector file; validators total, never throw, including on null list elements)
- ✓ Extraction returns a schema-valid `Invoice` or a typed failure, with a bounded validate-and-repair loop — Phase 2 (MaxRepairs 0..5, default 2; nulls and non-exact enums are schema_invalid, D-25)
- ✓ Synchronous eval endpoint returns result, validator outcomes, attempts, tokens, cost, latency and trace ID — Phase 2 (eval contract 2; typed failures are HTTP 200 with every paid attempt)

### Active

Current milestone: **Milestone 1: Extraction, measured** (brief Phases 1–4).

- [ ] Domain records (`Invoice`, `Party`, `LineItem`, `Taxes`, `Decision`) defined in C#; JSON Schema exported and committed; CI fails on a stale schema
- [ ] Seeded, reproducible synthetic dataset generator producing paired `{case}.xml` ground truth and `{case}.pdf` DANFE, with a Code 128 barcode of the access key
- [ ] At least 150 cases: single and multi-page, many line items, several tax regimes, degraded scans (rotation, blur, low DPI), barcode-unreadable variants; ground truth validates against the exported schema
- [ ] LLM gateway with retries, token and cost accounting, an OpenTelemetry span per call, and a request-hash response cache (dev/eval only)
- [ ] Eval runner executes a dataset against the eval endpoint with bounded concurrency, writing JSONL per case plus a run summary
- [ ] Graders: schema validity, exact match (IDs, CNPJ, access key), numeric tolerance (amounts, taxes), line-item matching
- [ ] `compare` lists per-field deltas and regressed cases between two runs
- [ ] CI runs a small eval subset on PRs and fails below configured thresholds
- [ ] First results table comparing two models or two prompt versions

### Out of Scope

- Real customer or personal data — all data is synthetic (D-14)
- Real payments, ERP or SEFAZ integration — side effects are simulated and recorded
- UI beyond a demo (API + CLI + reports) — not what the project demonstrates
- Multi-tenant auth — a single static API key is enough
- Model fine-tuning — not part of the thesis
- Agent frameworks (Microsoft Agent Framework, Semantic Kernel agents) — they hide the loop being demonstrated (D-08)
- Webhooks for intake — clients poll (D-13)
- Milestone 2 (MCP tools, agent loop, decision evals) and Milestone 3 (Temporal, async intake, idempotency, full observability and publication) — later milestones via `/gsd-new-milestone`, not this roadmap

## Context

- **Author background:** low-latency backend systems, payments (PIX) and market data. .NET is the strongest stack. The project should read as "a payments engineer applying production discipline to LLM agents".
- **Why NF-e:** the official XML gives free, exact ground truth for the DANFE PDF, and the 44-digit access key gives a natural business identifier for idempotency.
- **Language split:** .NET for the production pipeline (domain, validators, LLM gateway, extraction, later agent, MCP, Temporal, API); Python for datagen, evals and reports. Python never reimplements pipeline logic and calls the .NET service over HTTP.
- **Planned .NET components:** `Domain`, `Validators`, `Llm`, `Extraction`, `Agent`, `Tools`, `Workflows`, `Api`. **Python:** `datagen`, `evals`, `reports`.
- **Source documents:** `docs/PROJECT-BRIEF.md` (full brief, all three milestones) and `docs/DECISIONS.md` (D-01..D-17 plus open questions). Consult `DECISIONS.md` in every discuss phase.
- **Open questions (resolve in discuss phases):** local services (devenv vs docker-compose vs .NET Aspire), .NET model SDK (Anthropic C# SDK vs `Microsoft.Extensions.AI` vs both), DANFE rendering and barcode decoding libraries, which two models to compare first, CI eval subset size and thresholds.

## Constraints

- **Tech stack:** .NET 10 (LTS), Python 3.12+, ASP.NET Core minimal APIs, PostgreSQL, OpenTelemetry with a local viewer (e.g. Jaeger), xUnit; Python uses uv, ruff, pyright or mypy, pytest, typer, pandas — decided in the brief
- **Model provider:** Anthropic models, comparing at least two — decided
- **Needs research before planning:** .NET model SDK, `ModelContextProtocol` C# SDK, JSON Schema to Pydantic codegen, DANFE rendering and barcode libraries — these ecosystems move fast; verify against current docs
- **Dev environment:** NixOS-WSL with project-specific devenv; must also work for an external reviewer
- **Agent runtimes:** Claude Code and OpenCode, both supported
- **Budget:** modest API spend — response cache mandatory for dev and eval reruns; CI uses a small dataset subset
- **Language:** English for code, docs and commit messages
- **Timeline:** no hard deadline; size phases for quality

## Key Decisions

Locked decisions from `docs/DECISIONS.md`. A planner must not reverse one without raising it explicitly; record changes as new superseding entries there.

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| D-01 .NET pipeline, Python evals and data | Author's strongest stack for production; Python's data tooling for analysis | — Pending |
| D-02 Evals call the real pipeline over HTTP (sync eval endpoint) | Eval results must describe production code | — Pending |
| D-03 C# records are the schema source of truth; JSON Schema exported, Python generated, CI checks staleness | One schema for model contract, validation and ground truth | — Pending |
| D-04 Domain layer is pure | No I/O, model calls or framework deps in `Domain` | — Pending |
| D-05 Deterministic before probabilistic | Hard guarantees for regulated finance; free graders | — Pending |
| D-06 Structured validator errors; bounded repair loop (default 2) | Good repair prompts and eval data; bounded cost | — Pending |
| D-07 LLM gateway with request-hash cache (dev/eval only) | Near-free eval reruns; central cost accounting | — Pending |
| D-08 Hand-written agent loop (M2) | Small, testable, explainable | — Pending |
| D-09 The agent decides, the workflow acts; MCP tools read-only (M2/M3) | Side effects outside the non-deterministic component | — Pending |
| D-10 Uncertainty escalates (M2) | False approvals and rejections cost more than review | — Pending |
| D-11 Temporal: deterministic workflows, everything else an activity (M3) | Replay requires determinism | — Pending |
| D-12 Idempotency in three layers plus side-effect keys (M3) | Each layer catches a different duplicate | — Pending |
| D-13 Async intake, 202 + polling (M3) | Simple, standard | — Pending |
| D-14 Synthetic data only, seeded and versioned | No real data, reproducible | — Pending |
| D-15 JSONL per case + committed summaries; CI subset gating | Visible quality history in git | — Pending |
| D-16 LLM-as-judge calibrated against human labels (M2) | Unvalidated judge is an unmeasured metric | — Pending |
| D-17 OpenTelemetry end to end; spans carry tokens and cost | Per-invoice traceability and cost | — Pending |
| v1 roadmap covers Milestone 1 only | Keep the first milestone focused on measured extraction | — Pending |
| D-18 Canonical schema, model-facing projection and generated models (refines D-03) | Model-facing schema must fit structured-output limits; Python models are generated, never hand-written | ✓ Implemented in Phase 1 (schema-check gate) |
| D-19 Request-hash cache keys, modes and reporting (refines D-07) | The cache is the reproducibility mechanism, since current models take no temperature | — Pending (Phase 3) |
| D-20 Committed per-case scores and replay fixtures (refines D-15) | Quality history visible in git and replayable offline | — Pending |
| D-21 Direct Anthropic SDK behind ILlmGateway; MaxRetries 0 in Phase 1, Phase 3 owns the single retry policy | Live spike: the IChatClient path lost usage and stop details | ✓ Implemented in Phase 1 (spike spend US$0.26 of US$5) |
| D-22 Invoice v2: the full DANFE-visible invoice is the extraction target | Measure extraction of everything a reviewer sees, not a subset | ✓ Implemented in Phase 2 |
| D-23 Validation findings and bounded repair; feedback discloses expected values only for arithmetic and date rules | Repair must not invite fabrication of identifiers | ✓ Implemented in Phase 2 (live Haiku 4.5 skeleton runs repaired 0 cases) |
| D-24 Eval contract 2 | validation_failed candidates are graded; caught failures counted apart from success | ✓ Implemented in Phase 2 |
| D-25 NULL_VALUE, null-element rejection and exact-name enums (refines D-23) | "Success means schema-valid" must hold for nulls and enums too | ✓ Implemented in Phase 2 (gap closure 02-11, 02-12) |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-10-08 after Phase 2 (gap closure 02-13)*
