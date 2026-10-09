# carimbo

carimbo turns Brazilian electronic invoices (NF-e), received as DANFE PDFs, into strictly typed invoice records, and later into typed approve / reject / escalate decisions. It is a portfolio project: a payments engineer applying production rigor to LLM agents in a regulated fintech setting, backed by evidence (structured outputs, deterministic validators, a reproducible eval suite, durable execution, idempotency, tracing and cost attribution). The core value is measured quality: an eval suite that exercises the real pipeline and publishes results anyone can reproduce.

Phase 1 is the walking skeleton: generate a few synthetic DANFEs, extract them live through the .NET eval endpoint with Claude, and grade the stored results from Python, end to end, before any breadth is added.

## Quick start without Nix

You need three tools. Nothing else is installed globally.

- The .NET SDK that `global.json` pins (10.0.100, or a later 10.0 feature band).
- [uv](https://docs.astral.sh/uv/) 0.8.17 or newer. It provisions Python 3.12 and the locked ruff, pyright and pytest.
- [just](https://github.com/casey/just). If you do not want to install it, `uv tool run --from rust-just just` runs it.

Then, from the repo root:

```sh
just check
```

`just check` runs every offline gate: both stacks, the contract chain (JSON Schema and generated Pydantic models), byte identity of the synthetic dataset, the end-to-end test against a scripted model, the docs check and the secrets check. It needs no API key and no network beyond the package registries.

The pyright package on PyPI is a wrapper around the Node tool and fetches a Node runtime on first use when none is installed. That is expected on a fresh machine.

CI runs this same path (the same recipes, on a clean Ubuntu runner without Nix) on every pull request.

## With Nix

On NixOS or any machine with [devenv](https://devenv.sh):

```sh
devenv shell
just check
```

or one-shot: `devenv shell -- just dotnet-check py-check`. The shell provides the .NET 10 SDK, uv, just, Python 3.12, Node (for pyright), git, curl and the secretspec CLI. It runs no services, and ruff and pyright still come from the uv lock, not from nixpkgs.

## Commands

| Command | What it does |
|---------|--------------|
| `just dotnet-check` | Build with warnings as errors, verify formatting, run the xUnit v3 tests |
| `just py-check` | `uv sync --locked`, ruff check and format check, pyright, pytest |
| `just schema` | Export `schema/*.json` from the .NET Domain, then regenerate the Pydantic models |
| `just schema-check` | Regenerate, then fail if the committed schema or models differ |
| `just datagen` | Regenerate the seeded synthetic dataset in `data/skeleton` |
| `just datagen-check` | Fail unless a regeneration is byte-identical to the committed dataset |
| `just e2e` | Committed cases over real HTTP to a graded summary, against a scripted model |
| `just docs-check` | Fail unless the decision records and the spike recommendation exist |
| `just secrets-check` | Fail when a tracked file holds a provider-key-shaped string |
| `just check` | All of the above, offline |
| `just skeleton` | Live: extract the three skeleton cases with Claude and grade them (paid) |
| `just skeleton 1.00 0` | Same with repair disabled (`Extraction:MaxRepairs` 0); the default budget is 2 |
| `just spike-live` | Live: re-run the LLM gateway spike (paid, rewrites `docs/spikes/01-llm-gateway.md`) |
| `just schema-probe` | Live: probe the v2 schema and a cached two-turn conversation (paid, rewrites `docs/spikes/02-schema-probe.md`) |

## Secrets and live calls

The provider key is optional. Without it, `just check` runs everything against the fake model client, and the two live recipes exit 2 with a message that names the options below.

**Without secretspec (any reviewer).** Export `CARIMBO_ANTHROPIC_API_KEY` in the shell that runs just. To keep the value out of your shell history:

```sh
read -rs CARIMBO_ANTHROPIC_API_KEY
export CARIMBO_ANTHROPIC_API_KEY
```

`ANTHROPIC_API_KEY` works as a fallback. carimbo prefers its own name because Claude Code uses `ANTHROPIC_API_KEY` for its own authentication.

**With secretspec (optional; the author's dev setup).** [secretspec](https://secretspec.dev) keeps the key in the OS keyring instead of your shell. The repo's `secretspec.toml` declares the key as optional.

1. Run `secretspec config global init` once per machine and pick a provider such as the OS keyring. The `development` profile that init selects is already declared in `secretspec.toml`.
2. Run `secretspec set CARIMBO_ANTHROPIC_API_KEY` without `--profile` and paste the value at the prompt. Never pass the value as an argument.
3. Run `just skeleton`, `just spike-live` or `just schema-probe` directly. When the variable is not exported they call `secretspec run` themselves. Or launch the agent session as `secretspec run -- claude --continue` (or `secretspec run -- opencode`). A session sees only the variables present when it was launched, so restart it after setting the key.

An agent that calls secretspec itself must pass `--reason` or set `SECRETSPEC_REASON`; the recipes already set one. If secretspec is installed but not configured for this repo, exporting the variable bypasses it.

`just skeleton` costs at most US$1.00 per run (the runner stops at the cap). Its second argument is the repair budget (default 2); `just skeleton 1.00 0` runs without repair. Read the result in `evals/runs/<run>/summary.md`; the directory is git-ignored.

Never print, log or commit the key. Use a dedicated key with a low spend limit.

## Layout

```
dotnet/            .NET 10 solution (Carimbo.slnx)
  src/             Carimbo.Domain, .Llm, .Extraction, .Api
  tests/           xUnit v3 test projects and the scripted host used by the e2e test
  tools/           SchemaExport (writes schema/), LlmSpike (live gateway spike)
python/            one uv project: carimbo_datagen, carimbo_evals, carimbo_models (generated)
schema/            canonical and model-facing JSON Schema, exported from the Domain
data/skeleton/     seeded synthetic NF-e cases (XML ground truth + DANFE PDF) and a manifest
evals/runs/        run output (git-ignored)
docs/              DECISIONS.md and the spike records
.github/workflows/ the CI workflow (the same just recipes)
```

All data in this repository is synthetic: fictitious companies, generated CNPJs with valid check digits, no real taxpayer data (DECISIONS D-14).

Agent instructions for Claude Code and OpenCode live in `AGENTS.md`.
