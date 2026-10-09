# carimbo

carimbo turns Brazilian electronic invoices (NF-e), received as DANFE PDFs, into strictly typed invoice records, and later into typed approve / reject / escalate decisions. It is a portfolio project: a payments engineer applying production rigor to LLM agents in a regulated fintech setting, backed by evidence (structured outputs, deterministic validators, a reproducible eval suite, durable execution, idempotency, tracing and cost attribution). The core value is measured quality: an eval suite that exercises the real pipeline and publishes results anyone can reproduce.

Milestone 1, "Extraction, measured", has six phases. Phases 1 and 2 are complete (2026-10-08). The evidence so far is small: three synthetic cases, one model, one run per configuration. The sections below separate what is measured from what is not.

## Status

| Phase | What it delivers | State |
|-------|------------------|-------|
| 1. Walking Skeleton | A few synthetic DANFEs extracted live through the eval endpoint, graded offline from Python, summarized | Complete (2026-10-08) |
| 2. Validated Extraction | Full DANFE-visible target, deterministic validators, bounded repair loop with every attempt recorded | Complete (2026-10-08) |
| 3. Replayable Runs | Request-hash cache with replay modes, single retry policy, costed spans in a local trace viewer, per-request overrides | Next |
| 4. Synthetic Dataset | 150+ seeded, varied and degraded cases with validated ground truth, manifest and fixed CI subset | Not started |
| 5. Comparable Measurement | Full graders, confidence intervals, silent-error rate, failure taxonomy and run comparison | Not started |
| 6. Gated CI and Results | Fork-safe PR gate from committed fixtures, budgeted live tier, published two-model results and failure analysis | Not started |

The plan is in [`.planning/ROADMAP.md`](.planning/ROADMAP.md). Milestones 2 and 3 (agent, durable execution) are outside this roadmap.

## What works today

Decisions D-22 to D-25 are recorded in [`docs/DECISIONS.md`](docs/DECISIONS.md).

- **Invoice v2 (D-22).** Issuer, recipient (CNPJ or CPF), 14-column line items, 11 totals boxes and installments. Every field is required and only the two IE fields are nullable, so the model-facing schema uses 0 optional and 2 union properties against the provider limits of 24 and 16. CNPJ and access key accept numeric and alphanumeric forms. Money crosses the wire as pattern-constrained decimal strings and rounds half away from zero identically in C# and Python. [`docs/DANFE-MAPPING.md`](docs/DANFE-MAPPING.md) maps every field to its DANFE label and NF-e XML source, and a test keeps its rows equal to the 42 schema leaf paths.
- **Validators (D-23).** `Carimbo.Validation` is pure, takes a reference date, never throws, and returns findings `{field, rule_id, expected, actual, severity}` for CNPJ and CPF check digits, the access-key check digit, key-versus-field mismatches, item and tax arithmetic, totals, installments and date plausibility.
- **Shared oracle.** `data/vectors/validator-vectors.json` holds hand-curated valid and invalid CNPJs, CPFs, access keys and rounding cases, and runs under both xUnit and pytest.
- **Bounded repair (D-23).** Only error findings trigger repair. The conversation continues under prompt `repair-001` with the PDF cached, and `Extraction:MaxRepairs` defaults to 2 (allowed 0 to 5). Feedback reveals expected and actual values only for arithmetic and date rules, never for identifiers, check digits or keys, so repair cannot invent an identifier that merely satisfies a check. Exhausting the budget returns `validation_failed` with the last candidate and its findings.
- **Eval contract 2 (D-24).** Statuses `success`, `validation_failed`, `refused`, `truncated`, `schema_invalid` and `infrastructure_failure`; every attempt with its output, findings, tokens, cost and latency; the effective configuration echoed. Total cost is null with a warning when any answered attempt is unpriced, never a partial sum.
- **Ground-truth gate.** `Carimbo.GroundTruth` maps each committed case's NF-e XML to an Invoice, which must produce zero validator errors at the manifest `as_of_date`.
- **Strict parse boundary (D-25).** Success means schema-valid. Null list elements and enum values that are not the exact wire name are `schema_invalid`.
- **Current prompt.** `extract-003`, pinned by SHA-256 in the tests. It has not been run live (see Not measured yet).

## Results so far

The live runs are committed verbatim under [`evals/results/`](evals/results/): each run directory holds the per-case records (`cases.jsonl`), the run metadata (`run.json`) and the graded summary (`summary.json`, `summary.md`). New runs land in the git-ignored `evals/runs/` first. Each case was run once per configuration (n=1), so these numbers show the loop working and where it fails. They are not an accuracy estimate.

### Phase 2 live skeleton runs

Model requested `claude-haiku-4-5` (returned `claude-haiku-4-5-20251001`), prompt `extract-002`, repair prompt `repair-001`, dataset `skeleton-002`, 27 graded fields per case, recorded 2026-10-08. Commands: `just skeleton` (repair budget 2) and `just skeleton 1.00 0` (repair disabled). Runs: [repair budget 2](evals/results/skeleton-20261008T183505Z/summary.md), [repair budget 0](evals/results/skeleton-20261008T183611Z/summary.md). Analysis: [`02-10-SUMMARY.md`](.planning/phases/02-validated-extraction/02-10-SUMMARY.md).

