# Stack Research

**Domain:** LLM extraction pipeline for Brazilian NF-e (DANFE PDF to typed record) with a reproducible eval harness. .NET 10 production pipeline plus Python eval and data tooling.
**Researched:** 2026-10-03
**Confidence:** HIGH overall. Versions were read straight from the NuGet and PyPI registries on the research date. The riskiest integration points were run in spikes, not just read about (see "Verified by running it").

Locked decisions are respected and not relitigated: .NET 10 LTS, Python 3.12+, Anthropic models, ASP.NET Core minimal APIs, PostgreSQL, xUnit, Temporal (later), D-01..D-17.

## How to Read the Confidence Tags

- **HIGH (verified):** version read from the registry on 2026-10-03, or behaviour I executed myself (spikes below, run with nixpkgs `dotnet-sdk` 10.0.401 and Python 3.12 via uv on Linux x64).
- **MEDIUM:** official vendor docs or source fetched, but not executed here, or a design judgement.
- **LOW:** web-search summary only. Verify in the phase.
- Per the `classify-confidence` seam, web search and web fetch alone score LOW/MEDIUM. I upgrade a claim to HIGH only when I ran it or read the registry directly.

---

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

---

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

---

## The .NET Model SDK Decision in Detail

**Use the official `Anthropic` package through its `IChatClient` adapter, wrapped by a domain-typed `ILlmGateway`.** This is "both", but there is only one provider package to depend on, because `Anthropic` already implements `IChatClient` on top of `Microsoft.Extensions.AI.Abstractions`.

Why this and not raw `AnthropicClient`:
- **Test seam (D-07, D-08).** A hand-written fake `IChatClient` makes the repair loop, the cache and the M2 agent loop trivially testable without network.
- **M2 forward-compat.** The SDK docs show MCP client tools (`McpClientTool` is an `AIFunction`) used directly through `IChatClient`. The hand-written agent loop (D-08) can still be explicit while sharing tool types.
- **OTel for free.** `.UseOpenTelemetry(sourceName: "Carimbo.Llm")` emits `gen_ai.*` spans (verified). Prompt content is not recorded unless `EnableSensitiveData` is set. Leave it off so base64 PDFs never reach spans.
- **The adapter covers what M1 needs (source read, MEDIUM-HIGH):**
  - `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(...)` maps to `output_config.format` and the adapter sanitizes the schema (`oneOf` to `anyOf`, strips unsupported constraints).
  - `DataContent(bytes, "application/pdf")` maps to a base64 `DocumentBlockParam`.
  - Usage maps to `UsageDetails`, with `CachedInputTokenCount` set from `cache_read_input_tokens` and `AdditionalCounts["CacheCreationInputTokens"]`.
  - `RawRepresentationFactory` and `AIContentCacheExtensions` give escape hatches for `cache_control` and effort.

Why a gateway on top (not `IChatClient` called directly from `Extraction`):
- Cost needs a **versioned pricing table** keyed by model (token classes: input, output, cache-read, cache-write). Never hard-code prices in code.
- **Request-hash cache (D-07)**: implement as your own `DelegatingChatClient`, not the built-in `UseDistributedCache`. You need to control exactly what is in the key (model, effort/thinking, schema hash, prompt version, PDF SHA-256) and to record `cache_hit` plus the *original* cost, so a cached eval rerun reports the cost it would have had. Store as human-readable JSON files under a git-ignored `.cache/llm/`. MEDIUM (design judgement).
- **Retries (D-07): use the SDK's built-in retry** (`MaxRetries`, honours `Retry-After` and server `x-should-retry`; retries 408/409/429/5xx and connection errors). Do not stack Polly or `Microsoft.Extensions.Http.Resilience` on top: that multiplies attempts. Add only a concurrency limiter and a timeout in the gateway. Each HTTP attempt shows up as a child span via `OpenTelemetry.Instrumentation.Http`.

