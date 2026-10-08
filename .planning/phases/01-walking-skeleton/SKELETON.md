# Walking Skeleton — carimbo

**Phase:** 1
**Generated:** 2026-10-04

Citation convention: bare `D-NN` means a decision in `01-CONTEXT.md`; `DECISIONS D-NN` means an entry in `docs/DECISIONS.md` (D-18 and later exist only there).

## Capability Proven End-to-End

A developer runs one command that generates three seeded synthetic DANFEs, sends each PDF through `POST /eval/extractions` on the .NET service to Claude, stores one JSONL record per case with the raw model output, and grades those stored records offline from Python into a run summary with field-level grades, tokens, cost, latency and trace IDs.

The measurement loop is the application in Milestone 1, so the skeleton is that loop: **datagen → .NET eval endpoint → Python runner → offline grader**. The project has no database and no UI in this milestone. The tracer slice (Plan 01-03) proves the loop first against a scripted model. Later plans make each stage real and live.

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Production runtime | .NET 10 (LTS), ASP.NET Core minimal API (`Carimbo.Api`), SDK pinned in root `global.json` (`10.0.100`, `latestFeature`), xUnit v3 on Microsoft.Testing.Platform | DECISIONS D-01: .NET is the author's strongest stack. Root `global.json` is found by every `dotnet` command run from the repo root or below it, so the MTP opt-in always applies |
| Eval/data runtime | One uv project in `python/` (Python 3.12, exact pins in `uv.lock`), packages `carimbo_models` (generated), `carimbo_datagen`, `carimbo_evals` | DECISIONS D-01 and CONTEXT D-12. Python never reimplements pipeline logic (DECISIONS D-02) |
| Schema source of truth | C# records in `Carimbo.Domain` → `CanonicalSchema.Export()` → `schema/invoice.schema.json`; `ModelSchemaProjector` → `schema/invoice.model.schema.json` (what is sent to the model, byte for byte); `datamodel-code-generator` → `python/src/carimbo_models/generated.py` | DECISIONS D-03 as refined by D-18. One `Wire.Options` object drives schema export and strict parsing of model output, so the two cannot drift |
| Wire conventions | snake_case JSON, string enums, money as a pattern-constrained decimal string (`^-?[0-9]+\.[0-9]{2}$`), access key and CNPJ patterns that admit the alphanumeric forms | Fixed in Phase 1 so later phases only add fields (CONTEXT, research Conflict 1 option A) |
| Model access | `ILlmGateway` (`Carimbo.Llm`) is the only seam. The bottom adapter (direct `Anthropic` SDK or `IChatClient` + raw factory) is chosen by the LLM-06 spike and checkpoint (01-10) and recorded as D-21. Cost comes from the versioned `pricing.json` through `CostAccountingLlmGateway` | DECISIONS D-07 / LLM-06. Provider types never leave `Carimbo.Llm` |
| Extraction | `IInvoiceExtractor` (`Carimbo.Extraction`) branches on the stop reason before a strict parse, producing typed outcomes: success / refused / truncated / schema_invalid / infrastructure_failure | EXT-01, EXT-02. Typed failures are never counted as wrong answers |
| Data layer | None. Files only: committed schemas, committed skeleton cases in `data/skeleton/`, git-ignored run directories in `evals/runs/<run_id>/` | Postgres arrives in Milestone 2. Phase 1 needs no persistence |
| Auth | The static eval key goes in the `X-Api-Key` header and is compared in fixed time against `CARIMBO_EVAL_API_KEY`. The route is mapped only in the Development or Eval environment, only when that key is configured, and only when a model gateway is available (provider key `CARIMBO_ANTHROPIC_API_KEY`, then `ANTHROPIC_API_KEY`, per D-10). Kestrel binds to 127.0.0.1 by default | API-03, D-10. Single static key, no tenants (out of scope) |
| Deployment target | None in Milestone 1. Local run only: `just skeleton` mints an ephemeral eval key, starts the Api on 127.0.0.1, runs the runner with a cost cap, stops the Api and grades | Definition of Done: clone, run, read measured results |
| Task runner / env | `just` recipes, one command per stack (`dotnet-check`, `py-check`, `check`). devenv supplies the toolchain only. Reviewer path: .NET 10 SDK + uv + just, verified by CI on a clean Ubuntu runner | D-13, D-14 |
| Directory layout | `dotnet/{src/Carimbo.{Domain,Llm,Extraction,Api},tests/*,tools/{SchemaExport,LlmSpike}}`, `python/{src/carimbo_*,tests}`, `schema/`, `data/skeleton/`, `docs/`, `evals/runs/` (ignored) | D-12 |
| Agent instructions | Root `AGENTS.md` is canonical and self-sufficient. Root `CLAUDE.md` is `@AGENTS.md`. `.claude/CLAUDE.md` (GSD) stays | D-15. OpenCode reads only `AGENTS.md` |

## Stack Touched in Phase 1

- [x] Project scaffold: .NET solution (CPM, xUnit v3/MTP, warnings as errors) and uv project (ruff, pyright, pytest), plans 01-01 and 01-02
- [x] Routing: `GET /healthz` and `POST /eval/extractions`, plans 01-03 and 01-08
- [ ] Database: N/A in Milestone 1. Persistence is files (committed cases and schemas, JSONL run records). See Out of Scope
- [ ] UI: N/A. The interactive surface is the CLI (`carimbo-datagen`, `carimbo-evals`) and `just` recipes calling the HTTP API (plans 01-07, 01-09, 01-13)
- [x] Deployment: documented local full-stack command `just skeleton` (plan 01-13), and CI on every PR (plan 01-13)

## Out of Scope (Deferred to Later Slices)

- Validators, alphanumeric CNPJ/key check digits as validation, bounded repair loop, attempts list (Phase 2)
- Full DANFE-visible target: line items, taxes, and half-up rounding vectors (Phase 2)
- Request-hash response cache, replay modes, single retry policy, OpenTelemetry spans, Jaeger via docker compose, per-request overrides, golden response fixtures parsed by pytest (Phase 3)
- 150+ case dataset, degraded image-only PDFs, XSD validation with placeholder signature, CI subset manifest (Phase 4)
- Full graders, confidence intervals, silent-error rate, failure taxonomy, `compare` (Phase 5)
- CI eval gating, live tier, published results table (Phase 6)
- Postgres, MCP tools, agent loop (Milestone 2); Temporal, intake API, idempotency (Milestone 3)

## Subsequent Slice Plan

Each later phase adds one vertical slice on top of this skeleton without changing its architectural decisions:

- Phase 2: every extraction targets the full DANFE-visible invoice, passes deterministic validators and is repaired within a bounded budget
- Phase 3: eval reruns replay from a request-hash cache, and every model call is a costed span in a local trace viewer
- Phase 4: evaluation runs against 150+ varied and degraded cases with ground truth proven valid
- Phase 5: full-dataset runs are graded field by field with confidence intervals and compared case by case
- Phase 6: every PR is gated on eval quality at no cost, and a two-model results table is published
