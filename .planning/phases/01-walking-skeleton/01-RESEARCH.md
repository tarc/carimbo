# Phase 1: Walking Skeleton - Research

**Researched:** 2026-10-04
**Domain:** Two-stack scaffold (.NET 10 + uv Python), C# -> JSON Schema -> Pydantic contract chain, seeded synthetic DANFE generation, live LLM extraction behind a minimal-API eval endpoint, offline-gradable eval runner
**Confidence:** HIGH for stack, schema chain, datagen determinism, SDK request/response shapes (executed here). MEDIUM for live-API behaviour (not exercised; the LLM-06 spike settles it). MEDIUM-LOW for OpenCode/devenv/NixOS items (docs blocked or untestable here).

This phase builds on the project-level research (`.planning/research/*`, `.claude/CLAUDE.md`, 2026-10-03). It does not redo it. Everything below is either phase-specific or a correction/refinement found by running code in this session. Spike scratch code lived in the session scratchpad and is not committed; every snippet marked "executed" was run against the pinned versions.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward (already locked, not re-discussed)
- Stack and pinned versions per `.planning/research/STACK.md` and `.claude/CLAUDE.md`.
- Wire conventions fixed now so later phases only add fields: snake_case JSON, string enums, money as pattern-constrained decimal strings (research Conflict 1, option A).
- Structured outputs (`output_config.format`), never forced `tool_choice`, never `Temperature`/`TopP`, never assistant prefill.
- D-03, D-07, D-15 refinements accepted; Phase 1 writes them into `docs/DECISIONS.md` as superseding entries (RES-03).
- Gateway shape (IChatClient adapter vs direct SDK) and retry ownership are NOT pre-decided: the LLM-06 live spike settles them (research Conflicts 2 and 4; recommendation B for shape, single non-stacked retry policy).
- Postgres is not wired in M1. docker compose is the canonical local-services path, devenv is toolchain only (no compose services needed in Phase 1 itself).

#### First Invoice subset
- **D-01:** Phase 1 `Invoice` = header + parties + totals: access key, number, series, issue date, issuer (CNPJ, name), recipient (CNPJ, name), invoice total. No line items or tax breakdown yet; Phase 2 completes the DANFE-visible target. - **Reversibility:** reversible - fields are additive under the fixed wire conventions.
- **D-02:** `Decision` exists as a minimal pure stub record in `Domain` (satisfies DOM-01) but is NOT part of the exported extraction schema.
- **D-03:** Access key is constrained by a 44-character pattern in the schema only; no check-digit validation (validators are Phase 2). Pattern must allow the alphanumeric form Phase 2 will formalize, or be loosened then.
- **D-04:** Phase 1 grader scores per case: schema validity, exact match on IDs / CNPJs / date / names-as-normalized-strings, `Decimal` compare with configurable tolerance (default R$0.01) on the total. At least one field-level grade per case in the summary.

#### Skeleton cases
- **D-05:** Exactly 3 cases.
- **D-06:** All clean text-layer PDFs, one tax regime; one of the three overflows to 2+ pages to prove multi-page rendering early. Degradation stays in Phase 4.
- **D-07:** Commit the 3 skeleton XML + PDF files (tiny). CI regenerates from the seed and asserts byte identity with the committed files. Synthetic parties only (generated CNPJs with correct check digits, Faker `pt_BR` seeded). - **Reversibility:** costly - committed PDF bytes become part of future cache keys (PDF SHA-256); regenerating changes them.

#### Live runs & budget
- **D-08:** Default model `claude-haiku-4-5` for the spike and skeleton runs; the spike must confirm Haiku 4.5 accepts the model-facing schema. Run `claude-sonnet-5-5` once (thinking/effort config explicit, no temperature) to confirm its 400 behaviours. Final model pair stays a Phase 6 decision.
- **D-09:** Total Phase 1 live spend cap US$5; runner default per-run cost cap ~US$1.
- **D-10:** Live calls run in this cloud environment. The key lives in the environment variable `CARIMBO_ANTHROPIC_API_KEY` (not `ANTHROPIC_API_KEY`, which Claude Code reserves for its own auth and which cloud env settings flag). The Api resolves the key as `CARIMBO_ANTHROPIC_API_KEY` first, falling back to `ANTHROPIC_API_KEY` for local dev; it is never logged, echoed or written to spike docs. Cloud env variables are visible to anyone using the environment, so it is a dedicated low-limit key. Without a key, live steps are blocked, everything else proceeds against a fake `IChatClient`/fake transport, and the endpoint is unavailable. - **Reversibility:** reversible - one config lookup.
- **D-11:** The LLM-06 spike result is recorded in committed `docs/spikes/` markdown (request/response shapes, usage fields, finish reasons; no PDF bytes, no secrets) and produces a new `docs/DECISIONS.md` entry for gateway shape and retry ownership.

#### Repo layout & commands
- **D-12:** Top-level layout: `dotnet/` (solution; `dotnet/src/Carimbo.{Domain,Llm,Extraction,Api}`, `dotnet/tests/...`, `dotnet/tools/SchemaExport`) and `python/` (single uv project with `carimbo_datagen`, `carimbo_evals` packages, generated models module). Shared artifacts at root: `schema/` (canonical + model-facing JSON Schema) and `data/` (skeleton cases). - **Reversibility:** costly - paths are referenced from CI, justfile, codegen and docs.
- **D-13:** `just` is the task runner: one command per stack (e.g. `just dotnet-check`, `just py-check`), plus `just schema` (export + codegen) and `just skeleton` (generate -> run -> grade). Exact recipe names at planner discretion.
- **D-14:** Reviewer non-Nix path = documented manual install (dotnet 10 SDK via `global.json`, uv which provisions Python 3.12 and pinned ruff/pyright, just). CI uses the same path so it is verified on every PR. No devcontainer in Phase 1.
- **D-15:** `AGENTS.md` at repo root is canonical; a root `CLAUDE.md` contains `@AGENTS.md`. The existing GSD-generated `.claude/CLAUDE.md` stays.

### Claude's Discretion
- Exact recipe names, project/test project names, JSONL record field names and run-summary format (JSON + short markdown), runner CLI flags, eval output paths (git-ignored run dirs), generator seed handling.

### Deferred Ideas (OUT OF SCOPE)
None: the discussion stayed within the phase scope.

(Phase boundary, from CONTEXT `<domain>`: "Not in this phase: validators and repair loop (Phase 2), response cache and trace viewer (Phase 3), degraded or large datasets (Phase 4), full graders and statistics (Phase 5), CI eval gating (Phase 6).")
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| REPO-01 | Build/test .NET solution (.NET 10, `global.json`, CPM, xUnit v3) with one documented command | xUnit v3 needs `global.json` `"test": {"runner": "Microsoft.Testing.Platform"}`; exact failure without it reproduced (Pitfall 1). `dotnet build -warnaserror` + `dotnet format --verify-no-changes` + `dotnet test` verified. |
| REPO-02 | Build/lint/type-check/test single uv Python project with one command | All pins (ruff 0.16.10, pyright 1.1.414, pytest 9.1.1) resolve and run under uv here; `just py-check` recipe in Architecture Patterns. |
| REPO-03 | devenv on NixOS-WSL + documented non-Nix path | `nix shell nixpkgs#dotnet-sdk_10 nixpkgs#just` yields dotnet 10.0.401 + just 1.58.0 (executed). devenv.nix itself cannot be run here (Open Question / human checkpoint). |
| REPO-04 | Claude Code and OpenCode from one canonical instruction file | Claude Code: root `CLAUDE.md` with `@AGENTS.md` import; OpenCode reads `AGENTS.md` first and ignores CLAUDE.md when AGENTS.md exists. Existing `.claude/CLAUDE.md` is NOT read by OpenCode, so AGENTS.md must be self-sufficient. |
| CI-01 | GitHub Actions builds and tests both stacks on every PR | Action majors verified via `git ls-remote`; no-secrets `pull_request` workflow design below. |
| DOM-01 | Pure domain records | Architecture test (assembly allowlist + no PackageReference). Exporter-friendly attributes are BCL-only. |
| DOM-05 | Canonical JSON Schema exported deterministically, committed | Executed: `JsonSchemaExporter` + `TransformSchemaNode` + post-process hoist to `$defs` (no NJsonSchema needed). |
| DOM-06 | Model-facing schema via pure projector + budget test | Counting rule from Anthropic docs (verbatim below); Phase 1 Invoice is 0 optional / 0 union by construction; the test must still be a real counter that fails on a synthetic over-budget schema. |
| DOM-07 | Pydantic models generated from committed schema, pinned codegen | Executed: datamodel-code-generator 0.83.0, byte-identical across two runs, pyright/ruff clean; money maps to `str` when the Money node has no `title`. |
| DOM-08 | CI fails if schema or generated models stale | Two-layer check (fast xUnit snapshot + CI `git diff --exit-code`). |
| LLM-02 | Tokens per class + cost from versioned pricing table | Pricing page verified (table below). Usage fields confirmed via offline fake-transport spike. |
| LLM-06 | Live spike settles gateway shape + retry ownership | Pre-spike evidence (offline, executed) materially changes the prior: see "LLM-06 Spike Protocol". |
| EXT-01 | Extraction returns schema-valid Invoice or typed failure | Strict STJ parse is the .NET gate (executed: rejects unknown/missing/truncated/bad money). |
| EXT-02 | Refusal / max_tokens / infra failure are typed outcomes | Mapping of `stop_reason` verified through both SDK paths with canned responses. |
| API-03 | Eval endpoint disabled outside dev/eval, static API key | Design + test matrix below; `Activity.Current` W3C trace ID works without the OTel SDK (executed). |
| DATA-01 | Seeded byte-reproducible XML+PDF pairs with Code 128 barcode | Executed: identical SHA-256 across 3 interpreters / 2 zlib versions; barcode decodes with zxing-cpp; multi-page confirmed. |
| DATA-07 | Synthetic-only data | Own CNPJ generator + mod-11 validated against 18 independent nfelib sample keys; automated "no real/sample CNPJ reuse" test. |
| EVAL-01 | Runner: bounded concurrency, cost cap, resume, JSONL with raw output | httpx2 verified (`AsyncClient`, `MockTransport`, `Timeout`); design below. |
| EVAL-02 | Offline grading stage | Separate `grade` command, no network imports; design below. |
| RES-03 | DECISIONS.md superseding entries | Entry content + numbering plan below. |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

Extracted from `/home/user/carimbo/.claude/CLAUDE.md` (the planner must verify compliance; same authority as locked decisions):

- Tech stack fixed: .NET 10 (LTS), Python 3.12+, ASP.NET Core minimal APIs, xUnit; Python uses uv, ruff, pyright (or mypy), pytest, typer, pandas.
- Model provider is Anthropic only; compare at least two models (pair decided in Phase 6).
- **Core value is measured quality.** When tradeoffs arise, choose whatever keeps quality measurable and reproducible.
- Budget: modest API spend. Response cache is mandatory for dev/eval reruns, but it lands in Phase 3 (no cache in Phase 1; Phase 1 spend is capped at US$5 by D-09).
- English for code, docs and commit messages.
- Dev environment is NixOS-WSL with project-specific devenv, and it must also work for an external reviewer.
- Both Claude Code and OpenCode must be supported.
- Do NOT use `JsonSerializerDefaults.Web` for schema export; do NOT set `Temperature`/`TopP`; no forced `tool_choice`; no assistant prefill; no Polly on top of SDK retries; no `ZXing.Net.Bindings.SkiaSharp` next to `PDFtoImage`; no `FluentAssertions` 8.x; no Pillow `save(format="PDF")` for the dataset.
- GSD workflow enforcement: repo edits go through a GSD command (planning/execution handles this).
- CLAUDE.md "Technology Stack" notes `httpx2` as MEDIUM; this research resolves it (see Standard Stack).