**Model facts that shape the gateway (HIGH, from the current Claude API docs bundled with Claude Code on 2026-10-03):**
- Forced `tool_choice` (`any`/`tool`) returns **400** on Sonnet 5.5, Opus 5.5 and Fable 5.1. So "extraction via forced tool call" is dead. **Use structured outputs (`output_config.format` = JSON Schema)**.
- `temperature`/`top_p`/`top_k` non-default values return **400** on Sonnet 5.5 and Opus 5.5 (Haiku 4.5 still accepts them). You cannot pin temperature 0, so **model output is not reproducible run to run**. The response cache is the reproducibility mechanism, which makes D-07 load-bearing, not an optimization. Never set `Temperature` in `ChatOptions` for these models.
- Opus 5.5: thinking cannot be disabled, effort defaults to `medium` (set it explicitly). Sonnet 5.5: `thinking: disabled` is a 400; use `between_tools` or leave thinking on at low effort. Both go in the cache key.
- Model IDs (exact, no date suffix): `claude-sonnet-5-5` ($2/$10 per MTok), `claude-opus-5-5` ($4/$20), `claude-haiku-4-5` ($1/$5, 200K context). Sonnet 4.5 is deprecated (EOL 2026-11-30), so do not pick it.
- Candidate first comparison (open question, recommendation only): **Sonnet 5.5 vs Haiku 4.5** for the cost-quality frontier, with Opus 5.5 as an optional ceiling run. Verify structured-output support on Haiku 4.5 before committing. MEDIUM.
- PDF input: each page goes to the model as image plus extracted text; max 32 MB / 600 pages (100 if context < 1M). Roughly 1k tokens per 3 pages text-only, around 7k per 3 pages with images, so budget about 2k+ tokens/page.
- Structured-output schema limits to design for (HIGH, official docs): no `minimum/maximum/minLength/maxLength/multipleOf/maxItems/uniqueItems`, no `oneOf`, no recursion, `additionalProperties` must be false, `minItems` only 0/1, **max 24 optional properties and max 16 union-typed properties (`anyOf` or `["string","null"]`) across the schema**. See Pitfall 3.

## JSON Schema Export and Pydantic Codegen (verified end to end)

Exporter configuration that produced a correct schema (spike):

```csharp
var opts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,       // NOT JsonSerializerDefaults.Web
    RespectNullableAnnotations = true,                        // string? => ["string","null"], required
    RespectRequiredConstructorParameters = true,              // positional record params => required
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, // => additionalProperties:false
    Converters = { new JsonStringEnumConverter() },           // enums => {"enum":[...]}
    WriteIndented = true,
};
var node = JsonSchemaExporter.GetJsonSchemaAsNode(opts, typeof(Invoice), new JsonSchemaExporterOptions
{
    TreatNullObliviousAsNonNullable = true,
    TransformSchemaNode = (ctx, s) => { /* add title = type name, description from [Description], $schema at root */ return s; },
});
```

Observed output: required nullable fields stay required, `DateOnly?` becomes `{"type":["string","null"],"format":"date"}`, `DateTimeOffset` becomes `format: date-time`, `decimal` becomes `"number"`, and a record parameter with a default value becomes optional with `"default": null`. Repeated types (`Party` as issuer and recipient) are **inlined**, not `$ref`'d, so add `title = ty.Name` in `TransformSchemaNode`.

Codegen command that produced correct Pydantic v2 models (spike), run through `uv run` so ruff is the pinned one:

```bash
datamodel-codegen --input schemas/invoice.schema.json --input-file-type jsonschema \
  --output-model-type pydantic_v2.BaseModel --use-annotated --field-constraints \
  --use-standard-collections --use-union-operator --target-python-version 3.12 \
  --use-title-as-name --reuse-model --disable-timestamp \
  --formatters ruff-format ruff-check --output python/src/carimbo/models/invoice.py
```

With `--use-title-as-name --reuse-model`, `Party` is generated once and reused. Without titles you get `Issuer` and `Recipient` duplicates. Models get `extra="forbid"`. Plain `number` maps to Python `float` (the only decimal flag is `--use-decimal-for-multiple-of`), so money is `float` in Pydantic. That is acceptable because graders already specify numeric tolerance. Parse the ground-truth XML with `Decimal`, compare with tolerance. Revisit only if a grader needs exact cents. Newer `datamodel-code-generator` also has dated immutable `--preset` bundles; consider one after the first run, since they pin generator behaviour.

## Barcode and PDF on the .NET side (verified)

```csharp
using var bmp = Conversion.ToImage(pdfStream, page: 0, options: new RenderOptions(Dpi: 300, Grayscale: true));
using var gray = bmp.ColorType == SKColorType.Gray8 ? bmp.Copy() : bmp.Copy(SKColorType.Gray8);
var view = new ZXingCpp.ImageView(gray.GetPixelSpan().ToArray(), gray.Width, gray.Height,
                                  ZXingCpp.ImageFormat.Lum, gray.RowBytes, 1);
var reader = new ZXingCpp.BarcodeReader { Formats = ZXingCpp.BarcodeFormat.Code128, TryInvert = true };
string? key = reader.From(view).FirstOrDefault()?.Text;   // then validate 44 digits + check digit (D-05)
```

Cost: about 260 ms cold, 15 to 40 ms warm at 300 dpi for page 1 (A4, about 8.7 MP). Render **page 0 only** (the Controle do Fisco barcode is on page 1). Strategy: decode, and on failure fall through to the model (D-05 says "when possible"). Never trust a decoded key without the check-digit validator.

---

