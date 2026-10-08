<!-- GSD:project-start source:PROJECT.md -->

## Project

**carimbo**

carimbo turns Brazilian electronic invoices (NF-e), received as DANFE PDFs, into strictly typed invoice records, and later into typed approve / reject / escalate decisions. It is a portfolio project. It shows a payments engineer applying production rigor to LLM agents in a regulated fintech setting, and backs that with evidence: structured outputs, deterministic validators, a reproducible eval suite, durable execution, idempotency, tracing and cost attribution.

The audience is reviewers hiring for senior AI platform roles (e.g. an "Operational AI / Agents Platform" team). They should be able to clone the repo, start local services with one command, process a sample invoice, run the eval suite and read measured results.

**Core Value:** **Measured quality.** A reproducible eval suite that exercises the real pipeline, compares runs, gates CI on regressions and publishes a results table. The evidence is the product. When tradeoffs arise, choose whatever keeps quality measurable and reproducible.

### Constraints

- **Tech stack:** .NET 10 (LTS), Python 3.12+, ASP.NET Core minimal APIs, PostgreSQL, OpenTelemetry with a local viewer (e.g. Jaeger), xUnit; Python uses uv, ruff, pyright or mypy, pytest, typer, pandas — decided in the brief
- **Model provider:** Anthropic models, comparing at least two — decided
- **Needs research before planning:** .NET model SDK, `ModelContextProtocol` C# SDK, JSON Schema to Pydantic codegen, DANFE rendering and barcode libraries — these ecosystems move fast; verify against current docs
- **Dev environment:** NixOS-WSL with project-specific devenv; must also work for an external reviewer
- **Agent runtimes:** Claude Code and OpenCode, both supported
- **Budget:** modest API spend — response cache mandatory for dev and eval reruns; CI uses a small dataset subset
- **Language:** English for code, docs and commit messages
- **Timeline:** no hard deadline; size phases for quality

<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->

## Technology Stack

## How to Read the Confidence Tags

- **HIGH (verified):** version read from the registry on 2026-10-03, or behaviour I executed myself (spikes below, run with nixpkgs `dotnet-sdk` 10.0.401 and Python 3.12 via uv on Linux x64).
- **MEDIUM:** official vendor docs or source fetched, but not executed here, or a design judgement.
- **LOW:** web-search summary only. Verify in the phase.
- Per the `classify-confidence` seam, web search and web fetch alone score LOW/MEDIUM. I upgrade a claim to HIGH only when I ran it or read the registry directly.

## Resolved Research Items (the brief's open questions)

| Brief item | Decision | Confidence |
|---|---|---|
| .NET model SDK | **Official `Anthropic` NuGet package (12.53.0), used through its `IChatClient` implementation (`AsIChatClient`), behind our own `ILlmGateway`.** Both, but one package and one provider. | HIGH |
| MCP C# SDK | `ModelContextProtocol.AspNetCore` **2.2.0**, separate `Tools` host, Streamable HTTP, **stateless**, read-only tools. | HIGH |
| C# to JSON Schema | **`System.Text.Json.Schema.JsonSchemaExporter`** (built in). Not NJsonSchema. Do NOT use `JsonSerializerDefaults.Web` (it corrupts the schema, see Pitfalls). | HIGH |
| JSON Schema to Pydantic | **`datamodel-code-generator` 0.83.0**, run with pinned flags and a stale check in CI. | HIGH |
| DANFE rendering (Python) | **`BrazilFiscalReport` 1.2.0 + `nfelib` 3.0.0** (XML in, DANFE PDF out). `reportlab` 5.0.1 only for the degraded-scan wrapper. Not WeasyPrint. | HIGH (works, deterministic) |
| Code 128 generation | Comes free with BrazilFiscalReport (python-barcode). Self-check with `zxing-cpp` in the generator. | HIGH |
| Degraded scans | **Pillow 12.3.0 + numpy 2.5.3 + pypdfium2 5.13.0**, output as an **image-only PDF** (no text layer). | HIGH |
| .NET barcode decode | **`ZXingCpp` 0.5.3** (official zxing-cpp .NET wrapper). `ZXing.Net` only as a managed fallback. | HIGH |
| .NET PDF rasterization | **`PDFtoImage` 5.4.0** (PDFium + SkiaSharp). | HIGH |
| Local services | **docker compose is the canonical one-command path.** devenv provides the toolchain only. **Not .NET Aspire.** | MEDIUM |
| OpenTelemetry + viewer | OpenTelemetry .NET **1.19.x**, OTLP direct to **Jaeger v2** (container). | HIGH (versions), MEDIUM (Jaeger image tag) |
| Python tooling | uv 0.12.23, ruff 0.16.10, pyright 1.1.414, pytest 9.1.1, typer 0.27.2, pandas 3.0.6 | HIGH |

