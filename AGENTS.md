# carimbo agent instructions

This file is the single source of instructions for Claude Code and OpenCode. The root `CLAUDE.md` only imports it. Planning artifacts live under `.planning/`; `.claude/CLAUDE.md` is generated project context and is not duplicated here.

## Project

carimbo turns Brazilian electronic invoices (NF-e), received as DANFE PDFs, into strictly typed invoice records, and later into typed approve / reject / escalate decisions. It is a portfolio project that shows production rigor applied to LLM agents in a regulated fintech setting.

Core value: measured quality. A reproducible eval suite that exercises the real pipeline is the product. When a tradeoff arises, choose whatever keeps quality measurable and reproducible.

## Layout

```
dotnet/               .NET 10 solution (Carimbo.slnx)
  src/Carimbo.Domain      pure invoice records and the wire options (no dependencies)
  src/Carimbo.Llm         ILlmGateway, the Anthropic adapter, pricing and cost accounting
  src/Carimbo.Extraction  extraction contract, schema projection, the repair loop
  src/Carimbo.Api         ASP.NET Core minimal API composition root and the eval endpoint
  tests/                  xUnit v3 projects; Carimbo.ScriptedHost is the scripted-model host for e2e
  tools/SchemaExport      writes (or with --check verifies) schema/*.json
  tools/LlmSpike          live gateway spike, never part of dotnet test
python/               one uv project
  src/carimbo_datagen     seeded synthetic NF-e XML + DANFE PDF generator
  src/carimbo_evals       runner, grader and summary for the eval endpoint
  src/carimbo_models      Pydantic models generated from the schema (never hand-edited)
  tests/                  pytest; markers e2e and live
schema/               canonical and model-facing JSON Schema exported from the Domain
data/skeleton/        committed synthetic cases (XML ground truth, PDF) and manifest.json
evals/runs/           run output, git-ignored
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
| `just e2e` | committed cases over HTTP to a graded summary, against a scripted model |
| `just docs-check` | decision records D-18 to D-24 and the spike recommendation exist |
| `just secrets-check` | no key-shaped string in a tracked file |
| `just check` | every offline gate above |
| `just skeleton` | live paid run of the three skeleton cases, then grade (cap US$1.00 per run) |
| `just spike-live` | live paid gateway spike (cap US$1.00); rewrites `docs/spikes/01-llm-gateway.md` |
| `just schema-probe` | live paid schema probe (cap US$0.25); rewrites `docs/spikes/02-schema-probe.md` |

Raw commands behind the two per-stack recipes:

- .NET: `dotnet build dotnet/Carimbo.slnx -warnaserror`, `dotnet format dotnet/Carimbo.slnx --verify-no-changes`, `cd dotnet && dotnet test`
- Python: `uv sync --project python --locked`, then `uv run --project python ruff check python`, `ruff format --check python`, `pyright -p python` and `pytest python/tests -q`

The root `global.json` pins the SDK and opts into Microsoft.Testing.Platform. The e2e and live pytest markers are deselected by default; `-m e2e` selects the former.

## Conventions

- English for code, docs and commit messages. Pin exact versions; no floating ranges.
- Wire format: snake_case names, string enums, and money as strings matching `^-?[0-9]+\.[0-9]{2}$`.
- The Domain stays pure (no package dependencies). One `Wire.Options` drives both schema export and parsing of model output.
- Generated files are never hand-edited: `schema/*.json`, `python/src/carimbo_models/generated.py` and `data/skeleton/*`. Regenerate with `just schema` and `just datagen`.
- xUnit v3 on Microsoft.Testing.Platform. pytest markers `e2e` and `live`. LF line endings, per `.editorconfig`.
- All data is synthetic (DECISIONS D-14). Never add real taxpayer data.
- Model output is not reproducible run to run (no temperature control on the current models); the response cache and replay fixtures are the reproducibility mechanism.

## Secrets and live calls

- The provider key is read from `CARIMBO_ANTHROPIC_API_KEY`, then `ANTHROPIC_API_KEY` (D-10). It is optional: without it, everything except the live recipes runs against the fake model client.
- Dev procedure with secretspec (optional; exporting the variable works too): run `secretspec config global init` once per machine, then `secretspec set CARIMBO_ANTHROPIC_API_KEY` at the prompt (never with the value as an argument), then `secretspec run -- claude --continue` (or `secretspec run -- opencode`), because an agent session sees only the variables present at launch.
- An agent that invokes secretspec itself must pass `--reason` or set `SECRETSPEC_REASON`; the live recipes already do.
- Never print, log, echo or commit a key. Agents check presence only, never the value.
- Live paid steps run only through `just spike-live`, `just schema-probe` and `just skeleton`, with caps of US$1.00 per run (US$0.25 for the schema probe) and US$5 per phase. CI uses no provider key.

## What not to use

- `JsonSerializerDefaults.Web` for schema export: it turns every number into a string-or-number union. Use explicit options.
- Forced `tool_choice`, assistant prefill, `Temperature`/`TopP`: 400 on the current models. Use structured outputs; omit sampling options.
- Polly or `Http.Resilience` on top of the SDK retries: double retries. One retry owner (D-21).
- `UseDistributedCache` as the response cache: the key must be ours (D-19).
- `ZXing.Net.Bindings.SkiaSharp` next to `PDFtoImage`: SkiaSharp native version clash. Use `ZXingCpp`.
- Alpine-based .NET images: the PDFium and Skia natives are glibc builds.
- `FluentAssertions` 8.x (commercial license): use xUnit `Assert`.
- Pillow `save(format="PDF")` for datasets (not reproducible), text-layer PDFs for degraded scans, `httpx` 0.28.1 (use `httpx2`), Swashbuckle, .NET Aspire as orchestrator, `pyzbar`.

## Planning

GSD artifacts live under `.planning/`. Decisions are recorded in `docs/DECISIONS.md`, as superseding entries rather than rewrites.