## Verified by Running It (spikes, 2026-10-03)

1. **Barcode decoder sweep** (20 single-factor degradations of one DANFE-like page, rendered at 300 dpi, Code 128 of a 44-digit key). Python `zxing-cpp` and .NET `ZXingCpp` agreed on **20 of 20**. `ZXing.Net` 0.16.11 additionally failed 1-degree rotation, 150 dpi and a "typical" combined scan (rot 2, blur 0.8, 150 dpi, noise 8, JPEG 60) that `ZXingCpp` read. Both failed at blur >= 1.5, scan dpi <= 100, rotation 10 degrees and noise sigma 30. So these are natural "barcode-unreadable" generators, and **datagen can label readability with `zxing-cpp` + `pypdfium2` and trust it matches the .NET decoder**, provided the render DPI and library versions are pinned and a .NET test spot-checks the dataset.
2. **SkiaSharp mismatch (real failure reproduced).** `PDFtoImage 5.4.0` + `ZXing.Net.Bindings.SkiaSharp 0.16.24` threw at runtime: `native libSkiaSharp (150.0) incompatible ... supported [151.0, 152.0)`. The binding pulls managed SkiaSharp 4.151.1 while PDFtoImage pulls natives 4.150.1. The recommended pair `PDFtoImage` + `ZXingCpp` has no such conflict (`SkiaSharp 4.150.1` and natives `4.150.1` throughout; verified on Linux x64).
3. **Determinism:** reportlab with `invariant=1` is byte-identical across runs (also for image-only pages). Pillow's PDF writer is not. BrazilFiscalReport is byte-identical once `set_creation_date()` is fixed. Without it the PDF embeds the wall-clock `CreationDate`.
4. **Text layer:** a clean DANFE has an extractable text layer (`pypdfium2` returns the text); the degraded image-only PDF returns an empty string. The model receives text **and** page image for PDFs, so see Pitfall 4.
5. **MCP 2.2.0:** an `[McpServerTool(ReadOnly = true)]` method served over `AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<T>()` + `app.MapMcp("/mcp")`. A legacy `initialize` (protocolVersion 2025-06-18) followed by `tools/list` worked statelessly, with `readOnlyHint: true` in the tool annotations.
6. **`IChatClient` from `Anthropic` 12.53.0** compiles with `AsIChatClient("claude-sonnet-5-5").AsBuilder().UseOpenTelemetry(...)`, `ChatResponseFormat.ForJsonSchema(...)` and a PDF `DataContent`. (Compiled, not called live, since no API key.) The OTel middleware emitted `chat claude-sonnet-5-5` spans with `gen_ai.operation.name`, `gen_ai.request.model`, `gen_ai.response.*`.
7. **datamodel-code-generator 0.83.0** produced the expected Pydantic v2 output from the exporter's schema, with `Party` reused.
8. Everything above ran under the nixpkgs-provided `dotnet-sdk` 10.0.401 on Linux, which is good evidence for NixOS-WSL but **not run on NixOS-WSL itself** (see Gaps).

---

## Installation

```bash
# .NET (central package management; pin exactly)
dotnet add src/Llm package Anthropic --version 12.53.0
dotnet add src/Llm package Microsoft.Extensions.AI --version 10.10.0
dotnet add src/Extraction package PDFtoImage --version 5.4.0
dotnet add src/Extraction package ZXingCpp --version 0.5.3
dotnet add src/Api package OpenTelemetry.Extensions.Hosting --version 1.19.1
dotnet add src/Api package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.19.1
dotnet add src/Api package OpenTelemetry.Instrumentation.AspNetCore --version 1.19.0
dotnet add src/Api package OpenTelemetry.Instrumentation.Http --version 1.19.0
dotnet add tests/Unit package xunit.v3 --version 4.0.1
dotnet add tests/Api package Microsoft.AspNetCore.Mvc.Testing --version 10.0.12
# M2 (later)
dotnet add src/Tools package ModelContextProtocol.AspNetCore --version 2.2.0

# Python (pyproject.toml; commit uv.lock)
uv add httpx2 pydantic pandas typer matplotlib jsonschema lxml
uv add nfelib brazilfiscalreport reportlab pillow numpy pypdfium2 zxing-cpp faker
uv add --dev ruff pyright pytest pytest-asyncio datamodel-code-generator pandas-stubs types-lxml
```

Smoke test in CI (cheap, catches native-library drift): render a committed fixture PDF with `PDFtoImage`, decode with `ZXingCpp`, and assert the expected key.

---

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

**If a reviewer has no Nix and no .NET SDK:**
- Provide a `compose.yaml` that includes Jaeger and (optionally) a profile that builds and runs the Api and the Python harness in containers.
- Because the DoD says "clone, start local services with one command, process a sample invoice". Nix is the author's convenience, not a prerequisite.