## Recommended Stack

### Core Technologies: .NET production pipeline

| Technology | Version | Purpose | Why Recommended |
|---|---|---|---|
| `Anthropic` (NuGet) | **12.53.0** (2026-09-30) | Claude Messages API client | Official SDK since v10; the old community package now lives at `tryAGI.Anthropic`. GA, netstandard2.0+. Ships a full `IChatClient` adapter. Releases weekly, so **pin exactly** and bump deliberately. |
| `Microsoft.Extensions.AI` | **10.10.0** (+ `.Abstractions` 10.10.1, transitive) | `IChatClient` seam, `UseOpenTelemetry`, `DelegatingChatClient` | `Anthropic` already depends on Abstractions. Gives a fake-model test seam (D-08), OTel gen_ai spans, and the type MCP tools plug into in M2. |
| `ModelContextProtocol.AspNetCore` | **2.2.0** (2026-08-13) | MCP server over Streamable HTTP (M2) | Official C# SDK, maintained with Microsoft. 2.x aligns with MCP spec 2026-07-28. Stateless by default. Verified to serve a legacy `initialize` handshake. |
| `System.Text.Json` + `JsonSchemaExporter` | in-box (.NET 10) | Export JSON Schema from `Domain` records | Same contract as the deserializer that parses model output, so the schema describes exactly what is accepted. Zero dependency keeps `Domain` pure (D-04). |
| `PDFtoImage` | **5.4.0** (2026-08-16, MIT, targets net10.0) | Rasterize DANFE page 1 for barcode decode | Only maintained .NET PDFium rasterizer. Brings PDFium 152 + SkiaSharp 4.150.1 natives. |
| `ZXingCpp` | **0.5.3** (2026-07-29, Apache-2.0, net5.0+/netstandard2.0) | Decode the Code 128 access key (D-05) | Native zxing-cpp. Beat ZXing.Net in my sweep (see spikes). Same engine as the Python `zxing-cpp` used in datagen. |
| OpenTelemetry .NET | **1.19.1** (`OpenTelemetry.Extensions.Hosting`, `...Exporter.OpenTelemetryProtocol`), **1.19.0** (`...Instrumentation.AspNetCore`, `.Http`, `.Runtime`) | Traces, one span per model call (D-17) | Standard. Export OTLP directly, no collector needed in M1. |
| xUnit v3 (`xunit.v3`) | **4.0.1** (2026-09-12) | .NET tests | The xUnit line is v3 now (v2 stopped at 2.9.3 in Jan 2025). Runs on Microsoft.Testing.Platform. See Pitfall 9. |

### Core Technologies: Python eval and data

