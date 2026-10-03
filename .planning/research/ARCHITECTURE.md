# Architecture Research

**Domain:** LLM extraction pipeline (Brazilian NF-e DANFE PDF to typed record) with a Python eval harness calling a .NET service over HTTP
**Researched:** 2026-10-03
**Scope:** Milestone 1 (foundation + validators, synthetic dataset, extraction pipeline, eval harness v1), kept forward-compatible with M2 (MCP tools, agent loop) and M3 (Temporal, idempotent async intake).
**Overall confidence:** MEDIUM-HIGH. Vendor facts (Anthropic structured outputs, PDF limits, C# SDK, `JsonSchemaExporter`) were read from primary docs. Layout, gateway and CI design are synthesis and opinion, labelled as such. Note: the GSD confidence seam classifies `WebFetch`/`WebSearch` as LOW; items marked HIGH/MEDIUM below were read directly from vendor documentation and cross-checked against the brief and DECISIONS.md.

All locked decisions (D-01..D-17) are respected. Where a recommendation touches a decision it says so. Nothing here reverses one.

---

## Standard Architecture

### System Overview

```
 PYTHON (data + evals, never reimplements pipeline logic)            .NET 10 (production pipeline)
┌──────────────────────────────────────────────────────────┐      ┌─────────────────────────────────────────────┐
│  datagen            evals                    reports      │      │  Carimbo.Api (composition root, HTTP, OTel)  │
│  ┌──────────┐      ┌────────────────────┐   ┌─────────┐  │ HTTP │   POST /eval/extractions  GET /eval/meta    │
│  │ sampler  │      │ dataset loader     │   │ summary │  │─────►│   (later: POST /invoices, GET /invoices/id) │
│  │ xml writer│     │ runner (asyncio)   │   │ tables  │  │ JSON │        │                                    │
│  │ DANFE pdf │     │ graders registry   │   │ charts  │  │◄─────│        ▼                                    │
│  │ degrade   │     │ summarize/compare  │   └────▲────┘  │      │  Carimbo.Extraction                         │
│  └────┬─────┘      │ gate (thresholds)  │        │       │      │   prompts, schema projector, repair loop    │
│       │            └─────┬───────▲──────┘        │       │      │      │                  │                   │
│       ▼                  ▼       │               │       │      │      ▼                  ▼                   │
│  datasets/…  ◄────── loader   runs/*.jsonl ──────┘       │      │  Carimbo.Llm          Carimbo.Validators    │
│  (xml+pdf+expected.json+manifest)  evals/reports (commit)│      │   gateway decorators   pure rules → errors  │
└───────▲──────────────────────────▲───────────────────────┘      │   retry/cost/otel/cache      │              │
        │                          │                               │      │                       ▼              │
        │       SHARED CONTRACT    │                               │      ▼                  Carimbo.Domain      │
        │   schema/carimbo.schema.json  (generated from C#)        │  Anthropic SDK          (records, pure)     │
        └───────── python/src/carimbo/models/generated.py ◄────────┴───── (generated, CI-checked) ───────────────┘
```

Two hard seams between the stacks, and only two:

1. **Schema seam (build time):** C# records are exported to one committed JSON Schema bundle, Pydantic models are generated from it, and CI fails if either hop is stale (D-03).
2. **HTTP seam (run time):** the eval endpoint returns the full result, validator outcomes, attempts, tokens, cost, latency and trace ID (D-02). The request and response DTOs are part of the same schema bundle, so the HTTP contract cannot drift either.

### Component Responsibilities

| Component | Responsibility | Depends on | Must NOT |
|-----------|----------------|------------|----------|
| `Carimbo.Domain` | Records (`Invoice`, `Party`, `LineItem`, `Taxes`, `Decision`), value objects (`Cnpj`, `AccessKey` parse/format/check-digit math), wire-format attributes and `[Description]`s | BCL only | Any I/O, HTTP, DI, logging, model calls (D-04) |
| `Carimbo.Validators` | Pure rules returning `IReadOnlyList<ValidationError>`; never throw (D-06). Owns `ValidationError`, `Severity`, rule codes | Domain | Call the model, read files, know about attempts |
| `Carimbo.Llm` | Provider-agnostic gateway: `ILlmGateway`, decorators (cache, telemetry/accounting, retry), pricing table, Anthropic adapter. Provider SDK types never leave this assembly | Anthropic SDK, `Microsoft.Extensions.*` abstractions, `System.Diagnostics` | Reference Domain/Validators/Extraction (so M2's agent reuses it unchanged) |
| `Carimbo.Extraction` | PDF to `Invoice`: prompt assets, model-facing schema projection, validate-and-repair loop, typed `ExtractionOutcome` | Domain, Validators, Llm | Know about HTTP, Temporal, or the eval harness |
| `Carimbo.Api` | Composition root: DI wiring, OTel SDK + exporter, auth, eval endpoints, DTO mapping | everything | Contain pipeline logic; it is an adapter |
| `tools/Carimbo.SchemaTool` | Console tool exporting the schema bundle (and `--check` mode) | Domain, Validators, Extraction/Api contracts | Run in production |
| `python/.../models` | Generated Pydantic models. Never hand-edited | generated | Be edited by hand |
| `python/.../datagen` | Seeded sampler, NF-e XML writer, DANFE PDF renderer, degraders, manifest writer | models | Import `evals` |
| `python/.../evals` | Dataset loader, runner, graders, summarize, compare, gate | models, httpx | Import `datagen`; reimplement validators or extraction |
| `python/.../reports` | README tables/charts from committed summaries | evals summary schema | Call the API |

Dependency direction (compile-time, enforced):

```
Domain ◄── Validators ◄── Extraction ──► Llm
   ▲            ▲              ▲          ▲
   └────────────┴──────────────┴──────────┴── Api (composition root)
M2 adds: Agent ─► Llm, Domain, Tools(abstractions);   Tools (MCP host exe) ─► Domain
M3 adds: Workflows ─► Extraction, Agent (activities wrap them); Persistence ─► Domain
```

---

## Recommended Project Structure

### Monorepo

```
carimbo/
├── Carimbo.slnx                    # .NET 10 SDK default solution format (MEDIUM; plain .sln also fine)
├── global.json                     # pins SDK 10.0.x, rollForward: latestFeature
├── Directory.Build.props           # Nullable, TreatWarningsAsErrors, LangVersion, Deterministic, analyzers
├── Directory.Packages.props        # central package management: one version per package
├── nuget.config
├── Makefile                        # the ONLY command surface; CI and humans call the same targets
├── AGENTS.md                       # canonical agent instructions (OpenCode reads it)
├── CLAUDE.md                       #   -> "@AGENTS.md" import (Claude Code); opencode.json if needed
├── src/
│   ├── Carimbo.Domain/
│   ├── Carimbo.Validators/
│   ├── Carimbo.Llm/
│   ├── Carimbo.Extraction/         # Prompts/extract.v1.md as EmbeddedResource (prompts are pipeline code)
│   └── Carimbo.Api/
├── tests/
│   ├── Carimbo.Domain.Tests/       # incl. schema snapshot test + "Domain references only BCL" test
│   ├── Carimbo.Validators.Tests/   # known-valid/invalid vectors from testdata/ + dataset conformance
│   ├── Carimbo.Llm.Tests/          # fake HttpMessageHandler, cache-key golden vectors, retry matrix
│   ├── Carimbo.Extraction.Tests/   # scripted fake gateway: repair loop scenarios
│   ├── Carimbo.Api.Tests/          # WebApplicationFactory + fake gateway; emits contract fixtures
│   └── Carimbo.TestSupport/        # FakeLlmGateway, ScriptedGateway, builders (optional, add when 2+ projects need it)
├── tools/
│   └── Carimbo.SchemaTool/
├── schema/
│   └── carimbo.schema.json         # COMMITTED, generated: Invoice + eval contract DTOs in one $defs bundle
├── testdata/                       # shared, language-neutral fixtures (both stacks read these)
│   ├── vectors/cnpj.json           #   known valid / invalid CNPJs
│   ├── vectors/access-key.json     #   known valid / invalid 44-digit keys
│   └── contract/                   #   golden eval responses emitted by .NET tests, parsed by Python tests
├── python/                         # ONE uv project (pyproject.toml, uv.lock), src layout
│   ├── pyproject.toml              # dependency groups: datagen (reportlab, pillow, numpy…), evals (httpx, pandas…), dev
│   ├── src/carimbo/
│   │   ├── models/                 # generated.py (+ __init__ re-exports). CI-checked.
│   │   ├── datagen/                # sampler, nfe_xml, danfe, degrade, manifest, cli
│   │   ├── evals/                  # dataset, runner, graders/, summarize, compare, gate, cli
│   │   ├── reports/                # tables, charts
│   │   └── cli.py                  # typer root: carimbo datagen|eval|report
│   └── tests/
├── datasets/
│   └── nfe-synth/v1/               # id/version; see "Dataset versioning"
│       ├── manifest.jsonl          # one row per case: id, tags, sha256s, seed
│       ├── dataset.json            # dataset version, generator version, schema hash, content hash
│       ├── subsets/ci.txt          # fixed, stratified case ids for PR gating
│       └── cases/{case}.xml | .pdf | .expected.json
├── evals/                          # eval ARTIFACTS and CONFIG (not code)
│   ├── thresholds/ci.yaml          # per-grader floors (+ optional baseline-delta tolerance)
│   ├── reports/{run_id}/summary.json|md   # COMMITTED (D-15)
│   ├── cache-fixtures/             # optional: committed LLM cache for the published run (see Cache section)
│   └── runs/                       # GITIGNORED: full JSONL per run
├── .github/workflows/              # ci.yml, eval-full.yml (workflow_dispatch)
└── docs/                           # PROJECT-BRIEF.md, DECISIONS.md (existing)
```

### Structure Rationale

- **One uv project with three sub-packages** (not a uv workspace): three pyprojects is ceremony with no payoff here. Dependency *groups* keep heavy datagen deps (reportlab, pillow, numpy) out of the CI eval job (`uv sync --frozen --group evals --no-group datagen`). Enforce "evals never imports datagen" with a 10-line pytest that greps imports (or `import-linter`).
- **`evals/` at repo root is data/config, `python/src/carimbo/evals/` is code.** D-15 fixes `evals/reports/` as the committed-summaries path; keeping code under `python/` avoids two meanings of the same directory.
- **`schema/` and `testdata/` at root, not inside either stack**: they belong to neither language; both stacks read them.
- **Prompts live in `Carimbo.Extraction` as embedded resources**: they are pipeline behaviour, versioned by id (`extract.v1`), and echoed in every eval record.
- **No `Persistence`/`Workflows`/`Agent`/`Tools` projects in M1.** M1 needs no database; Postgres arrives in M2. This also defers the devenv vs docker-compose vs Aspire question: M1 needs at most an optional Jaeger container (`make up`).
- **Makefile as the single command surface** (`make build test schema schema-check dataset eval-smoke eval-ci`). It works on NixOS-WSL and for an external reviewer with zero extra installs (`just` would be one more).
- **`Directory.Packages.props`** (central package management) keeps SDK/OTel/xUnit versions in one place across 10+ csproj files.

### Enforcing the boundaries (cheap, worth doing in Phase 1)

| Rule | Mechanism |
|------|-----------|
| Domain is pure (D-04) | xUnit test: `typeof(Invoice).Assembly.GetReferencedAssemblies()` must be within an allowlist (`System.*`, `netstandard`). Plus no `PackageReference` in `Carimbo.Domain.csproj` (grep test) |
| Llm is domain-agnostic | Project references: `Carimbo.Llm.csproj` has none to other Carimbo projects |
| Provider types do not leak | Public API of `Carimbo.Llm` exposes only Carimbo types (review rule; optionally `PublicApiAnalyzers`) |
| Python evals does not import datagen/pipeline logic | pytest import check |
| Schema/Pydantic never stale | `make schema-check` in CI (below) |

---

## Architectural Patterns

### Pattern 1: Gateway as a decorator chain over a Carimbo-owned interface

**What:** `ILlmGateway` with Carimbo request/response types; behaviours are decorators composed in the Api: `Telemetry( Cache( Retry( AnthropicClient ) ) )`.

**Why this order:**
- **Telemetry outermost** so every logical call produces exactly one span (the brief's "span per call"), including cache hits (`carimbo.llm.cache_hit=true`) and failures.
- **Cache inside telemetry, outside retry** so retries happen only on misses and a hit never touches the network.
- **Retry innermost around the SDK call.** Configure the Anthropic SDK with `MaxRetries = 0` and own the policy (Polly v8 via `Microsoft.Extensions.Resilience`). The SDK retries 2 times by default on connection errors, 408, 409, 429 and 5xx, so leaving it on and adding your own retry multiplies attempts (3 x 3) and hides them from spans/accounting. Honor `retry-after`; do not retry 400/401/403/404/422, refusals or `max_tokens`. (SDK facts: HIGH, official C# SDK page.)

**Direct SDK, not `IChatClient`, for the bottom adapter (answers an open question, MEDIUM):** the official `Anthropic` NuGet package (v10+, GA, netstandard2.0; v3.x was the community SDK, now `tryAGI.Anthropic`) gives typed document blocks, raw `OutputConfig.Format.Schema`, cache-token usage fields, raw response headers and request IDs. All of that is needed here, and D-08's hand-written loop wants explicit tool-use blocks in M2. The SDK also ships `AsIChatClient()`, so a `Microsoft.Extensions.AI` adapter can be added later for MCP interop without changing this design.

**Trade-offs:** one extra interface to maintain vs. testability (fake gateway) and provider isolation. Worth it: D-07 mandates a gateway.

**Sketch:**

```csharp
public interface ILlmGateway
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    string Model,
    string? System,
    IReadOnlyList<LlmMessage> Messages,        // content blocks: Text, Document(bytes, mediaType) now; ToolUse/ToolResult added in M2
    JsonElement? OutputSchema,                 // model-facing JSON Schema, passed raw
    int MaxTokens,
    double? Temperature,
    CacheDirective Cache,                      // Default | Bypass | Refresh | ReplayOnly
    CallContext Context);                      // purpose, caseId, runId, attempt: telemetry only, EXCLUDED from cache key

public sealed record LlmResponse(
    string? Text, JsonElement? Json, StopReason StopReason,
    TokenUsage Usage,                          // input, output, cacheRead, cacheWrite
    decimal? CostUsd,                          // notional: what the call costs at current pricing; null if model unpriced
    decimal BilledCostUsd,                     // 0 on cache hit
    bool CacheHit, TimeSpan Latency, TimeSpan OriginalLatency,
    string Model, string? ProviderRequestId, int HttpAttempts);
```

Design rules that fall out of D-07/D-17:
- **Content blocks from day 1** (Text, Document now; ToolUse/ToolResult in M2) so the cache-key format and message model do not break when the agent arrives. Implement tool blocks only in M2.
- **No ambient mutable state.** Accounting flows through return values (`Extraction` sums attempts), not `AsyncLocal` counters. This keeps the code activity-safe for M3.
- **Time and jitter through `TimeProvider`** so retry tests are instant and deterministic.

### Pattern 2: Cost accounting from usage x a versioned pricing table

- `pricing.json` (embedded, versioned `pricing_version`): per model, per-MTok input, output, cache-write, cache-read. Do not hardcode prices in code; do not invent numbers in docs.
- `cost = Σ tokens_class x price_class` in `decimal`. Unknown model gives `CostUsd = null` plus a warning log, never silent zero.
- **Cost is recomputed from stored usage at read time**, not stored in the cache, so a pricing change never needs cache invalidation. Runs record `pricing_version`.
- **Cache hits report notional cost and billed cost separately** (`CostUsd` vs `BilledCostUsd = 0`). Eval summaries show both. This keeps cost comparisons between two models meaningful on a fully cached rerun, while the CI budget guard uses billed cost.
- **Naive `input_tokens x price` is wrong once prompt caching is on.** Anthropic usage separates cache-creation and cache-read tokens; each has its own price class. Mark the PDF document block with `cache_control` so repair attempts re-read it at cache-read price (minimum cacheable-prompt thresholds apply; verify per model in Phase 3).

### Pattern 3: OpenTelemetry span per call, GenAI semconv isolated behind one class

- Library code uses only `ActivitySource("Carimbo.Llm")` (no OTel SDK reference); `Carimbo.Api` configures the SDK and OTLP exporter to Jaeger. With no listener the instrumentation is near-free, and tests can assert spans with an `ActivityListener`.
- Span name `chat {model}`; attributes (GenAI semconv, MEDIUM: still "Development" status as of mid-2026, names have churned): `gen_ai.operation.name`, `gen_ai.provider.name` (older: `gen_ai.system`), `gen_ai.request.model`, `gen_ai.request.max_tokens`, `gen_ai.response.model`, `gen_ai.response.id`, `gen_ai.response.finish_reasons`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `error.type`. Custom, namespaced `carimbo.*`: `carimbo.llm.cost_usd`, `carimbo.llm.billed_cost_usd`, `carimbo.llm.cache_hit`, `carimbo.llm.request_hash`, `carimbo.llm.http_attempts`, `carimbo.llm.purpose`, `carimbo.eval.case_id`, `carimbo.eval.run_id`, `carimbo.extraction.attempt`.
- **Put every attribute-name constant in one `LlmTelemetry` static class.** Semconv churn then costs one file.
- Per-HTTP-attempt detail goes as **span events** on the single span, not child spans (the brief asks for one span per call).
- **Never record prompts, responses or PDF bytes as span attributes** (size, PII, base64). Content capture, if ever wanted, is opt-in and off by default. The request hash is the join key to the cache entry.
- Trace ID comes from `Activity.Current.TraceId` in the Api and is returned in the eval response (D-15, D-17). ASP.NET Core honours an inbound `traceparent`, so the Python runner can mint one per case and know the trace ID even if the response is lost to a timeout.

### Pattern 4: Request-hash cache with strict normalization (D-07)

**Key = SHA-256 over canonical JSON of the semantically significant request.**

| Included (changes output) | Excluded (must not change the key) |
|---------------------------|------------------------------------|
| `CacheKey.Version` constant (bump to invalidate deliberately) | `CallContext` (case id, run id, purpose, attempt number) |
| provider, model id | trace/span ids, request ids |
| system prompt, messages (all blocks) | timeouts, retry policy, base URL, API key |
| **document blocks as `{media_type, sha256(bytes)}`** (never inline base64) | cache directive itself |
| output schema (canonical JSON) | `cache_control` markers (cost optimisation only; see note) |
| max_tokens, temperature, top_p, thinking/other output-affecting params, with defaults resolved before hashing | |

Normalization rules (all unit-tested with **golden key vectors** committed in `testdata/`, so a refactor or a different OS/culture cannot silently change keys):
1. Canonical JSON: object keys sorted ordinally, no insignificant whitespace, invariant-culture numbers (RFC 8785-style).
2. Unicode NFC and `\r\n` to `\n` on text blocks; **no trimming** (whitespace can be meaningful).
3. Resolve defaults first, so "temperature omitted" and "temperature = default" hash identically.
4. Schema hashed by canonical content, not by file path or type name.

Note on `cache_control`: if it is excluded from the key, toggling it reuses stored responses (fine, it does not change output). Decide once and document it.

**Storage: content-addressed files, behind `ICacheStore`.**
- Layout `{cacheDir}/{hash[0..2]}/{hash}.json`; entry = key, normalized request (documents replaced by hashes, for debugging), response (text/json, stop reason, usage, provider model id, provider request id), `recorded_at`, `original_latency_ms`, `cache_key_version`.
- Atomic write (temp file + rename), safe under bounded concurrency; **in-process single-flight** (`ConcurrentDictionary<key, Lazy<Task>>`) so identical concurrent requests make one call.
- Why files, not SQLite/Postgres: zero dependencies in M1, inspectable and diffable, trivially cached by `actions/cache`, committable as fixtures. Postgres is not needed until M2 and the cache should not depend on it.
- **Modes** (`Llm:Cache:Mode`): `Off` (the production default, enforced by config, D-07), `ReadWrite` (dev/eval), `ReplayOnly` (a miss is an explicit error: CI fork PRs, reviewer reproduction). Per-request `Bypass`/`Refresh` is accepted only on eval endpoints.
- **Cache is a determinism device, not just a cost saver.** Temperature 0 is not bitwise reproducible, so a cache hit means "same sample as before". Every run summary reports `cache_hit_rate`; a run with misses is measuring a fresh sample.
- **Latency on a hit is meaningless**, so records carry both `latency_ms` (observed) and `original_latency_ms` (recorded); published latency percentiles use the original.
- **Reviewer-reproducibility idea (differentiator, ~5 MB):** commit the cache entries produced by the published results run under `evals/cache-fixtures/`. A reviewer with no API key can run `make eval-replay` and reproduce the results table at $0. Because documents are keyed by hash and keys are version-pinned, it works as long as the prompt and code at that tag are unchanged.

### Pattern 5: Schema-constrained extraction with validate-and-repair (D-05, D-06)

```
ExtractAsync(ExtractionRequest{ pdf, options, knownAccessKey? })
  0. Pre-flight (no tokens spent): is PDF header, size <= 32 MB request limit, pages <= 100
     -> else typed failure InvalidInput
  1. attempt 1 (initial): messages = [ user: Document(pdf) + instruction ]
       OutputConfig.Format = json_schema( ModelSchemaProjector.Project(canonicalSchema) )
  2. outcome handling
       stop_reason = refusal   -> Failure(ModelRefused)      (schema may not hold; do not parse)
       stop_reason = max_tokens-> Failure(Truncated)         (output incomplete)
       parse w/ strict System.Text.Json (Disallow unmapped members, respect nullability/required)
         fails                 -> Failure(SchemaInvalid)     (should be near-impossible; count it)
  3. errors = Validators.Validate(invoice, { knownAccessKey })
       no Error-severity       -> Success(invoice, attempts, warnings)
  4. attempt n+1 (repair), n <= 1 + maxRepairs (default 2, D-06):
       messages = [ user: Document + instruction ]
                + [ assistant: previous JSON ]
                + [ user: errors as JSON (field, rule, expected, actual) + repair instruction ]
  5. budget exhausted         -> Failure(ValidationFailed, lastInvoice, lastErrors, attempts)
```

Key decisions:
- **Use the committed exported schema as the model's output contract, passed raw via `OutputConfig.Format`; do NOT use the SDK's `Create<T>()` auto-schema.** `Create<T>` derives its own schema from the CLR type and would bypass the committed artifact, violating D-03's "one schema". (HIGH that `Create<T>` exists and derives; opinion on avoiding it.)
- **Structured-output schema subset (HIGH, vendor docs):** no `minimum`/`maximum`/`multipleOf`, no `minLength`/`maxLength`, `minItems` only 0 or 1, `additionalProperties` must be `false`, no recursive schemas, no external `$ref`; `$ref`/`$defs`/`anyOf`/`enum`/`const` and string formats like `date`, `date-time`, `uuid` are supported. Therefore:
  - The **canonical schema** (committed) may carry rich constraints, used for the Python `jsonschema` check and ground-truth validation.
  - A pure `ModelSchemaProjector` derives the **model-facing schema**: forces `additionalProperties:false`, folds unsupported constraints into `description` text (what the SDK transform also does), asserts no recursion. Unit-test it and snapshot its output.
  - Keep the Domain shape flat and non-recursive so the projection is nearly identity.
- **Structured output does not guarantee a schema-valid answer in two cases:** `stop_reason: refusal` (HTTP 200, billed, output may violate the schema) and `max_tokens` (truncated). Both must be typed failures, never "parse and hope". Also: enum/const string casing can differ in capitalisation, so compare case-insensitively or use lowercase enums.
- **Changing `output_config.format` invalidates the prompt cache**, so keep the schema byte-identical across repair attempts.
- **Repair prompt must not invite fabrication.** Say: "re-read the document; correct only fields the document supports; if a rule cannot be satisfied from the document, keep the document's value." Otherwise the model "fixes" totals to satisfy the validator. Track `passed_first_try` vs `repaired` as eval metrics; ground-truth graders catch validator-gaming.
- **`Severity { Error, Warning }` on `ValidationError`** (small addition to D-06's shape): only Errors drive repair/failure; soft rules such as date plausibility are recorded as warnings. Rule codes are stable machine strings (`cnpj.check_digit`, `access_key.issuer_mismatch`, `totals.line_sum`) so evals can aggregate "which rules fire most".
- **Typed outcome, not exceptions:** `abstract record ExtractionOutcome` with `Succeeded` / `Failed(Kind, Message, LastInvoice?, LastErrors)` and `IReadOnlyList<AttemptRecord>` on both. Exceptions are for bugs and cancellation only. Failure kinds: `InvalidInput | ModelRefused | Truncated | SchemaInvalid | ValidationFailed | ProviderError`.
- **Barcode seam (D-05):** `ExtractionRequest.KnownAccessKey` (optional) feeds the access-key cross-check validators. Decoding the Code 128 barcode in .NET needs a PDF rasteriser plus a decoder library (open research). Recommend deferring the decoder implementation to M3 intake and keeping only the seam plus an `IBarcodeReader` port in M1.
- **PDF input (HIGH, vendor docs):** `document` content block, base64 or Files API; 32 MB request cap, 100 pages when context < 1M tokens; each page is processed as text and image, so image-only degraded scans work but cost more tokens (docs: roughly 1k tokens per 3-page text-only PDF vs 7k when each page is also imaged). Multi-page, many-item invoices are the token-cost driver; budget `max_tokens` for the output of 100+ line items.
- **Versioned prompts and per-request overrides:** the eval request may override `model`, `prompt_version`, `max_repair_attempts`; the response echoes the effective config. "Compare two models or two prompt versions" then becomes two runner invocations against one running server, no redeploy.

### Pattern 6: One-way schema flow C# to JSON Schema to Pydantic, double-checked in CI (D-03)

```
Carimbo.Domain records (+ [Description], snake_case wire policy)
Carimbo.Validators.ValidationError, Carimbo.Extraction attempt/usage types, Carimbo.Api eval DTOs
        │  tools/Carimbo.SchemaTool  (JsonSchemaExporter + TransformSchemaNode)
        ▼
schema/carimbo.schema.json   (committed; one $defs bundle; draft 2020-12; stable key order)
        │  datamodel-codegen (pinned in uv.lock) -> pydantic v2 (default output)
        ▼
python/src/carimbo/models/generated.py   (committed; header without timestamp)
```

- **Exporter (HIGH on API existence, MEDIUM on ergonomics):** `System.Text.Json.Schema.JsonSchemaExporter.GetJsonSchemaAsNode` is in the BCL (.NET 9+, so present in .NET 10) and is options-driven by the same `JsonSerializerOptions` the app uses, so the schema matches the real wire format (naming policy, string enums). `JsonSchemaExporterOptions.TransformSchemaNode` (`Func<JsonSchemaExporterContext, JsonNode, JsonNode>`) is the extension point.
- **Known gap to design around (MEDIUM, from memory of exporter behaviour, spike in Phase 1):** by default the exporter inlines nested types (no `$defs`), so `Party` appears twice (issuer, recipient) and codegen produces `Issuer`/`Recipient`-style duplicate classes. Use `TransformSchemaNode` to hoist each named type into `$defs` and emit `$ref`, and to read `[Description]` into `description` (these descriptions are a real quality lever for the model since they reach the output contract). Fallback if hoisting is fiddly: NJsonSchema. Add the `$schema` URI in the tool.
- **Wire conventions decided once, in Phase 1:** `JsonNamingPolicy.SnakeCaseLower` for properties and enums (Pydantic gets idiomatic names with no aliases); English field names, with the NF-e XML tag mapping documented in datagen (`vNF` -> `total_amount`); enums as strings; dates as `date`; no polymorphism in DTOs (`expected`/`actual` in `ValidationError` are strings).
- **Money representation: decide in Phase 1.** `decimal` exports as JSON `number`; datamodel-codegen emits `float`, so Python graders must not compare floats naively. Options: (a) keep numbers, parse graders' JSON with `parse_float=Decimal`, apply the numeric-tolerance grader (the brief's own tolerance requirement makes this acceptable); (b) string-typed amounts with a pattern. Recommend (a).
- **Two staleness checks, one command (`make schema-check`, run in CI):**
  1. **Fast, local, in `dotnet test`:** a snapshot test regenerates the bundle in memory and compares with `schema/carimbo.schema.json`; the failure message says "run `make schema`". Developers find out before pushing.
  2. **CI, cross-stack:** `make schema` (dotnet run SchemaTool, then `uv run datamodel-codegen ...`) followed by `git diff --exit-code -- schema python/src/carimbo/models`. This is the check that proves the Pydantic hop. Pin `datamodel-code-generator` and the formatter in `uv.lock`: output differs across versions. Verify `--disable-timestamp`, `--reuse-model`, `--use-annotated`, `--check` flags against the pinned version in Phase 1 (they were not confirmed in the docs I could fetch; default output is already Pydantic v2).
- **Ground truth validates against the same schema:** Phase 2's generator runs `jsonschema` (draft 2020-12) over each `expected.json`.
- **Model-facing vs canonical schema:** see Pattern 5; the projector, not a second hand-written schema, is the only derivation.

### Pattern 7: Synchronous eval endpoint as a thin adapter (D-02)

Endpoints (Api, mapped only when `Eval:Enabled=true`; default true in Development, false in Production; static API key header, no tenant auth):

- `POST /eval/extractions`: run extraction.
- `GET /eval/meta`: available models, prompt versions, pricing version, schema hash, build/git SHA, cache mode. The runner stamps this into `run.json` instead of inferring it.
- `GET /healthz`.

**JSON body with base64 PDF, not multipart (opinion):** cases are ~100 KB, base64 overhead is irrelevant, and a JSON request DTO can sit in the schema bundle (typed on both sides). Multipart avoids none of the cost and cannot be described in the schema, and minimal-API form binding adds antiforgery friction. Keep multipart for M3's `POST /invoices`; both just call `IInvoiceExtractor` with bytes.

```jsonc
// POST /eval/extractions
{
  "contract_version": 1,
  "case_id": "case-0042",                 // echoed; span attr carimbo.eval.case_id
  "run_id": "2026-10-03T…-sonnet-p1-abc123", // span attr carimbo.eval.run_id (cost attribution)
  "document": { "media_type": "application/pdf", "content_base64": "…", "sha256": "…" },
  "options": { "model": null, "prompt_version": null, "max_repair_attempts": null, "cache": "default" }
}
// 200 OK  (HTTP 200 whenever the pipeline ran, including a typed extraction failure)
{
  "contract_version": 1, "case_id": "case-0042", "trace_id": "4bf9…",
  "effective": { "model": "…", "prompt_version": "extract.v1", "max_repair_attempts": 2,
                 "schema_hash": "sha256:…", "pricing_version": "…", "build": "git-sha", "cache_mode": "read_write" },
  "outcome": { "status": "success", "invoice": { /* Invoice */ }, "failure": null },
  "validation": { "final_errors": [ /* ValidationError */ ], "passed_first_try": false },
  "attempts": [ { "number": 1, "kind": "initial", "validation_errors": [ … ],
                  "usage": { "input_tokens": 0, "output_tokens": 0, "cache_read_tokens": 0, "cache_write_tokens": 0 },
                  "cost_usd": 0.0, "billed_cost_usd": 0.0, "latency_ms": 0, "original_latency_ms": 0,
                  "cache_hit": false, "stop_reason": "end_turn", "model": "…" } ],
  "totals": { "input_tokens": 0, "output_tokens": 0, "cost_usd": 0.0, "billed_cost_usd": 0.0,
              "latency_ms": 0, "cache_hits": 0 },
  "barcode": { "access_key": null }       // reserved for D-05; null in M1
}
```

Contract rules:
- **Pipeline failure is a 200 with `outcome.status = "failure"`**, including `provider_error` after retries. Use **4xx** for malformed requests/auth, **5xx** only for bugs in carimbo itself. The runner then separates three classes in summaries and never mixes them: *pipeline said no* (counts against accuracy), *harness/transport error* (excluded and reported), *cache miss under ReplayOnly* (not evaluated).
- Additive evolution only, with `contract_version` integer. Python parses responses with `extra="ignore"`, but ground truth/invoices with `extra="forbid"`.
- The endpoint bypasses Temporal/idempotency by design (D-02) but calls the identical `IInvoiceExtractor` the M3 activity will call.
- **Contract test across stacks:** `Carimbo.Api.Tests` (WebApplicationFactory + scripted fake gateway) writes golden responses to `testdata/contract/*.json` (success, repaired, validation-failed, refusal, cache-hit); Python tests parse them with the generated models. This catches enum casing, null handling and decimal formatting drift that schema-by-construction cannot.
- Sync calls take 10-120 s for multi-page scans with repairs. Set the client timeout (~180 s), cap runner concurrency, and let the gateway absorb 429s. No streaming needed.
- **Forward-compat (M2):** add `POST /eval/decisions` with its own response type in the same bundle; the Python runner is generic over a `Target(endpoint, build_request, graders)` abstraction from day 1 (a few lines now, avoids a rewrite).

### Pattern 8: Eval runner as a pipeline of pure, separately runnable stages

```
datasets/<id>/<ver>/manifest.jsonl + dataset.json + subsets/*.txt
   │ load()            Case{ id, pdf_path, pdf_sha256, expected: Invoice, tags }
   ▼
 run     asyncio + httpx.AsyncClient + Semaphore(N) + per-case timeout
   │      (retry ONLY transport errors, bounded; never retry a pipeline outcome)
   │      streaming append to runs/<run_id>/cases.jsonl (flush per case: crash-safe, `--resume`)
   ▼
 grade   pure fn: (expected, response) -> [GraderResult]; re-runnable on existing JSONL, no API calls
   ▼
 summarize  pandas -> runs/<run_id>/summary.json  ──copy──► evals/reports/<run_id>/summary.{json,md}  (COMMIT)
   ▼
 compare A B   join on case_id -> per-field deltas, regressed / improved cases, cost & latency deltas
 check         summary + thresholds/ci.yaml (+ optional baseline tolerance) -> exit 1 on breach
 report        committed summaries -> README tables/charts
```

**JSONL record (one line per case, versioned `record_version`)**, per D-15: `run_id, case_id, tags, request{config echo, pdf_sha256 (no bytes)}, http{status, wall_ms, error?}, response{outcome, invoice, validation, attempts, totals, trace_id}, grades[{name, score, passed, detail}]`. **Store the raw returned invoice, not just scores**, so graders can be changed and re-run offline (`carimbo eval grade --run X`). That is the second cache layer: the server-side LLM cache makes pipeline reruns free, the stored responses make grader reruns free.

**`run.json`:** run id, UTC timestamps, dataset id/version/content-hash, subset, `effective` config from `/eval/meta`, git SHA, concurrency, pricing version, harness version, cache mode.

**Graders (all pure, unit-tested on hand-built invoice pairs):**

| Grader | Compares | Notes |
|--------|----------|-------|
| `schema_validity` | response invoice vs generated Pydantic model (strict) **and** `jsonschema` | Independent of the .NET check; exercises canonical constraints |
| `exact_match` | access key, issuer/recipient CNPJ, number, series, dates, IDs | Per-field boolean; digits-only normalization only |
| `numeric_tolerance` | amounts, tax values | `Decimal`, absolute tolerance (e.g. 0.01) configured in a grader config, not in code |
| `line_items` | item alignment then per-field | Align by item number when present, else greedy/Hungarian on (code, description similarity, amount); report item-level precision/recall/F1 plus per-field accuracy; penalise missing/extra items |
| `validator_clean` / `repaired` | from the response, no re-implementation | Free signals (D-05); tracks first-try validity and repair rate |

**Summary content:** per-grader mean and pass-rate with counts (n), per-field accuracy, breakdown by dataset tag (pages, tax regime, degradation, barcode readability), failure-kind counts, repair rate, cost (notional and billed), latency p50/p95 (original latency), tokens, cache hit rate, harness-error count.

**Committed-summary hygiene (D-15):** summaries must be byte-stable for identical input: sort by `case_id` (JSONL order is completion order), round floats, keep volatile fields (timestamps, host) in `run.json`/a `meta` block excluded from compare. Otherwise git history of `evals/reports/` is noise.

**Compare:** refuse (or loudly warn) when dataset content hashes differ; show new/missing cases; report **counts, not just percentages** (n=150 or a ~20-case CI subset cannot support significance claims; a paired bootstrap or McNemar-style note on regressed/improved counts is a cheap credibility win).

**Gate:** per-grader absolute floors in `evals/thresholds/ci.yaml`, derived from the first baseline minus a margin (record the derivation), optionally plus "no more than K regressed cases vs committed baseline". With a warm cache runs are deterministic, so thresholds can be tight.

### Pattern 9: Dataset versioning and byte-stability

- **Identity:** `datasets/nfe-synth/v1/dataset.json` = `{dataset_version, generator_version, seed, schema_hash, case_count, content_hash}`; `manifest.jsonl` has per-case `{id, seed, tags, xml_sha256, pdf_sha256, expected_sha256}`; `content_hash` = SHA-256 over the sorted per-file hashes. Eval runs record `dataset id + content_hash`.
- **Commit the generated dataset** if it stays under a size budget (~25 MB: 150 PDFs, mostly small; low-DPI scans help). Reason: the LLM cache key contains the PDF hash, so cache hits, CI subset gating and reviewer reproduction all require the exact same bytes, and "clone and run, no generation step" matches the Definition of Done. Fallback if it grows: commit only the CI subset plus manifest, and add `datagen verify` that regenerates and compares hashes.
- **Make rendering deterministic anyway** (invariant PDF metadata/IDs/dates in the PDF library, seeded numpy/Pillow for degradation, pinned library versions) and test it: `datagen verify` regenerates N cases and compares SHA-256 to the manifest. PDF byte-reproducibility across library versions is the fragile part: record library versions in `dataset.json`.
- **Subsets:** `subsets/ci.txt` is a fixed, committed, stratified id list; a pytest asserts it covers single/multi-page, many-items, each tax regime, degraded and barcode-unreadable tags. Never "first N" or random at CI time.
- **Cross-stack conformance:** a `Carimbo.Validators.Tests` test loads every `expected.json` and asserts zero Error-severity validation errors (except cases tagged `intentionally_invalid`). Combined with shared `testdata/vectors/*.json` read by both pytest and xUnit, this catches the generator and the validators agreeing on a wrong CNPJ/access-key algorithm. The generator must implement check-digit math to *produce* valid keys; that is data generation, not pipeline logic, and the shared vectors keep it honest.

---

## Data Flow

### Eval request flow (M1 critical path)

```
Python runner                          Carimbo.Api                  Extraction                 Llm gateway               Anthropic
  case(pdf, expected)
  mint traceparent ───POST /eval/extractions──►  auth, map DTO
                                                 Activity (server span, trace id)
                                                      │──ExtractAsync──►  pre-flight
                                                      │                   attempt 1 ──CompleteAsync──► Telemetry span
                                                      │                                                  └► Cache lookup
                                                      │                                                       hit  → recorded response (cost recomputed)
                                                      │                                                       miss → Retry → SDK ──HTTPS──► messages (document + output_config.format)
                                                      │                   ◄── LlmResponse (usage, cost, stop_reason)
                                                      │                   parse → Validators → errors?
                                                      │                   repair attempt 2..n (same path)
                                                 ◄── ExtractionOutcome + AttemptRecords
  ◄─────────── 200 EvalExtractResponse (invoice, validation, attempts, totals, trace_id)
  append cases.jsonl → grade → (later) summarize/compare/gate
```

### Key Data Flows

1. **Schema flow (build time):** Domain + contract types → SchemaTool → `schema/carimbo.schema.json` → datamodel-codegen → `models/generated.py`. Consumers: Extraction (model contract), Python datagen/evals, `jsonschema` checks.
2. **Dataset flow:** datagen builds typed `Invoice` objects (generated Pydantic) from a seed → writes `{case}.xml` (ground truth), `{case}.expected.json` (derived, test-asserted equal to parsed XML), `{case}.pdf` (DANFE + Code 128) and degraded variants → `manifest.jsonl` + hashes. Evals consume only the dataset directory, never datagen code.
3. **Cost flow:** gateway usage x pricing → per-attempt cost → Extraction totals → eval response → JSONL → summary. Same numbers appear on the span (`carimbo.llm.cost_usd`) for per-trace cost (D-17). No second accounting path.
4. **Trace flow:** runner `traceparent` (or server-generated) → server span → extraction/attempt spans → one gateway span per call → `trace_id` in response and JSONL (D-15) → click-through to Jaeger.
5. **Quality-history flow:** `runs/*.jsonl` (ignored) → `evals/reports/<run_id>/summary.*` (committed) → `compare` → README results table.

---

## CI Design (GitHub Actions)

| Job | Trigger | What | Secrets | Cost |
|-----|---------|------|---------|------|
| `dotnet` | every PR/push | `setup-dotnet` from `global.json`, NuGet cache, `dotnet build -warnaserror`, `dotnet test` (unit + Api tests with fake gateway; `Category=Live` tests excluded), `dotnet format --verify-no-changes` | none | free |
| `python` | every PR/push | `setup-uv` (cache), `uv sync --frozen`, `ruff check` + `ruff format --check`, pyright/mypy, `pytest` (graders, loader, manifest/hash check, contract-fixture parsing, ground-truth vs schema, runner vs mocked HTTP) | none | free |
| `contracts` | every PR/push | `make schema` then `git diff --exit-code -- schema python/src/carimbo/models` | none | free |
| `eval-smoke` | every PR/push | Start Api with `Llm:Cache:Mode=ReplayOnly` + committed CI-subset cache fixtures, run runner on `subsets/ci.txt`, run `check`. Proves the whole wiring end to end at $0 | none | free |
| `eval-gate` | PR touching pipeline/prompt/schema/graders/dataset paths, from the same repo | Same as smoke but `ReadWrite` against the real API for cache misses (prompt/model changed), `actions/cache` restoring the LLM cache dir, `--max-cost-usd` budget | `ANTHROPIC_API_KEY` | small, bounded |
| `eval-full` | `workflow_dispatch` only | Full dataset, inputs: model, prompt version, dataset; uploads JSONL as artifact, writes compare table to `$GITHUB_STEP_SUMMARY` | `ANTHROPIC_API_KEY` | explicit, manual |

Design points:

- **Two eval tiers is the cost-control design.** Tier 1 (`eval-smoke`, replay-only, committed fixtures) gates *pipeline, validators, schema and grader changes* for free and without secrets, and works on fork PRs. Tier 2 (`eval-gate`, live on cache misses) gates *prompt/model changes*. A replay miss on a fork PR is a reported "not evaluated" with a clear message, not a silent pass.
- **Secrets:** `ANTHROPIC_API_KEY` exposed only to the steps that need it; the gate job guarded with `github.event.pull_request.head.repo.full_name == github.repository`; never `pull_request_target` combined with checking out PR code; `permissions: contents: read`; `concurrency: group: eval-${{ github.ref }}, cancel-in-progress: true`; `timeout-minutes`. Also set a hard spend limit on the Anthropic workspace in the console (defence in depth against a bug in the harness budget).
- **Cost control layers:** fixed ~15-25-case stratified subset (size is an open question; derive from `cases x avg attempts x tokens x price` once a baseline exists) → response cache restored by `actions/cache` (content-addressed entries, so stale ones are harmless: restore with prefix keys, save under a unique key; caches saved on `main` are readable from PRs) → cheapest adequate model for the gate, fixed in `thresholds/ci.yaml` → runner `--max-cost-usd` abort using billed cost → path filters.
- **Path filters + required checks pitfall:** a workflow skipped by `paths:` leaves a required check "pending" forever. Use an always-running `changes` job (e.g. `dorny/paths-filter`) and let the gate job succeed trivially when nothing relevant changed.
- **API process in CI:** `dotnet run` (or published output) in the background, wait on `/healthz` with `curl --retry`, OTel exporter off (`OTEL_SDK_DISABLED=true`). No Postgres/Jaeger in M1 CI.
- **Pin tool versions** (`global.json`, `uv.lock`, action versions by SHA or major) so the schema staleness check does not fail on tool drift.

---

## Suggested Build Order (with dependencies)

Maps onto brief Phases 1-4. Items marked ∥ can run in parallel with their siblings.

| # | Step | Needs | Notes / de-risks |
|---|------|-------|------------------|
| **Phase 1: Foundation** | | | |
| 1.1 | Repo skeleton: sln + empty projects, `Directory.*.props`, `global.json`, `python/` uv project, Makefile, `.gitignore`, AGENTS.md/CLAUDE.md, CI that builds+tests both stacks (trivially green) | none | Establish CI first; every later step lands behind it |
| 1.2 | Domain records, value objects, wire conventions (snake_case, string enums, `[Description]`), money decision | 1.1 | Everything downstream depends on this shape; keep it flat and non-recursive |
| 1.3 | SchemaTool (hoist to `$defs`, descriptions, `$schema`) + snapshot test + committed `schema/` | 1.2 | **Spike first**: exporter hoisting vs NJsonSchema fallback |
| 1.4 | Pydantic codegen, generated models committed, `make schema`, `contracts` CI job | 1.3 | **Spike**: verify codegen flags and deterministic output |
| 1.5 ∥ | Validators + `ValidationError`/`Severity`/rule codes + shared `testdata/vectors` + unit tests | 1.2 (not 1.3/1.4) | Independent of schema tooling; can start as soon as Domain exists |
| 1.6 | Architecture tests (Domain purity), agent-runtime config verified under Claude Code and OpenCode | 1.1 | Small; closes the phase |
| **Phase 2: Synthetic dataset (Python only)** | | | |
| 2.1 | Seeded sampler producing `Invoice` objects (valid CNPJ/key math, regimes, item counts) | 1.4, 1.5 vectors | |
| 2.2 | NF-e XML writer + XML to Invoice round-trip + `jsonschema` validation of ground truth | 2.1 | Ground truth correctness is the root of all eval validity |
| 2.3 | DANFE PDF renderer: Code 128, multi-page, many items | 2.1 (**library research**) | Determinism requirement affects library choice |
| 2.4 | Degradation pipeline (rotation, blur, low DPI, barcode-unreadable), seeded | 2.3 | |
| 2.5 | Manifest, hashes, subsets, `datagen verify`, .NET conformance test over `expected.json` | 2.2-2.4, 1.5 | Closes the loop with the validators |
| **Phase 3: Extraction pipeline** | | | |
| 3.1 ∥ | `Llm`: contracts, pricing table, cache key + normalization + golden vectors, file store + single-flight, retry, telemetry, Anthropic adapter | 1.1 only | **Can start during Phase 2**; needs no dataset |
| 3.2 | `Extraction`: prompts v1, `ModelSchemaProjector`, repair loop, typed outcomes; scripted-gateway tests (valid first try, repair success, budget exhausted, refusal, truncation, invalid input) | 1.3, 1.5, 3.1 | The core logic; fully testable offline |
| 3.3 | `Api`: DI/OTel wiring, auth, `/eval/*`, DTOs added as SchemaTool roots, regenerate Pydantic, emit `testdata/contract/*` | 3.2 | SchemaTool must already accept a list of root types (design it so in 1.3) |
| 3.4 | Live smoke on 3-5 dataset PDFs (manual, `Category=Live`), first cache entries, prompt iteration | 3.3, 2.3 | **Research flag:** structured output + document block + raw schema in the C# SDK; prompt caching thresholds |
| **Phase 4: Eval harness v1** | | | |
| 4.1 ∥ | Graders (pure) + unit tests; line-item matcher | 1.4 (needs only generated models) | **Can start right after 1.4**, parallel to Phases 2-3 |
| 4.2 | Dataset loader + case model | 2.5 | |
| 4.3 | Runner (async, semaphore, JSONL streaming, resume, `--max-cost-usd`) + `grade` re-grade command | 3.3 contract models, 4.1, 4.2 | Test against mocked HTTP and the contract fixtures |
| 4.4 | summarize, `compare`, `check`/thresholds, byte-stable committed summaries | 4.3 | |
| 4.5 | CI: `eval-smoke` (replay fixtures), `eval-gate`, `eval-full` | 4.4, 3.4 | Needs seeded CI-subset cache fixtures from a live run |
| 4.6 | First results: two models or two prompt versions, committed summaries, `reports` tables, optional committed cache fixtures for replay | 4.4, API key + budget | Calibrate thresholds from this run, then switch the gate on |

**Critical path:** 1.2 → 1.3 → 1.4 → 2.1 → 2.3 → (3.4 smoke) → 4.5 → 4.6, with 1.2 → 1.5 → 3.2 → 3.3 → 4.3 → 4.4 feeding it.

**Roadmap implications:** keep the brief's four phases but (a) keep Phase 2 before Phase 3 for the extraction work, but the `Llm` gateway (3.1) has no dataset dependency and can start alongside Phase 2 if phases are overlapped; (b) treat graders (4.1) as early work since they depend only on 1.4; (c) put the schema/contract spike at the very start of Phase 1; (d) split Phase 3 into "gateway" and "extraction + eval endpoint" plans.

### M2 / M3 forward-compatibility (what M1 must already do)

| Future need | M1 provision |
|-------------|--------------|
| M2 agent loop (D-08) reuses the gateway | `Llm` has no Domain dependency; content-block message model; tool blocks additive; cache-key versioned |
| M2 `Decision` evals | Runner `Target` abstraction; per-endpoint response types in one schema bundle; `Decision` already in Domain |
| M2 MCP tools / Postgres | Separate `Tools` host project later; cache store stays file-based; no DB in M1 |
| M3 Temporal activities (D-11) | `IInvoiceExtractor` stateless, records in/out, JSON-serializable, `CancellationToken`, `TimeProvider`; no ambient state; M3 passes a document reference (not bytes) through payloads and loads inside the activity |
| M3 idempotency (D-12) | `ExtractionRequest.KnownAccessKey`, `barcode.access_key` reserved in contract; eval endpoint explicitly bypasses idempotency |
| M3 cost per invoice/run | `run_id`/`case_id` span attributes and per-call cost on spans exist from M1 |

---

## Scaling Considerations

For this project "scale" means eval size and spend, not users.

| Scale | Architecture Adjustments |
|-------|--------------------------|
| ~20-case CI subset | Replay fixtures + incremental live misses; runner concurrency 4; wall time minutes |
| 150-case full dataset | Concurrency 4-8, bounded by Anthropic rate limits (429 handled by gateway backoff with `retry-after`); JSONL tens of MB, ignored; summary KBs; a full run costs real money on first pass, ~$0 on replay |
| 1.5k+ cases or many model variants | Move cache fixtures out of git (artifact/release or LFS), shard runs by tag, add Batch API for non-latency-sensitive runs (changes the eval-endpoint story: a deliberate later decision, not M1) |

### Scaling Priorities

1. **First bottleneck: API rate limits and wall time** on multi-page scans with repairs. Fix: bounded semaphore, 429-aware retry, `--resume`, cache.
2. **Second bottleneck: token cost of image-processed PDF pages.** Fix: prompt-cache the document block across repair attempts, cheaper model for the PR gate, small stratified subset.

---

## Anti-Patterns

### Anti-Pattern 1: Hand-synced contracts
**What people do:** write the Pydantic models and the eval DTOs by hand "to match".
**Why it's wrong:** silent drift is exactly what D-03 exists to prevent; evals then grade a different shape than production emits.
**Do this instead:** one generated bundle (Invoice + eval contract), two staleness checks, cross-stack golden fixtures.

### Anti-Pattern 2: SDK `Create<T>()` auto-schema as the contract
**What people do:** pass a CLR type to the SDK and let it derive the schema.
**Why it's wrong:** bypasses the committed schema; ground truth and model contract can diverge; unsupported constraints are folded in invisibly.
**Do this instead:** committed canonical schema, pure `ModelSchemaProjector`, raw `OutputConfig.Format`.

### Anti-Pattern 3: Cache keys with volatile or oversized inputs
**What people do:** hash the raw request object (includes base64 PDFs, run ids, timestamps) or leave defaults implicit.
**Why it's wrong:** zero hit rate, or hits that differ by machine/culture; multi-MB hashing; cache entries unreadable.
**Do this instead:** canonical JSON, documents by content hash, defaults resolved, context excluded, golden key vectors, `CacheKey.Version`.

### Anti-Pattern 4: Double retry, or retry inside the cache's miss accounting gap
**What people do:** keep SDK retries and add Polly on top; or put the cache inside retry.
**Why it's wrong:** multiplicative attempts, invisible spend, impossible-to-test timing.
**Do this instead:** SDK `MaxRetries = 0`, single retry policy, `TimeProvider`, attempts visible as span events and in `HttpAttempts`.

### Anti-Pattern 5: Trusting structured output as "always valid"
**What people do:** deserialize whatever comes back.
**Why it's wrong:** `refusal` (billed, may violate schema) and `max_tokens` (truncated) both produce non-conforming output.
**Do this instead:** inspect `stop_reason` first; typed `ModelRefused`/`Truncated` failures; strict parse; count `SchemaInvalid` as a metric.

### Anti-Pattern 6: Repair prompts that say "make the validator pass"
**What people do:** feed errors back with imperative "fix these".
**Why it's wrong:** teaches the model to fabricate consistent numbers.
**Do this instead:** "re-read the document, change only what it supports"; measure `repaired` rate and ground-truth accuracy post-repair.

### Anti-Pattern 7: Grading inside the run loop only
**What people do:** compute scores while calling the API and keep only scores.
**Why it's wrong:** every grader tweak costs a rerun; no failure analysis.
**Do this instead:** store raw responses in JSONL; `grade` is a separate pure stage.

### Anti-Pattern 8: Mixing failure classes in the accuracy number
**What people do:** count timeouts, 5xx and refusals as the same "wrong".
**Why it's wrong:** an infra blip looks like a quality regression and trips the gate.
**Do this instead:** pipeline failure (200 + typed failure) vs harness error vs not-evaluated; report separately; gate on pipeline outcomes.

### Anti-Pattern 9: Naive cost math and cached-run latency
**What people do:** `input_tokens x price`; quote latency from cached reruns.
**Why it's wrong:** ignores cache-read/write price classes; reports 3 ms "latency" and $0 "cost".
**Do this instead:** four token classes, notional vs billed cost, `original_latency_ms`.

### Anti-Pattern 10: Float money and float comparison in graders
**What people do:** `float` amounts, `==` comparisons.
**Why it's wrong:** spurious mismatches or hidden ones.
**Do this instead:** `Decimal` parsing in graders, explicit tolerance config; `decimal` in C#.

### Anti-Pattern 11: Live-API tests in the default test run
**What people do:** unit tests that call the model.
**Why it's wrong:** flaky, costs money, fails for reviewers without keys.
**Do this instead:** scripted fake gateway for all logic; `[Trait("Category","Live")]` opt-in smoke tests.

### Anti-Pattern 12: Eval-only behaviour reachable in production
**What people do:** ship cache bypass flags or an eval endpoint enabled everywhere.
**Why it's wrong:** violates D-07 (cache off in prod) and leaks an unauthenticated expensive endpoint.
**Do this instead:** `Eval:Enabled` default false outside Development, `Cache:Mode=Off` default, per-request cache directives honoured only on eval endpoints.

---

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Anthropic Messages API | Official `Anthropic` NuGet (v10+) behind `Carimbo.Llm`; `document` block (base64) + `OutputConfig.Format` json_schema | PDF: 32 MB / 100 pages per request; refusal and `max_tokens` break schema guarantees; model ids/pricing from config, not code |
| OTLP collector / Jaeger | OTel SDK configured only in `Carimbo.Api`; libraries use `ActivitySource` | Optional in M1 (`make up`), required for M3 acceptance |
| GitHub Actions | `actions/cache` for LLM cache, `$GITHUB_STEP_SUMMARY` for compare tables | Secrets only on same-repo PRs and manual runs |
| `datamodel-code-generator` | Pinned dev dependency run by `make schema` | Output differs across versions; verify flags in Phase 1 |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Python evals ↔ .NET Api | HTTP/JSON, schema-bundle DTOs, `contract_version` | Additive changes only; golden fixtures guard serialization |
| Extraction ↔ Llm | `ILlmGateway` in-process | Extraction never sees SDK types or the cache |
| Extraction ↔ Validators | Direct call, returns error list | No exceptions for rule failures |
| Api ↔ Extraction | `IInvoiceExtractor` | Same service M3's activity will call |
| datagen ↔ evals | Files only (`datasets/`) | Neither imports the other |
| C# ↔ Python (build time) | `schema/*.json`, `testdata/*` | Single sources of truth, CI-enforced |

---

## Research Flags for Phases

- **Phase 1:** needs a short spike: (a) `JsonSchemaExporter` + `TransformSchemaNode` hoisting to `$defs` with descriptions (fallback NJsonSchema), (b) datamodel-codegen flags and byte-stable output, (c) money representation, (d) `.slnx` tooling support in the chosen CI/IDE setup. Everything else is standard.
- **Phase 2:** needs library research (DANFE/PDF rendering with deterministic output, Code 128, raster degradation) and an NF-e 4.00 XML layout reference. Barcode *decoding* library choice can wait for M3.
- **Phase 3:** needs targeted research on the exact C# SDK shapes for document blocks + raw `OutputConfig.Format` schema + `cache_control`, per-model prompt-cache thresholds, and a GenAI semconv version pin. Retry/cache/telemetry patterns are standard.
- **Phase 4:** standard patterns except the line-item matching algorithm and threshold calibration (data-dependent).

## Open Questions Touched (answered with opinion; confirm in discuss phases)

| Question (DECISIONS.md) | Recommendation |
|-------------------------|----------------|
| .NET model SDK | Official `Anthropic` SDK directly at the bottom of `Carimbo.Llm`; `IChatClient` adapter only if MCP interop needs it later |
| Local services | M1 needs none; optional Jaeger via a compose file or devenv process; decide in M2 when Postgres appears |
| CI eval subset size | ~15-25 stratified cases, fixed ids in `subsets/ci.txt`; finalize after the first baseline cost is known |
| Thresholds | Derive per grader from first baseline minus a documented margin |

## Sources

- Anthropic structured outputs (request shape, schema limits, refusal/max_tokens, prompt-cache invalidation, C# `Create<T>`): https://platform.claude.com/docs/en/build-with-claude/structured-outputs (vendor docs, fetched 2026-10-03)
- Anthropic PDF support (limits, text+image processing, base64/Files API): https://platform.claude.com/docs/en/build-with-claude/pdf-support (vendor docs)
- Anthropic C# SDK (official `Anthropic` package v10+, retries, timeouts, IChatClient, raw responses): https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp and https://github.com/anthropics/anthropic-sdk-csharp
- `JsonSchemaExporterOptions` (`TreatNullObliviousAsNonNullable`, `TransformSchemaNode`): https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Text.Json/src/System/Text/Json/Schema/JsonSchemaExporterOptions.cs ; `GetJsonSchemaAsNode` API listing for .NET 8/9/10 via Microsoft Learn search results (learn.microsoft.com itself was egress-blocked in this environment)
- OpenTelemetry GenAI semantic conventions (usage token attributes, Development status, content as opt-in): search results incl. https://opentelemetry.io/blog/2026/genai-observability/index.md and https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-spans (spec page itself blocked; attribute names MEDIUM, re-verify when pinning)
- datamodel-code-generator (Pydantic v2 output default, `--output-model-type`): https://pypi.org/project/datamodel-code-generator/ and docs search results; specific flags (`--disable-timestamp`, `--reuse-model`, `--check`) UNVERIFIED, confirm in Phase 1
- Project inputs: `/home/user/carimbo/docs/PROJECT-BRIEF.md`, `/home/user/carimbo/docs/DECISIONS.md`, `/home/user/carimbo/.planning/PROJECT.md`

---
*Architecture research for: LLM extraction pipeline + eval harness (carimbo, Milestone 1)*
*Researched: 2026-10-03*
