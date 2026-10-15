# Phase 3: Replayable Runs - Pattern Map

**Mapped:** 2026-10-15
**Files analyzed:** 27 new/modified
**Analogs found:** 25 / 27

All analog paths below were listed by `git ls-files` (tracked source).

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match |
|---|---|---|---|---|
| `dotnet/src/Carimbo.Llm/LlmContracts.cs` (mod: request/response fields, `CacheMiss`) | model | transform | itself (`FollowUps`/`CacheDocument`/`Cost` init-property pattern, lines 36-44, 80-82) | exact |
| `dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs` | service (decorator) | request-response + file-I/O | `CostAccountingLlmGateway` in `dotnet/src/Carimbo.Llm/LlmPricing.cs:107-121` | role-match |
| `dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs` | utility | transform | `EvalEndpoint.cs` SHA-256 use (api-key `FixedTimeEquals`); `SchemaBudget.cs`/`ExtractionContract` prompt SHA pins | partial |
| `dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs` | service | file-I/O | none in repo (use RESEARCH Pattern 3) | none |
| `dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs`, `LlmTelemetry.cs` | service (decorator) | request-response | `CostAccountingLlmGateway` (`LlmPricing.cs:107-121`) | role-match |
| `dotnet/src/Carimbo.Extraction/PromptCatalog.cs` | config | transform | `ExtractionContract.Default` in `InvoiceExtractor.cs` | role-match |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` (mod) | service | request-response | itself | exact |
| `dotnet/src/Carimbo.Api/Program.cs` (mod: decoration, OTel, Llm:Cache, MaxRetries) | config (composition root) | request-response | itself: `DecorateGatewayWithCostAccounting`, `RegisterAnthropicGatewayWhenUnclaimed` | exact |
| `dotnet/src/Carimbo.Api/EvalEndpoint.cs` (mod: overrides, contract 3, root span) | controller | request-response | itself: `ReferenceDate` lenient parse + `Invalid(...)` | exact |
| `dotnet/Directory.Packages.props`, `Carimbo.Api.csproj`, `Carimbo.Api.Tests.csproj` | config | n/a | existing entries | exact |
| `dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs`, `CachingGatewayTests.cs`, `TracingGatewayTests.cs` | test | request-response | `AnthropicGatewayTests.cs`, `PricingTests.cs` | role-match |
| `dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs` (ext: retry) | test | request-response | itself (`RecordingHandler`, `Gateway(...)`) | exact |
| `dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs`, `TracingWiringTests.cs`, overrides tests | test | request-response | `EvalEndpointTests.cs` (`TestHost.StartAsync`) | exact |
| `dotnet/tests/Carimbo.Api.Tests/GoldenResponseTests.cs` | test | file-I/O | `dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs` | role-match |
| `dotnet/tests/Carimbo.ScriptedHost/Program.cs` (mod: `HttpAttempts: 1`) | test host | request-response | itself, line 76 | exact |
| `python/src/carimbo_evals/contract.py` | model | transform | `carimbo_models/generated.py` (Pydantic, do not hand-edit) and `grader.py` GroundTruth | partial |
| `python/src/carimbo_evals/runner.py` (mod: `CONTRACT_VERSION`, `account()`, overrides payload) | service | request-response | itself, lines 31, 133, 309-318 | exact |
| `python/src/carimbo_evals/cli.py`, `summary.py` (mod) | CLI / utility | batch | themselves | exact |
| `python/tests/test_contract_golden.py` | test | file-I/O | `python/tests/test_models.py`, `test_vectors.py` | role-match |
| `python/tests/test_e2e_fake.py` (ext), `test_runner.py`, `test_repo_layout.py` (ext) | test | request-response | themselves | exact |
| `data/contract/eval-responses/*.json` | generated fixture | file-I/O | `schema/invoice.schema.json` (generated + stale-checked) | role-match |
| `compose.yaml` | config | n/a | none | none |
| `justfile` (`golden`, `golden-check`, `docs-check` D-27..) | config | batch | `schema`/`schema-check` (justfile:34-36), `docs-check` (51-56) | exact |
| `docs/DECISIONS.md` (D-27..), README, AGENTS.md | docs | n/a | existing D-18..D-25 entries | exact |

## Pattern Assignments

### Decorators: `CachingLlmGateway`, `TracingLlmGateway`
**Analog:** `dotnet/src/Carimbo.Llm/LlmPricing.cs:107-121`
```csharp
public sealed class CostAccountingLlmGateway(ILlmGateway inner, LlmPricingTable table) : ILlmGateway
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var response = await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        ...
        return response with { Cost = table.Price(model, response.Usage) };
    }
}
```
Copy: primary-constructor decorator, `ConfigureAwait(false)`, copy via `with`. A hit is `stored with { HttpAttempts = 0, Cache = ... }`.

**Contract additions** (`LlmContracts.cs`): follow the init-property style at lines 40-43 and 81 (`public IReadOnlyList<LlmTurn> FollowUps { get; init; } = [];`, `public LlmCost? Cost { get; init; }`). New `LlmFailureKind.CacheMiss` goes in the enum at lines 84-94. Errors use `LlmGatewayException(kind, message, httpStatus, requestId, inner)` (lines 97-109); the span code catches it to set status and `error.type`.

### Composition: `Program.cs`
**Analog:** itself, `DecorateGatewayWithCostAccounting` (lines 143-173)
```csharp
var original = services.LastOrDefault(d => d.ServiceType == typeof(ILlmGateway) && !d.IsKeyedService);
if (original is null) { return; }
services.Remove(original);
services.Add(new ServiceDescriptor(typeof(ILlmGateway),
    provider => new CostAccountingLlmGateway(CreateOriginal(provider, original), provider.GetRequiredService<LlmPricingTable>()),
    original.Lifetime));
```
Extend this single method (or add a sibling called right after line 87) to build `Tracing(Caching(CostAccounting(original)))`. Reuse `CreateOriginal` (160-173).
- Config reading and validation style (`section.GetValue<int?>("MaxRetries") ?? Default`, then `throw new InvalidOperationException("Llm:Anthropic:... ")`), lines 116-128. Use the same for `Llm:Cache:Directory`/`Mode`. Superseded default (D-27, user decision 2026-10-15): the cache is on by default in Development and Eval at `.cache/llm` under the content root, `Llm:Cache:Mode off` disables it, and outside Development and Eval a set directory or a mode other than off throws at startup (plan 03-03).
- `DefaultMaxRetries` const (line 22, comment says Phase 3 owns it): change to 2 and add the `0..5` upper bound next to line 125. `MaxRepairsLimit` (line 16) is private; make it shareable for request validation.
- `TryAddSingleton(TimeProvider.System)` (line 75) is what the cache's `TimeProvider` and golden tests replace.
- Conditional OTel wiring: register only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (RESEARCH "Exporting to Jaeger").

### `EvalEndpoint.cs` (overrides, contract 3, root span)
**Analog:** itself.
- Version literal: `ContractVersion = "2"` (line 30) and check at 124-126: `return Invalid("contract_version", ...)`.
- Lenient field parse: request record keeps `JsonElement? ReferenceDate = null` (line 210) and parses by hand at line 136 (`if (request.ReferenceDate is { } suppliedDate)`). Copy this for `overrides.*` so 400s name `overrides.<field>`.
- Error shape (line ~199): `Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [problem] })`, never echo payload.
- Root span, lines 169-194: replace `new Activity("Carimbo.Eval")` with `ActivitySource.StartActivity(name, ActivityKind.Server, parentContext)`, keeping the `ActivityContext.TryParse(context.Request.Headers["traceparent"]...)` code and the bare-Activity fallback; the `try/finally { ownedActivity?.Stop(); }` stays.
- `EvalResponse.From(ContractVersion, ...)` and the `effective` record (lines ~290-297) gain `cache_mode`, `replicate_salt`; attempts gain `cache{status,key}` and `incurred_cost_usd`. `ExtractionContext(referenceDate)` (line 185) gains an overrides record.
- JSON options: snake_case, `UnmappedMemberHandling.Disallow` (Program.cs:38-44), so unknown members are 400 for free.

### Tests: Llm project
**Analog:** `dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs`
```csharp
private static CancellationToken Ct => TestContext.Current.CancellationToken;
private static LlmRequest Request(string? schema = null, string model = "claude-haiku-4-5") =>
    new(model, 1024, "Extract the invoice.", new LlmDocument("application/pdf", Pdf), schema ?? CommittedModelSchema());
private static AnthropicLlmGateway Gateway(RecordingHandler handler, int maxRetries = 0, TimeSpan? timeout = null) =>
    new(new AnthropicLlmGatewayOptions(DummyKey, new Uri("http://anthropic.invalid"), timeout ?? TimeSpan.FromSeconds(30), maxRetries), handler);
```
- `RecordingHandler(params (HttpStatusCode, string)[] script)` (line 473+; last scripted response repeats, `.Attempts`, `.RequestBodies`) is `internal sealed`; to reuse it in cache/retry-chain tests, keep new tests in this project.
- Fixtures: `Fixture("messages-haiku-end-turn.json")` reading from `AppContext.BaseDirectory/Fixtures`.
- xUnit v3: `TestContext.Current.CancellationToken`, `Assert.*` only.
- Counting inner gateway: no shared double exists; `ScriptedGateway` in `EvalEndpointTests.cs` is private. Extract or duplicate.

### Tests: Api project
**Analog:** `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs`
`await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());` (e.g. lines 40-129). `TestHost.StartAsync` has a `config` dictionary parameter: pass `Llm:Cache:Directory` with a temp dir. The cache is on by default in Development (D-27), so TestHost seeds `Llm:Cache:Mode` off unless the test's config sets a cache key (plan 03-03); a test that wants a cache passes its own per-test directory.

### `GoldenResponseTests.cs`
**Analog:** `dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs:30-40`
```csharp
var expected = new UTF8Encoding(false).GetBytes(CanonicalSchema.ExportJson());
var committed = File.ReadAllBytes(CommittedSchemaPath);
Assert.True(expected.AsSpan().SequenceEqual(committed),
    "schema/invoice.schema.json is stale. Run: dotnet run --project dotnet/tools/SchemaExport");
```
Also `RepoRoot.Find()` for locating the repo and the "ends with one LF, no CR, no BOM" test (lines 17-25). Add the `CARIMBO_UPDATE_GOLDEN=1` rewrite branch and a "Run: just golden" failure message. The scripted host `HttpAttempts: 0` at `dotnet/tests/Carimbo.ScriptedHost/Program.cs:76` becomes 1.

### `justfile`
**Analog:** `justfile:34-36`
```
schema-check:
    "{{ just_executable() }}" --justfile "{{ justfile() }}" schema
    git diff --exit-code -- schema python/src/carimbo_models
```
Copy for `golden`/`golden-check` (`git diff --exit-code -- data/contract`). `docs-check` (lines 51-56) loops `for id in D-18 ... D-25`; append the new ids. New recipes must be real if mentioned in README/AGENTS (`test_every_documented_just_recipe_exists`, `python/tests/test_repo_layout.py:69`).

### Python runner / contract
**Analog:** `python/src/carimbo_evals/runner.py`
- `CONTRACT_VERSION = "2"` (line 31) and payload builder (`"contract_version": CONTRACT_VERSION`, line 133): bump to "3", add `overrides` when set.
- `account()` (lines 309-318): currently `parse_cost(response.get("cost_usd"))`; switch to `incurred_cost_usd` when present, keep the `unpriced`/`assumed` branch for `None`.
- Cost parsing stays through `parse_cost` and `Decimal` (money is a string on the wire).
- `contract.py`: Pydantic v2 `ConfigDict(extra="forbid", strict=True)`; `outcome.invoice` may reuse `carimbo_models.generated.Invoice`.

### Python tests
**Analog:** `python/tests/test_e2e_fake.py` (module docstring, subprocess drive of `carimbo-evals run` then `grade`, ScriptedHost on a free port, `new_traceparent`). Extend with `Llm__Cache__Directory` env for the host: `read_write` run then `read_only` run, asserting 0 attempts. Golden test: glob `data/contract/eval-responses/*.json` and validate with the strict models; static compose test: add to `test_repo_layout.py` using its `_read(relative)` helper (line 24).

## Shared Patterns

### Fail-fast configuration
**Source:** `Program.cs:47-70, 116-128` (`throw new InvalidOperationException("Section:Key must ...")`). Apply to cache dir/mode, MaxRetries upper bound, OTel endpoint.

### No content, no keys in errors/spans
**Source:** `Invalid(...)` in `EvalEndpoint.cs` (names field, never echoes payload). Apply to override validation and `TracingLlmGateway` tag allowlist.

### Money as string
**Source:** wire rule in AGENTS.md; `cost_usd` strings parsed by `parse_cost`. Apply to `incurred_cost_usd`, `carimbo.llm.cost_usd`.

### Generated-file convention
**Source:** AGENTS.md "Generated files are never hand-edited". Add `data/contract/*` to that list; stale-check as for `schema/`.

### Format gates
`dotnet build -warnaserror` and `dotnet format --verify-no-changes` over the whole solution; packages only via `Directory.Packages.props` (exact versions).

## No Analog Found

| File | Role | Data Flow | Reason |
|---|---|---|---|
| `Cache/LlmCacheStore.cs` | service | file-I/O | No atomic file store exists; use RESEARCH Pattern 3 (temp file + `File.Move(overwrite: true)`, filename = hex digest, corrupt = miss) |
| `compose.yaml` | config | n/a | No compose file yet; use the RESEARCH snippet (Jaeger 2.22.0, loopback ports) |

`LlmCacheKey.cs` has only partial analogs: follow the pinned-hash test style in `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` (prompt SHA pins) for the pinned-vector test, and build canonical bytes with `Utf8JsonWriter` per RESEARCH Pattern 2.

## Metadata

**Analog search scope:** `dotnet/src`, `dotnet/tests`, `python/src`, `python/tests`, `justfile` (via `git ls-files`)
**Files read in full or targeted:** LlmContracts.cs, Program.cs, LlmPricing.cs (decorator), EvalEndpoint.cs (segments), AnthropicGatewayTests.cs (segments), SchemaSnapshotTests.cs, runner.py (segments), test_e2e_fake.py (header), justfile (segments)
**Pattern extraction date:** 2026-10-15