| Technology | Version | Purpose | Why Recommended |
|---|---|---|---|
| Python | 3.12 (`requires-python = ">=3.12"`) | Runtime | numpy 2.5.3 requires >=3.12; pandas 3.0.6 requires >=3.11. 3.12 is also nixpkgs `python312`. |
| uv | **0.12.23** | Env, lockfile, runner | Decided. Commit `uv.lock`. Everything (datagen output, codegen formatting) depends on pinned versions. |
| ruff | **0.16.10** | Lint + format | Decided. Also formats the generated Pydantic file, so pin it (see Pitfall 6). |
| pyright | **1.1.414** | Type check | Pick pyright over mypy (2.4.0): faster, better on Pydantic/pandas-stubs, same engine as most editor LSPs. nixpkgs ships the same 1.1.414. mypy is an acceptable swap. |
| pytest | **9.1.1** (+ `pytest-asyncio` 1.4.0) | Tests | Decided. |
| typer | **0.27.2** | CLI (`evals run`, `compare`, `datagen build`) | Decided. |
| pandas | **3.0.6** | Aggregation and `compare` | Decided. v3 means Copy-on-Write and the new string dtype by default, so write code against v3 from day one. |
| pydantic | **2.13.5** | Models for eval records and the generated invoice schema | Target of datamodel-code-generator. |
| `httpx2` | **2.13.1** | Async HTTP client from the runner to the .NET eval endpoint | Pydantic-stewarded continuation of httpx. `httpx` itself has had no stable release since 0.28.1 (Dec 2024). MEDIUM, see note below. |

### Data generation (Python)

| Library | Version | Purpose | When to Use |
|---|---|---|---|
| `nfelib` | **3.0.0** (MIT) | Official NF-e 4.00 XSDs (285 files) + xsdata-generated typed bindings | Build the synthetic `{case}.xml` as typed objects and validate against the official XSD. Ground truth is schema-valid by construction. Use its bundled sample XMLs only as structural reference, never as dataset (D-14). |
| `BrazilFiscalReport` | **1.2.0** (LGPL-3.0; fpdf2 + python-barcode) | XML to DANFE PDF with Code 128 barcode | Primary renderer. Realistic layout, multi-page item overflow verified (70 items gave 3 pages). Use as a pip dependency only (LGPL is fine that way). |
| `fpdf2` | 2.8.9 (transitive) | PDF engine under BrazilFiscalReport | Not used directly. Call `set_creation_date()` for reproducible bytes. |
| `reportlab` | **5.0.1** | Wrap degraded rasters into an image-only PDF | `Canvas(invariant=1)` yields byte-identical output (verified). Pillow's own PDF writer is NOT deterministic (verified). |
| `Pillow` | **12.3.0** | Rotate, blur, downscale, JPEG-compress | Whole degradation chain. No OpenCV needed. |
| `numpy` | **2.5.3** | Seeded noise (`default_rng(seed)`) | See Pitfall 7 on cross-version stability. |
| `pypdfium2` | **5.13.0** | Rasterize clean DANFE for degradation | Same PDFium as the .NET side. |
| `zxing-cpp` | **3.1.1** | Label barcode readability in datagen | Same engine as .NET `ZXingCpp`. 20 of 20 sweep cases agreed with .NET. |
| `lxml` | 6.1.3 | XML handling / XSD validation | If not going through nfelib's own parsing. |
| `Faker` | 40.40.0 (`pt_BR`) | Fictitious names/addresses | Seeded. Generate CNPJs yourself so check digits are under your control. |
| `jsonschema` | 4.26.0 | Validate ground truth against the committed schema | Satisfies the Phase 2 criterion literally (Draft 2020-12). |
| `matplotlib` | 3.11.2 | README charts | Reports phase. |
| `anthropic` (Python) | 1.11.0 | LLM-as-judge only (M2, D-16) | Not needed in M1. The pipeline calls models from .NET only (D-02). |

### Database

| Technology | Version | Purpose | Why |
|---|---|---|---|
| PostgreSQL | 17 or 18 image (nixpkgs has 17.11 and 18.6) | Reference data (M2), idempotency tables (M3) | **Not needed in M1.** Do not wire it into M1 code. Provision in compose only when M2 starts. |
| `Npgsql` + `Npgsql.OpenTelemetry` | 10.0.3 | .NET driver + tracing | M2+. Skip an ORM for the first slice, add Dapper (2.1.89) or EF only if M3 demands it. |