**If the MCP server must be consumed by Claude Code and OpenCode (M2):**
- Expose Streamable HTTP, stateless, on `/mcp` from a dedicated `Tools` host; optionally add a stdio entry point reusing the same tool classes (`WithStdioServerTransport()`).
- Because both clients support remote HTTP MCP servers. Stateless matches SDK 2.x defaults, and the read-only tools (D-09) need no session. Set `AllowedHosts` explicitly to avoid DNS-rebinding exposure. Client config syntax for each runtime was not verified (MEDIUM). Since 2.2.0 `SessionMode = StatefulForInitializeClients` can serve old handshake clients with real sessions if one is found that needs it.

**If the CI eval subset has to be cheap:**
- Run the subset against the response cache committed as a cache artifact, or against a fixed small dataset with `claude-haiku-4-5`.
- Because Sonnet/Opus outputs are not reproducible without a cache (no temperature control).

**If PDF bytes must be reproducible across machines:**
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

1. **Reproducibility without temperature.** Sonnet 5.5 and Opus 5.5 reject non-default sampling, so reruns differ. The response cache plus recorded model, effort, prompt version and schema hash per run is the whole reproducibility story. Report run-to-run variance in the first results table. (HIGH)
2. **SkiaSharp native/managed mismatch** crashes at first use, not at build. Central Package Management plus the CI smoke test. (HIGH, reproduced)
3. **Structured-output complexity budget:** at most 24 optional properties and 16 union-typed properties across the whole schema. Every nullable field (`string?`, `DateOnly?`) costs one union, so an NF-e `Invoice` with `Taxes` sub-objects can blow the budget and fail with "Schema is too complex". Design `Domain` records with this budget in mind: prefer required fields with sentinel/empty values over nullables where semantics allow, or send the model a slimmer `ExtractedInvoice` schema and map it to the full `Invoice`. Count unions in a schema unit test. Also: no min/max/length constraints in the exported schema (put them in validators per D-05), no `oneOf`, no recursion, grammar compile adds first-request latency (cached 24 h), and changing the schema invalidates the prompt cache. (HIGH from docs; exact counting rules MEDIUM, verify in Phase 1/3.)
4. **Text layer leakage.** Claude reads each PDF page as image **and** extracted text. A "degraded scan" that keeps the vector text layer tests nothing. Produce degraded cases as image-only PDFs, and keep a separate clean-text-layer cohort so the results table can show both. (HIGH)
5. **The schema the model sees is not the schema you commit.** The adapter transforms it (`oneOf` to `anyOf`, strips constraints). Eval "schema validity" must validate against the committed schema in .NET deserialization and Python Pydantic, not against what was sent.
6. **Generated-code staleness flakiness.** The committed Pydantic file depends on datamodel-code-generator, ruff and Python versions. Run codegen only via `uv run` from the lockfile, in CI and locally, and fail on `git diff`. Also add `--disable-timestamp`.
7. **Seeded generators are not stable across library upgrades** (`numpy` Generator streams, Faker locale data, PDFium rendering). Seed plus `uv.lock` plus the manifest hash is the reproducibility contract. Bumping any of them is a dataset version bump (D-14).
8. **Barcode ground truth.** Label "barcode-unreadable" by running the actual decoder (verified agreement with .NET), not by trusting the degradation parameters. Dataset metadata should record `barcode_readable` and the decoder version.
9. **xUnit v3 + `dotnet test`:** with the .NET 10 SDK, Microsoft.Testing.Platform runs require opting in via `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`). Verify in Phase 1, since CI will fail confusingly otherwise. (MEDIUM, from memory of the .NET 10 release notes, not re-verified.)
10. **MCP 2.x semantics.** Stateless HTTP by default, Roots/Sampling/Logging deprecated (warning MCP9005), Tasks moved to a wire-incompatible `Extensions.Tasks` package. Do not write M2 code from 1.x tutorials. (MEDIUM, web search + spike.)
11. **Jaeger in-memory storage loses traces on restart**, so older eval JSONL trace IDs dead-link. Acceptable for dev. For the published README, either capture screenshots or configure persistent storage. The container's OTLP receiver may need to bind `0.0.0.0` (earlier 2.0 images bound localhost). Smoke-test the compose file. (MEDIUM)
12. **SDK churn.** `Anthropic` shipped 12.49 to 12.53 in two weeks, and `Microsoft.Extensions.AI` ships monthly. Pin exact versions in `Directory.Packages.props`, upgrade in dedicated commits, and let the eval suite be the regression gate.

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

---
*Stack research for: LLM extraction pipeline for Brazilian NF-e with eval harness (carimbo)*
*Researched: 2026-10-03*
