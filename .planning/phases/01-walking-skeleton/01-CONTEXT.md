# Phase 1: Walking Skeleton - Context

**Gathered:** 2026-10-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Scaffold both stacks (.NET 10 solution, single uv Python project) and the C# → canonical JSON Schema → model-facing schema → Pydantic contract chain. Generate 3 seeded synthetic DANFE cases (XML ground truth + PDF with Code 128 access-key barcode). Extract them live through `POST /eval/extractions` (static API key, dev/eval only), returning a schema-valid `Invoice` or a typed refusal / truncation / infrastructure failure with tokens, cost, latency and trace ID. Run the Python runner over the cases (bounded concurrency, cost cap, resume) writing JSONL, then grade offline into a run summary. CI builds and tests both stacks on every PR.

Requirements: REPO-01..04, CI-01, DOM-01, DOM-05..08, LLM-02, LLM-06, EXT-01, EXT-02, API-03, DATA-01, DATA-07, EVAL-01, EVAL-02, RES-03.

Not in this phase: validators and repair loop (Phase 2), response cache and trace viewer (Phase 3), degraded or large datasets (Phase 4), full graders and statistics (Phase 5), CI eval gating (Phase 6).

</domain>

<decisions>
## Implementation Decisions

### Carried forward (already locked, not re-discussed)
- Stack and pinned versions per `.planning/research/STACK.md` and `.claude/CLAUDE.md`.
- Wire conventions fixed now so later phases only add fields: snake_case JSON, string enums, money as pattern-constrained decimal strings (research Conflict 1, option A).
- Structured outputs (`output_config.format`), never forced `tool_choice`, never `Temperature`/`TopP`, never assistant prefill.
- D-03, D-07, D-15 refinements accepted; Phase 1 writes them into `docs/DECISIONS.md` as superseding entries (RES-03).
- Gateway shape (IChatClient adapter vs direct SDK) and retry ownership are NOT pre-decided: the LLM-06 live spike settles them (research Conflicts 2 and 4; recommendation B for shape, single non-stacked retry policy).
- Postgres is not wired in M1. docker compose is the canonical local-services path, devenv is toolchain only (no compose services needed in Phase 1 itself).

### First Invoice subset
- **D-01:** Phase 1 `Invoice` = header + parties + totals: access key, number, series, issue date, issuer (CNPJ, name), recipient (CNPJ, name), invoice total. No line items or tax breakdown yet; Phase 2 completes the DANFE-visible target. — **Reversibility:** reversible — fields are additive under the fixed wire conventions.
- **D-02:** `Decision` exists as a minimal pure stub record in `Domain` (satisfies DOM-01) but is NOT part of the exported extraction schema.
- **D-03:** Access key is constrained by a 44-character pattern in the schema only; no check-digit validation (validators are Phase 2). Pattern must allow the alphanumeric form Phase 2 will formalize, or be loosened then.
- **D-04:** Phase 1 grader scores per case: schema validity, exact match on IDs / CNPJs / date / names-as-normalized-strings, `Decimal` compare with configurable tolerance (default R$0.01) on the total. At least one field-level grade per case in the summary.

### Skeleton cases
- **D-05:** Exactly 3 cases.
- **D-06:** All clean text-layer PDFs, one tax regime; one of the three overflows to 2+ pages to prove multi-page rendering early. Degradation stays in Phase 4.
- **D-07:** Commit the 3 skeleton XML + PDF files (tiny). CI regenerates from the seed and asserts byte identity with the committed files. Synthetic parties only (generated CNPJs with correct check digits, Faker `pt_BR` seeded). — **Reversibility:** costly — committed PDF bytes become part of future cache keys (PDF SHA-256); regenerating changes them.