### Infrastructure and local services

| Technology | Version | Purpose | Why |
|---|---|---|---|
| docker compose (`compose.yaml`) | n/a | **Canonical one-command local services** | The only thing an external reviewer reliably has. Same file serves Jaeger (M1), Postgres (M2), Temporal (M3). |
| Jaeger v2 image `cr.jaegertracing.io/jaegertracing/jaeger` | **2.x** (2.20 as of Jul 2026, per web search; confirm tag at build time) | Local trace viewer | Matches the brief. v1 is end of life (2025-12-31). OTLP on 4317/4318, UI on 16686. Deep link per eval record: `http://localhost:16686/trace/{traceId}`. |
| devenv (`devenv.nix`) | current | Toolchain only: `dotnet-sdk_10` (10.0.401), `python312`, `uv`, `ruff`, `pyright`, docker CLI, `just` | `languages.dotnet` exists. Do NOT use devenv `services.*` for the viewer: devenv has no Jaeger module and **nixpkgs has no `jaeger` package** (checked). |
| GitHub Actions | n/a | CI | `setup-dotnet` driven by `global.json`; `setup-uv` with lockfile cache. Pin action versions at scaffold time. |

### Development Tools

| Tool | Purpose | Notes |
|---|---|---|
| `global.json` | Pin .NET SDK (10.0.x) and select the test runner | Required for xUnit v3 / MTP on `dotnet test` (Pitfall 9). |
| Central Package Management (`Directory.Packages.props`) | One version per package across projects | Directly prevents the SkiaSharp native/managed mismatch (Pitfall 2). |
| `tools/SchemaExport` (console) + xUnit staleness test | Export and verify `invoice.schema.json` | Test regenerates the schema in memory and asserts byte equality with the committed file. CI also runs `git diff --exit-code` after the Python codegen. |
| `AGENTS.md` canonical, `CLAUDE.md` containing `@AGENTS.md` | Both agent runtimes | MEDIUM. Convention, not verified here. |

### Supporting Libraries (.NET)

| Library | Version | Purpose | When to Use |
|---|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | 10.0.12 | OpenAPI document for the eval endpoint | Built in for minimal APIs. Skip Swashbuckle. |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.12 | `WebApplicationFactory` integration tests | Eval endpoint tests with a fake `IChatClient`. |
| `Temporalio` + `Temporalio.Extensions.OpenTelemetry` | 1.20.0 | M3 | Forward-compat only. Nothing to add in M1. |
| `Aspire.*` | n/a | Not used | See Alternatives. |

## The .NET Model SDK Decision in Detail

