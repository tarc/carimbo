# Phase 3: Replayable Runs - Research

**Researched:** 2026-10-15
**Domain:** .NET LLM gateway decorators (response cache, tracing, retry ownership), OpenTelemetry to Jaeger v2, eval-endpoint contract evolution, cross-stack golden fixtures
**Confidence:** HIGH for the codebase map and for the behaviours I executed (SDK retry, OTel sampling, OTLP to Jaeger); MEDIUM for the GenAI semantic-convention attribute names (status "Development", no tagged release)

## Summary

Phase 3 adds four things around the gateway that Phase 1 and 2 built, and none of them needs a new architecture. (1) A cache decorator over `ILlmGateway` (D-19) keyed on the canonical final request, with four modes and on-disk JSON under a git-ignored directory. (2) A tracing decorator that emits exactly one `ActivitySource` span per `CompleteAsync` call, with model, tokens and cost and with no content. (3) Retry ownership: the Anthropic SDK's own retry becomes the single policy (`MaxRetries` default 2, bounded), and a test with a failing fake transport proves no second layer exists. (4) Per-request overrides on `POST /eval/extractions`, an eval contract bump, and golden response fixtures emitted by xUnit and parsed by pytest.

The existing seams already carry most of the load. `CarimboApi.CreateApp` decorates whichever `ILlmGateway` the host registered with `CostAccountingLlmGateway`, so the cache and tracing decorators slot in the same way and apply to the real adapter, the scripted host and test stubs alike. `AnthropicLlmGateway` already counts HTTP attempts per call and `RecordingHandler` in `AnthropicGatewayTests.cs` is a ready failing-transport fake. `EvalResponse.effective` already echoes model, prompt version, max repairs and the schema hash. The Python side already grades from stored dicts and the runner already mints a sampled W3C `traceparent` per case. What is missing is listed per success criterion in the Requirement Map below.

Three findings change the plan and were verified by running code in this session: the SDK retries 5xx, 408, 409, 429 and connection errors with ~0.4 s / ~1 s / ~1.8 s backoff and honours `retry-after`, `retry-after-ms` and `x-should-retry` (so one policy already exists and is good enough); a bare `new Activity(...)` root, which is what `EvalEndpoint` creates when nothing else did, is **not sampled** by the OpenTelemetry SDK and silently drops every child model-call span, so the eval root span must come from an `ActivitySource`; and Jaeger v2.22.0 receives OTLP gRPC on 127.0.0.1:4317 from the .NET exporter with only `OTEL_EXPORTER_OTLP_ENDPOINT` set, and serves the trace at `/api/traces/{traceId}`.

**Primary recommendation:** Compose the gateway chain as `Tracing( Caching( CostAccounting( AnthropicLlmGateway ) ) )` inside `CarimboApi.CreateApp`, make the cache opt-in by an explicit `Llm:Cache:Directory` (never on by environment default, so existing hermetic tests stay hermetic), let the SDK own retries, add the overrides as a typed `overrides` object on a new contract version "3", and prove each criterion first with an in-process vertical tracer (cache write, then read-only replay with a counting inner gateway) before building breadth.

## User Constraints

No CONTEXT.md exists for this phase (the user continued without discuss-phase). The binding constraints are the accepted decision records and requirements. They are quoted from the files I read this session.

### Locked by decision records (docs/DECISIONS.md)

- **D-19** (refines D-07): "The cache key is SHA-256 over the canonical final request: model, parameters, schema hash, prompt version, PDF SHA-256 and a replicate salt. Modes are read-write, read-only (replay) and refresh. Hits report the original latency and cost, and summaries show incurred versus notional cost and the hit rate. Truncated or refused responses are never cached. The production configuration runs with the cache off." Rejected: "The built-in distributed-cache chat client (no control over the key, no original cost)."
- **D-21**: "The bottom adapter is the official Anthropic SDK used directly (`client.Messages.Create`) behind `ILlmGateway` ... Phase 3 (LLM-01) owns the single retry policy, in one place, and must not stack a second one on top of the SDK's." Also: "send the alias ... in development, always record both `model_requested` and `model_returned`, and pin the dated snapshot (`claude-haiku-4-5-20251001`) for published eval runs." And: "The request carries no sampling, tool-choice, thinking or cache-control fields in Phase 1."
- **D-24** rejected, for Phase 2: "Per-request overrides of the repair budget: Phase 3." and "Keeping '1' with additive fields: a consumer that does not know the new statuses would grade them wrongly." (the precedent for bumping the contract version).
- **D-17**: "One trace per invoice spans API, workflow, activities, model and tool calls. Spans carry token counts and cost. Eval records carry trace IDs."
- **D-02**: evals call the real pipeline over HTTP. **D-14**: synthetic data only. **D-26**: Phase 3 goes first because its cache and traces make agent runs (Phase 3.1) replayable.

### Claude's Discretion

All implementation choices not fixed above: decorator order, cache entry format, key field list, override wire shape, contract version, fixture location, prompt catalog contents, compose file layout. Recommendations are given below and the genuinely open ones are in Open Questions.

### Deferred Ideas (OUT OF SCOPE)

From REQUIREMENTS.md and ROADMAP.md: committed cache fixtures for a published run (CI-04, Phase 6), the replay PR gate (CI-02, Phase 6), confidence intervals and the cache hit rate as a graded statistic (EVAL-05, Phase 5), replicate analysis (MEAS-02), the MCP tools and agent loop (Phase 3.1), Postgres, Temporal, LLM-as-judge. Phase 3 only has to make the cache directory relocatable and the key stable enough that Phase 6 can commit fixtures.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| LLM-01 | All model calls go through a gateway with a single (non-stacked) retry policy with backoff | SDK `MaxRetries` is the one policy (executed probe below); tests with `RecordingHandler` count attempts; a guard test forbids Polly / `Http.Resilience` references |
| LLM-03 | One OTel span per call with model, tokens and cost, no prompt/response/PDF content | `TracingLlmGateway` over an `ActivitySource`, allowlisted tag set, in-memory assertions; OTLP to Jaeger verified |
| LLM-04 | Request-hash cache (SHA-256 over canonical final request incl. replicate salt), on in dev/eval, off in production | `CachingLlmGateway` + key builder with pinned vector; opt-in by `Llm:Cache:Directory`; startup failure if configured in Production |
| LLM-05 | Read-write, read-only (replay) and refresh modes; hits report original latency and cost; truncated/refused never cached | Mode matrix, eligibility = `EndTurn` only, entry stores latency and cost, typed `cache_miss` for read-only misses |
| EXT-05 | Prompts versioned; prompt version in cache key and result metadata | `LlmRequest` gains prompt-version fields; prompt catalog; metadata already in `effective` and `attempts[]` |
| REPO-05 | One docker compose command starts a local trace viewer | `compose.yaml` with `cr.jaegertracing.io/jaegertracing/jaeger:2.22.0`, loopback ports; executed against this machine's Docker |
| API-02 | Per-request overrides (model, prompt version, max repairs, cache mode, replicate salt), echoed as effective config | Typed `overrides` object, field-specific 400s, model allowlist = pricing table, `effective` gains `cache_mode` and `replicate_salt` |
| API-04 | Golden response fixtures emitted by .NET tests are parsed by pytest | xUnit scenario matrix writes `data/contract/eval-responses/*.json`; Pydantic `extra="forbid"` models in `carimbo_evals`; compare-mode in dotnet, parse in pytest |
</phase_requirements>

## Project Constraints (from CLAUDE.md / AGENTS.md)