## Summary

The phase is fully de-risked at the tooling level. In this session I built the .NET 10 SDK from nixpkgs (10.0.401), restored the pinned NuGet packages, and executed: the xUnit v3 + `dotnet test` setup, the C# to JSON Schema export with `$defs` hoisting, strict System.Text.Json parsing of model output, a Pydantic codegen round trip with pyright and ruff, DANFE rendering from XML (with Code 128), byte-reproducibility across three Python builds, a W3C trace ID inside ASP.NET Core without any OpenTelemetry package, and both Anthropic SDK call paths against a fake HTTP transport to capture the exact wire request and map canned `stop_reason`/usage payloads. No paid or even authenticated model call was made (one request with a dummy key confirmed the endpoint is reachable and the SDK throws a typed `AnthropicUnauthorizedException`).

Four findings change the plan relative to the project-level research and are worth the planner's attention. (1) The `IChatClient` adapter's default `ChatResponseFormat.ForJsonSchema` path **rewrites the schema it sends** (moves `pattern` into a `description` string and drops `$schema`), so the committed model-facing schema is not what the model sees; the raw `RawRepresentationFactory` escape hatch and `DataContent.WithCacheControl(...)` do pass everything verbatim, but the direct SDK call is lossless by construction and exposes usage/`stop_details`/`model`/`id` without mapping. The spike should therefore compare "direct SDK behind `ILlmGateway`" against "IChatClient + raw factory" and default to the direct SDK unless the live call shows a reason not to. (2) Haiku 4.5's minimum cacheable prefix is 4096 tokens, so a one-page skeleton PDF will silently report zero cache tokens on Haiku; the cache-token confirmation must use the multi-page case and/or Sonnet 5.5 (512-token minimum). (3) `decimal.Parse` with the default `NumberStyles` accepts `"12,34"` as 1234, which is exactly the pt-BR failure mode; the `Money` converter must validate the pattern and restrict `NumberStyles`. (4) `nfe_v4.00.xsd` requires a `ds:Signature`, so XSD validation of synthetic XML needs a placeholder signature (not required for Phase 1; defer to Phase 4's DATA-04).

**Primary recommendation:** Sequence the phase scaffold -> contract chain (Domain, exporter, projector, codegen, staleness) -> datagen (3 cases) -> gateway + eval endpoint against a fake transport -> live spike and live skeleton run -> runner/grader -> DECISIONS.md and CI hardening; keep `ILlmGateway` as the single seam, default the bottom adapter to the direct `Anthropic` SDK, and keep Phase 1 free of OpenTelemetry, PDF rasterizing, barcode decoding, caching and Postgres.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Domain records, wire options, canonical schema export | Domain (pure) + SchemaExport tool | - | D-04; the same `JsonSerializerOptions` object drives export and strict parsing of model output so schema and acceptance cannot drift. |
| Model-facing schema projection, prompt asset | Extraction | - | Provider-limit knowledge lives next to the prompt, not in Domain. |
| Model call, usage -> cost, pricing table, stop-reason mapping | Llm (gateway) | - | Provider types never leave `Carimbo.Llm`. |
| PDF -> typed outcome | Extraction | Llm | Branch on stop reason before parsing; strict parse is the schema-valid gate. |
| `POST /eval/extractions`, auth, feature gating, trace ID | Api (adapter) | - | Thin adapter over `IInvoiceExtractor`; `Activity.Current` supplies the trace ID. |
| Synthetic XML + DANFE PDF generation | Python datagen | - | D-01 split; Python generates, never re-implements pipeline logic. |
| Pydantic models | Python (generated, committed) | - | Never hand-edited; drift caught in CI. |
| Runner (HTTP, concurrency, cost cap, resume, JSONL) | Python evals | - | Calls the real endpoint (D-02). |
| Grading and run summary | Python evals (pure, offline) | - | Separate stage; re-gradable from stored raw output. |
| Static API key check, env-based config | Api | - | No tenant auth. |

## Standard Stack

### Core (Phase 1 subset of STACK.md)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK | 10.0.401 (nixpkgs; `global.json` `rollForward: latestFeature` from 10.0.100) | Build/test | [VERIFIED: executed `dotnet --version` -> `10.0.401` via `nix shell nixpkgs#dotnet-sdk_10`] |
| `Anthropic` (NuGet) | 12.53.0 | Messages API client | [VERIFIED: NuGet flat container lists 12.53.0 as latest stable; restored and compiled here] Official SDK per Anthropic's own C# docs ([CITED: platform.claude.com/docs/en/api/sdks/csharp]). |
| `xunit.v3` | 4.0.1 | .NET tests | [VERIFIED: NuGet flat container; test run passed here] |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.12 | `WebApplicationFactory` endpoint tests | [VERIFIED: NuGet flat container latest = 10.0.12] |
| Python | >=3.12 (uv provisions; system 3.12.3 used here) | Runtime | [VERIFIED: executed] |
| uv | pin `required-version` (container has 0.8.17; STACK.md says 0.12.23; nixpkgs 0.12.17) | Env/lock/run | [VERIFIED: `uv sync` of the full pinned set succeeded under 0.8.17] |
| `ruff` | 0.16.10 | Lint+format (also formats generated Pydantic) | [VERIFIED: resolved and ran via uv] |
| `pyright` | 1.1.414 | Type check | [VERIFIED: ran, 0 errors on generated models] |
| `pytest` / `pytest-asyncio` | 9.1.1 / 1.4.0 | Tests | [VERIFIED: resolved via uv] |
| `typer` | 0.27.2 | CLIs (`datagen`, `evals run/grade`) | [VERIFIED: resolved via uv] |
| `pydantic` | 2.13.5 | Generated models, record parsing | [VERIFIED: resolved via uv] |
| `datamodel-code-generator` | 0.83.0 | Schema -> Pydantic | [VERIFIED: executed twice, byte-identical output] |
| `httpx2` | 2.13.1 | Async HTTP client for the runner | [VERIFIED: PyPI JSON + executed `AsyncClient`, `MockTransport`, `Timeout`; import name `httpx2`] Resolves the STATE.md blocker: it is the Pydantic-org project (`project_urls.Source = https://github.com/pydantic/httpx2`), first release 2026-05-11, 18 releases, depends on `httpcore2`, `anyio`, `truststore`. The GitHub repo itself was not reachable here, so the org claim rests on PyPI metadata. Plain `httpx` has had no stable release since 0.28.1 (latest on PyPI now `1.0.dev6`). |
| `jsonschema` | 4.26.0 | Independent schema-validity grade (Draft 2020-12) | [VERIFIED: resolved via uv] |

### Supporting (datagen)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `nfelib` | 3.0.0 | Official NF-e 4.00 XSDs + sample XMLs (structure reference + independent test oracle for key DV) | Structural reference only; never ship its sample data (D-07 / DATA-07). [VERIFIED: installed, XSD loads] |
| `BrazilFiscalReport` | 1.2.0 | XML -> DANFE PDF + Code 128 | [VERIFIED: rendered, multi-page (70 extra items -> 2 pages on the sample), deterministic, barcode decodes] |
| `fpdf2` | 2.8.9 (transitive) | PDF engine | Call `set_creation_date()` (otherwise wall-clock date is embedded). |
| `python-barcode` | 0.16.1 (transitive) | Code 128 writer used by the renderer | Numeric and alphanumeric keys both rendered and round-tripped (see Pitfall 11). |
| `Faker` | 40.40.0 | Seeded `pt_BR` names/addresses | Generate CNPJ/access key yourself. |
| `lxml` | 6.1.3 | Build/inspect XML | Used to assemble the XML tree; `xsdata`-backed `nfelib` is optional. |
| `pypdfium2` | 5.13.0 | Tests only: text-layer check, rasterize for barcode self-check | [VERIFIED: executed] |
| `zxing-cpp` | 3.1.1 | Tests only: decode Code 128 from rendered page | [VERIFIED: decoded `Code128` with exact key] |
| `Pillow` | 12.3.0 | Transitive of pypdfium2 render-to-PIL in tests | Not used for PDF writing. |

### Not needed in Phase 1 (do not add)