### Live runs & budget
- **D-08:** Default model `claude-haiku-4-5` for the spike and skeleton runs; the spike must confirm Haiku 4.5 accepts the model-facing schema. Run `claude-sonnet-5-5` once (thinking/effort config explicit, no temperature) to confirm its 400 behaviours. Final model pair stays a Phase 6 decision.
- **D-09:** Total Phase 1 live spend cap US$5; runner default per-run cost cap ~US$1.
- **D-10:** Live calls run in this cloud environment. The user will add `ANTHROPIC_API_KEY` as an environment variable; until it is present, live steps are blocked and everything else proceeds against a fake `IChatClient`/fake transport. The endpoint reads the key from configuration and is unavailable without it.
- **D-11:** The LLM-06 spike result is recorded in committed `docs/spikes/` markdown (request/response shapes, usage fields, finish reasons; no PDF bytes, no secrets) and produces a new `docs/DECISIONS.md` entry for gateway shape and retry ownership.

### Repo layout & commands
- **D-12:** Top-level layout: `dotnet/` (solution; `dotnet/src/Carimbo.{Domain,Llm,Extraction,Api}`, `dotnet/tests/...`, `dotnet/tools/SchemaExport`) and `python/` (single uv project with `carimbo_datagen`, `carimbo_evals` packages, generated models module). Shared artifacts at root: `schema/` (canonical + model-facing JSON Schema) and `data/` (skeleton cases). — **Reversibility:** costly — paths are referenced from CI, justfile, codegen and docs.
- **D-13:** `just` is the task runner: one command per stack (e.g. `just dotnet-check`, `just py-check`), plus `just schema` (export + codegen) and `just skeleton` (generate → run → grade). Exact recipe names at planner discretion.
- **D-14:** Reviewer non-Nix path = documented manual install (dotnet 10 SDK via `global.json`, uv which provisions Python 3.12 and pinned ruff/pyright, just). CI uses the same path so it is verified on every PR. No devcontainer in Phase 1.
- **D-15:** `AGENTS.md` at repo root is canonical; a root `CLAUDE.md` contains `@AGENTS.md`. The existing GSD-generated `.claude/CLAUDE.md` stays.

### Claude's Discretion
- Exact recipe names, project/test project names, JSONL record field names and run-summary format (JSON + short markdown), runner CLI flags, eval output paths (git-ignored run dirs), generator seed handling.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project scope and decisions
- `.planning/PROJECT.md` — vision, constraints, key decisions table
- `.planning/REQUIREMENTS.md` — Phase 1 requirement IDs and wording
- `.planning/ROADMAP.md` §Phase 1 and §Planning Notes — success criteria and the spike list to run first
- `docs/DECISIONS.md` — D-01..D-17 (locked); Phase 1 adds superseding entries for D-03, D-07, D-15 and the LLM-06 outcome
- `docs/PROJECT-BRIEF.md` — full brief, all milestones

### Research
- `.planning/research/SUMMARY.md` — conflicts 1-4 and proposed D-03/D-07/D-15 refinements
- `.planning/research/STACK.md` — pinned versions, structured-output schema limits, model 400 behaviours, JsonSchemaExporter pitfall (`JsonSerializerDefaults.Web`)
- `.planning/research/ARCHITECTURE.md` — component layout and gateway design
- `.planning/research/PITFALLS.md` — determinism, SkiaSharp pinning, xUnit v3 runner opt-in, codegen staleness
- `.planning/research/FEATURES.md` — feature breakdown

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- None: the repository holds only docs (`docs/`, `.planning/`, `LICENSE`). Phase 1 is greenfield.

### Established Patterns
- None in code yet. Conventions come from STACK.md (Central Package Management, `global.json`, pinned Python tools via `uv.lock`).

### Integration Points
- GitHub Actions (none exist yet) and the cloud environment's `ANTHROPIC_API_KEY` variable (not yet set as of 2026-10-04).

</code_context>

<specifics>
## Specific Ideas

- Spike first, per ROADMAP planning notes: live SDK contract (LLM-06), JsonSchemaExporter `$defs` hoisting (fallback NJsonSchema), datamodel-codegen determinism, xUnit v3 runner opt-in, DANFE + Code 128 rendering for a few cases, confirming which package `httpx2` is.
- Cost/token accounting needs a versioned pricing table (Haiku 4.5 $1/$5, Sonnet 5.5 $2/$10 per MTok; confirm cache-write multipliers).

</specifics>

<deferred>
## Deferred Ideas

None: the discussion stayed within the phase scope.

</deferred>

---

*Phase: 01-walking-skeleton*
*Context gathered: 2026-10-04*
