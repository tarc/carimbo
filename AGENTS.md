# carimbo agent instructions

This file is the single source of instructions for Claude Code and OpenCode. The root `CLAUDE.md` only imports it. Planning artifacts live under `.planning/`; `.claude/CLAUDE.md` is generated project context and is not duplicated here.

## Project

carimbo turns Brazilian electronic invoices (NF-e), received as DANFE PDFs, into strictly typed invoice records, and later into typed approve / reject / escalate decisions. It is a portfolio project that shows production rigor applied to LLM agents in a regulated fintech setting.

Core value: measured quality. A reproducible eval suite that exercises the real pipeline is the product. When a tradeoff arises, choose whatever keeps quality measurable and reproducible.

## Layout

```
dotnet/               .NET 10 solution (Carimbo.slnx)
  src/Carimbo.Domain      pure invoice records and the wire options (no dependencies)
  src/Carimbo.Decisions   decision cases, buyer policy, the labelling rule and what each tool returns
  src/Carimbo.Llm         ILlmGateway, the Anthropic adapter, pricing and cost accounting
  src/Carimbo.Extraction  extraction contract, schema projection, the repair loop
  src/Carimbo.Tools       the stateless MCP host serving the decision cases read-only
  src/Carimbo.Agent       the hand-written decision loop and its MCP client
  src/Carimbo.Api         ASP.NET Core minimal API composition root and the eval endpoint
  tests/                  xUnit v3 projects; Carimbo.ScriptedHost is the scripted-model host for e2e
  tools/SchemaExport      writes (or with --check verifies) schema/*.json
  tools/DecisionCases     writes (or with --check verifies) data/decision
  tools/LlmSpike          live gateway spike, never part of dotnet test
python/               one uv project
  src/carimbo_datagen     seeded synthetic NF-e XML + DANFE PDF generator, and image-only scan twins
  src/carimbo_evals       runner, grader and summary for the eval endpoint
  src/carimbo_models      Pydantic models generated from the schema (never hand-edited)
  tests/                  pytest; markers e2e and live
schema/               canonical and model-facing JSON Schema exported from the Domain
data/skeleton/        committed synthetic cases (XML ground truth, PDF) and manifest.json
data/dataset/         generated Phase 4 dataset: 154 synthetic cases, XML ground truth, text-layer and image-only PDFs, manifest.json
data/decision/        generated decision cases, policy, manifest and expected labels
data/contract/        golden extraction and decision responses (generated) and the two hand-written request examples
evals/runs/           run output, git-ignored
evals/results/        committed copies of the live runs the README cites (verbatim)
docs/                 DECISIONS.md (decision records) and docs/spikes/
.github/workflows/    ci.yml, which calls the same just recipes
.planning/            GSD planning artifacts
```

## Commands

Run from the repo root. `just check` is the one command that checks everything offline.

| Recipe | Purpose |
|--------|---------|
| `just dotnet-check` | build with warnings as errors, verify formatting, run the .NET tests |
| `just py-check` | sync from the lock, ruff check and format check, pyright, pytest |
| `just schema` | export `schema/*.json` from the Domain, then regenerate the Pydantic models |
| `just schema-check` | regenerate, then fail if the committed artifacts differ (DOM-08) |
| `just datagen` | regenerate `data/skeleton` from the seed |
| `just datagen-check` | fail unless a regeneration is byte-identical to `data/skeleton` |
| `just dataset` | regenerate `data/dataset` from the quota table and the seed |
| `just dataset-check` | fail unless a regeneration is byte-identical to `data/dataset` |
| `just decision-cases` | regenerate `data/decision` from the skeleton cases and the specs in `dotnet/tools/DecisionCases` |
| `just decision-cases-check` | fail unless a regeneration is byte-identical to `data/decision` |
| `just golden` | regenerate the golden extraction and decision responses from the .NET tests; the compare-mode tests fail `just dotnet-check` when they are stale |
| `just e2e` | committed extraction and decision cases over HTTP (the decision e2e starts the Tools host and the scripted Api as two processes) to a graded summary, against a scripted model |
| `just docs-check` | decision records D-18 to D-40, the spike recommendation and the schema-probe and agent-probe Result sections exist |
| `just secrets-check` | no key-shaped string in a tracked file |
| `just check` | every offline gate above |
| `just skeleton` | live paid run of the three skeleton cases, then grade (cap US$1.00 per run) |
| `just skeleton 1.00 0` | the same with repair disabled (second argument is `Extraction:MaxRepairs`, default 2) |
| `just skeleton 1.00 2 read_only` | replay the last recorded skeleton answers from `.cache/llm`: zero model calls, US$0 incurred (third argument is the cache mode: `read_write` default, `read_only`, `refresh`, `off`) |
| `just spike-live` | live paid gateway spike (cap US$1.00); rewrites `docs/spikes/01-llm-gateway.md` |
| `just schema-probe` | live paid schema probe (cap US$0.25); rewrites `docs/spikes/02-schema-probe.md` |
| `just agent-probe` | live paid probe of tools plus structured output on the agent conversation (cap US$0.10); rewrites `docs/spikes/03-agent-probe.md` |
| `just decisions` | live paid run of the eight decision cases: starts the Tools host and the Api, runs `carimbo-evals decisions`, then grades (cap US$1.00 per run; second argument is the cache mode) |
| `just decisions 1.00 read_only` | replay the last recorded decision answers from `.cache/llm`: zero model calls, US$0 incurred (the Tools host still runs; the provider key must be present) |