Per field, over the three cases:

| Field | Repair budget 2 | Repair budget 0 |
|-------|-----------------|-----------------|
| `access_key` (44 digits) | 2/3 | 1/3 |
| The other 26 graded fields, each | 3/3 | 3/3 |
| All 27 fields, all cases | 80/81 | 79/81 |
| Schema-valid answers (JSON Schema and Pydantic) | 3/3 | 3/3 |
| Wrong answers flagged `validation_failed` | 1 of 1 | 2 of 2 |
| Cases repaired | 0 | 0 |
| Model calls | 5 | 3 |
| Cost | US$0.0843 | US$0.0642 |

The other 26 fields are `number`, `series`, `issue_date`, `operation_nature`, the issuer's CNPJ, name, IE and UF, the recipient's tax id, tax id kind, name, IE and UF, the item and installment counts, and the 11 totals boxes.

Per case:

| Case | What it covers | max_repairs 2 | max_repairs 0 |
|------|----------------|---------------|---------------|
| case-001 | One page, Simples Nacional CSOSN 101, numeric CNPJs, 3 items | success, 1 attempt, 27/27 | validation_failed, 1 attempt, 26/27 (access_key wrong), KEY_CHECK_DIGIT |
| case-002 | One page, Regime Normal with IPI, freight and discount, 2 installments, 4 items | validation_failed, 3 attempts, 26/27 (access_key wrong), KEY_CHECK_DIGIT, KEY_NUMBER_MISMATCH, KEY_SERIES_MISMATCH | validation_failed, 1 attempt, 26/27 (access_key wrong), KEY_CHECK_DIGIT, KEY_NUMBER_MISMATCH, KEY_SERIES_MISMATCH |
| case-003 | Two pages, 50 items, alphanumeric issuer CNPJ, CPF recipient | success, 1 attempt, 27/27 | success, 1 attempt, 27/27 |

Totals: cost US$0.0843 against US$0.0642, 5 attempts against 3, 0 cases repaired. Both runs: 0 harness errors, 0 unpriced cases.

### What the runs show

- The only wrong field in either run was the 44-digit access key, and every wrong key was flagged `validation_failed` by `KEY_*` rules (also UAT test 5 in [`02-UAT.md`](.planning/phases/02-validated-extraction/02-UAT.md)). No case reported `success` with a wrong field in these six extractions. That is an observation about six extractions, not a silent-error rate.
- Repair repaired 0 cases. The three attempts on case-002 gave three different wrong keys. The feedback withholds values for check-digit and key rules by design, so the model has nothing to anchor on except re-reading the image.
- The case-001 difference between the runs is run-to-run variance, not a repair effect: nothing was repaired in either run, and these models give no sampling control. With n=1 per cell this says only that access-key transcription is unstable on Haiku 4.5.
- Repair cost US$0.0157 extra on case-002 for no gain. With repair enabled, the PDF cache write made single-attempt cases dearer (+15% on case-001, +7% on case-003, indicative only). Run A read 12994 cache tokens on case-002's repair attempts.
- case-003 (50 items, output 6093 tokens) took 49586 ms and 49269 ms.
- The source lists three remedies for the access-key misread: a barcode-decoded key as input or cross-check, a stronger transcription instruction, or a different model. None is built. The barcode design intent is D-05 in `docs/DECISIONS.md`; the requirement sits under the v2 requirements and is not in the Phase 3 roadmap.

### Before validators

Phase 1 ran the same three cases on the first, smaller target (prompt `extract-001`, dataset `skeleton-001`, 9 graded fields per case): 3 of 3 `success`, but access_key was wrong on case-001 and case-002 (8/9 each) and case-003 was 9/9. Cost US$0.0195. Phase 1 had no check-digit validation, so those two wrong keys were reported as `success`. This is not like-for-like with Phase 2 (different dataset version, target and prompt). Run: [`skeleton-20261008T001300Z`](evals/results/skeleton-20261008T001300Z/summary.md). Analysis: [`01-13-SUMMARY.md`](.planning/phases/01-walking-skeleton/01-13-SUMMARY.md).

### Spikes

- [`docs/spikes/01-llm-gateway.md`](docs/spikes/01-llm-gateway.md) (2026-10-07): the direct Anthropic SDK behind `ILlmGateway` was chosen because the `IChatClient` path is not lossless on usage decomposition or stop details (D-21). `thinking: disabled` and a non-default `temperature` return HTTP 400 on Sonnet 5.5. A cache write followed by a read was observed on Haiku 4.5 and Sonnet 5.5. Spend US$0.2551 across four runs of the tool. Its per-case field counts are not a model comparison.
- [`docs/spikes/02-schema-probe.md`](docs/spikes/02-schema-probe.md) (2026-10-08): the v2 model-facing schema was accepted unchanged (HTTP 200, schema-valid invoice, 0 pattern violations). A two-turn conversation with a cached PDF block read 6321 cache tokens on the repair turn. US$0.0151 for the recorded run, US$0.0526 across three runs ([`02-03-SUMMARY.md`](.planning/phases/02-validated-extraction/02-03-SUMMARY.md)).