- **Test seam (D-07, D-08).** A hand-written fake `IChatClient` makes the repair loop, the cache and the M2 agent loop trivially testable without network.
- **M2 forward-compat.** The SDK docs show MCP client tools (`McpClientTool` is an `AIFunction`) used directly through `IChatClient`. The hand-written agent loop (D-08) can still be explicit while sharing tool types.
- **OTel for free.** `.UseOpenTelemetry(sourceName: "Carimbo.Llm")` emits `gen_ai.*` spans (verified). Prompt content is not recorded unless `EnableSensitiveData` is set. Leave it off so base64 PDFs never reach spans.
- **The adapter covers what M1 needs (source read, MEDIUM-HIGH):**
- Cost needs a **versioned pricing table** keyed by model (token classes: input, output, cache-read, cache-write). Never hard-code prices in code.
- **Request-hash cache (D-07)**: implement as your own `DelegatingChatClient`, not the built-in `UseDistributedCache`. You need to control exactly what is in the key (model, effort/thinking, schema hash, prompt version, PDF SHA-256) and to record `cache_hit` plus the *original* cost, so a cached eval rerun reports the cost it would have had. Store as human-readable JSON files under a git-ignored `.cache/llm/`. MEDIUM (design judgement).
- **Retries (D-07): use the SDK's built-in retry** (`MaxRetries`, honours `Retry-After` and server `x-should-retry`; retries 408/409/429/5xx and connection errors). Do not stack Polly or `Microsoft.Extensions.Http.Resilience` on top: that multiplies attempts. Add only a concurrency limiter and a timeout in the gateway. Each HTTP attempt shows up as a child span via `OpenTelemetry.Instrumentation.Http`.
- Forced `tool_choice` (`any`/`tool`) returns **400** on Sonnet 5.5, Opus 5.5 and Fable 5.1. So "extraction via forced tool call" is dead. **Use structured outputs (`output_config.format` = JSON Schema)**.
- `temperature`/`top_p`/`top_k` non-default values return **400** on Sonnet 5.5 and Opus 5.5 (Haiku 4.5 still accepts them). You cannot pin temperature 0, so **model output is not reproducible run to run**. The response cache is the reproducibility mechanism, which makes D-07 load-bearing, not an optimization. Never set `Temperature` in `ChatOptions` for these models.
- Opus 5.5: thinking cannot be disabled, effort defaults to `medium` (set it explicitly). Sonnet 5.5: `thinking: disabled` is a 400; use `between_tools` or leave thinking on at low effort. Both go in the cache key.
- Model IDs (exact, no date suffix): `claude-sonnet-5-5` ($2/$10 per MTok), `claude-opus-5-5` ($4/$20), `claude-haiku-4-5` ($1/$5, 200K context). Sonnet 4.5 is deprecated (EOL 2026-11-30), so do not pick it.
- Candidate first comparison (open question, recommendation only): **Sonnet 5.5 vs Haiku 4.5** for the cost-quality frontier, with Opus 5.5 as an optional ceiling run. Verify structured-output support on Haiku 4.5 before committing. MEDIUM.
- PDF input: each page goes to the model as image plus extracted text; max 32 MB / 600 pages (100 if context < 1M). Roughly 1k tokens per 3 pages text-only, around 7k per 3 pages with images, so budget about 2k+ tokens/page.
- Structured-output schema limits to design for (HIGH, official docs): no `minimum/maximum/minLength/maxLength/multipleOf/maxItems/uniqueItems`, no `oneOf`, no recursion, `additionalProperties` must be false, `minItems` only 0/1, **max 24 optional properties and max 16 union-typed properties (`anyOf` or `["string","null"]`) across the schema**. See Pitfall 3.

## JSON Schema Export and Pydantic Codegen (verified end to end)

## Barcode and PDF on the .NET side (verified)

## Verified by Running It (spikes, 2026-10-03)

## Installation

# .NET (central package management; pin exactly)

# M2 (later)

