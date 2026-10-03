# Project brief: invoice-agent

> Input for `/gsd-new-project`. Implementation decisions and their rationale
> live in `DECISIONS.md`; consult it during discuss phases.

## 1. Purpose

A portfolio project demonstrating production-grade engineering of LLM-powered
agents in a regulated fintech context. It targets senior AI platform roles
(e.g. an "Operational AI / Agents Platform" team) and must show, with evidence:

- LLM systems shipped with production rigor, not prototypes: structured
  outputs, orchestrated model calls, quality measured over time.
- Agents that take actions through tools (MCP), with safe boundaries.
- An evaluation system: datasets, graders, run comparison, regression gating.
- Reliability engineering: durable execution, idempotency, observability,
  cost attribution.

The author's background is low-latency backend systems, payments (PIX) and
market data. The project should read as "a payments engineer applying
production discipline to LLM agents".

## 2. Problem statement

Finance teams receive Brazilian electronic invoices (NF-e) as DANFE PDFs and
must extract their data, match them to vendors and purchase orders, detect
duplicates and anomalies, categorize the expense, and decide whether to
approve, reject or escalate to a human. The system automates this end to end,
and proves its quality with a reproducible eval suite.

## 3. Goals

1. Extract a DANFE PDF into a strictly typed invoice record, verified by
   deterministic validators, with a validate-and-repair loop.
2. Run an agent that uses tools to reach a typed decision
   (approve / reject / escalate) with a stated reason.
3. Execute the pipeline durably (Temporal) behind an async intake API with
   layered idempotency: no invoice is ever processed or approved twice.
4. Measure quality with an eval harness that exercises the real pipeline,
   compares runs, and gates CI on regressions.
5. Trace every model and tool call, and attribute cost per invoice and per
   eval run.
6. Publish results: a README with a results table comparing at least two
   models or prompt versions, plus a written failure analysis.

## 4. Non-goals

- No real customer or personal data. All data is synthetic (see section 7).
- No real payments, ERP or SEFAZ integration. Side effects are simulated and
  recorded.
- No UI beyond what is needed for a demo (API + CLI + reports).
- No multi-tenant auth. A single static API key is enough.
- No model fine-tuning.
- No agent framework. The agent loop is hand-written (see `DECISIONS.md`).

## 5. Architecture overview

### Language split

- **.NET (production pipeline):** domain, validators, LLM gateway,
  extraction, agent, MCP tool server, Temporal workflows, intake API.
- **Python (evaluation and data):** synthetic dataset generation, eval
  runner, graders, reports, run comparison, analysis notebooks.
- **Boundary:** Python never reimplements pipeline logic. It calls the .NET
  service over HTTP (a synchronous eval endpoint that returns the full result
  and trace). The shared invoice schema is exported from C# as JSON Schema.

### Components (.NET)

| Component | Responsibility |
|---|---|
| `Domain` | Records: `Invoice`, `Party`, `LineItem`, `Taxes`, `Decision`. Pure, no I/O. |
| `Validators` | CNPJ check digits, access-key check digit, access key vs extracted fields, line items vs totals, tax consistency, date plausibility. Return structured errors. |
| `Llm` | Gateway abstraction over the model provider: retries, token and cost accounting, OpenTelemetry spans, request-hash response cache (dev/eval only). |
| `Extraction` | PDF to `Invoice` via schema-constrained output; validate-and-repair loop. |
| `Agent` | Hand-written tool loop with step budget, ending in a typed `Decision`. |
| `Tools` | MCP server: vendor lookup, PO match, duplicate / near-duplicate check, expense categorization. Read-only. |
| `Workflows` | Temporal workflows and activities; decision execution as a separate idempotent activity. |
| `Api` | Async intake (`POST /invoices` returns 202), status endpoint, sync eval endpoint. |

### Components (Python)

| Component | Responsibility |
|---|---|
| `datagen` | Synthetic NF-e XML generation, DANFE PDF rendering with a Code 128 barcode of the access key, degraded variants. |
| `evals` | Dataset loader, grader registry, async runner, JSONL run artifacts, `compare` command, CI subset mode. |
| `reports` | Run summaries, per-field accuracy, cost and latency, regression lists, charts for the README. |

## 6. Phases and acceptance criteria

### Milestone 1: Extraction, measured

**Phase 1: Foundation**
- .NET solution and Python project scaffolded in one repo; both build and
  test in CI (GitHub Actions).
- Domain records defined; JSON Schema exported from C# and committed.
- Validators implemented with unit tests, including known-valid and
  known-invalid CNPJs and access keys.
- Works out of the box under both Claude Code and OpenCode.