- English for code, docs and commit messages. Pin exact versions; no floating ranges. LF endings (`.editorconfig`).
- Wire format: snake_case names, string enums, money as strings matching `^-?[0-9]+\.[0-9]{2}$`. New cost fields follow this (strings, never JSON numbers).
- The Domain stays pure; `Wire.Options` drives schema export and model-output parsing. Do not touch it for HTTP contract work (the endpoint uses the ASP.NET `HttpJsonOptions`, configured in `Program.cs`).
- Generated files are never hand-edited (`schema/*.json`, `python/src/carimbo_models/generated.py`, `data/skeleton/*`). The new golden fixtures are also generated; treat them the same and say so in AGENTS.md.
- Model output is not reproducible run to run: the cache and replay fixtures are the reproducibility mechanism. Never set `Temperature`/`TopP`, never force `tool_choice`, never prefill.
- One retry owner (D-21): no Polly, no `Http.Resilience` on top of the SDK. `UseDistributedCache` must not be the response cache (D-19).
- No FluentAssertions 8.x; xUnit `Assert`. xUnit v3 on Microsoft.Testing.Platform. pytest markers `e2e` and `live`.
- Provider key only from `CARIMBO_ANTHROPIC_API_KEY` then `ANTHROPIC_API_KEY`; agents check presence, never print. Live paid steps only through capped `just` recipes (US$1.00 per run, US$5 per phase). CI uses no provider key.
- `just check` must stay the one offline command; any new recipe mentioned in README/AGENTS must exist (`test_every_documented_just_recipe_exists`).
- Work goes through a GSD workflow before file edits (executor concern, not research).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Response cache (key, modes, store) | API / Backend (`Carimbo.Llm` decorator) | Storage (local JSON files) | The key must see the final `LlmRequest`, below the extractor and above the provider; D-19 forbids framework cache clients |
| Retry with backoff | API / Backend (Anthropic SDK inside `AnthropicLlmGateway`) | none | One owner. Placing it anywhere else stacks (D-21) |
| Model-call spans and cost attribute | API / Backend (`TracingLlmGateway`, BCL `ActivitySource`) | Observability infra (OTLP exporter, Jaeger) | Span must wrap the cache so hits are visible; `Carimbo.Llm` stays free of OTel packages |
| Exporter wiring and trace viewer | API composition root (`Carimbo.Api`) | Docker compose (Jaeger) | Packages live in the Api only; the viewer is infrastructure, not code |
| Per-request overrides, validation, echo | API / Backend (`EvalEndpoint`) | Extraction (`ExtractionContext`) | Validation at the HTTP edge (400 naming the field); extraction consumes already-validated values |
| Prompt catalog and versions | Extraction library | none | Prompt text, version and SHA-256 pin live next to `ExtractionContract` |
| Golden response fixtures | Test tier in .NET (emit and compare) | Python test tier (parse) | The .NET host is the producer; Python is the independent consumer |
| Incurred vs notional cost, hit rate, cost cap | Python eval harness | none | Runner and summary only read the wire; they must not charge replays against the cap |

## Standard Stack

### Core (already in the repo, no change)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Anthropic` (NuGet) | 12.53.0 | Messages client; owns the single retry policy | Pinned in `dotnet/Directory.Packages.props`: `<PackageVersion Include="Anthropic" Version="12.53.0" />` [VERIFIED: dotnet/Directory.Packages.props:6] |
| `System.Diagnostics.ActivitySource` | in-box (.NET 10) | Span creation in `Carimbo.Llm` / `Carimbo.Api` without OTel packages | BCL; the OTel SDK listens to it [VERIFIED: executed probe, spans exported] |
| `System.Text.Json`, `System.Security.Cryptography` | in-box | Canonical key JSON, SHA-256, entry files | Already used for the same purposes in `EvalEndpoint` |
| xUnit v3 `xunit.v3` | 4.0.1 | Tests | Pinned in the same file [VERIFIED: dotnet/Directory.Packages.props:7] |

### New for this phase (Carimbo.Api and Carimbo.Api.Tests only)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `OpenTelemetry.Extensions.Hosting` | 1.19.1 | `AddOpenTelemetry().WithTracing(...)` | Official OTel .NET host integration [CITED: opentelemetry.io/docs/languages/dotnet/traces/getting-started-aspnetcore/] |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.19.1 | OTLP export to Jaeger | Executed: exported spans reached Jaeger v2.22.0 [VERIFIED: executed probe] |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.19.0 | Server span honouring inbound `traceparent`; becomes the parent of model-call spans | Same docs page; 1.19.0 is its latest stable (no 1.19.1 exists) [VERIFIED: NuGet flat container] |
| `OpenTelemetry.Exporter.InMemory` | 1.19.1 | Test only: assert exported spans through the real SDK pipeline | Ran in probe [VERIFIED: executed probe] |

Do **not** add `OpenTelemetry.Instrumentation.Http`: it would add one child span per HTTP attempt next to the one span per model call and blur the "one span per model call" criterion. `http_attempts` is a tag instead. Do **not** add `Microsoft.Extensions.AI` / `UseOpenTelemetry` (gateway is not an `IChatClient`, D-21).

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SDK-owned retry | Custom retry in the gateway with SDK `MaxRetries = 0` | Deterministic injected delays in tests, but re-implements `Retry-After`, `x-should-retry` and jitter that the SDK already honours (executed). Choose only if per-error-class policy (for example no retry on timeout) becomes necessary |
| Raw `ActivityListener` in tests | `OpenTelemetry.Exporter.InMemory` | Listener needs no package and is enough for attribute assertions; use InMemory once, for the wiring test (sampler, source names, trace-id continuity) |
| Hand-written Pydantic response models | Generate from a JSON Schema of the response | A generated route needs a response schema exporter that does not exist; golden fixtures plus strict hand models satisfy API-04 with far less machinery |

**Installation (central package management; pin exactly):**
```xml
<!-- dotnet/Directory.Packages.props -->
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.19.1" />
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.19.1" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.19.0" />
<PackageVersion Include="OpenTelemetry.Exporter.InMemory" Version="1.19.1" />
```
`PackageReference` (no version) in `Carimbo.Api.csproj` for the first three and in `Carimbo.Api.Tests.csproj` for `Exporter.InMemory`.

**Version verification:** NuGet flat container queried 2026-10-15: Extensions.Hosting 1.19.1, Exporter.OpenTelemetryProtocol 1.19.1, Exporter.InMemory 1.19.1, Instrumentation.AspNetCore 1.19.0 (latest stable of each) [VERIFIED: api.nuget.org v3-flatcontainer]. The Anthropic SDK 12.53.0 is already restored in `~/.nuget/packages/anthropic/12.53.0`.

## Package Legitimacy Audit

The `gsd-tools package-legitimacy check` seam accepts only `npm|pypi|crates` (its usage line: "gsd-tools package-legitimacy check --ecosystem <npm|pypi|crates> <pkg1> ..."), so it could not return a verdict for NuGet. I checked each package against the NuGet search API directly. NuGet has no install-time script hook equivalent to npm `postinstall`.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| OpenTelemetry.Extensions.Hosting | NuGet | first published 2020-05-19 | 460,593,117 total | github.com/open-telemetry/opentelemetry-dotnet | OK (manual; verified owner "OpenTelemetry") | Approved |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | NuGet | multi-year | 371,556,978 total | same repo | OK (manual; verified owner) | Approved |
| OpenTelemetry.Instrumentation.AspNetCore | NuGet | multi-year | 400,796,969 total | same repo (contrib line) | OK (manual; verified owner) | Approved |
| OpenTelemetry.Exporter.InMemory | NuGet | multi-year | 11,286,112 total | same repo | OK (manual; verified owner) | Approved (test project only) |

**Packages removed due to [SLOP] verdict:** none. **Packages flagged [SUS]:** none. The seam gap is itself worth one line in the plan: a `checkpoint:human-verify` is not needed because all four are published by the verified OpenTelemetry owner and were already named in the project's stack research (`OpenTelemetry .NET 1.19.x`). Planner may add a one-line "versions match the table" assertion in the task instead.

## Architecture Patterns

### System Architecture Diagram