### Spend

Phase 1: US$0.2788. Phase 2: US$0.2011 (probe US$0.0526 plus the two skeleton runs). Each is under the US$5 phase cap. Every live run is capped at US$1.00 (US$0.25 for the schema probe).

### Verification and UAT

- Phase 2: verification passed, 5/5 ROADMAP success criteria, re-verified after the UAT gap-closure plan 02-13 ([`02-VERIFICATION.md`](.planning/phases/02-validated-extraction/02-VERIFICATION.md)). UAT ran 56 checks: 54 passed and 2 issues were found. G-02-1: the mapping doc pointed `recipient.name` at a box that prints the homologation notice. G-02-2: prompt `extract-002` used three totals labels and a column header that differ from the printed DANFE. Plan 02-13 closed both and introduced prompt `extract-003` ([`02-UAT.md`](.planning/phases/02-validated-extraction/02-UAT.md)).
- Phase 1: verification passed with 4/5 criteria verified automatically. The fifth (fresh clone, non-Nix path, both agent runtimes, CI on the first PR) was confirmed by UAT, 3 of 3 passed.
- Open review warnings WR-02 to WR-06 are tracked in [`02-REVIEW-DISPOSITION.md`](.planning/phases/02-validated-extraction/02-REVIEW-DISPOSITION.md). For example, an export recipient's UF `EX` is flagged `UF_UNKNOWN`.

### Not measured yet

- `extract-003` has had no live run. Every live number above used `extract-002` (Phase 2) or `extract-001` (Phase 1).
- Three synthetic cases, one run per configuration, one model on the pipeline: no accuracy estimate, no confidence intervals, no silent-error rate, no failure taxonomy (Phase 5), no model comparison or published results table (Phase 6).
- No response cache or replay yet, so a rerun calls the model again and can differ (Phase 3). No trace viewer yet (Phase 3).
- No dataset at scale and no degraded scans (Phase 4).
- Whether repair improves accuracy is unknown: it has repaired zero cases so far.


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

A minimal Linux image also lacks two system libraries: the ICU library (a .NET runtime prerequisite; the UAT image needed `libicu74`) and `libatomic1` (for the Node runtime that pyright downloads). Desktop distributions and GitHub runners ship both.

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
| `just docs-check` | Fail unless decision records D-18 to D-25, the gateway spike recommendation and the schema probe result exist |
| `just secrets-check` | Fail when a tracked file holds a provider-key-shaped string |
| `just check` | All of the above, offline |
| `just skeleton` | Live: extract the three skeleton cases with Claude and grade them (paid, capped at US$1.00 per run) |
| `just skeleton 1.00 0` | Same with repair disabled (`Extraction:MaxRepairs` 0); the default budget is 2 |
| `just spike-live` | Live: re-run the LLM gateway spike (paid, rewrites `docs/spikes/01-llm-gateway.md`) |
| `just schema-probe` | Live: probe the v2 schema and a cached two-turn conversation (paid, rewrites `docs/spikes/02-schema-probe.md`) |

## Secrets and live calls

The provider key is optional. Without it, `just check` runs everything against the fake model client, and the three live recipes (`just skeleton`, `just spike-live`, `just schema-probe`) exit 2 without calling the model when no key is available.

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

`just skeleton` costs at most US$1.00 per run (the runner stops at the cap). Its second argument is the repair budget (default 2); `just skeleton 1.00 0` runs without repair. Read the result in `evals/runs/<run>/summary.md`; that directory is git-ignored, and a run worth keeping is copied verbatim to `evals/results/<run>/`.

Never print, log or commit the key. Use a dedicated key with a low spend limit.

## Layout

```
dotnet/            .NET 10 solution (Carimbo.slnx)
  src/             Carimbo.Domain, .Llm, .Extraction, .Validation, .GroundTruth, .Api
  tests/           xUnit v3 test projects and the scripted host used by the e2e test
  tools/           SchemaExport (writes schema/), LlmSpike (live gateway spike)
python/            one uv project: carimbo_datagen, carimbo_evals, carimbo_models (generated)
schema/            canonical and model-facing JSON Schema, exported from the Domain
data/skeleton/     seeded synthetic NF-e cases (XML ground truth + DANFE PDF) and a manifest
data/vectors/      validator-vectors.json (shared oracle) and the shared valid-invoice.json fixture
evals/runs/        run output (git-ignored)
evals/results/     committed copies of the live runs the README cites
docs/              DECISIONS.md, DANFE-MAPPING.md and the spike records
.github/workflows/ the CI workflow (the same just recipes)
.planning/         GSD planning artifacts: plan summaries, verification and UAT reports
```

All data in this repository is synthetic: fictitious companies, generated CNPJs with valid check digits, no real taxpayer data (DECISIONS D-14).

Agent instructions for Claude Code and OpenCode live in `AGENTS.md`.