# Python (pyproject.toml; commit uv.lock)

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|---|---|---|
| `Anthropic` via `IChatClient` + own gateway | Raw `AnthropicClient` only | If you need a beta-only feature the adapter does not expose; use `RawRepresentationFactory` first, drop to raw only for that call. |
| Same | `Microsoft.Extensions.AI.OpenAI` pointed at Anthropic | Never. Wrong provider and loses structured-output mapping. |
| Same | `Anthropic.SDK` (community 5.10.0), `tryAGI.Anthropic` | Never. Unofficial and superseded. |
| Same | Semantic Kernel / Microsoft Agent Framework | Out of scope by D-08. |
| `JsonSchemaExporter` | `NJsonSchema` 11.6.1 | If you need a schema for a type that is not driven by System.Text.Json (Newtonsoft attributes, TypeScript/C# codegen). Here the deserializer is STJ, so the exporter is more faithful and dependency-free. |
| `datamodel-code-generator` | Hand-written Pydantic | Never; violates D-03. |
| `BrazilFiscalReport` DANFE | Custom `reportlab` layout | If a layout variation the library cannot produce is needed (e.g. a deliberately malformed DANFE). Keep as the escape hatch. Also the fallback if BrazilFiscalReport drops multi-page support. |
| `BrazilFiscalReport` | WeasyPrint 70.0 | Not recommended: HTML/CSS layout of a dense fiscal form is more work than the XML-to-PDF library, it needs Pango/Cairo system libraries (NixOS friction), and there is no native Code 128 (you would embed an SVG). Reproducibility not tested. |
| `ZXingCpp` | `ZXing.Net` 0.16.11 | If a native dependency is unacceptable. It is pure managed (but still needs PDFium natives to rasterize). Expect a lower read rate. Do not combine `ZXing.Net.Bindings.SkiaSharp` with `PDFtoImage` without pinning SkiaSharp (Pitfall 2). |
| `PDFtoImage` | `Docnet.Core` 2.6.0 (last stable 2023), `PdfiumViewer` | Never; stale. |
| docker compose + devenv toolchain | `devenv` services (`services.postgres`, `services.temporal`, `services.opentelemetry-collector` exist) | If every dev machine is Nix. Not true for the external reviewer, and Jaeger has no devenv module or nixpkgs package. |
| docker compose | .NET Aspire 13.6 AppHost | Reject for this repo: adds an AppHost project plus Aspire CLI/DCP tooling, still needs Docker, does not model the Python harness or Temporal natively, and hides the "one transparent command" the reviewer wants. Use the standalone **Aspire Dashboard container** (`mcr.microsoft.com/dotnet/aspire-dashboard`, OTLP gRPC only, in-memory) only as a Jaeger substitute. |
| Jaeger v2 | Aspire Dashboard container, Grafana LGTM | Aspire Dashboard if you want logs and metrics in the same UI. Jaeger wins on trace-ID deep links and the brief's wording. |
| pyright | mypy 2.4.0, `ty` 0.0.84 (beta), pyrefly 1.3.2 | mypy if you want zero Node dependency in the PyPI wrapper. Skip ty (beta). |
| `httpx2` | `aiohttp` 3.14.3 | If you want a mature non-httpx stack. Either works for a bounded-concurrency runner. |
| `xunit.v3` | NUnit, TUnit | Locked: xUnit. |

## What NOT to Use

| Avoid | Why | Use Instead |
|---|---|---|
| `JsonSerializerDefaults.Web` for schema export | Sets `AllowReadingFromString`, so every number becomes `["string","number"]` with a regex pattern. Breaks the schema and burns the 16-union budget (observed in spike). | Explicit options as shown above. |
| Forced `tool_choice` for extraction | 400 on Sonnet 5.5 / Opus 5.5 / Fable 5.1. | Structured outputs via `output_config.format`. |
| Setting `Temperature`/`TopP` | 400 on the current Sonnet/Opus. | Omit; rely on the response cache for reproducibility. |
| Assistant prefill to force JSON | 400 on the current models. | Structured outputs. |
| Polly/`Http.Resilience` retries on top of the SDK | Double retry multiplication, muddier cost accounting. | SDK `MaxRetries` + gateway-level concurrency limit. |
| `Microsoft.Extensions.AI` built-in `UseDistributedCache` as the D-07 cache | Key composition is not under your control and it does not record original cost. | Custom `DelegatingChatClient`. |
| `ZXing.Net.Bindings.SkiaSharp` next to `PDFtoImage` | SkiaSharp native/managed version clash (reproduced). | `ZXingCpp`, or pin SkiaSharp + NativeAssets together. |
| Alpine-based .NET images (later, for Docker) | PDFium/Skia natives here are glibc builds. | `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian). LOW-MEDIUM, verify when containerizing. |
| `FluentAssertions` 8.x | Commercial license since v8. | xUnit `Assert`, or `AwesomeAssertions` 9.6.0 (free fork). |
| Pillow `save(format="PDF")` for the dataset | Embeds timestamps, so PDFs are not reproducible (verified). | reportlab `invariant=1` wrapping a JPEG. |
| Text-layer PDFs for "degraded scan" cases | The model reads the clean text layer and the degradation tests nothing. | Image-only PDFs (Pitfall 4). |
| Aspire AppHost as the orchestrator | See Alternatives. | compose. |
| `httpx` 0.28.1 | No stable release since Dec 2024. | `httpx2`. |
| Swashbuckle | Not needed with built-in OpenAPI in .NET 10. | `Microsoft.AspNetCore.OpenApi`. |
| `pyzbar`, `python-barcode` for reading/validation | Unmaintained since 2022 / write-only. | `zxing-cpp`. |

## Stack Patterns by Variant

- Provide a `compose.yaml` that includes Jaeger and (optionally) a profile that builds and runs the Api and the Python harness in containers.
- Because the DoD says "clone, start local services with one command, process a sample invoice". Nix is the author's convenience, not a prerequisite.
- Expose Streamable HTTP, stateless, on `/mcp` from a dedicated `Tools` host; optionally add a stdio entry point reusing the same tool classes (`WithStdioServerTransport()`).
- Because both clients support remote HTTP MCP servers. Stateless matches SDK 2.x defaults, and the read-only tools (D-09) need no session. Set `AllowedHosts` explicitly to avoid DNS-rebinding exposure. Client config syntax for each runtime was not verified (MEDIUM). Since 2.2.0 `SessionMode = StatefulForInitializeClients` can serve old handshake clients with real sessions if one is found that needs it.
- Run the subset against the response cache committed as a cache artifact, or against a fixed small dataset with `claude-haiku-4-5`.
- Because Sonnet/Opus outputs are not reproducible without a cache (no temperature control).
- Commit ground-truth XML and a manifest of per-file SHA-256 for XML and metadata; treat rasterized (image-only) PDFs as build artifacts keyed by seed + pinned versions.
- Because PDFium anti-aliasing and font hinting can differ by platform, and 150 image-only PDFs are tens of MB.

## Version Compatibility

| Package A | Compatible With | Notes |
|---|---|---|
| `Anthropic` 12.53.0 | `Microsoft.Extensions.AI.Abstractions` >= 10.5.1 | Floor, not pin. `Microsoft.Extensions.AI` 10.10.0 resolves Abstractions 10.10.x fine. |
| `PDFtoImage` 5.4.0 | SkiaSharp + natives **4.150.1** (PDFium 152) | Do not float SkiaSharp. Managed and native versions must match or `libSkiaSharp` refuses to load. |
| `ZXing.Net.Bindings.SkiaSharp` 0.16.24 | SkiaSharp 4.151.1 | Conflicts with PDFtoImage's 4.150.1 natives. Avoid this pair. |
| `ModelContextProtocol.AspNetCore` 2.2.0 | `ModelContextProtocol` 2.2.0 exact | The AspNetCore package pins the same-version core (`[2.2.0, 2.2.0]`). Upgrade all three together. |
| `Npgsql.OpenTelemetry` 10.0.3 | `OpenTelemetry.API` 1.15.3 | Lower than the 1.19.x core; fine, resolves up. |
| `pandas` 3.0.6 | Python >= 3.11 | Copy-on-Write and string-dtype defaults. |
| `numpy` 2.5.3 | Python >= 3.12 | Why the baseline is 3.12. |
| `datamodel-code-generator` 0.83.0 | Python >= 3.10, `[ruff]` extra | Pin ruff; formatter output affects the staleness diff. |
| `xunit.v3` 4.0.1 | .NET 10 SDK, Microsoft.Testing.Platform 2.x | Needs `global.json` runner opt-in for `dotnet test`. |
| `Temporalio` 1.20.0 | .NET 10 | M3, forward-compat only. nixpkgs `temporal-cli` 1.8.3 gives `temporal server start-dev`. |

## Pitfalls That Come From the Stack (feed PITFALLS.md and phase planning)

## Gaps and Items to Re-verify in Their Phases

- **NixOS-WSL itself:** the native libraries (`libSkiaSharp`, PDFium, zxing-cpp) loaded under nixpkgs' `dotnet-sdk` on a non-NixOS Linux host. A NixOS machine without `nix-ld` may behave differently. Test in Phase 1 inside the author's actual WSL; if it fails, enable `programs.nix-ld` or set `NIX_LD_LIBRARY_PATH` in `devenv.nix`. (MEDIUM)
- **Live Claude calls:** the SDK adapter was compiled, not exercised. First extraction spike (Phase 3) should confirm: structured output plus a PDF `DataContent` in one request, `cache_control` on the PDF block so repair attempts reuse it, how refusals (`stop_reason: refusal`) surface in `ChatResponse.FinishReason`, and that Haiku 4.5 accepts the schema.
- **Jaeger tag and compose details:** version 2.20 came from web search; pin from the registry when writing `compose.yaml`.
- **Anthropic cache-write pricing multipliers** for the pricing table: confirm against the pricing page when implementing cost accounting (cache reads are $0.20/MTok on Sonnet 5.5 and Opus 5.5).
- **Official NF-e XSD version:** `nfelib` 3.0.0 bundles NF-e 4.00 schemas; confirm it is the layout version you want to emulate, and check which fields (e.g. 2026 tax-reform IBS/CBS groups) the DANFE renderer ignores.
- **Money as `number`:** decided to keep `decimal` as JSON `number` and compare with tolerance in Python. Revisit if exact-cent grading is needed.
- **`ZXingCpp` .NET wrapper maturity:** version 0.5.x, from the zxing-cpp maintainer. If it regresses, `ZXing.Net` is the managed fallback with a lower read rate.

## Sources

- NuGet registration/flat-container API, queried 2026-10-03: Anthropic 12.53.0, Microsoft.Extensions.AI 10.10.0 / Abstractions 10.10.1, ModelContextProtocol 2.2.0, PDFtoImage 5.4.0, ZXingCpp 0.5.3, ZXing.Net 0.16.11, SkiaSharp 4.153.1 (latest) vs 4.150.1 (PDFtoImage), OpenTelemetry 1.19.x, Npgsql 10.0.3, Temporalio 1.20.0, xunit.v3 4.0.1, Aspire.Hosting 13.6.0 (HIGH).
- PyPI JSON API, queried 2026-10-03: all Python versions listed (HIGH).
- Anthropic C# SDK docs: https://platform.claude.com/docs/en/api/sdks/csharp and repo releases https://github.com/anthropics/anthropic-sdk-csharp/releases (MEDIUM-HIGH).
- Anthropic C# SDK `AnthropicClientExtensions.cs` (IChatClient mapping), read via raw.githubusercontent (MEDIUM-HIGH).
- Anthropic structured outputs and PDF support docs: https://platform.claude.com/docs/en/build-with-claude/structured-outputs and `.../pdf-support` (HIGH, official).
- Claude API model/behaviour reference bundled with Claude Code (`claude-api` skill, cached 2026-09-25) for model IDs, pricing, 400 behaviours (HIGH).
- MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk, https://csharp.sdk.modelcontextprotocol.io/v2/, plus web-search summaries of the 2.0 release notes (MEDIUM; behaviour spiked).
- devenv module listing: https://github.com/cachix/devenv/tree/main/src/modules/services and `.../languages` (MEDIUM). Note: devenv.sh and learn.microsoft.com were blocked by the sandbox egress proxy, so their docs were not read directly.
- nixpkgs attribute evals via `nix eval nixpkgs#...` (jaeger absent; dotnet-sdk_10 10.0.401; ruff 0.16.8; pyright 1.1.414) (HIGH).
- Jaeger v2 and Aspire Dashboard standalone: web-search summaries (LOW-MEDIUM).
- Spikes (scratch, not committed): barcode sweep, SkiaSharp mismatch, determinism tests, schema export, codegen, MCP stateless, OTel spans (HIGH, executed).

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