```
 Python runner  --POST /eval/extractions (contract 3, overrides?, traceparent)-->  ASP.NET host (Development|Eval only)
                                                                                     |
                                              [AspNetCore instrumentation: server span, parent = inbound traceparent]
                                                                                     |
                                       EvalEndpoint: auth -> validate body + overrides (400 names field)
                                                                                     |
                                  resolve effective config: model, prompt version, max_repairs, cache_mode, salt
                                                                                     |
                                                  InvoiceExtractor.ExtractAsync (repair loop, unchanged logic)
                                                                                     |  per attempt: LlmRequest
                                                                                     |  (+ PromptVersion, RepairPromptVersion, ReplicateSalt, CacheMode)
                                                                                     v
   +-- TracingLlmGateway  (one "chat {model}" span per call; tags: model, tokens, cost, cache status; never content)
   |      |
   |      v
   |   CachingLlmGateway (only registered when Llm:Cache:Directory is set, and never in Production)
   |      | key = SHA-256(canonical final request)
   |      |-- mode off ------------------------------------------> inner
   |      |-- read_write: file hit --> return stored response (Latency, Cost = original; HttpAttempts = 0; Cache = hit)
   |      |               miss ------> inner --> store iff StopReason == EndTurn --> return (Cache = miss)
   |      |-- read_only:  hit --> as above;  miss --> LlmGatewayException(CacheMiss)  (NEVER calls inner)
   |      |-- refresh:    inner --> overwrite iff EndTurn --> return (Cache = refresh)
   |      v
   |   CostAccountingLlmGateway  (prices from pricing.json; unchanged)
   |      v
   |   AnthropicLlmGateway  --SDK (MaxRetries = the ONE retry policy)--> api.anthropic.com
   +--------------------------------------------------------------------------------------------
        spans --OTLP gRPC :4317--> Jaeger v2 (compose)   <-- developer opens http://localhost:16686/trace/{trace_id}
   EvalResponse: contract 3: effective{+cache_mode, replicate_salt}, attempts[]{+cache{status,key}, +incurred_cost_usd}, +incurred_cost_usd
        --> runner JSONL --> grader/summary (incurred vs notional, hit rate)   and   golden fixtures --> pytest
```

### Recommended Project Structure

```
dotnet/src/Carimbo.Llm/
├── LlmContracts.cs          # LlmRequest gains PromptVersion, RepairPromptVersion, ReplicateSalt, CacheMode; LlmResponse gains Cache; new LlmFailureKind.CacheMiss
├── Cache/
│   ├── LlmCacheKey.cs       # canonical JSON -> SHA-256 (pinned vector test)
│   ├── LlmCacheStore.cs     # entry read/write, atomic rename, corrupt = miss
│   └── CachingLlmGateway.cs # modes, eligibility, HttpAttempts = 0 on hit
├── Telemetry/
│   ├── LlmTelemetry.cs      # ActivitySource name, attribute-name constants
│   └── TracingLlmGateway.cs
dotnet/src/Carimbo.Extraction/
├── PromptCatalog.cs         # versions -> text + pinned SHA-256; Default = extract-003
dotnet/src/Carimbo.Api/
├── EvalEndpoint.cs          # overrides parsing, contract 3, eval root span from an ActivitySource
├── Program.cs               # cache + tracing decoration, Llm:Cache:*, OpenTelemetry wiring, MaxRetries default
dotnet/tests/...             # CacheTests, RetryOwnershipTests, TracingTests, OverridesTests, GoldenResponseTests
data/contract/eval-responses/ # generated golden fixtures (never hand-edited)
python/src/carimbo_evals/contract.py  # strict Pydantic models for contract 3
python/tests/test_contract_golden.py
compose.yaml                  # Jaeger only
```

### Pattern 1: Decorator order and where each concern sits

**What:** `Tracing( Caching( CostAccounting( Anthropic ) ) )`. Cost sits under the cache so a stored entry carries its original `Cost`; tracing sits over the cache so a replayed call still produces a span (with `carimbo.cache.status = hit`) and sees cost.
**When to use:** always; compose in one place in `CreateApp`, extending the existing `DecorateGatewayWithCostAccounting` pattern (it removes the last `ILlmGateway` descriptor and re-adds a wrapping factory).
**Example (proposed shape, new names are mine):**
```csharp
// Source: pattern from dotnet/src/Carimbo.Api/Program.cs DecorateGatewayWithCostAccounting (read this session)
services.Add(new ServiceDescriptor(typeof(ILlmGateway), provider =>
    new TracingLlmGateway(
        cacheEnabled
            ? new CachingLlmGateway(CreateOriginal(provider, costDecorated), store, defaultMode, provider.GetRequiredService<TimeProvider>())
            : CreateOriginal(provider, costDecorated)),
    ServiceLifetime.Singleton));
```

### Pattern 2: Canonical cache key

**What:** Build a JSON object with a fixed, explicit member order using `Utf8JsonWriter` (not reflection serialization), UTF-8 encode, SHA-256, lowercase hex. Fields:

| Field | Source | Why |
|-------|--------|-----|
| `key_version` | constant `"llm-cache-key-1"` | A deliberate key change invalidates old entries visibly |
| `model` | `request.Model` as sent (alias or dated id are different keys) | D-21 distinguishes alias from snapshot |
| `max_tokens` | `request.MaxTokens` | Parameter of the final request; truncation depends on it |
| `cache_document` | `request.CacheDocument` | Part of the final body and changes billed usage (see Open Question 1) |
| `prompt_version`, `repair_prompt_version` | new `LlmRequest` fields | EXT-05 wants the version in the key |
| `prompt_sha256` | SHA-256 of `request.Prompt` | A prompt text edited without a version bump must not hit |
| `schema_sha256` | SHA-256 of `request.OutputSchemaJson` string | The exact bytes sent |
| `document_media_type`, `document_sha256` | `request.Document` | PDF identity without storing the PDF |
| `follow_ups` | array of `{role, text_sha256}` | Repair turns: model answer and validator feedback are part of the request |
| `replicate_salt` | new field, `""` when absent | LLM-04 |

Excluded on purpose: API key, base URL, timeout, SDK retry count, reference date (validation is local and is re-run on replay), case id, pricing version (stored inside the entry), cache mode.
**Guard:** a test that enumerates `typeof(LlmRequest).GetProperties()` and asserts every property is either in the key field list or in an explicit exclusion list. Phase 3.1 will extend `LlmRequest` with tool turns; this test forces that change to decide about the key instead of silently ignoring it.
**Guard:** a pinned-vector test (known request -> known hex), like the prompt SHA pins in `ExtractorTests.cs`, so accidental key drift fails before Phase 6 commits fixtures.

### Pattern 3: Mode matrix and eligibility

| Mode | Hit | Miss | Writes |
|------|-----|------|--------|
| `off` | n/a (bypass) | inner | never |
| `read_write` | return stored | inner | only if `StopReason == EndTurn` |
| `read_only` | return stored | throw `LlmGatewayException(CacheMiss)`; **inner is never called** | never |
| `refresh` | ignored | inner | overwrite only if `StopReason == EndTurn`; a truncated or refused refresh must not destroy the old valid entry |

Eligibility is a whitelist, not "not truncated and not refused": cache only `LlmStopReason.EndTurn`. The enum is `EndTurn, MaxTokens, Refusal, StopSequence, Other` [VERIFIED: dotnet/src/Carimbo.Llm/LlmContracts.cs:46-53, read this session], so `StopSequence` and `Other` (for example `pause_turn`, see `An_unknown_stop_reason_maps_to_other`) are also not cached. Gateway exceptions are never cached. Schema-invalid or validation-failed model text with `EndTurn` **is** cached: it is a real model output and the repair chain depends on it.
A hit returns `Latency = original`, `Cost = original`, `Usage = original`, `ProviderMessageId = original`, and `HttpAttempts = 0` (this call made no HTTP attempt; the sum over attempts is then 0 on a pure replay, which makes "zero provider calls" observable on the wire). The entry keeps `original_http_attempts`.
Entry file: `<dir>/<sha256>.json`, pretty-printed, holding `entry_version`, `key`, `key_fields` (the non-content summary: model, prompt versions, schema and document hashes, salt, follow-up count), `recorded_at` (via `TimeProvider`), and the response (text, stop reason, stop detail, usage, model returned, message id, latency ms, cost amount string, pricing version, original http attempts). Write to a temp file in the same directory and `File.Move(..., overwrite: true)` so a concurrent reader never sees a torn file. Unparseable or mismatched-key entries count as a miss. The filename comes only from the hex digest, never from request input (no traversal).