`Microsoft.Extensions.AI` (add only if the spike selects the IChatClient path; `Anthropic` already brings `Microsoft.Extensions.AI.Abstractions` transitively), `OpenTelemetry.*` (Phase 3; the trace ID comes from `Activity.Current`), `PDFtoImage`, `ZXingCpp` (barcode decoding in .NET is DGEN-01/M3), `Npgsql`, `ModelContextProtocol.*`, `Temporalio`, `reportlab`/`numpy` (Phase 4 degradation), `pandas` (percentiles over 3 cases need only `statistics`; STACK.md's pandas decision stays for Phase 5), `matplotlib`, `anthropic` (Python), `Polly`/`Http.Resilience`.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Direct `Anthropic` SDK behind `ILlmGateway` (recommended default) | `AsIChatClient()` + `RawRepresentationFactory` | Both send the schema verbatim when used as shown; IChatClient adds a mapping layer (sums cache-creation into `InputTokenCount`, collapses refusal to `content_filter`, drops `stop_details`) and a second package. Gains: Phase 3 `UseOpenTelemetry`, M2 MCP tool types. The spike decides (D-11). |
| `JsonSchemaExporter` + own hoist | `NJsonSchema` | Not needed: hoisting executed in ~25 lines. Keep NJsonSchema only as the documented fallback. |
| `rust-just` via `uv tool` | `just` from package manager | `uv tool run --from rust-just just --version` -> 1.58.0 works (executed); a reviewer with uv needs no extra install step. |

**Installation (planner: pin exactly, commit lockfiles):**
```bash
# .NET (Central Package Management; versions live in dotnet/Directory.Packages.props)
#   Anthropic 12.53.0 (Llm), xunit.v3 4.0.1 (tests), Microsoft.AspNetCore.Mvc.Testing 10.0.12 (Api tests)
# Python (python/pyproject.toml; commit python/uv.lock)
uv add httpx2==2.13.1 pydantic==2.13.5 typer==0.27.2 jsonschema==4.26.0 \
       nfelib==3.0.0 brazilfiscalreport==1.2.0 faker==40.40.0 lxml==6.1.3
uv add --dev ruff==0.16.10 pyright==1.1.414 pytest==9.1.1 pytest-asyncio==1.4.0 \
       datamodel-code-generator==0.83.0 pypdfium2==5.13.0 zxing-cpp==3.1.1
```

**Version verification:** NuGet versions from `https://api.nuget.org/v3-flatcontainer/<id>/index.json`: Anthropic latest `12.53.0`, xunit.v3 `4.0.1`, Microsoft.Extensions.AI `10.10.0`, Mvc.Testing `10.0.12`. PyPI: httpx2 2.13.1 (2026-09-23), rust-just 1.58.0; the rest resolved by `uv sync` with exact pins. `[VERIFIED: registry queries, 2026-10-04]`

## Package Legitimacy Audit

The `package-legitimacy` seam supports npm/pypi/crates only and **returned `SUS` for every PyPI package** (including pytest, numpy, pandas), because the environment cannot obtain download counts (`unknown-downloads`) and the "too-new" signal fires on the latest release date of actively maintained projects. That uniform result carries no discriminating information; it is reported here rather than hidden. Discrimination below comes from repository linkage, history and official-doc references. NuGet is not covered by the seam.

| Package | Registry | Age / history | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|---------------|-----------|-------------|---------|-------------|
| httpx2 | PyPI | 18 releases since 2026-05-11 | unknown (seam) | github.com/pydantic/httpx2 (PyPI metadata) | SUS (seam: too-new, unknown-downloads) | Keep. Newest, least-established package in the set; executed a mock-transport round trip. Planner adds a `checkpoint:human-verify` on first install (inspect `uv.lock` hash + repo) per protocol. |
| datamodel-code-generator | PyPI | long-lived, org repo `datamodel-code-generator/datamodel-code-generator` | unknown | yes | SUS (seam: too-new) | Keep (dev-only; named in STACK.md/ARCHITECTURE.md). |
| brazilfiscalreport | PyPI | repoUrl null in seam metadata | unknown | none in metadata | SUS (seam: no-repository) | Keep with human-verify checkpoint before first install: LGPL-3.0, pip-dependency only. `[ASSUMED]` that the upstream project is the Engenere BrazilFiscalReport (file headers in the installed package name Engenere/Edson Bernardino). |
| nfelib | PyPI | repo github.com/akretion/nfelib | unknown | yes | SUS (seam: too-new) | Keep. |
| faker, jsonschema, typer, pytest, pytest-asyncio, pydantic, lxml, pypdfium2, zxing-cpp, ruff, pyright, pandas, numpy, reportlab, pillow | PyPI | mature, widely known | unknown | yes (except pandas/numpy: no repo in seam metadata) | SUS (seam artifact) | Keep. Seam noise; none flagged SLOP. |
| Anthropic, xunit.v3, Microsoft.AspNetCore.Mvc.Testing, Microsoft.Extensions.AI | NuGet | n/a | n/a | referenced by official docs / Microsoft | not covered by seam | Approved. `Anthropic` is the SDK named in Anthropic's own C# documentation. |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** all PyPI packages by seam artifact; planner inserts one `checkpoint:human-verify` covering "first `uv sync` of the lockfile" (httpx2, brazilfiscalreport called out), not one per package.

*Package names above were confirmed in this session on the correct registry and, for the non-trivial ones, imported and executed. Names appear in `.claude/CLAUDE.md` project research (authoritative project input).*

## Architecture Patterns

### System Architecture Diagram

```
 BUILD-TIME CONTRACT CHAIN (just schema)                       RUN-TIME MEASUREMENT LOOP (just skeleton)

 dotnet/src/Carimbo.Domain                                      python/carimbo_datagen  (seed)
   Invoice, Party, Money, Decision(stub)                           │ build XML (lxml tree)  ──►  data/skeleton/v1/{case}.xml
        │  Wire.Options (snake_case, strict)                       │ BrazilFiscalReport     ──►  data/skeleton/v1/{case}.pdf (Code 128)
        ▼                                                          ▼                              manifest.json (sha256, seed)
 dotnet/tools/SchemaExport                                    python/carimbo_evals  run (typer)
   JsonSchemaExporter + TransformSchemaNode + hoist               │ httpx2.AsyncClient, Semaphore(N), cost cap, --resume
        │                                                         │  POST /eval/extractions  {case_id, pdf_base64}  X-Api-Key
        ├─► schema/invoice.schema.json   (canonical, rich)        ▼
        │        │ datamodel-codegen (uv, pinned)            ┌───────────── Carimbo.Api ────────────┐
        │        └─► python/src/carimbo_models/generated.py   │ gate: Eval:Enabled + key configured   │
        ▼                                                     │ auth: X-Api-Key (fixed-time compare)  │
 Carimbo.Extraction.ModelSchemaProjector (pure)               │ trace_id = Activity.Current.TraceId   │
        └─► schema/invoice.model.schema.json (model-facing)   └──────────────┬────────────────────────┘
                                                                             ▼
 STALENESS: xUnit snapshot tests (fast) +                        Carimbo.Extraction (IInvoiceExtractor)
            CI: just schema && git diff --exit-code                 prompt asset ─► ILlmGateway.CompleteAsync
                                                                             ▼
                                                                  Carimbo.Llm gateway
                                                                    Anthropic SDK (MaxRetries per spike) ─► api.anthropic.com
                                                                    usage ─► pricing table ─► cost
                                                                             ▲  stop_reason branch FIRST:
                                                                    end_turn ─ strict STJ parse ─► Success(Invoice) | SchemaInvalid
                                                                    refusal ─► Refused   max_tokens ─► Truncated   exception ─► InfrastructureFailure
 response (HTTP 200 for every typed outcome) ◄───────────────────────────────────────────────┘
        │ tokens(in/out/cache_read/cache_write), cost_usd, latency_ms, trace_id, effective config, raw model text
        ▼
 runs/<run_id>/cases.jsonl  (1 line per case, raw output kept)  ──►  python/carimbo_evals grade (offline, no network)
        ▼                                                              ──►  summary.json + summary.md
```

### Recommended Project Structure
```
carimbo/
├── AGENTS.md                       # canonical, self-sufficient (OpenCode will not read .claude/CLAUDE.md)
├── CLAUDE.md                       # single line: @AGENTS.md
├── justfile                        # dotnet-check, py-check, schema, schema-check, datagen, skeleton, spike-live
├── devenv.nix / devenv.yaml        # toolchain only: dotnet-sdk_10, python312, uv, just, docker CLI
├── .editorconfig                   # end_of_line=lf, insert_final_newline (needed by dotnet format --verify-no-changes)
├── .gitattributes                  # * text=auto eol=lf ; *.pdf binary ; generated files eol=lf
├── .gitignore                      # bin/ obj/ TestResults/ .venv/ evals/runs/ .cache/ .env*
├── .github/workflows/ci.yml
├── dotnet/
│   ├── global.json                 # sdk 10.0.100 latestFeature + test.runner MTP
│   ├── Directory.Build.props       # net10.0, Nullable, TreatWarningsAsErrors, Deterministic
│   ├── Directory.Packages.props    # ManagePackageVersionsCentrally; exact pins
│   ├── Carimbo.slnx
│   ├── src/Carimbo.Domain/         # records, Money, Wire.Options, Decision stub  (BCL only)
│   ├── src/Carimbo.Llm/            # ILlmGateway, LlmRequest/Response, AnthropicGateway, Pricing (embedded json)
│   ├── src/Carimbo.Extraction/     # IInvoiceExtractor, ModelSchemaProjector, prompts/extract.v1.md (EmbeddedResource)
│   ├── src/Carimbo.Api/            # minimal API composition root, eval endpoint, key auth
│   ├── tests/Carimbo.{Domain,Llm,Extraction,Api}.Tests/
│   └── tools/SchemaExport/         # writes ../../schema/*.json with LF endings; `--check` mode
├── python/
│   ├── pyproject.toml, uv.lock, .python-version
│   ├── src/carimbo_models/generated.py   # generated, committed, header says DO NOT EDIT
│   ├── src/carimbo_datagen/        # cli.py, party.py (CNPJ/key math), nfe_xml.py, danfe.py, manifest.py
│   ├── src/carimbo_evals/          # cli.py, runner.py, records.py, graders.py, summary.py
│   └── tests/
├── schema/invoice.schema.json, schema/invoice.model.schema.json
├── data/skeleton/v1/{case-001..003}.xml|pdf, manifest.json
├── evals/runs/                     # git-ignored
└── docs/DECISIONS.md, docs/spikes/01-llm-gateway.md
```
`carimbo_models` is a third top-level package so `carimbo_evals` and `carimbo_datagen` never import each other (D-12 lists "generated models module"; this is the placement that keeps that rule enforceable with a ten-line import test).

### Pattern 1: One `JsonSerializerOptions` for export and for parsing model output
**What:** `Wire.Options` lives in Domain and is used by `SchemaExport` and by the extractor.
**Executed configuration (works on 10.0.401):**
```csharp
// Source: executed spike; STJ in-box. Do NOT use JsonSerializerDefaults.Web.
var opts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),   // REQUIRED, see Pitfall 3
};
```
Verified behaviours with this exact object: unknown property -> `JsonException`; missing required property -> `JsonException`; truncated JSON -> `JsonException`; `access_key` pattern is **not** enforced by STJ (Phase 2 validators own that). Money round-trips as `"1234.50"`.

### Pattern 2: Exporter with `TransformSchemaNode` + hoist post-pass
Executed output for the Phase 1 Invoice (abbreviated; property order is deterministic):
```json
{ "$schema": "https://json-schema.org/draft/2020-12/schema", "type": "object",
  "properties": {
    "access_key": {"type":"string","description":"44-character access key","pattern":"^[0-9]{6}[A-Z0-9]{12}[0-9]{26}$"},
    "number": {"type":"integer"}, "series": {"type":"integer"},
    "issue_date": {"type":"string","format":"date"},
    "issuer": {"$ref":"#/$defs/Party"}, "recipient": {"$ref":"#/$defs/Party"},
    "total_amount": {"type":"string","pattern":"^-?[0-9]+\\.[0-9]{2}$"} },
  "required": ["access_key","number","series","issue_date","issuer","recipient","total_amount"],
  "additionalProperties": false, "title": "Invoice",
  "$defs": { "Party": { "type":"object", "properties": {"cnpj": {"type":"string","pattern":"^[A-Z0-9]{12}[0-9]{2}$"}, "name": {"type":"string"}},
             "required":["cnpj","name"], "additionalProperties": false, "title":"Party" } } }
```
Rules found while executing:
- Check `ctx.TypeInfo.Type == typeof(Money)` **before** the `s is not JsonObject` guard: types with a custom converter export as the JSON literal `true` (any), not an object, so a guard-first order silently leaves `"total_amount": true`.
- Property-level `[RegularExpression]` and `[Description]` are read through `ctx.PropertyInfo.AttributeProvider.GetCustomAttributes(false)` inside `TransformSchemaNode`; the exporter does not read them natively.
- Hoisting: set `title = Type.Name` on object nodes in the transform, then rebuild the tree in a post-pass, moving each titled object (non-root) into `$defs` and replacing it with `{"$ref":"#/$defs/<Title>"}`. **Build new nodes rather than re-parenting**: assigning an existing `JsonNode` that already has a parent throws; the working version constructs fresh `JsonObject`/`JsonArray` and `DeepClone()`s leaves. Sort `$defs` ordinally.
- Give the Money node **no `title`**. With a title, datamodel-codegen generates `class Money(RootModel[str])` and `total_amount` becomes a `Money` wrapper (graders then need `.root`); without it, the field is `Annotated[str, Field(pattern=...)]` (executed, pyright clean).
- Write files with `\n` endings explicitly (`File.WriteAllText` of a string normalized to LF, UTF-8 no BOM). `Console.WriteLine` on Windows would emit CRLF. Relaxed JSON escaping (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`) avoids `"`-style noise in descriptions.

### Pattern 3: Pure `ModelSchemaProjector` (canonical -> model-facing)
Input: canonical schema node. Output: model-facing schema. Phase 1 rules (all unit-tested, snapshot committed as `schema/invoice.model.schema.json`):
1. Remove keywords the API does not support: `minimum`, `maximum`, `exclusiveMinimum/Maximum`, `multipleOf`, `minLength`, `maxLength`, `maxItems`, `uniqueItems`, `minItems` other than 0/1 `[CITED: platform.claude.com/docs/en/build-with-claude/structured-outputs]`. Phase 1 has none, but the projector must handle them for Phase 2.
2. Rewrite `oneOf` -> `anyOf`; assert no recursion, no external `$ref`, no `allOf`+`$ref`.
3. Force `additionalProperties: false` on every object.
4. Strip `$schema` (and decide on `title` in the spike; both were accepted by the SDK serializer, API acceptance is unconfirmed - `[ASSUMED]` until the spike).
5. Keep `pattern` and `format: date` (`date` is in the documented supported string-format list; `pattern` support for simple character-class regexes comes from project research PITFALLS.md Pitfall 1/7 and was NOT confirmed on the docs page fetched this session, `[ASSUMED]` until the spike). If the live call rejects `pattern`, fall back to moving it into `description` (the same thing the IChatClient adapter does) and record it in the spike doc.

**Budget counter (DOM-06), counting rule from the docs (verbatim):** "Total optional parameters across all strict tool schemas and JSON output schemas. Each parameter not listed in `required` counts toward this limit." and "Total parameters that use `anyOf` or type arrays (for example, `"type": ["string", "null"]`) across all strict schemas." Limits: 24 optional, 16 union. `[CITED: platform.claude.com/docs/en/build-with-claude/structured-outputs]`. Implement by walking the schema **expanding each `$ref` per use** (conservative: a shared `Party` used twice counts twice). Test the counter itself with a synthetic schema that exceeds each limit (a counter that cannot fail proves nothing). The Phase 1 Invoice is 0/0.

### Pattern 4: `ILlmGateway` as the single seam
Carimbo-owned request/response records (content blocks Text + Document now; ToolUse later). Fake gateway for all unit tests. Response carries: `Text`, `StopReason` (enum mapped from the SDK string), `Usage` (input, output, cache_read, cache_write_5m, cache_write_1h), `ModelReturned`, `ProviderMessageId`, `Latency`, `HttpAttempts`. Cost is computed in the gateway layer from a versioned pricing table (embedded `pricing.json`, `pricing_version` string, decimal arithmetic, unknown model -> null cost + typed warning, never silent zero).

**Pricing table content to ship (verified 2026-10-04 against the pricing page, USD per MTok):**

| Model | Base input | 5m cache write | 1h cache write | Cache read | Output |
|-------|-----------:|---------------:|---------------:|-----------:|-------:|
| claude-haiku-4-5 | 1.00 | 1.25 | 2.00 | 0.10 | 5.00 |
| claude-sonnet-5-5 | 2.00 | 2.50 | 4.00 | 0.20 | 10.00 |

`[CITED: platform.claude.com/docs/en/about-claude/pricing]` ("5-minute cache write 1.25x base input price; 1-hour cache write 2x; cache read 0.1x base input price"). Resolves the CONTEXT/STACK "confirm cache-write multipliers" item. Cost = `uncached_input*in + cache_write_5m*w5 + cache_write_1h*w1 + cache_read*r + output*out`. Thinking tokens are billed as output and are included in `usage.output_tokens` `[ASSUMED]` (confirm in spike).

**Usage decomposition (executed with canned response `input_tokens=12, cache_creation_input_tokens=5000`):**
- Direct SDK: `Usage.InputTokens=12`, `Usage.CacheCreationInputTokens=5000`, `Usage.CacheReadInputTokens=0`, and `usage.cache_creation.ephemeral_5m_input_tokens/ephemeral_1h_input_tokens` are in the payload. `InputTokens` is the **uncached remainder only** `[CITED: shared/prompt-caching.md in claude-api skill: "input_tokens is the uncached remainder only"]`.
- IChatClient adapter: `Usage.InputTokenCount=5012` (sum of uncached + creation), `CachedInputTokenCount=0`, `AdditionalCounts["CacheCreationInputTokens"]=5000`. A pricing function written for one shape double-charges the other. Whichever path wins, assert the decomposition with a recorded-usage test (LLM-02).

### Pattern 5: Typed outcomes (EXT-01/EXT-02)
```
Outcome = Success(Invoice, raw_text) | Refusal(stop_details?) | Truncated | SchemaInvalid(raw_text, parse_error)
        | InfrastructureFailure(kind: Auth|RateLimit|Overloaded|Timeout|Network|BadRequest, http_status?, request_id?)
```
Branch on `StopReason` first (`"refusal"`, `"max_tokens"`), then strict-parse. Executed mapping with canned payloads: direct SDK `StopReason` prints as `"end_turn"`/`"refusal"`/`"max_tokens"` and compares equal to the plain strings; `StopDetails.Category/Explanation` available on refusal; IChatClient maps to `ChatFinishReason` `stop`/`content_filter`/`length` and loses `stop_details`. Infrastructure failures arrive as `Anthropic.Exceptions.*` (`AnthropicUnauthorizedException`, `Anthropic5xxException`, `AnthropicApiException` base; `[VERIFIED: executed]` for 401 and 529). **HTTP contract:** the endpoint returns 200 whenever the pipeline ran, including typed failures; 4xx only for malformed request/auth; 5xx only for Carimbo bugs. The runner must classify "harness error" (transport/5xx from Carimbo) separately from "pipeline said no" (typed outcome).

### Pattern 6: Eval endpoint gating and trace ID
- Register the route group only when `Eval:Enabled` is true (default false; true under Development, or when explicitly set for eval runs) **and** an eval key is configured; otherwise the route is absent (404). Wrong/missing `X-Api-Key` -> 401. Compare with `CryptographicOperations.FixedTimeEquals` over UTF-8 bytes (hash both first to equalise lengths). Key read from env `CARIMBO_EVAL_API_KEY` (proposed name, discretion). Bind Kestrel to `127.0.0.1` by default.
- Missing Anthropic key (D-10) -> the extractor returns `InfrastructureFailure(kind=Auth/NotConfigured)` rather than 500, so the endpoint shape is testable without a key.
- **Trace ID:** `Activity.Current?.TraceId.ToString()` inside a minimal-API handler returned a 32-hex W3C id with no OpenTelemetry package, and honoured an inbound `traceparent` (executed: sent `traceparent: 00-4bf92f35...-01`, got `4bf92f3577b34da6a3ce929d0e0e4736` back). The runner can therefore mint the trace ID per case. Phase 3 adds the OTel SDK/exporter; do not add it now.
- Request: `{contract_version, case_id, document:{media_type, content_base64}}`. `case_id` is an opaque correlation id, never placed in the prompt or sent to the provider. Response (additive in later phases): `{contract_version, case_id, trace_id, effective:{model, prompt_version, schema_hash, pricing_version}, outcome:{status, invoice?, failure?, raw_output}, usage:{input_tokens, output_tokens, cache_read_tokens, cache_write_5m_tokens, cache_write_1h_tokens}, cost_usd, latency_ms, stop_reason, model_returned, provider_message_id}`. Money and cost fields as decimal strings or numbers - pick strings for `cost_usd` too (parse with `Decimal`).
- Request body limit: Kestrel default 30 MB is far above the skeleton PDFs; keep the default but set an explicit lower `MaxRequestBodySize` (e.g. 10 MB) on the route.

### Pattern 7: Runner (EVAL-01) and grader (EVAL-02)
- `carimbo-evals run --cases data/skeleton/v1 --base-url http://127.0.0.1:5xxx --concurrency 2 --max-cost-usd 1.00 --out evals/runs/<run_id> [--resume]`.
- `httpx2.AsyncClient(timeout=httpx2.Timeout(180.0, connect=5.0), headers={"X-Api-Key": ...})`; `asyncio.Semaphore(N)`; each case gets a minted `traceparent`.
- **Cost cap semantics:** accumulate `cost_usd` from completed responses; stop *dispatching* when `spent + reserve > cap`; in-flight requests can overshoot by at most `N * max_case_cost`. Document it and test it (the cap test uses a mock transport returning fixed costs and asserts dispatch stops and the process exits non-zero with a clear message).
- **Resume:** `cases.jsonl` is append-only, flushed per record; `--resume` skips any `case_id` with an existing record whose status is not `harness_error`. Stable sort of output by `case_id` happens only in the summary.
- **JSONL record (one per case):** `record_version`, `run_id`, `case_id`, `request:{pdf_sha256, traceparent, config echo}`, `http:{status, wall_ms, error?}`, `response:` verbatim endpoint body including raw model output, so grading never needs the network. No PDF bytes, no key.
- **Grader (pure, imports no network library; a test asserts that):** schema validity (Pydantic strict parse **and** `jsonschema` against the committed canonical schema, independently of .NET), exact match on `access_key` (strip spaces, uppercase), CNPJs (strip `. / -`), `number`, `series`, `issue_date`; names via NFKC + casefold + whitespace-collapse + punctuation-strip; `total_amount` via `Decimal` with `--tolerance` default `0.01`. Ground truth is parsed from the case XML (`Decimal`, `dhEmi` local date), not stored separately. Typed failures and harness errors are separate statuses, never "wrong answers", and all stay in the denominator counts of the summary.
- **Summary:** `summary.json` (sorted by case_id, floats rounded, volatile fields in a `meta` block) + short `summary.md`. Fields: run/dataset/manifest hash, model requested+returned, prompt version, schema hash, pricing version, grader version, per-case grades, outcome counts, tokens, `cost_usd` (sum), latency (min/median/max; p95 is meaningless at n=3), harness-error count.

### Pattern 8: Datagen (DATA-01, DATA-07)
- Per-case RNG derived from `(master_seed, case_id)`; no global RNG. Faker `pt_BR` seeded per case; names carry a visible synthetic marker (e.g. "SINTETICA" in the company name) so DANFEs cannot be confused with real entities.
- Own CNPJ and access-key math. Both verified here against an independent oracle: the mod-11 key DV matched **18 of 18** `Id="NFe..."` keys in nfelib's sample XMLs; the CNPJ DV matched 17 of 18 issuer CNPJs (the mismatch is presumably a CPF-padded or special issuer; not investigated) and the textbook vector `11.222.333/0001-81` -> DV `81`. Use those samples only as test oracles, never as dataset content.
- Build XML from a structural template of `<ide>/<emit>/<dest>/<det>*/<total>/<transp>/<pag>` (the sample's children), with the date/key/items/parties generated. The NF-e root for the renderer is `<NFe>` (optionally wrapped in `<nfeProc>`); the sample file's root is `nfeProc`.
- Render: `Danfe(xml=...)`, `set_creation_date(fixed)`, `output(path)`. Core fonts (Times/Courier) are not embedded, so no system-font lookup exists to diverge across machines.
- **Multi-page case:** the overflow case needs enough `<det>` items (the sample with 74 items rendered 2 pages; tune the count and assert `pages_count >= 2`).
- **Determinism evidence (executed):** same render in separate processes and on three interpreters (system 3.12.3 zlib 1.3, system 3.13 zlib 1.3, Nix 3.12.14 zlib 1.3.2) produced the identical SHA-256 prefix `23232c8bd974506a` / 5425 bytes. The only date in the PDF is the creation date you set; `/Title (DANFE)` is the only info string. A uv-managed (python-build-standalone) interpreter was not available here to test (GitHub blocked), so CI's regenerate-and-compare check is the guard; if it ever differs, `set_compression(False)` is the fallback to try.
- Printed forms: the renderer prints CNPJ formatted (`AB.1C2.D3E/0001-30` for an alphanumeric one, executed), so graders normalise printed vs raw forms.

### Anti-Patterns to Avoid
- **Default `ChatResponseFormat.ForJsonSchema` as the contract path:** rewrites the schema (see Pitfall 5).
- **`Create<T>()` auto-schema** (SDK helper): bypasses the committed schema.
- **Putting `case_id`/file names in the prompt or provider request.**
- **Parsing before checking `stop_reason`.**
- **Hand-editing `generated.py` or committing it from an unpinned toolchain.**
- **Adding OTel, PDF rasterizing, barcode decoding or a cache "while you're there".** Phase boundaries are explicit.
- **Treating the SUS seam verdicts as a reason to install nothing, or as clearance to skip the single human-verify checkpoint.**

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| C# -> JSON Schema | Custom reflection walker | `JsonSchemaExporter` + `TransformSchemaNode` | Same options as the deserializer; ~25 lines for hoist. |
| Schema -> Pydantic | Hand-written models | `datamodel-code-generator` 0.83.0 | D-03; output verified deterministic. |
| DANFE layout + Code 128 | Own PDF layout/barcode | `BrazilFiscalReport` | Realistic layout, multi-page; barcode via python-barcode. |
| HTTP resilience around the SDK | Polly/own retry loop (Phase 1) | SDK `MaxRetries` (spike decides) | Stacked retries multiply attempts. Phase 3 owns the single policy. |
| Static key comparison | `==` on strings | `CryptographicOperations.FixedTimeEquals` | Timing-safe, 1 line. |
| JSON Schema validation in Python | Regex checks | `jsonschema` Draft 2020-12 + Pydantic | Independent of .NET. |
| Task runner | Makefile/scripts per OS | `just` (via nix, package manager or `uv tool run --from rust-just just`) | D-13. |
| Trace IDs | Own id scheme | W3C `Activity.Current.TraceId` | Free in ASP.NET Core; honours `traceparent`. |
| Decimal handling | `float`/`double` anywhere | `decimal` in C#, `Decimal` in Python, strings on the wire | Pitfall 3 (project research). |

**Key insight:** every deceptively hard problem in this phase already has a verified, small solution; the risk is in the seams (schema rewriting by adapters, formatter/version drift, hidden defaults), not in missing libraries.

## Common Pitfalls

### Pitfall 1: `dotnet test` fails on .NET 10 with xUnit v3 unless `global.json` opts in
**What goes wrong:** exact error reproduced: `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later. If you use dotnet test, you should opt-in to the new dotnet test experience.`
**How to avoid:** `dotnet/global.json`:
```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" } }
```
Test projects need `<OutputType>Exe</OutputType>` and a `xunit.v3` PackageReference. With the opt-in, `dotnet test` in the solution directory discovered the `.slnx` and passed (executed, `total: 1 succeeded: 1`). `dotnet new sln --format slnx` works on this SDK. MTP writes a `TestResults/` directory: git-ignore it. **Warning signs:** CI red with the quoted message. `[VERIFIED: executed]`

### Pitfall 2: `dotnet format --verify-no-changes` fails on a new repo for whitespace
Executed: `error WHITESPACE: Fix whitespace formatting. Insert '\n'.` Add a root `.editorconfig` (`end_of_line = lf`, `insert_final_newline = true`, `charset = utf-8`) and run `dotnet format` once before the first commit.

### Pitfall 3: `JsonSchemaExporter` with explicit options throws without a `TypeInfoResolver`
Executed: `InvalidOperationException: JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only.` Set `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` (reflection; fine for a tool and tests).

### Pitfall 4: `Money.Read` with the default `NumberStyles` silently accepts pt-BR input
Executed test: `"12,34"` was **accepted** (parsed as 1234) by `decimal.Parse(s, InvariantCulture)`. Fix (executed, all 6 tests green): validate against `^-?[0-9]+\.[0-9]{2}$` first, parse with `NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint`, throw `JsonException` on mismatch (so a bad model value becomes a typed `SchemaInvalid`, not a crash or a wrong number). `ToString("0.00", InvariantCulture)` on write. Half-up rounding rules are Phase 2 (DOM-04); do not do arithmetic in Phase 1 beyond tolerance compare.

### Pitfall 5: The IChatClient adapter rewrites the schema you commit
**What goes wrong (executed, canned transport):** with `ChatResponseFormat.ForJsonSchema(schema, "invoice")` the wire request contained `"cnpj":{"type":"string","description":"{pattern: \"^[A-Z0-9]{12}[0-9]{2}$\"}"}` and the `$schema` key moved into a root `description`; the DataContent PDF block carried no `cache_control`. With `ChatOptions.RawRepresentationFactory` returning a `MessageCreateParams` that has `OutputConfig.Format = new JsonOutputFormat { Schema = ... }`, and `new DataContent(bytes,"application/pdf").WithCacheControl(new CacheControlEphemeral())` (extension on `AIContentCacheExtensions`), the request body matched the committed schema byte for byte and carried `"cache_control":{"type":"ephemeral"}` on the document block.
**Why it matters:** the Anthropic docs state the SDK "strips unsupported keywords, adds them to descriptions, and validates client-side" `[CITED: PITFALLS.md citing the structured-outputs docs]`; "the schema the model sees is not the schema you commit" is exactly STACK.md pitfall 5. A measured-quality project must be able to say "this exact schema was sent".
**How to avoid:** in the gateway, send the schema through the raw path only, and add a golden test asserting the outgoing JSON schema equals `schema/invoice.model.schema.json` (parse the captured request body from a fake `HttpMessageHandler`, which `AnthropicClient.HttpClient`/`BaseUrl` make possible).

### Pitfall 6: Haiku 4.5 will not cache a short PDF
Minimum cacheable prefix: Haiku 4.5 **4096** tokens; Sonnet 5.5 **512** `[CITED: claude-api skill shared/prompt-caching.md table; prompt-caching docs say to check the page for Sonnet 5.5's value]`. A one-page DANFE (~2-3k tokens with text+image per page, estimate `[ASSUMED]`) with `cache_control` set silently yields `cache_creation_input_tokens: 0`. To "confirm cache-token usage" (LLM-06) the spike must either use the multi-page case on Haiku and check the numbers, or run two identical Sonnet 5.5 requests and expect creation then read. Keep `output_config.format` byte-identical between the two requests: "Changing the `output_config.format` parameter will invalidate any prompt cache for that conversation thread" `[CITED: structured-outputs docs]`. Do not ship caching logic in Phase 1; only record the usage fields and price them.

### Pitfall 7: `nfe_v4.00.xsd` requires a signature
Executed: sample `<NFe>` validates (`True`); removing `ds:Signature` fails with `Missing child element(s). Expected is one of ( infNFeSupl, Signature )`. Validation of synthetic XML against the XSD therefore needs a placeholder `Signature` block clearly marked synthetic. Not required in Phase 1 (DATA-04 is Phase 4); do not let it block the 3 cases.

### Pitfall 8: Model IDs and the snapshot the API returns
Structured-outputs supported-model list names `claude-haiku-4-5-20251001` and `claude-sonnet-5-5` (the project docs use the alias `claude-haiku-4-5`). The canned-response spike returned `model: "claude-haiku-4-5-20251001"` for a request sent with the alias. Send the alias (works with the SDK), **record both `model_requested` and `model_returned`** in every response and JSONL line; whether to pin the dated snapshot is a spike output (Pitfall 18 in project research). `[ASSUMED]` the alias resolves to the 2025-10-01 snapshot on the live API (the response in the spike was synthetic).

### Pitfall 9: Sonnet 5.5 request shape
No `Temperature`/`TopP`; do not send `thinking: disabled` (400); thinking runs adaptive when omitted; effort default `high`; set `OutputConfig.Effort` explicitly (C# `OutputConfig` carries both `Effort` and `Format`) `[CITED: claude-api skill; csharp/claude-api/README.md "Effort is nested under OutputConfig"]`. Thinking tokens consume `max_tokens`: use a generous cap (e.g. 8192) for the Sonnet run. Whether structured outputs, adaptive thinking and an effort setting combine in one request on Sonnet 5.5 is `[ASSUMED]` until the spike. Sending a deliberately invalid request to "confirm 400 behaviours" (thinking disabled, non-default temperature) is rejected at validation and costs no tokens `[ASSUMED]`; do these as separate, clearly labelled spike steps.

### Pitfall 10: Agent instruction files
Claude Code: "An `AGENTS.md` and a `CLAUDE.md` ... in your working directory or above it -> Your `CLAUDE.md` files only", and "A `CLAUDE.md` that already imports `AGENTS.md` -> Your `CLAUDE.md`, with `AGENTS.md` included through the import" `[CITED: code.claude.com/docs/en/memory]`. So root `CLAUDE.md` containing `@AGENTS.md` is correct, and `.claude/CLAUDE.md` (GSD-managed) also loads. OpenCode: AGENTS.md wins over CLAUDE.md ("if you have both AGENTS.md and CLAUDE.md, only AGENTS.md is used"), and it falls back to `~/.claude/CLAUDE.md` only `[CITED: web search summary of opencode.ai/docs/rules; the docs site itself was egress-blocked, treat as MEDIUM]`. Consequence: **OpenCode never sees `.claude/CLAUDE.md`**, so AGENTS.md must carry the commands, conventions, secrecy rules, and layout on its own. Avoid symlinks (Windows checkouts).

### Pitfall 11: Alphanumeric key/CNPJ already renders and round-trips
Executed: with CNPJ `AB1C2D3E000130` and key `351808AB1C2D3E000130550010000476051695511864` (DV recomputed with ASCII-48 mod-11), BrazilFiscalReport rendered, printed `AB.1C2.D3E/0001-30`, and `zxing-cpp` decoded `Code128` with the exact key. Not required in Phase 1 (D-06 numeric-only suggested), but cheap insurance: the D-03 pattern `^[0-9]{6}[A-Z0-9]{12}[0-9]{26}$` accepts it. Recommend making one of the 3 skeleton cases alphanumeric only if the planner wants the early proof; otherwise leave for Phase 4 (open question).

### Pitfall 12: `uv`/pyright/ruff version drift changes the committed generated file
Run codegen only through `uv run` from the lockfile (CI and local), invoke it with an identical relative path each time (the header embeds `filename: invoice.schema.json`), `--disable-timestamp`, and fail on `git diff`. nixpkgs ships different uv/ruff versions than the pins (uv 0.12.17, ruff 0.16.8 vs pins); devenv must provide `uv` only and let uv install pinned tools (do not use nixpkgs `ruff`/`pyright` for checks). Set `[tool.uv] required-version`.

### Pitfall 13: pyright's PyPI wrapper needs Node
The `pyright` package ran here because Node 24 is installed; on a machine without Node it downloads one `[ASSUMED]`. Document in the reviewer path, or pick `mypy`. Not a blocker.

### Pitfall 14: Live-key hygiene
The key never appears in logs, spike docs, JSONL, exception messages or CI. Never construct `HttpClient` logging at `Trace`; keep `ILogger` request-body logging off; the SDK reads `ANTHROPIC_API_KEY` by default, so pass `ApiKey` explicitly from `CARIMBO_ANTHROPIC_API_KEY` (D-10 order). Add a `just secrets-check` that fails on `sk-ant-` in tracked files and in `docs/spikes/`. Cloud env vars are visible to others (D-10): the key is a dedicated low-limit key; do not `printenv`.

## Code Examples

### global.json / Central Package Management (executed)
```xml
<!-- dotnet/Directory.Packages.props -->
<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Anthropic" Version="12.53.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
  </ItemGroup>
</Project>
```
```xml
<!-- dotnet/Directory.Build.props -->
<Project><PropertyGroup>
  <TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors><Deterministic>true</Deterministic>
</PropertyGroup></Project>
```

### Direct SDK call, lossless (compiled and run against a fake transport)
```csharp
// Source: executed spike (Anthropic 12.53.0); shapes also in claude-api skill csharp/claude-api/{README,tool-use}.md
AnthropicClient client = new() { ApiKey = key, MaxRetries = 0 /* or SDK default 2: spike decides */,
                                 Timeout = TimeSpan.FromSeconds(120) };
var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modelFacingSchemaJson)!;
Message m = await client.Messages.Create(new MessageCreateParams
{
    Model = "claude-haiku-4-5", MaxTokens = 4096,
    OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
    Messages = [ new MessageParam { Role = Role.User, Content = new List<ContentBlockParam> {
        new DocumentBlockParam { Source = new Base64PdfSource { Data = Convert.ToBase64String(pdf) },
                                 CacheControl = new CacheControlEphemeral() },   // omit for Phase 1 if no cache goal
        new TextBlockParam { Text = promptText } } } ],
});
// m.StopReason ("end_turn"|"refusal"|"max_tokens"), m.StopDetails, m.Usage.{InputTokens,OutputTokens,
// CacheCreationInputTokens,CacheReadInputTokens}, m.Model (snapshot), m.ID ; text via b.TryPickText(out var t)
```
Captured request body (verbatim shape): `{"model":..,"max_tokens":..,"output_config":{"format":{"type":"json_schema","schema":{...}}},"messages":[{"role":"user","content":[{"type":"document","source":{"media_type":"application/pdf","type":"base64","data":".."},"cache_control":{"type":"ephemeral"}},{"type":"text","text":".."}]}]}`.

### IChatClient with verbatim schema (executed)
```csharp
IChatClient c = client.AsIChatClient("claude-haiku-4-5");
var opts = new ChatOptions { MaxOutputTokens = 4096,
  RawRepresentationFactory = _ => new MessageCreateParams { Model = "claude-haiku-4-5", MaxTokens = 4096, Messages = [],
      OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } } } };
var msg = new ChatMessage(ChatRole.User, [ new DataContent(pdf, "application/pdf").WithCacheControl(new CacheControlEphemeral()),
                                            new TextContent(promptText) ]);
ChatResponse r = await c.GetResponseAsync([msg], opts);   // r.FinishReason, r.Usage, r.ModelId, r.ResponseId
```

### Retry visibility (executed against a fake handler returning 529, 529, 200)
`MaxRetries = 2`: **3 HTTP calls** in ~1.5 s then success; `MaxRetries = 0`: **1 call** then `Anthropic5xxException`. A counting `HttpMessageHandler` passed via `AnthropicClient.HttpClient` therefore gives per-attempt visibility without Polly. The SDK retried 529 (overloaded) with backoff. Pure statement of fact for the DECISIONS entry: in Phase 1 either (a) `MaxRetries=0` and the gateway reports `http_attempts=1` with a typed infra failure (simple, nothing hidden), or (b) SDK retries on with a counting handler. Phase 3 (LLM-01) then chooses the single policy; the Phase 1 recommendation is (a) because it keeps cost and attempts exactly attributable while the cap is US$5.

### Pydantic codegen (executed, byte-identical twice, pyright 0 errors, ruff clean)
```bash
uv run datamodel-codegen --input schema/invoice.schema.json --input-file-type jsonschema \
  --output-model-type pydantic_v2.BaseModel --use-annotated --field-constraints \
  --use-standard-collections --use-union-operator --target-python-version 3.12 \
  --use-title-as-name --reuse-model --disable-timestamp \
  --formatters ruff-format ruff-check --output python/src/carimbo_models/generated.py
```
Resulting model: `Party` once (reused for issuer and recipient), both with `extra="forbid"`, `access_key: Annotated[str, Field(description=..., pattern=...)]`, `total_amount: Annotated[str, Field(pattern="^-?[0-9]+\\.[0-9]{2}$")]`, `issue_date: date`.

### Mocked runner transport (executed)
```python
import httpx2
transport = httpx2.MockTransport(lambda req: httpx2.Response(200, json={...}))
async with httpx2.AsyncClient(transport=transport, base_url="http://x",
                              timeout=httpx2.Timeout(180.0, connect=5.0), headers={"X-Api-Key": key}) as c:
    r = await c.post("/eval/extractions", json=payload)
```

### Deterministic DANFE (executed)
```python
d = Danfe(xml=xml_text); d.set_creation_date(datetime(2026, 1, 1, tzinfo=timezone.utc)); d.output(path)
# d.pages_count for the multi-page assertion; pypdfium2 page.get_textpage().get_text_range() for text-layer check
# zxingcpp.read_barcodes(page.render(scale=300/72).to_pil()) -> [('Code128', key)]
```

### Mod-11 oracle (executed against nfelib samples)
```python
def key_dv(k43: str) -> int:           # weights 2..9 cycling right-to-left, ASCII-48 values
    w, s = 2, 0
    for ch in reversed(k43): s += (ord(ch) - 48) * w; w = 2 if w == 9 else w + 1
    r = s % 11; return 0 if r < 2 else 11 - r
```
CNPJ weights `[5,4,3,2,9,8,7,6,5,4,3,2]` then `[6,5,4,3,2,9,8,7,6,5,4,3,2]`, same `r<2 -> 0` rule.

## LLM-06 Spike Protocol (what the live spike must confirm; budget well under US$1)

Run as a small committed console tool (e.g. `dotnet/tools/LlmSpike`, never part of `dotnet test`), reading `CARIMBO_ANTHROPIC_API_KEY` (fallback `ANTHROPIC_API_KEY`), never printing headers, request or key. It writes a sanitised markdown to `docs/spikes/01-llm-gateway.md` (shapes, usage numbers, stop reasons, latencies; no PDF bytes, no secrets). Steps, in order, each producing a recorded observation:

1. **Reachability/auth:** one tiny Haiku request without document (cheap): record response `model` snapshot and `id`, headers present (`request-id`?), `usage` keys.
2. **Haiku 4.5 + model-facing schema + PDF document block** (skeleton case 1): does the API accept `$defs/$ref`, `pattern`, `format: date`, `title`, `$schema`? If a 400 arrives, bisect which keyword and record the fallback. Record `stop_reason`, `usage`, tokens per page (calibrates `max_tokens` and the Haiku 4096-token cache threshold), latency, and whether the text parses with the strict options and yields the right values.
3. **Cache-token confirmation:** two identical requests, `cache_control` on the document, schema byte-identical. On the multi-page case with Haiku (expect creation>0 only if the prefix exceeds 4096 tokens) and/or on Sonnet 5.5 (512 threshold): record `cache_creation_input_tokens`, `cache_read_input_tokens`, the `cache_creation` 5m/1h breakdown.
4. **Sonnet 5.5, once:** explicit `OutputConfig.Effort`, no `Thinking`, no temperature; record acceptance of Format+Effort together, thinking tokens in usage, `max_tokens` sufficiency. Then the 400 checks as separate labelled requests (`thinking: disabled`, non-default `temperature`): record the error bodies; Haiku-only note that Haiku still accepts sampling params but we never send them.
5. **Refusal / truncation paths:** `max_tokens` very small on one request to observe `stop_reason: max_tokens` and the (invalid) text; refusal cannot be forced reliably, so rely on the canned-payload tests and record that explicitly.
6. **Path comparison on the same request:** direct SDK vs IChatClient+raw factory (`WithCacheControl`): same wire body, how usage and `FinishReason` map, whether `request-id` is reachable. Decision rule: choose the direct SDK unless the IChatClient path is lossless on usage decomposition and stop details; record the outcome in a new DECISIONS entry (D-11).
7. **Retry ownership:** with the counting handler, record that a real 4xx (e.g. an invalid request) is not retried and a 5xx/529/429 is; settle `MaxRetries` for Phase 1 and the Phase 3 hand-off.
8. **Capture sanitised golden response bodies** (usage + stop_reason, text replaced) as xUnit fixtures for the pricing and mapping tests.

Spend estimate (all `[ASSUMED]`): Haiku 1-3 pages ~2-8k input tokens at $1/MTok plus ~300 output tokens at $5/MTok is well under $0.02 per call; 20 spike calls including two Sonnet 5.5 calls stay under ~$0.50, leaving most of the US$5 cap for the skeleton run.

## DECISIONS.md entries (RES-03)

`docs/DECISIONS.md` format (read this session): `## D-NN Title` / `**Phase:**` / `**Decision:**` / `**Rationale:**` / `**Rejected:**`, and the header says changes are recorded "as a new entry that supersedes the old one". Existing IDs run D-01..D-17. Proposed new entries (planner discretion on numbers; keep them appended, and add a one-line "Superseded in part by D-NN" under the old entry's heading):
- **D-18 (refines D-03):** canonical schema (rich) exported from C#; pure projector derives the model-facing schema (strip unsupported keywords, `additionalProperties:false`, assert <=24 optional / <=16 union with per-use `$ref` expansion); extraction target is the DANFE-visible projection; Pydantic generated from the canonical schema; staleness checked for all three artifacts; money is a pattern-constrained decimal string.
- **D-19 (refines D-07):** cache key includes a replicate salt; modes read-write / read-only / refresh; hits report original latency and cost; summaries show incurred vs notional cost and hit rate; truncated/refused never cached. (Implementation is Phase 3; the entry records the accepted refinement.)
- **D-20 (refines D-15):** commit `summary.json` plus a compact per-case score table; raw JSONL stays out of git; optional committed cache fixtures for the published run so PR CI and reviewers replay at $0.
- **D-21 (LLM-06 outcome, written after the spike):** gateway shape, bottom adapter choice, retry ownership for Phase 1 and the Phase 3 hand-off, model-id pinning, schema-keyword fallbacks actually needed.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| xUnit v2 + VSTest `dotnet test` | xUnit v3 (`xunit.v3` 4.0.1) on Microsoft.Testing.Platform, opt-in via `global.json` | .NET 10 SDK | Mandatory `global.json` `test.runner`. |
| `.sln` | `.slnx` (default from `dotnet new sln --format slnx`) | .NET 9/10 | Works with `dotnet test`/`build` here. Fine for CI; verify IDE support if a contributor needs `.sln`. |
| Forced `tool_choice` for JSON | `output_config.format` structured outputs | current Sonnet/Opus | 400 on forced tool choice for Sonnet 5.5/Opus 5.5. |
| `httpx` | `httpx2` | 2026 | Active maintainer line; `httpx` has only `1.0.dev*` since Dec 2024. |

**Deprecated/outdated:** `ZXing.Net.Bindings.SkiaSharp` + PDFtoImage (SkiaSharp native clash; N/A in Phase 1); Pillow PDF writer (non-deterministic; N/A in Phase 1).

## Runtime State Inventory

Not applicable: greenfield phase (no rename/refactor/migration). Verified by listing the repo: only `.planning/`, `.claude/`, `docs/`, `LICENSE`.

## Environment Availability

Probed in this container (the executor environment). "Here" = this Claude Code cloud container.

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| dotnet SDK | REPO-01, all .NET | **not on PATH**; obtainable | 10.0.401 via `nix shell nixpkgs#dotnet-sdk_10` (executed, 1.6 s warm) | Use nix shell (`nix` 2.34.6 with flakes enabled, cache.nixos.org reachable). Worth adding a `SessionStart` hook / documented prefix `nix shell nixpkgs#dotnet-sdk_10 nixpkgs#just -c ...` for cloud execution. |
| NuGet (api.nuget.org) | restore | Yes (HTTP 200; restore of Anthropic/MEAI/xunit worked) | - | - |
| Python 3.12 | Python stack | Yes (`/usr/bin/python3.12` 3.12.3; default `python3` is 3.11.15) | 3.12.3 | `uv sync --python 3.12` picks it up. `uv python install` of managed builds is blocked (GitHub 403). |
| uv | Python stack | Yes | 0.8.17 (STACK says 0.12.23) | Full pinned set resolves under 0.8.17; set `[tool.uv] required-version = ">=0.8"` unless a newer feature is needed. |
| PyPI | deps | Yes | - | - |
| just | D-13 | **No** | - | `nix shell nixpkgs#just` (1.58.0) or `uv tool run --from rust-just just` (1.58.0), both executed. |
| ruff / pyright (global) | - | ruff 0.15.20 and pyright present globally | - | Ignore: use pinned versions via `uv run`. |
| Node | pyright wrapper | Yes (24.21.0) | - | - |
| Docker daemon | not needed in Phase 1 | CLI 29.6.2 present, **daemon not running** (`docker ps` fails) | - | None needed; Jaeger/compose are Phase 3. |
| devenv | REPO-03 | not installed here; nixpkgs has 2.4.0 | - | `devenv.nix` can be written but not exercised here; human checkpoint on the author's NixOS-WSL. |
| GitHub REST (`gh api repos/...`) | - | "GitHub access to this repository is not enabled" for repo queries; `git ls-remote` over HTTPS works | - | Use `git ls-remote --tags` (executed). GitHub release/ZIP downloads (python-build-standalone) are 403. |
| api.anthropic.com | live spike | Reachable (dummy-key request returned a typed 401) | - | - |
| `CARIMBO_ANTHROPIC_API_KEY` | live steps | Set (value not read) | - | If absent, live steps blocked, rest proceeds on fakes (D-10). |
| opencode.ai, learn.microsoft.com, builds.dotnet.microsoft.com, cr.jaegertracing.io | docs/downloads | Blocked (000/403) | - | Use nixpkgs for the SDK; docs via search summaries. |

**What can run here vs only in CI / on the author's machine**
- Here (via nix shell + uv): dotnet build/format/test, xUnit tests, SchemaExport, Python lint/type/tests, datagen, endpoint tests with fakes, the live spike and skeleton run (key present), runner/grader.
- Only CI: the GitHub Actions workflows themselves (first push), non-Nix reviewer-path proof on a clean ubuntu runner, byte-identity of regenerated skeleton on the runner's Python.
- Only the author's machine: devenv on NixOS-WSL, native-lib/`nix-ld` concerns (none expected in Phase 1 since no native NuGet packages are used; the Anthropic SDK is managed), OpenCode and Claude Code discovery of the instruction files.

**Missing dependencies with no fallback:** none for planning.
**Missing dependencies with fallback:** dotnet (nix), just (nix/uv), docker daemon (not needed).

## Validation Architecture

(`workflow.nyquist_validation` is `true` in `.planning/config.json`.)

### Test Framework

| Property | Value |
|----------|-------|
| .NET framework | xunit.v3 4.0.1 on Microsoft.Testing.Platform (opt-in in `dotnet/global.json`) |
| Python framework | pytest 9.1.1 (+ pytest-asyncio 1.4.0) |
| .NET config | `dotnet/global.json`, `dotnet/Directory.Build.props` (no separate test config) |
| Python config | `python/pyproject.toml` (`[tool.pytest.ini_options]`, `asyncio_mode = "auto"`; `[tool.pyright]`, `[tool.ruff]`) |
| .NET quick | `cd dotnet && dotnet test` (executed: ~1-2 s per small project after restore) |
| .NET full (`just dotnet-check`) | `dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` |
| Python quick | `cd python && uv run pytest -q` |
| Python full (`just py-check`) | `uv run ruff check . && uv run ruff format --check . && uv run pyright && uv run pytest -q` |
| Contract chain | `just schema && git diff --exit-code -- schema python/src/carimbo_models` |
| Live (opt-in, needs key, costs money) | `just spike-live`, `just skeleton` — never part of default test runs |

### Phase Requirements -> Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| REPO-01 | One command builds, lints, tests .NET | smoke (CI + local) | `just dotnet-check` | Wave 0 |
| REPO-02 | One command lint/type/test Python | smoke | `just py-check` | Wave 0 |
| REPO-03 | Non-Nix path works from clean ubuntu | CI job (same recipes, `setup-dotnet` + `setup-uv`, no Nix) | CI workflow green | Wave 0 |
| REPO-03 | devenv path on NixOS-WSL | manual-only (human checkpoint; cannot run in CI/container) | `devenv shell -- just dotnet-check py-check` by the author | manual |
| REPO-04 | AGENTS.md canonical; CLAUDE.md imports it; commands named in AGENTS.md exist in justfile | unit (pytest or shell) | `uv run pytest python/tests/test_repo_layout.py -q` | Wave 0 |
| REPO-04 | Both runtimes actually discover the file | manual-only (open repo in Claude Code and OpenCode, ask "what is the check command?") | - | manual |
| CI-01 | PR workflow builds+tests both stacks, no secrets, no `pull_request_target` | static (actionlint or grep test) + first PR run | `grep -L pull_request_target .github/workflows/*.yml` in `test_repo_layout.py`; green run on the first PR | Wave 0 |
| DOM-01 | Domain pure | unit | `cd dotnet && dotnet test` (class `DomainPurityTests`; MTP class-filter flag syntax `[ASSUMED]`, plain `dotnet test` is verified) (assembly refs allowlist `System.*`/`netstandard`; csproj has no PackageReference) | Wave 0 |
| DOM-05 | Exported canonical schema equals committed file; export is deterministic | unit | `dotnet test` (`SchemaSnapshotTests`) + `just schema` leaves tree clean | Wave 0 |
| DOM-06 | Projector rules; budget <=24 optional / <=16 union; counter fails on synthetic over-budget schema | unit | `dotnet test` (`ModelSchemaProjectorTests`, `SchemaBudgetTests`) | Wave 0 |
| DOM-07 | Generated models parse golden valid Invoice JSON and reject invalid; pyright clean | unit | `uv run pytest python/tests/test_models.py -q` | Wave 0 |
| DOM-08 | CI fails when any of 3 artifacts stale | CI job + one-off negative proof | `just schema && git diff --exit-code -- schema python/src/carimbo_models`; plan includes a documented deliberate-staleness check | Wave 0 |
| LLM-02 | Cost from versioned table across token classes incl. cache read/write; unknown model -> null not 0 | unit | `dotnet test` (`PricingTests`, recorded usage fixtures from step 8 of the spike) | Wave 0 |
| LLM-02 | Outgoing request schema equals committed model-facing schema (no rewrite) | unit (fake `HttpMessageHandler`) | `dotnet test` (`GatewayWireTests`) | Wave 0 |
| LLM-06 | Spike executed and recorded; DECISIONS entry exists; no secrets in docs | manual-with-key run + automated doc check | `just spike-live` (manual); `just docs-check` (greps `docs/spikes/` for `sk-ant-`, asserts D-21 heading) | manual + Wave 0 |
| EXT-01 | Valid model text -> `Success(Invoice)`; invalid/unknown-field/truncated -> `SchemaInvalid` | unit (fake gateway) | `dotnet test` (`ExtractorTests`) | Wave 0 |
| EXT-02 | refusal, max_tokens, auth/5xx/timeout each map to distinct typed outcomes | unit (fake gateway + counting handler) | `dotnet test` (`OutcomeMappingTests`) | Wave 0 |
| API-03 | Disabled -> 404; no/wrong key -> 401; right key -> 200 incl. typed failures as 200; not Development without flag -> unavailable | integration (`WebApplicationFactory`, fake gateway) | `dotnet test` (`EvalEndpointTests`) | Wave 0 |
| API-03 | Trace id in response is 32 hex and equals inbound `traceparent` trace | integration | `dotnet test` (`TraceIdTests`) | Wave 0 |
| DATA-01 | Same seed twice -> identical bytes; committed files equal regenerated; barcode decodes to the key; text layer present; multi-page case has >=2 pages | unit/integration | `uv run pytest python/tests/test_datagen.py -q`; CI `just datagen && git diff --exit-code -- data` | Wave 0 |
| DATA-07 | No CNPJ in dataset appears in nfelib samples; names carry synthetic marker; key and CNPJ DV valid by independent oracle | unit | `uv run pytest python/tests/test_synthetic_only.py -q` | Wave 0 |
| EVAL-01 | Concurrency bound respected; cost cap stops dispatch; resume skips done cases; one JSONL line per case with raw output; no key in output | unit (httpx2 `MockTransport`) | `uv run pytest python/tests/test_runner.py -q` | Wave 0 |
| EVAL-02 | Grade makes no network calls (monkeypatched socket + import check), is deterministic, >=1 field grade per case, separates typed failures/harness errors | unit | `uv run pytest python/tests/test_grader.py -q` | Wave 0 |
| RES-03 | DECISIONS.md has D-18..D-20 superseding entries | static | `just docs-check` | Wave 0 |
| (cross-cutting) | End-to-end skeleton against a fake model | integration | `uv run pytest python/tests/test_e2e_fake.py -q` (runner -> real ASP.NET host started with fake gateway -> grader) | Wave 0 |
| (cross-cutting) | Live skeleton: 3 cases, real Haiku, graded | manual-with-key | `just skeleton` | manual |

### Sampling Rate
- **Per task commit:** the touched stack's quick command (`dotnet test` in `dotnet/` or `uv run pytest -q` in `python/`), plus `just schema-check` when Domain or the projector changed.
- **Per wave merge:** `just dotnet-check`, `just py-check`, `just schema && git diff --exit-code`, `just docs-check`.
- **Phase gate:** all of the above green in CI on a clean runner, then `just skeleton` once against the real API within the US$5 cap, before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] Entire repo scaffold (justfile, global.json, props, slnx, pyproject/uv.lock, editorconfig, gitattributes, gitignore, CI workflow, AGENTS.md/CLAUDE.md)
- [ ] `dotnet/tests/*` projects with xunit.v3 and `Microsoft.AspNetCore.Mvc.Testing`
- [ ] `python/tests/` including `test_repo_layout.py`, `test_models.py`, `test_datagen.py`, `test_synthetic_only.py`, `test_runner.py`, `test_grader.py`, `test_e2e_fake.py`
- [ ] Fake gateway + counting `HttpMessageHandler` test support (small shared test project or per-project copy)
- [ ] Sanitised recorded-usage fixtures (from spike step 8; until then, hand-built from the documented usage shape, labelled as synthetic)

## Security Domain

(`security_enforcement` is `true`, ASVS level 1, block on high.)

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes (static API key only) | `X-Api-Key` header, fixed-time compare, key from env, endpoint absent when unconfigured |
| V3 Session Management | no | Stateless endpoint |
| V4 Access Control | yes | Route mapped only when `Eval:Enabled`; 404 vs 401 behaviours tested; localhost bind default |
| V5 Input Validation | yes | Strict STJ parse of both request and model output (`Disallow` unmapped, required params), size limit, media type check (`application/pdf`, `%PDF-` magic), base64 validation; typer/Pydantic strict parsing in Python |
| V6 Cryptography | limited | No custom crypto; `FixedTimeEquals`; TLS to Anthropic via the SDK; never hand-roll hashing beyond `SHA-256` for manifests |
| V7/V8 Error handling, data protection | yes | Provider key never logged/echoed/written; no prompt/PDF content in logs; exception messages from the SDK may include response bodies: log type and status only |
| V14 Config | yes | CI: `permissions: contents: read`, no secrets in Phase 1 workflows, `pull_request` only |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Unauthenticated expensive endpoint (free LLM proxy) | Elevation/Denial of wallet | Disabled by default, static key, localhost bind, per-run cost cap in runner, US$5 key limit |
| Provider key leakage via logs, spans, spike docs, JSONL, CI | Information disclosure | Explicit `ApiKey` injection, no header logging, `just secrets-check` grep, CI has no secrets in this phase |
| Prompt injection via DANFE text | Tampering | Prompt treats document as data; schema-constrained output; typed outcome; (injection dataset cases are Phase 4+) |
| Oversized/malformed upload | Denial of service | Body size limit, magic-byte check, base64 decode guarded |
| Timing attack on key compare | Information disclosure | `CryptographicOperations.FixedTimeEquals` |
| Supply chain (new/obscure packages) | Tampering | Exact pins + lockfiles, single human-verify checkpoint for first `uv sync`, `Directory.Packages.props` pinning |
| Fork PR secret exfiltration | Information disclosure | No `pull_request_target`; no secrets in Phase 1 CI |
| Synthetic CNPJ colliding with a real company | Spoofing/legal | Visible synthetic marker in names; documented limitation (check-digit-valid by construction) |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | API accepts the model-facing schema with `$defs/$ref`, `pattern`, `format: date`, `title`, `$schema` for Haiku 4.5 | Pattern 3 | Spike finds 400; fall back to stripping/moving keywords (planned) |
| A2 | Alias `claude-haiku-4-5` resolves to the 2025-10-01 snapshot | Pitfall 8 | Pinning/recording logic changes (low) |
| A3 | Thinking tokens are included in `usage.output_tokens` and billed as output | Pattern 4 | Cost under-reported for Sonnet 5.5 runs (spike confirms) |
| A4 | Structured outputs + adaptive thinking + effort combine in one Sonnet 5.5 request | Pitfall 9 | Sonnet run needs a different config; Phase 6 impact |
| A5 | One skeleton PDF page is ~2-3k tokens (text+image) | Pitfall 6 | Cache confirmation plan shifts models |
| A6 | Invalid-parameter 400s consume no tokens | Pitfall 9 | Trivial cost |
| A7 | OpenCode ignores CLAUDE.md when AGENTS.md exists (from search summaries; docs egress-blocked) | Pitfall 10 | AGENTS.md self-sufficiency is cheap insurance either way |
| A8 | pyright wrapper downloads Node on machines without it | Pitfall 13 | Reviewer-path doc line |
| A9 | uv-managed Python yields the same PDF bytes as system/Nix Python | Pattern 8 | CI byte-identity check fails; mitigation `set_compression(False)` or regenerate-and-commit from CI's interpreter |
| A10 | BrazilFiscalReport on PyPI is the Engenere upstream project (seam shows no repo URL) | Package Audit | Supply-chain risk; human-verify checkpoint planned |
| A11 | `number` and `series` as `int` (not string) in Invoice | Open Question 2 | Leading-zero printing differences; revisit in Phase 2 |
| A12 | devenv.nix works on NixOS-WSL without extra `nix-ld` for this phase (no native NuGet packages used) | Environment | Author hits linker error; add `programs.nix-ld` |
| A13 | xUnit v3 MTP trait filter syntax for excluding Live tests (not needed if the spike is a console tool) | Spike Protocol | None if console tool is used |

## Open Questions (RESOLVED)

Resolutions recorded by the planner on 2026-10-04. Each item names the plan that implements it.

1. **What does "static API key ... without the key the endpoint is unavailable" mean exactly?**
   - Known: D-10 describes the *Anthropic* key; success criterion 4 says "with the static API key" and "without the key, or outside dev/eval, the endpoint is unavailable".
   - Unclear: whether one key or two (Anthropic provider key vs a caller-facing eval key).
   - Recommendation: two keys. `CARIMBO_EVAL_API_KEY` (caller auth, required to map the route) and the Anthropic key (missing -> typed `InfrastructureFailure(NotConfigured)` so fakes still work). State it in AGENTS.md and the API test matrix.
   - RESOLVED: two keys. `CARIMBO_EVAL_API_KEY` is the caller key (`X-Api-Key`, fixed-time compare). The provider key resolves per D-10 (`CARIMBO_ANTHROPIC_API_KEY`, then `ANTHROPIC_API_KEY`). Locked decision D-10 says "without a key ... the endpoint is unavailable", so the route is not mapped (404) when no model gateway is available, instead of returning a typed NotConfigured failure as research suggested. Tests and the e2e host inject a scripted or stub `ILlmGateway`, so the endpoint shape stays testable without a key. Plans 01-03, 01-08, 01-12.
2. **`number`/`series` as `int` or `string`?** Access key embeds them zero-padded; the DANFE prints unpadded. Recommend `int` for Phase 1 (grading by value; string-vs-int compare bugs are Pitfall 2 in project research), revisit with DOM-03/04 in Phase 2.
   - RESOLVED: `int` in Phase 1 (assumption A11), graded by value; revisit in Phase 2. Plan 01-02.
3. **Alphanumeric CNPJ in one skeleton case?** Verified to render and round-trip. D-06 does not require it. Recommend yes for one case (cheap early proof, exercises the pattern and the printed-form normalisation), flag as optional; the planner/user can decline without affecting any criterion.
   - RESOLVED: yes. case-002 uses an alphanumeric issuer CNPJ, so its access key is alphanumeric too. case-001 is numeric and single-page; case-003 is numeric and multi-page. All three are clean text-layer PDFs under one tax regime, consistent with D-06. Plans 01-06, 01-07.
4. **Gateway default (direct SDK vs IChatClient+raw factory).** Evidence leans direct; CONTEXT says the spike decides and names B as the prior. The plan must treat this as a spike outcome, not a pre-decision, but should schedule the spike *before* the gateway implementation task so only one implementation is written.
   - RESOLVED: the spike (plan 01-10) runs before any adapter exists. The tracer uses a scripted `ILlmGateway` under `dotnet/tests/Carimbo.ScriptedHost`. The spike ends at a `checkpoint:decision` (direct-sdk / ichatclient-raw / direct-sdk-retries) informed by the recorded evidence and research's decision rule. Plan 01-12 implements only the chosen adapter and records D-21.
5. **Model-id pinning (alias vs dated snapshot)** — decided from spike step 1 evidence.
   - RESOLVED: requests send the alias (`claude-haiku-4-5`), and every response and JSONL record carries both `model` requested and `model_returned`. The developer chooses at the 01-10 checkpoint whether to pin the dated snapshot, and D-21 (plan 01-12) records the choice. The pricing table maps the snapshot to its base model through `aliases`.
6. **Where does `global.json` live (root vs `dotnet/`)?** Recommend `dotnet/global.json` plus `global-json-file: dotnet/global.json` in `setup-dotnet` (verified majors: `actions/setup-dotnet` v6). Planner confirm.
   - RESOLVED: repo root, not `dotnet/`. The dotnet CLI resolves global.json from the working directory upward. Every verify, just and CI command runs from the repo root, so a root file applies the SDK pin and the Microsoft.Testing.Platform opt-in everywhere, including `cd dotnet && dotnet test`. CI uses `global-json-file: global.json`. Plans 01-02, 01-13.
7. **GitHub Actions pins.** `git ls-remote --tags` shows latest majors: `actions/checkout` v7, `actions/setup-dotnet` v6, `astral-sh/setup-uv` v7, `actions/upload-artifact` v7, `extractions/setup-just` v4 `[VERIFIED: git ls-remote, 2026-10-04]`. Pin to majors (or SHAs) at scaffold time.
   - RESOLVED: pin to the verified majors (checkout@v7, setup-dotnet@v6, setup-uv@v7, setup-just@v4). upload-artifact is not needed in Phase 1. SHA pinning can follow with the Phase 6 gate. Plan 01-13.
8. **Cloud-execution ergonomics:** dotnet/just are not on PATH here; the plan's verify commands must be prefixed with `nix shell nixpkgs#dotnet-sdk_10 nixpkgs#just -c` (or a `SessionStart` hook added) when executed in this container.
   - RESOLVED: every .NET and just verify command uses the `nix shell nixpkgs#dotnet-sdk_10 [nixpkgs#just] -c` prefix, verified by the planner on 2026-10-04 (dotnet 10.0.401, just 1.58.0). Python commands use `uv run --project python ...` from the repo root, which keeps relative paths rooted there. The existing SessionStart hook is not changed.

## Sources

### Primary (HIGH confidence)
- Executed in this session: dotnet SDK 10.0.401 via nixpkgs; xUnit v3 + MTP opt-in (success and quoted failure); `JsonSchemaExporter` export + hoist; strict STJ deserialization tests; `Anthropic` 12.53.0 and `Microsoft.Extensions.AI` 10.10.0 compiled and run against a fake `HttpMessageHandler` (wire bodies, usage, stop reasons, retry counts); ASP.NET Core `Activity.Current` trace ID; uv + all pinned Python deps; datamodel-code-generator 0.83.0 determinism; pyright/ruff; httpx2 2.13.1 `MockTransport`; BrazilFiscalReport 1.2.0/nfelib 3.0.0/pypdfium2/zxing-cpp rendering, barcode decode, determinism across 3 interpreters, alphanumeric key round trip; mod-11 oracle vs nfelib samples.
- NuGet flat container and PyPI JSON registries (2026-10-04).
- `git ls-remote --tags` for GitHub Action majors (2026-10-04).
- Anthropic pricing page https://platform.claude.com/docs/en/about-claude/pricing (table and multipliers quoted).
- Anthropic structured outputs page https://platform.claude.com/docs/en/build-with-claude/structured-outputs (supported models, limits, counting wording, refusal/max_tokens, cache invalidation by format change).
- `claude-api` skill reference files (bundled, 2026-09-25): `csharp/claude-api/README.md`, `csharp/claude-api/tool-use.md`, `shared/prompt-caching.md` (minimum cacheable prefix table, usage-field semantics).
- Repo inputs read this session: CONTEXT.md, REQUIREMENTS.md, STATE.md, ROADMAP.md, `.planning/research/{SUMMARY,STACK,ARCHITECTURE,PITFALLS}.md`, `docs/DECISIONS.md`, `.planning/config.json`, `.claude/CLAUDE.md`.

### Secondary (MEDIUM confidence)
- https://code.claude.com/docs/en/memory (AGENTS.md / CLAUDE.md / `@` import behaviour).
- Web search summary of OpenCode rules precedence (opencode.ai blocked; https://opencode.ai/docs/rules/ cited by the search result).

### Tertiary (LOW confidence)
- Token-count and spend estimates for the spike; live-API acceptance of specific schema keywords (spike resolves).

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - every Phase 1 package resolved and exercised here.
- Architecture: HIGH for the contract chain, datagen, endpoint gating and runner design; MEDIUM for the gateway choice (spike pending).
- Pitfalls: HIGH for the six found by execution; MEDIUM for live-API ones.

**Research date:** 2026-10-04
**Valid until:** ~2026-10-18 for SDK/versions (`Anthropic` ships weekly, `Microsoft.Extensions.AI` monthly); 30 days for the rest. Re-pin deliberately, in dedicated commits.