`docker compose up -d` starts the local trace viewer (Jaeger, UI http://localhost:16686, loopback only).

Raw commands behind the two per-stack recipes:

- .NET: `dotnet build dotnet/Carimbo.slnx -warnaserror`, `dotnet format dotnet/Carimbo.slnx --verify-no-changes`, `cd dotnet && dotnet test`
- Python: `uv sync --project python --locked`, then `uv run --project python ruff check python`, `ruff format --check python`, `pyright -p python` and `pytest python/tests -q`

The root `global.json` pins the SDK and opts into Microsoft.Testing.Platform. The e2e and live pytest markers are deselected by default; `-m e2e` selects the former.

## Conventions

- English for code, docs and commit messages. Pin exact versions; no floating ranges.
- Wire format: snake_case names, string enums, and money as strings matching `^-?[0-9]+\.[0-9]{2}$`.
- The Domain stays pure (no package dependencies). One `Wire.Options` drives both schema export and parsing of model output.
- Generated files are never hand-edited: `schema/*.json`, `python/src/carimbo_models/generated.py`, `python/src/carimbo_models/decision_generated.py`, `data/skeleton/*`, `data/dataset/*`, `data/decision/*`, `data/contract/eval-responses/*` and `data/contract/decision-responses/*`. Regenerate with `just schema` (both model files), `just datagen`, `just dataset`, `just decision-cases` and `just golden` (both response sets). `data/contract/eval-request.json` and `data/contract/decision-request.json` are the hand-written files there.
- xUnit v3 on Microsoft.Testing.Platform. pytest markers `e2e` and `live`. LF line endings, per `.editorconfig`.
- All data is synthetic (DECISIONS D-14). Never add real taxpayer data.
- Model output is not reproducible run to run (no temperature control on the current models); the response cache and replay fixtures are the reproducibility mechanism.

## Secrets and live calls

- The provider key is read from `CARIMBO_ANTHROPIC_API_KEY`, then `ANTHROPIC_API_KEY` (D-10). It is optional: without it, everything except the live recipes runs against the fake model client.
- Dev procedure with secretspec (optional; exporting the variable works too): run `secretspec config global init` once per machine, then `secretspec set CARIMBO_ANTHROPIC_API_KEY` at the prompt (never with the value as an argument), then `secretspec run -- claude --continue` (or `secretspec run -- opencode`), because an agent session sees only the variables present at launch.
- An agent that invokes secretspec itself must pass `--reason` or set `SECRETSPEC_REASON`; the live recipes already do.
- Never print, log, echo or commit a key. Agents check presence only, never the value.
- Live paid steps run only through `just spike-live`, `just schema-probe`, `just agent-probe`, `just skeleton` and `just decisions`, with caps of US$1.00 per run (US$0.25 for the schema probe, US$0.10 for the agent probe) and US$5 per phase. CI uses no provider key.

## What not to use

- `JsonSerializerDefaults.Web` for schema export: it turns every number into a string-or-number union. Use explicit options.
- Forced `tool_choice`, assistant prefill, `Temperature`/`TopP`: 400 on the current models. Use structured outputs; omit sampling options.
- Polly or `Http.Resilience` on top of the SDK retries: double retries. One retry owner (D-21).
- `UseDistributedCache` as the response cache: the key must be ours (D-19).
- `ZXing.Net.Bindings.SkiaSharp` next to `PDFtoImage`: SkiaSharp native version clash. Use `ZXingCpp`.
- Alpine-based .NET images: the PDFium and Skia natives are glibc builds.
- `FluentAssertions` 8.x (commercial license): use xUnit `Assert`.
- Pillow `save(format="PDF")` for datasets (not reproducible), text-layer PDFs for degraded scans, `httpx` 0.28.1 (use `httpx2`), Swashbuckle, .NET Aspire as orchestrator, `pyzbar`.

## Branching and pull requests

- `main` changes only through pull requests. Nothing is pushed to `main` directly.
- Every phase runs on its own branch, `gsd/phase-{phase}-{slug}` (for example `gsd/phase-04-synthetic-dataset`), from `git.phase_branch_template` in `.planning/config.json`, where `git.branching_strategy` is `phase`. Create it from `origin/main` before the phase's first commit, the discuss-phase context commit: `git fetch origin main`, then `git switch --no-track -c gsd/phase-04-synthetic-dataset origin/main`. GSD creates the branch itself only when execute-phase starts, also from `origin/main`, so context, research and plan commits made on `main` before then would be stranded there. execute-phase reuses an existing local branch of that name.
- Quick tasks run on `gsd/quick-{num}-{slug}` (`git.quick_branch_template`), which the quick workflow forks from `origin/main`. A standalone docs change may use `docs/<slug>`.
- A phase ships with `/gsd-ship` as a pull request into `main`, merged with a merge commit once CI is green. No squash merge and no rebase merge.
- After a merge, rebase every branch still in flight onto `origin/main`.
- Before `/gsd-ship`, README.md reflects the completed phase: its Status row reads `Complete (YYYY-MM-DD)` with the completion date from the ROADMAP progress table, its Verification and UAT section has a `- Phase N:` bullet that links the phase's `*-VERIFICATION.md`, results and spend cite only committed, measured files (run directories under `evals/results/`), and decision-record ranges reach the newest D-id.
- `python/tests/test_readme_drift.py` runs in `just py-check`, so `just check` and CI fail until the mechanical parts hold: the Status rows against the ROADMAP, the verification links, the `just docs-check` range in README.md, this file and the justfile, and the links to `evals/results/`. The ROADMAP marks a phase Complete only after its verification, so the Status row and the verification bullet are written then, on the phase branch, before `/gsd-ship`.
- The last plan of every phase carries the README task for the narrative parts (what works today, results, spend, what is not measured yet). Plan checkers flag a phase plan set whose last plan lacks it.

## Planning

GSD artifacts live under `.planning/`. Decisions are recorded in `docs/DECISIONS.md`, as superseding entries rather than rewrites.