### Pattern 4: Tracing decorator (one span per call)

**What:** `TracingLlmGateway` starts one `Activity` with kind `Client`, name `chat {model}` (the GenAI inference span name rule is `{gen_ai.operation.name} {gen_ai.request.model}` [CITED: github.com/open-telemetry/semantic-conventions-genai docs/gen-ai/client-inference.md]), sets tags from the response, sets `ActivityStatusCode.Error` plus `error.type` on `LlmGatewayException`, and rethrows. No `RecordException`.

Attribute set (the allowlist the test asserts; anything else fails):

| Tag | Source |
|-----|--------|
| `gen_ai.operation.name` = `chat` | const |
| `gen_ai.provider.name` = `anthropic` | const [CITED: client-inference.md, well-known value `anthropic`] |
| `gen_ai.request.model`, `gen_ai.request.max_tokens` | request |
| `gen_ai.response.model`, `gen_ai.response.id`, `gen_ai.response.finish_reasons` | response (`ModelReturned`, `ProviderMessageId`, stop reason) |
| `gen_ai.usage.input_tokens` | uncached + cache read + cache write (the spec says this attribute includes cached tokens, while `LlmUsage.InputTokens` is only the uncached remainder, see `LlmContracts.cs` doc comment) |
| `gen_ai.usage.output_tokens`, `gen_ai.usage.cache_read.input_tokens`, `gen_ai.usage.cache_write.input_tokens` | usage (5m + 1h writes summed) |
| `carimbo.llm.cost_usd` (string, 8 decimals) and `carimbo.llm.pricing_version`, `carimbo.llm.cost_warning` | `LlmResponse.Cost` |
| `carimbo.llm.cache.status` (`hit|miss|refresh|off`), `carimbo.llm.cache.mode`, `carimbo.llm.http_attempts`, `carimbo.llm.prompt_version`, `carimbo.llm.replicate_salt` | cache info, request |
| `error.type` | failure kind name, on error only |