**Phase 2: Synthetic dataset**
- Generator produces paired `{case}.xml` (ground truth) and `{case}.pdf`
  (DANFE) from a seed, reproducibly.
- At least 150 cases, covering: single and multi-page invoices, many line
  items, several tax regimes, degraded scans (rotation, blur, low DPI), and
  barcode-unreadable variants.
- Ground truth parsed from XML validates against the exported JSON Schema.

**Phase 3: Extraction pipeline**
- LLM gateway with retries, cost accounting, tracing and request-hash cache.
- Extraction returns a schema-valid `Invoice` or a typed failure.
- Repair loop feeds validator errors back to the model, bounded attempts.
- Sync eval endpoint returns result, validator outcomes, attempts, tokens,
  cost, latency and trace ID.

**Phase 4: Eval harness v1**
- Runner executes a dataset against the eval endpoint with bounded
  concurrency; writes one JSONL record per case plus a run summary.
- Graders: schema validity, exact match (IDs, CNPJ, access key), numeric
  tolerance (amounts, taxes), line-item matching.
- `compare` lists per-field deltas and regressed cases between two runs.
- CI runs a small subset on PRs and fails below a configured threshold.
- First results table: two models or two prompt versions.

### Milestone 2: The agent

**Phase 5: MCP tool server and reference data**
- MCP server exposing vendor lookup, PO match, duplicate / near-duplicate
  check and categorization, backed by Postgres seeded from datagen.
- Tools usable from Claude Code as an MCP client (demo).

**Phase 6: Agent loop and decision evals**
- Agent reaches `approve | reject | escalate` with reason and evidence, within
  a step budget; budget exhaustion escalates.
- Dataset extended with agent scenarios: PO mismatch, unknown vendor,
  duplicate, near-duplicate, anomalous amount, clean approvals.
- Graders: decision correctness, required tool calls made, no forbidden tool
  calls, LLM-as-judge for reason quality (calibrated on a human-labeled
  sample).

### Milestone 3: Production hardening

**Phase 7: Durable execution and intake**
- Temporal workflow orchestrates extraction, validation, agent, persistence
  and decision execution; all LLM and tool calls are activities.
- Async intake API with the three idempotency layers and side-effect keys
  described in `DECISIONS.md`.
- Tests prove: client retries, double uploads, re-scans and worker crashes
  never produce a second approval.

**Phase 8: Observability and publication**
- OpenTelemetry traces span API, workflow, activities, model and tool calls.
- Cost attributed per invoice and per eval run.
- README: architecture diagram, results tables, failure analysis, design
  decisions, how to run locally.

## 7. Data strategy

- NF-e is chosen because the official XML is a free, exact ground truth for
  the DANFE PDF, and because the access key gives a natural business
  identifier for idempotency.
- All data is synthetic: generated CNPJs with valid check digits, fictitious
  company names and addresses. No real invoices, even scrubbed.
- Datasets are versioned; eval runs record the dataset version.

## 8. Tech stack

| Area | Choice | Status |
|---|---|---|
| Runtime | .NET 10 (LTS), Python 3.12+ | decided |
| Model provider | Anthropic models; compare at least two | decided |
| .NET model SDK | Anthropic C# SDK and/or `Microsoft.Extensions.AI` | **research** |
| MCP | `ModelContextProtocol` C# SDK | **research** (version, hosting) |
| Durable execution | Temporal, `Temporalio` .NET SDK | decided |
| API | ASP.NET Core minimal APIs | decided |
| Database | PostgreSQL | decided |
| Observability | OpenTelemetry, local trace viewer (e.g. Jaeger) | decided |
| Local services | devenv services vs docker-compose vs .NET Aspire | **open** |
| .NET tests | xUnit | decided |
| Python tooling | uv, ruff, pyright or mypy, pytest, typer, pandas | decided |
| Schema codegen | JSON Schema to Pydantic (e.g. datamodel-code-generator) | **research** |
| DANFE rendering, barcode decoding | libraries to be chosen | **research** |

Items marked **research** must be verified against current documentation
before planning, since these ecosystems change quickly.

## 9. Constraints

- Development environment: NixOS-WSL with project-specific devenv.
- Agent runtimes: Claude Code and OpenCode, both supported.
- API spend must stay modest: the response cache is mandatory for dev and
  eval reruns; CI uses a small dataset subset.
- English for code, docs and commit messages.

## 10. Definition of done (project)

A reviewer can clone the repo, start local services with one command, process
a sample invoice, run the eval suite, and read a README that shows measured
quality, cost and latency, explains failures, and justifies the design.