Forbidden by construction: `gen_ai.input.messages`, `gen_ai.output.messages`, `gen_ai.system_instructions` (these are the Opt-In content attributes in the same spec table). Never record prompt, text, schema, base64 or `StopDetail`.
The GenAI semantic conventions are status "Development", moved to `open-telemetry/semantic-conventions-genai` and have no published release or schema URL as of today [CITED: that repo's README via WebFetch]. The roadmap asked to pin the version: pin it by **owning the attribute-name constants in `LlmTelemetry.cs`** with a comment citing the repo path and date, and by the allowlist test. That is a stronger pin than a version string that does not exist yet.

### Pattern 5: Overrides and effective configuration

Request gains an optional `overrides` object (snake_case members, all optional): `model`, `prompt_version`, `max_repairs`, `cache_mode`, `replicate_salt`. `UnmappedMemberHandling.Disallow` is already set [VERIFIED: dotnet/src/Carimbo.Api/Program.cs:41], so an unknown member is a 400 `body`. To get field-specific 400s (`overrides.max_repairs`, as `reference_date` does today), bind `cache_mode` and numeric members leniently (`JsonElement?`/string) and parse by hand, following the `ReferenceDate` pattern in `EvalEndpoint.cs`.
Validation rules (all 400 `ValidationProblem` naming the field, never echoing the payload):

- `model`: must be priceable. `LlmPricingTable.CanPrice(model)` exists [VERIFIED: LlmPricing.cs:61], and the table currently holds `claude-haiku-4-5`, `claude-sonnet-5-5` and the alias `claude-haiku-4-5-20251001` [VERIFIED: pricing.json]. An unpriced or mistyped model would otherwise spend money with `cost_usd: null`.
- `max_repairs`: integer 0..5. `MaxRepairsLimit = 5` is currently a private const in `Program.cs:16`; share it (move to `ExtractionSettings` or a public const) so startup validation and request validation cannot drift.
- `prompt_version`: must exist in the `PromptCatalog`.
- `cache_mode`: exactly `off|read_write|read_only|refresh`; anything other than `off` is a 400 when the host has no cache configured.
- `replicate_salt`: 1..64 chars of `[A-Za-z0-9._-]`.

Effective config echo: `effective` already has `model`, `prompt_version`, `repair_prompt_version`, `max_repairs`, `schema_sha256`, `pricing_version`, `reference_date` [VERIFIED: EvalEndpoint.cs:290-297]. Add `cache_mode` and `replicate_salt` (null when unset). `max_repairs` and `model` must come from the resolved per-request values, not from `ExtractionSettings`.
Plumbing: extend `ExtractionContext(DateOnly ReferenceDate)` with an optional overrides record (its doc comment currently says "Deliberately only the reference date (D-11)"; update it, and note Phase 2's "not per request" decision for repairs is intentionally reversed by API-02). `InvoiceExtractor` reads `settings.Model`, `settings.MaxRepairs` and `contract.PromptVersion` today; resolve effective values once at the top of `ExtractAsync`.

### Pattern 6: Golden fixtures and staleness

xUnit `GoldenResponseTests` in `Carimbo.Api.Tests` posts a fixed scenario matrix through the existing `TestHost` helper (scripted gateways, `FixedTimeProvider`, a fixed `traceparent`), normalizes the one volatile field (`latency_ms`, which is `Stopwatch` wall time; either normalize in the writer or route the stopwatch through `TimeProvider.GetTimestamp`), and then either **compares** with the committed file or, when `CARIMBO_UPDATE_GOLDEN=1`, **rewrites** it. This mirrors `SchemaSnapshotTests.Export_equals_the_committed_schema_file` (byte comparison with a "Run: ..." message). Add `just golden` (rewrite) and `just golden-check` (rewrite then `git diff --exit-code -- data/contract`), parallel to `schema` / `schema-check`; the compare-mode test already fails `just dotnet-check` on drift, so `golden-check` is belt and braces and may be omitted.
Scenario list (each a file): success; repaired success (2 attempts); validation_failed (budget exhausted); refused; truncated; schema_invalid; infrastructure failure with `http_status`; unpriced model (null cost + warning); cache hit (replay, `http_attempts` 0, incurred cost "0.00000000"); read-only cache miss (`cache_miss` failure); a request with overrides (echoed in `effective`).
Python: `carimbo_evals/contract.py` holds Pydantic v2 models with `model_config = ConfigDict(extra="forbid", strict=True)` for the response, and `test_contract_golden.py` parses every file in `data/contract/eval-responses/`. `outcome.invoice` can reuse the generated `carimbo_models.generated.Invoice`. `extra="forbid"` is the point: an added or renamed .NET field fails pytest until the model is updated. Also add one golden **request** file that the .NET test posts verbatim and that the runner's payload builder must reproduce, so the contract is checked in both directions.

### Anti-Patterns to Avoid

- **Cache on by environment default.** `EvalEndpointTests` use `Development`, send identical PDFs repeatedly and assert gateway behaviour; a default-on cache would make them order-dependent and write into the test output directory. Cache is on only when `Llm:Cache:Directory` is set.
- **Stacked retry.** Any `DelegatingHandler`/loop that retries while `MaxRetries > 0`, or `Http.Resilience`/Polly packages.
- **A hit that reports the current call's latency or the live model call count.** D-19 says original latency and cost; `HttpAttempts` is the one field that must describe *this* call (0).
- **Charging replays against the runner cost cap.** The runner caps on `cost_usd` today; on a hit `cost_usd` is the notional cost. Cap on the new `incurred_cost_usd`.
- **Recording content in spans for debugging.** Use the cache entry and the JSONL record for content; spans are metadata only.
- **`new Activity("Carimbo.Eval")` as the eval root** (see Pitfall 2).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Retry/backoff/jitter/`Retry-After` | A custom loop or Polly | Anthropic SDK `MaxRetries` | Executed: honours `retry-after`, `retry-after-ms`, `x-should-retry`, retries 408/409/429/5xx and connection errors, 4xx not retried |
| Trace export | A custom exporter or log scraping | `OpenTelemetry.Exporter.OpenTelemetryProtocol` + `OTEL_EXPORTER_OTLP_ENDPOINT` | Executed end to end against Jaeger |
| Span capture in tests | A custom tracer | `ActivityListener` (BCL) for attributes; `Exporter.InMemory` for the pipeline | Both exercised in probes |
| Atomic file replacement | Lock files | temp file + `File.Move(overwrite: true)` in the same directory | Readers never see a torn entry |
| JSON-to-model parsing in Python | Ad-hoc dict checks for the contract | Pydantic v2 `extra="forbid"` | A drift becomes a validation error with a path |
| Local trace viewer | A custom UI | Jaeger v2 container | Matches the brief; deep link per trace |

**Key insight:** every hard part (retry semantics, OTLP, sampling, trace UI) is already solved by a component we are pinning; the phase's real work is the cache semantics and contract, which are project-specific and fully testable offline.

## Common Pitfalls

### Pitfall 1: The cache makes the existing hermetic tests order-dependent
**What goes wrong:** A cache enabled by `Development` default writes under the test working directory and later tests hit earlier entries.
**Why:** `EvalEndpointTests.TestHost.StartAsync` uses `Development`/`Eval`, one `Pdf`, and scripted gateways.
**How to avoid:** Opt-in by `Llm:Cache:Directory`; tests that exercise the cache pass a temp directory through the `config` dictionary parameter that `TestHost.StartAsync` already has.
**Warning signs:** a test passing alone and failing in the suite; files appearing under `bin/`.

### Pitfall 2: Model-call spans silently vanish under an unsampled local root
**What goes wrong:** Spans are created in code but never reach Jaeger.
**Why:** `EvalEndpoint.cs:172` creates `new Activity("Carimbo.Eval")` when `Activity.Current` is null. Executed probe with the OTel SDK 1.19.1: that root has `recorded=False flags=None`, and `ActivitySource.StartActivity` under it returned null (`A child created=False`). With a parent built from a sampled `traceparent` (`-01`) the child was created and exported on the same trace id (`B child created=True trace=4bf92f35...`). An `ActivitySource`-owned root worked (`C`).
**How to avoid:** Create the eval root with `ActivitySource.StartActivity(name, ActivityKind.Server, parentContext)` where `parentContext` comes from the `traceparent` header (the existing `ActivityContext.TryParse` code), and keep the bare `Activity` only as the no-listener fallback so `trace_id` is still real in tests. With `OpenTelemetry.Instrumentation.AspNetCore` registered, the server span already exists and `Activity.Current` is non-null, so this matters mainly when the instrumentation is absent. Test: InMemory exporter, post with no `traceparent`, assert the model-call span is exported and shares the response `trace_id`.
**Warning signs:** `trace_id` in the response, nothing in Jaeger.

### Pitfall 3: Replay charges the cost cap (and reports spend that never happened)
**What goes wrong:** `just skeleton` replays fill the cap at notional cost, or summaries show money that was not spent.
**Why:** `runner.account()` sums `response.cost_usd`; on a hit that is the original cost by D-19.
**How to avoid:** Add `incurred_cost_usd` per attempt and top-level (0 for hits, equal to `cost_usd` otherwise, null when unpriced) and make `account()` use it when present. Summaries report both totals and the hit rate (`hits / answered attempts`).

### Pitfall 4: Read-only replay of cases whose run had truncated or refused attempts
**What goes wrong:** Those attempts were never cached, so the replay hits `cache_miss` for them.
**Why:** The required "never cache truncated or refused" rule plus "read-only never calls the provider".
**How to avoid:** Surface it as a typed `infrastructure_failure` with kind `cache_miss` (new `LlmFailureKind.CacheMiss`; `ScriptedHost` maps `failure.kind` with `Enum.Parse<LlmFailureKind>(... Replace("_", ""))`, so the snake_case wire name `cache_miss` round-trips), distinct from a quality failure and not charged. Document that faithful replay of non-cacheable outcomes comes from the stored JSONL (EVAL-02 offline re-grade), not from the cache. Raise this in the Phase 6 discuss (CI-02 replay set).

### Pitfall 5: SDK retries multiply timeouts
**What goes wrong:** One stuck provider can hold one extraction for far longer than the runner's HTTP read timeout.
**Why:** The SDK documents that "An attempt that exceeds this timeout is retried like a connection error" [VERIFIED: Anthropic.xml / ClientOptions doc, fetched]. Per-attempt timeout default is 300 s (`DefaultTimeoutSeconds = 300`, Program.cs:19), a repair chain is up to 3 calls, and the runner read timeout is 900 s (Phase 2 plan 02-10). With `MaxRetries = 2` the worst case is 3 calls x 3 attempts x 300 s.
**How to avoid:** Keep `MaxRetries` at 2 but make the effective worst case explicit in config docs, and either lower the per-attempt timeout default or have the runner timeout track `(max_repairs + 1) x (MaxRetries + 1) x timeout`. The cost cap logic already treats ambiguous failures as possibly paid (`may_have_reached_provider`).

### Pitfall 6: Key drift silently invalidates fixtures Phase 6 will commit
**What goes wrong:** A refactor changes the canonical bytes; every replay misses.
**How to avoid:** `key_version` constant, pinned-vector test, `LlmRequest` property coverage test (Pattern 2). Note also the Phase 2 warning: "once Phase 3 keys the cache on PDF SHA-256, changing these bytes again invalidates fixtures" [VERIFIED: 02-CONTEXT.md line 61, grep this session]; `just datagen-check` already guards byte identity.

### Pitfall 7: `gen_ai.usage.input_tokens` semantics
**What goes wrong:** Dashboards show a tenth of the real input volume.
**Why:** The semconv text says the attribute includes cached tokens [CITED: gen-ai-spans.md via WebFetch]; `LlmUsage.InputTokens` is "the uncached remainder only" [VERIFIED: LlmContracts.cs:58 doc comment].
**How to avoid:** Sum the three classes for that attribute and keep the per-class attributes.

### Pitfall 8: Short-lived hosts lose batched spans
**What goes wrong:** `just skeleton` stops the Api with `kill` and the last spans are missing in Jaeger.
**Why:** The OTLP exporter is wrapped in a batch processor with a scheduled delay (5 s default per the OTel spec) [ASSUMED].
**How to avoid:** Rely on host shutdown disposing the `TracerProvider` (SIGTERM is graceful for `WebApplication`); in the live recipe wait for the Api to exit before reporting; in tests use `ForceFlush` or the InMemory exporter, never sleep.

### Pitfall 9: Format and analyzer gates
**What goes wrong:** `just dotnet-check` fails after the feature works.
**Why:** `dotnet build -warnaserror` and `dotnet format --verify-no-changes` run on the whole solution (`justfile`), and `Directory.Build.props` sets `TreatWarningsAsErrors`.
**How to avoid:** Run both locally per task; add new packages only via `Directory.Packages.props`.

## Code Examples

### Counting retries with the existing failing transport (LLM-01)

```csharp
// Source: pattern from dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs (RecordingHandler + Gateway(...) helper, read this session)
[Fact]
public async Task With_sdk_retries_two_a_persistent_529_is_attempted_three_times_in_total()
{
    var handler = new RecordingHandler(((HttpStatusCode)529, Overloaded));   // last scripted response repeats
    using var gateway = Gateway(handler, maxRetries: 2);
    var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));
    Assert.Equal(LlmFailureKind.Overloaded, ex.Kind);
    Assert.Equal(3, handler.Attempts);            // 1 + MaxRetries, never (1 + N) * (1 + M)
}
```
Also test: `maxRetries: 0` gives 1 attempt on a 529; a 400 gives 1 attempt even with `maxRetries: 2`; the full composed chain (`CarimboApi` host with the real `AnthropicLlmGateway` and a `BaseUrl`/handler seam, or the gateway wrapped by `CachingLlmGateway`) still shows `handler.Attempts == 3`, so no layer above the SDK retries. The host does not yet expose a handler seam for the real gateway; add an optional `HttpMessageHandler` registration used by `RegisterAnthropicGatewayWhenUnclaimed`, or test the chain by constructing it directly in the test.

### Offline span assertions without any package

```csharp
// Source: System.Diagnostics.ActivityListener (BCL); sampling behaviour executed in this session
var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == "Carimbo.Llm",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = captured.Add,
};
ActivitySource.AddActivityListener(listener);
// ... call the TracingLlmGateway ...
var span = Assert.Single(captured);
Assert.True(AllowedTags.IsSupersetOf(span.TagObjects.Select(t => t.Key)));
Assert.DoesNotContain(captured.SelectMany(a => a.TagObjects), t => t.Value?.ToString()?.Contains(ResponseText) == true);
```
Listeners are process-global: run these tests in a collection that disables parallelism with other host tests, or filter `ActivityStopped` by a per-test marker tag/trace id.

### Exporting to Jaeger (executed)

```csharp
// Source: executed probe against jaeger:2.22.0; registration style from opentelemetry.io getting-started-aspnetcore page
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("carimbo-api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource("Carimbo.Llm", "Carimbo.Api")
        .AddOtlpExporter());           // honours OTEL_EXPORTER_OTLP_ENDPOINT; default protocol gRPC (4317)
```
Register this only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so tests and CI never try to export. `ConfigureResource/AddService` needs `using OpenTelemetry.Resources;` (resource builder types ship in the base `OpenTelemetry` package).

### compose.yaml

```yaml
# Source: image and ports from https://www.jaegertracing.io/docs/2.22/getting-started/ ; pinned tag verified on Docker Hub; started and queried in this session
name: carimbo
services:
  jaeger:
    image: cr.jaegertracing.io/jaegertracing/jaeger:2.22.0
    ports:
      - "127.0.0.1:16686:16686"   # UI and query API
      - "127.0.0.1:4317:4317"     # OTLP gRPC
      - "127.0.0.1:4318:4318"     # OTLP HTTP
```
Bind to loopback: the UI is unauthenticated. The default configuration "uses a transient in-memory storage" [CITED: jaegertracing.io getting-started], so traces vanish on `docker compose down`; say so in the README.

### Looking a trace up by the id in an eval response

```bash
curl -s "http://127.0.0.1:16686/api/traces/<trace_id>"   # executed: returned the eval root and the "chat claude-haiku-4-5" child with gen_ai.* and carimbo.llm.cost_usd tags
# UI: http://localhost:16686/trace/<trace_id>
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| GenAI conventions under `opentelemetry.io/docs/specs/semconv/gen-ai/` | Moved to `open-telemetry/semantic-conventions-genai` (status Development, no release) | seen 2026-10-15 | Own the attribute constants; do not depend on a semconv NuGet |
| Jaeger v1 | Jaeger v2 (OTel Collector based), v1 end of life | v1 EOL 2025-12-31 (project stack research) | Use `jaegertracing/jaeger` image, not `all-in-one` |
| `IChatClient` + `UseOpenTelemetry` | Own gateway + own spans | D-21 (this repo) | Spans are written by `TracingLlmGateway` |

**Deprecated/outdated:** Jaeger v1 images; `Instrumentation.Http` is intentionally not used here.

## Runtime State Inventory

Not a rename/refactor/migration phase. Omitted. One migration-like fact the planner should know: no cache entries exist today (`ls .cache` -> "No such file or directory") and the earlier live run directories (`evals/runs/skeleton-*`) hold no replayable requests, so the first replay needs one new live `just skeleton` run (about US$0.08 on Haiku, per the 02-10 summary: US$0.08433365 for the 2-repair run).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | OTel batch processor default scheduled delay is 5 s | Pitfall 8 | Spans appear later or sooner than the recipe's wait; low impact, fix by flushing on shutdown |
| A2 | `Microsoft.Extensions.*`/ASP.NET behaviour of SIGTERM disposing the `TracerProvider` flushes remaining spans | Pitfall 8 | Last spans lost in `just skeleton`; verify in the live proof task |
| A3 | The Phase 6 replay set can tolerate `cache_miss` for non-cacheable outcomes (truncated/refused) | Pitfall 4 | Replay gate shows infra failures for those cases; may need the JSONL-based replay instead |
| A4 | Restoring `extract-002` as a second selectable prompt version is acceptable for the override and A/B story | Open Question 3 | If not, override tests must register a test-only prompt version |
| A5 | Including `cache_document` in the key is preferable to excluding it | Open Question 1 | Either more misses across repair configurations (include) or reported cost that does not match the config (exclude) |

Everything else in this document was read from the repo, executed, or cited.

## Open Questions

1. **Should `cache_document` (the PDF breakpoint) be part of the key?**
   - What we know: `InvoiceExtractor` sets `CacheDocument = settings.MaxRepairs > 0` [VERIFIED: InvoiceExtractor.cs:242], so `max_repairs` 0 and 2 produce different initial requests. Output does not depend on it, billed usage does.
   - Unclear: whether reuse across repair configurations (the Phase 6 repair ablation) outweighs a reported cost that matches the exact config.
   - Recommendation: include it (D-19 says "canonical final request"); the ablation then pays the initial attempt once per configuration, which is cents on Haiku.
2. **Contract version.** Recommendation: bump request and response to `"3"` (D-24 rule: existing fields change meaning on a hit; consumers must opt in). Cost: every `"2"` literal in `EvalEndpoint.cs:30`, `runner.py` (`CONTRACT_VERSION`), `Body()` in `EvalEndpointTests.cs` and Python tests changes in one mechanical task. If the planner prefers additive-only, `cache` still has to be exposed so the runner cap stays correct.
3. **Which prompt versions are selectable?** Only `extract-003` exists in code. Recommendation: a `PromptCatalog` holding `extract-003` (default) and `extract-002` restored from `git show 44c4323^:dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` (the commit that replaced it; it has known label defects G-02-1/G-02-2 but is schema-compatible), each with its SHA-256 pin, so RES-01's "two prompt versions" is possible and the override has a real effect. The repair prompt stays `repair-001`.
4. **Cost on spans as string vs double.** Recommendation: string (project convention, exact). Jaeger cannot aggregate it; acceptable for lookup by trace id.
5. **Mode names on the wire.** `off|read_write|read_only|refresh` (snake_case, matching D-19's names and `LlmStopReason` style).
6. **Decision records.** Add superseding entries (next free id is D-27): cache implementation and key v1; single retry owner = SDK `MaxRetries` default 2; contract 3 with overrides and incurred cost; tracing attribute set. Update `docs-check` in the `justfile` (it loops over `D-18 ... D-25` today) to include them.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | all .NET work | yes | 10.0.400 (nixpkgs wrapped; `global.json` pins 10.0.100 with `latestFeature` roll-forward) | none needed |
| uv | Python env | yes | 0.12.11 (`pyproject.toml` requires >=0.8.17) | none needed |
| Python | tests | yes | 3.12.14 | none needed |
| just | recipes | yes | 1.58.0 | none needed |
| devenv | dev shell | yes | 2.4.0 | `just` directly also works (ran `dotnet test`/`uv run pytest` outside devenv) |
| Docker engine + compose | REPO-05, live proof | yes | 29.8.1, compose 5.6.0, daemon running | CI and offline tests never need it: compose is checked statically |
| Jaeger image | REPO-05 | yes, pulled | `cr.jaegertracing.io/jaegertracing/jaeger:2.22.0` (175 MB) | pulled by compose on first use |
| NuGet registry | restore of new packages | yes | scratch restore of OTel 1.19.1 succeeded | none |
| Anthropic provider key | live proof only | not checked (agents never read the value) | n/a | scripted host proves everything offline; live proof is one capped `just skeleton` |

**Missing dependencies with no fallback:** none. **Note for reviewers:** `devenv.nix` does not provide Docker; the trace viewer needs a Docker host, nothing else in the phase does. Say so in the README.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 4.0.1 on Microsoft.Testing.Platform (.NET); pytest 9.1.1 + pytest-asyncio 1.4.0 (Python) |
| Config file | `global.json` (`"runner": "Microsoft.Testing.Platform"`); `python/pyproject.toml` (`addopts = ["--import-mode=importlib", "-m", "not e2e and not live"]`) |
| Quick run command | `cd dotnet && dotnet test --project tests/Carimbo.Llm.Tests` (about 4 s) and `cd dotnet && dotnet test --project tests/Carimbo.Api.Tests` (about 7 s incl. build); `uv run --project python pytest python/tests -q` (308 passed in 9 s) |
| Full suite command | `devenv shell -- just check` (or `just check` outside devenv) |
| Single class (MTP) | `dotnet test --project tests/Carimbo.Api.Tests -- --filter-class Carimbo.Api.Tests.EvalEndpointTests` [VERIFIED: ran, 71 passed] |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| LLM-04 | Key stable (pinned vector); every `LlmRequest` property covered; different model / prompt version / salt / follow-up / PDF miss; same request hits | unit | `dotnet test --project tests/Carimbo.Llm.Tests` | Wave 0 (new `CacheKeyTests.cs`) |
| LLM-04 | Cache absent unless directory set; startup error if configured in Production; Production host calls inner twice for identical requests | integration | `dotnet test --project tests/Carimbo.Api.Tests` | Wave 0 (new `CacheConfigurationTests.cs`) |
| LLM-05 | `read_write` stores then hits; `read_only` hit returns original latency, cost, usage and `HttpAttempts` 0 with a counting inner gateway at 0 calls; `read_only` miss throws `CacheMiss` without calling inner; `refresh` calls inner and overwrites; truncated, refused, `Other` never written (directory stays empty); failed refresh keeps old entry; corrupt file = miss | unit | `dotnet test --project tests/Carimbo.Llm.Tests` | Wave 0 (`CachingGatewayTests.cs`) |
| LLM-01 | 529 persistent -> `1 + MaxRetries` attempts; 400 -> 1; through the composed chain still `1 + MaxRetries`; no Polly/`Http.Resilience` package reference in any csproj or `Directory.Packages.props` | unit + static | `dotnet test --project tests/Carimbo.Llm.Tests` | extends `AnthropicGatewayTests.cs`; static check in pytest `test_repo_layout.py` style |
| LLM-03 | One span per `CompleteAsync` (including a hit); tag keys subset of the allowlist; model, tokens, cost, cache status present; text, prompt, base64 and schema absent; error sets status and `error.type`; with the real OTel pipeline + InMemory exporter the span shares the response `trace_id` even with no inbound `traceparent` | unit + integration | `dotnet test --project tests/Carimbo.Llm.Tests` / `Carimbo.Api.Tests` | Wave 0 (`TracingGatewayTests.cs`, `TracingWiringTests.cs`) |
| EXT-05 | Catalog has `extract-003` default and a pinned SHA per version; prompt version changes the key; `effective.prompt_version` and every `attempts[].prompt_version` echo the used version | unit | `dotnet test --project tests/Carimbo.Extraction.Tests` | extends `ExtractorTests.cs` |
| API-02 | Each override takes effect (gateway sees model; attempts count follows `max_repairs`; prompt text follows version; mode and salt reach the cache); each invalid value is a 400 naming `overrides.<field>`; unknown member is 400; unpriced model rejected; echo in `effective` | integration | `dotnet test --project tests/Carimbo.Api.Tests` | extends `EvalEndpointTests.cs` (or new `OverridesTests.cs`) |
| API-04 | Golden fixtures equal fresh output; every fixture parses under strict Pydantic; request fixture round-trips through the runner payload builder | contract | `dotnet test --project tests/Carimbo.Api.Tests` then `uv run --project python pytest python/tests/test_contract_golden.py -q` | Wave 0 (`GoldenResponseTests.cs`, `test_contract_golden.py`, `data/contract/**`) |
| REPO-05 | `compose.yaml` pins the Jaeger v2 tag (not `latest`), binds loopback, exposes 16686 and 4317; README documents the one command | static (text) | `uv run --project python pytest python/tests/test_repo_layout.py -q` | extends existing file |
| LLM-04/05 end to end | Run cases `read_write` against `ScriptedHost` (set `HttpAttempts` to 1 there), rerun `read_only`: zero attempts, same graded fields, `cache.status` hit, `incurred_cost_usd` "0.00000000" and notional equals original; a different `replicate_salt` rerun misses | e2e | `just e2e` (`pytest -m e2e`) | extends `test_e2e_fake.py` (set `Llm__Cache__Directory` env for the host) |
| Runner/summary | Cap uses `incurred_cost_usd`; summary reports incurred vs notional and hit rate; CLI flags map to `overrides` | unit | `uv run --project python pytest python/tests/test_runner.py python/tests/test_grader.py -q` | extends existing files |
| Criterion 1 and 2 (live) | Replay of the three skeleton cases: zero provider calls; a trace id from the response resolves in Jaeger with one `chat ...` span per attempt and no content | manual-with-evidence | one capped `just skeleton` then replay recipe, then `curl .../api/traces/<id>` | manual: needs provider key and Docker; record the evidence in the plan SUMMARY, never run in CI |

### Sampling Rate
- **Per task commit:** the single affected test project (`dotnet test --project ...` about 4-7 s) plus `uv run --project python pytest python/tests/<file> -q`.
- **Per wave merge:** `just dotnet-check` and `just py-check` (adds build with warnings as errors and `dotnet format`).
- **Phase gate:** full `just check` green (includes `schema-check`, `datagen-check`, `e2e`, `docs-check`, `secrets-check`) before `/gsd-verify-work`; then the capped live proof.

### Wave 0 Gaps
- [ ] `dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs`, `CachingGatewayTests.cs`, `TracingGatewayTests.cs` - LLM-04/05/03
- [ ] `dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs`, `TracingWiringTests.cs`, `GoldenResponseTests.cs`, overrides tests - LLM-04/03, API-02/04
- [ ] `python/tests/test_contract_golden.py`, `python/src/carimbo_evals/contract.py` - API-04
- [ ] `data/contract/eval-responses/*.json` (generated) and the `CARIMBO_UPDATE_GOLDEN` rewrite path - API-04
- [ ] `Carimbo.Api.Tests.csproj` gains `OpenTelemetry.Exporter.InMemory`; `Directory.Packages.props` gains four entries
- [ ] `ScriptedHost` reports `HttpAttempts: 1` (currently `HttpAttempts: 0`, `Program.cs` last argument of the `LlmResponse` constructor) so a replay is distinguishable from a call
- [ ] Shared test double: a counting `ILlmGateway` (the `ScriptedGateway` in `EvalEndpointTests.cs` is private; extract or duplicate)

## Security Domain

`security_enforcement` is enabled (absent = enabled; `.planning/config.json` has `"security_enforcement": true`, `"security_asvs_level": 1`).

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes (unchanged) | Static `X-Api-Key`, SHA-256 + `FixedTimeEquals` already in `EvalEndpoint`; overrides stay behind it |
| V3 Session Management | no | stateless endpoint |
| V4 Access Control | yes | Eval route exists only in Development/Eval with a key; cache decorator never registered in Production; override reachable only through the eval route |
| V5 Input Validation | yes | Overrides validated at the edge: model allowlist = priced models, `max_repairs` 0..5, prompt version from catalog, `cache_mode` enum, salt charset and length; 400 names the field and never echoes the payload |
| V6 Cryptography | yes (non-secret) | SHA-256 from `System.Security.Cryptography` for cache keys; no custom crypto |
| V7 Error handling and logging | yes | Spans carry metadata only (allowlist test); gateway error messages already exclude body and key; do not log salts or content |
| V8 Data protection | yes | Cache entries hold model output of synthetic invoices (D-14), under a git-ignored dir (`.cache/` is in `.gitignore`); no keys or request bodies stored |
| V12 Files | yes | File name derived only from the hex digest; atomic temp+rename; entries parsed defensively (corrupt = miss) |
| V14 Configuration | yes | Production + cache directory = startup `InvalidOperationException`; Jaeger ports bound to 127.0.0.1 |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Cost amplification through overrides (expensive model, max repairs) | Denial of service / spend | Priced-model allowlist, hard cap 5 on repairs, runner cost cap on incurred spend, US$ caps on live recipes |
| Content leaking into traces (PDF, prompt, output) | Information disclosure | `TracingLlmGateway` writes only allowlisted tags; test asserts tag keys and absence of response text; `EnableSensitiveData`-style options are not offered |
| Cache poisoning / tampered entry | Tampering | Local dev directory only; entry validated (`entry_version`, key matches filename) else miss; never executes content; fixtures are committed and reviewed in Phase 6 |
| Path traversal through salt or case id | Tampering | Neither reaches a path: file name = hash of the canonical request |
| Unauthenticated trace UI on all interfaces | Information disclosure | Loopback-only port bindings; traces contain no content anyway |
| Stale cache served in production | Integrity | Cache cannot be configured in Production (startup failure) |

## Sources

### Primary (HIGH confidence)
- Repository files read this session: `dotnet/src/Carimbo.Llm/{AnthropicLlmGateway,LlmContracts,LlmPricing}.cs`, `pricing.json`; `dotnet/src/Carimbo.Api/{EvalEndpoint,Program}.cs`; `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs`; tests `AnthropicGatewayTests.cs`, `EvalEndpointTests.cs` (structure), `SchemaSnapshotTests.cs`, `ScriptedHost/Program.cs`; `python/src/carimbo_evals/{runner,cli,summary,grader}.py`, `python/tests/test_e2e_fake.py`, `test_repo_layout.py`; `justfile`, `.github/workflows/ci.yml`, `.gitignore`, `devenv.nix`, `Directory.Packages.props`; `docs/DECISIONS.md` D-07, D-15, D-17, D-19, D-20, D-21, D-24, D-26; `.planning/{REQUIREMENTS,ROADMAP,STATE}.md`; Phase 2 CONTEXT/RESEARCH/10-SUMMARY.
- Executed in this session (scratch projects outside the repo): Anthropic SDK 12.53.0 retry probe with a fake handler; OpenTelemetry 1.19.1 sampling probe; OTLP export to a real Jaeger 2.22.0 container and a `/api/traces/{id}` query; `dotnet test` and `pytest` timings.
- NuGet flat-container and search APIs (versions, owners, download counts).
- Docker Hub tags API for `jaegertracing/jaeger` (2.22.0 pushed 2026-10-06, `latest` same).

### Secondary (MEDIUM confidence)
- https://www.jaegertracing.io/docs/2.22/getting-started/ (image, ports, in-memory default)
- https://opentelemetry.io/docs/languages/dotnet/traces/getting-started-aspnetcore/ (package names, registration)
- https://platform.claude.com/docs/en/api/sdks/csharp via search summary (SDK retry defaults)
- Anthropic SDK source `ClientOptions.cs` on GitHub (MaxRetries docs, timeout-as-retry statement)

### Tertiary (LOW confidence)
- GenAI semantic conventions: https://github.com/open-telemetry/semantic-conventions-genai (`docs/gen-ai/client-inference.md`, `gen-ai-spans.md`), status Development, no release; attribute names may change.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - versions read from the registry and exercised in probes.
- Architecture: HIGH - decorator pattern copies an existing, tested seam; the unsampled-root hazard and Jaeger path were run.
- Pitfalls: HIGH for the executed ones (2, SDK retry facts), MEDIUM for 5 and 8 (derived/assumed latency numbers).
- Semconv attribute names: MEDIUM-LOW (Development status).

### Executed retry probe results (Anthropic 12.53.0, fake handler)

| Scenario | MaxRetries | Attempts | Offsets (ms) | Result |
|----------|-----------|----------|--------------|--------|
| 529 | 2 | 3 | 120, 610, 1560 | `Anthropic5xxException` |
| 500 | 3 | 4 | 0, 464, 1464, 3249 | `Anthropic5xxException` |
| 429 with `retry-after: 2` | 1 | 2 | 0, 2002 | honoured |
| 429 with `retry-after-ms: 300` | 1 | 2 | 0, 302 | honoured |
| 500 with `x-should-retry: false` | 2 | 1 | 0 | not retried |
| 400 with `x-should-retry: true` | 2 | 3 | 0, 485, 1370 | retried |
| 400 | 2 | 1 | 0 | not retried |
| 408 / 409 | 1 | 2 | 0, ~395 | retried |
| connection exception | 2 | 3 | 0, 384, 1261 | `AnthropicIOException` |
| 500 with `x-should-retry: true`, MaxRetries 0 | 0 | 1 | 1 | `MaxRetries = 0` ignores API retry instructions |

**Research date:** 2026-10-15
**Valid until:** 2026-11-15 for the stack and code map; re-check the GenAI semconv repo and the Jaeger tag at plan execution.

## Suggested plan decomposition (input for the planner, not extra scope)

1. **Tracer (wave 1):** `LlmRequest`/`LlmResponse` additions, `LlmCacheKey`, `CachingLlmGateway` with modes, `Llm:Cache:Directory` wiring in `CreateApp`, `cache` block on the eval response, `ScriptedHost` `HttpAttempts` 1, one e2e: `read_write` then `read_only` with zero attempts. Proves criterion 1 vertically.
2. **Retry (wave 1, parallel):** default `MaxRetries` 2 (bounded 0..5), failing-transport tests, composed-chain test, no-stacking static guard, timeout-worst-case note and runner timeout decision.
3. **Prompt catalog + overrides + contract 3 (wave 2, after 1):** `PromptCatalog`, `ExtractionContext` overrides, endpoint validation, `effective` echo, runner flags (`--model`, `--prompt-version`, `--max-repairs`, `--cache-mode`, `--replicate-salt`), incurred-cost cap, summary incurred/notional/hit rate.
4. **Telemetry (wave 2, after 1):** `ActivitySource` eval root, `TracingLlmGateway`, OTel packages and conditional wiring, allowlist tests, InMemory wiring test.
5. **Compose and docs (wave 3):** `compose.yaml`, README/AGENTS (layout, commands, replay and trace lookup), decisions D-27.., `docs-check` update, static layout tests.
6. **Golden fixtures (wave 3, after 3):** scenario matrix, compare/rewrite, Pydantic models, pytest, `just golden`.
7. **Live proof (last, human-verify, capped):** one `just skeleton` populating `.cache/llm`, one replay at zero spend, one Jaeger lookup of a response trace id; record spend (Phase 3 budget US$5, Phase 2 ended at US$0.2011 of its own cap).
