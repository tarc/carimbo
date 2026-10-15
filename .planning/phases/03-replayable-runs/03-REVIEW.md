---
phase: 03-replayable-runs
reviewed: 2026-10-15T00:00:00Z
depth: standard
files_reviewed: 42
files_reviewed_list:
  - AGENTS.md
  - compose.yaml
  - data/contract/eval-request.json
  - docs/DECISIONS.md
  - dotnet/Directory.Packages.props
  - dotnet/src/Carimbo.Api/Carimbo.Api.csproj
  - dotnet/src/Carimbo.Api/EvalEndpoint.cs
  - dotnet/src/Carimbo.Api/Program.cs
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/src/Carimbo.Extraction/PromptCatalog.cs
  - dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs
  - dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs
  - dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs
  - dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs
  - dotnet/src/Carimbo.Llm/LlmContracts.cs
  - dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs
  - dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs
  - dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs
  - dotnet/tests/Carimbo.Api.Tests/Carimbo.Api.Tests.csproj
  - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
  - dotnet/tests/Carimbo.Api.Tests/GoldenResponseTests.cs
  - dotnet/tests/Carimbo.Api.Tests/RetryConfigurationTests.cs
  - dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/RetryOwnershipTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs
  - dotnet/tests/Carimbo.ScriptedHost/Program.cs
  - justfile
  - python/src/carimbo_evals/cli.py
  - python/src/carimbo_evals/contract.py
  - python/src/carimbo_evals/grader.py
  - python/src/carimbo_evals/runner.py
  - python/src/carimbo_evals/summary.py
  - python/tests/test_contract_golden.py
  - python/tests/test_e2e_fake.py
  - python/tests/test_grader.py
  - python/tests/test_repo_layout.py
  - python/tests/test_runner.py
  - README.md
findings:
  critical: 0
  warning: 5
  info: 3
  total: 8
status: issues_found
---

# Phase 3: Code Review Report

**Reviewed:** 2026-10-15
**Depth:** standard
**Files Reviewed:** 42
**Status:** issues_found

## Summary

I read the production sources in full (cache key, store and decorator, tracing, composition root, eval endpoint, extractor, runner, grader, summary, CLI, justfile, compose) and used the diff against `4864c12` to scope the test files, which I skimmed for reliability problems (no sleeps or flaky patterns found in the .NET tests; the e2e test polls `/healthz` with a bounded loop). I did not run the test suites. Nothing in scope reaches BLOCKER level: the cost-cap logic, key derivation, atomic cache writes, key-digest-only file names, the prompt catalog and override allow-lists all hold up when traced. No secret reaches a span, a log line or a cache entry.

The three residuals from `03-SECURITY.md` were checked against the code. All three are real (WR-01, WR-02, WR-03). The audit's severity read (non-blocking) is right for each. Two further warnings concern the evidence the eval produces (WR-04, WR-05), which matters because measured, reproducible results are the product (AGENTS.md core value).

## Warnings

### WR-01: A cache entry with a JSON `null` in a required member is read as a hit (confirms T-03-01 residual)

**File:** `dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs:221-227, 248, 257, 244`
**Issue:** `Parse` uses `GetProperty(...).GetString()!` for `model`, `schema_sha256`, `document_sha256`, `replicate_salt`, `response.text`, `response.model_requested` and `cost.pricing_version`. `GetString()` returns `null` for a JSON null and does not throw, so the `!` only silences the compiler. `TryRead` then returns a hit whose `Response.Text` is null. `CachingLlmGateway` serves it, and `InvoiceExtractor.Parse(response.Text)` calls `JsonSerializer.Deserialize<Invoice>((string)null)`. That throws `ArgumentNullException`, which `Parse` does not catch (it handles only `JsonException`, `FormatException` and `OverflowException`), so the eval request ends as a 500 instead of a cache miss. The class summary promises "anything that is not a valid entry for the key is a miss, never an exception", and `CachingGatewayTests.MalformedEntries` covers only `garbage`, `wrong_version` and `other_key`, so the gap is untested. The same parser also accepts negative token counts, a negative `latency_ms` and a `stop_reason` other than `end_turn` (the writer never stores those). That does not matter for honest entries but weakens the "valid entry" contract.
**Fix:** Add a helper that returns null for a non-string and make `Parse` return null, which `TryRead` already treats as a miss.
```csharp
private static string? RequiredString(JsonElement e, string name) =>
    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

// in Parse:
var text = RequiredString(r, "text");
var modelRequested = RequiredString(r, "model_requested");
if (text is null || modelRequested is null /* and the key_fields / pricing_version strings */)
{
    return null;
}
// also: reject Usage values < 0 and a non-end_turn stop_reason.
```
Add `null_text`, `null_model_requested` and `negative_tokens` cases to `MalformedEntries`.

### WR-02: Exporter switch and startup log read only `OTEL_EXPORTER_OTLP_ENDPOINT`, but the exporter honours the traces-specific variable (confirms T-03-18 residual)

**File:** `dotnet/src/Carimbo.Api/Program.cs:124, 135-143`
**Issue:** I checked the shipped assembly (`OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1): `OtlpExporterOptions` reads `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` and gives it precedence over `OTEL_EXPORTER_OTLP_ENDPOINT`. Two consequences follow.
1. With both set, spans go to the traces endpoint while the startup line says `tracing: OTLP export to <generic endpoint>`. The log names a destination that is not the actual one, which defeats its purpose as the "where do spans go" statement (T-03-18, AR-03-03).
2. With only `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` set, `AddOtlpExporter()` is never called, so nothing is exported. That fails closed, but contradicts the standard OTel variable semantics, and the log says "unset".
The log also prints the endpoint verbatim, so an endpoint with embedded userinfo (`https://user:pass@host`) would put credentials in the log.
**Fix:** Resolve the endpoint the way the exporter does and log only scheme, host and port.
```csharp
var endpoint = NullIfBlank(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"])
               ?? NullIfBlank(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
...
: $"tracing: OTLP export to {Redact(endpoint)}";   // Uri.TryCreate -> $"{u.Scheme}://{u.Authority}" without UserInfo
```
Update the comment and the AR-03-03 wording ("only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set") and add a test with each variable set.

### WR-03: The compose loopback test checks only quoted short-syntax ports (confirms T-03-06 residual)

**File:** `python/tests/test_repo_layout.py:183-188`
**Issue:** `re.findall(r'"(\d+(?:\.\d+){3}:\d+:\d+|\d+:\d+)"', compose)` matches only a double-quoted mapping that ends right after the container port. These forms are invisible to it, so the test passes while the file publishes the unauthenticated Jaeger UI and OTLP receivers on all interfaces.
- an unquoted entry: `- 0.0.0.0:16686:16686` or `- 4319:4319`
- a protocol suffix: `"4317:4317/tcp"`
- an interpolated host: `"${BIND:-0.0.0.0}:4317:4317"`
- IPv6: `"[::]:4317:4317"`
- long syntax with `published:` and no `host_ip`
- `network_mode: host`
As long as one quoted loopback mapping exists, `assert mappings` and the two `any(...)` checks pass. The current compose file is correct, so this is a guard that does not guard, not a live exposure.
**Fix:** Parse the YAML (PyYAML or `ruamel` as a dev dependency, or `yaml` via uv) and check every `services.*.ports` entry in both syntaxes, plus forbid `network_mode: host`.
```python
for svc in doc["services"].values():
    assert svc.get("network_mode") != "host"
    for p in svc.get("ports", []):
        host_ip = p.split(":")[0] if isinstance(p, str) and p.count(":") >= 2 else (p.get("host_ip") if isinstance(p, dict) else None)
        assert host_ip == "127.0.0.1", p
```

### WR-04: A cache replay is labelled with the current pricing version while its costs come from the pricing version stored in the entry

**File:** `dotnet/src/Carimbo.Api/EvalEndpoint.cs:210, 288, 412` (with `dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs:42-75`)
**Issue:** `effective.pricing_version` is always `pricing.Version` of the running host. On a hit, the attempt's `Cost` (amount and `PricingVersion`) is replayed as recorded. The pricing version is deliberately not in the cache key, so after a pricing table bump the response says `effective.pricing_version = <new>` next to `cost_usd` computed with `<old>`. `summary.config.pricing_versions` is built from this field, so the summary and `summary.md` assert a pricing provenance for the notional cost that is false for replayed runs. The reproducibility evidence is the product here, so a silently wrong provenance label is a correctness defect, not a style point. The seeded `evals/results/*` runs are unaffected today because the version has not changed.
**Fix:** Derive the reported version from what priced the attempts, for example the `Cost.PricingVersion` of the answered attempts, and report all distinct values (or a `mixed` marker) when they differ.
```csharp
var versions = answered.Select(r => r.Cost?.PricingVersion).Where(v => v is not null).Distinct().ToArray();
var pricingVersion = versions.Length == 1 ? versions[0]! : versions.Length == 0 ? pricing.Version : "mixed";
```
Alternatively, keep the label and add a `Cost.PricingVersion != pricing.Version` warning to the response.

### WR-05: `summary.md` under-reports spend when an attempt's incurred cost is unknown

**File:** `python/src/carimbo_evals/summary.py:129-135, 248-249` (rendering at the `- cost:` line of `render_markdown`)
**Issue:** `_totals` counts `unpriced_incurred_cases` but `render_markdown` prints only `unpriced_cases`, which counts the notional cost. The .NET side sets `incurred_cost_usd` to null while `cost_usd` stays non-null in a common case: an answered attempt followed by a provider failure on the next attempt (see `EvalResponse.From`, the `incurredAmounts.Any(a => a is null)` branch, and the `IncurredAmount` doc comment). The headline then reads `US$0.00000000 incurred, US$0.05 notional (0 unpriced)`, while the true spend is unknown and non-zero. A reader of the committed summary takes "incurred" as the amount paid. The runner correctly charges the reserve for such a case; the summary does not show it.
**Fix:** Render the incurred side's unknown count, or mark the figure as a lower bound.
```python
f"- cost: US${totals['incurred_cost_usd']} incurred"
f"{' (at least; ' + str(totals['unpriced_incurred_cases']) + ' unknown)' if totals['unpriced_incurred_cases'] else ''}, "
f"US${totals['cost_usd']} notional ({totals['unpriced_cases']} unpriced)"
```
Add a grader or summary test with an answered-then-failed case.

## Info

### IN-01: Tracing leaves the span status unset for exceptions other than gateway and cancellation errors

**File:** `dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs:47-56`
**Issue:** Only `LlmGatewayException` and `OperationCanceledException` mark the span as an error. Anything else the inner gateway throws (for example the `ArgumentException` from `BuildParameters`, or a failure of the cache layer) disposes the span with status `Unset`, so the Jaeger trace for a request that ended in a 500 shows no error. The comment says exceptions are not recorded as events on purpose, which is sound; the status can still be set without a message.
**Fix:** Add `catch (Exception) { Fail(activity, "internal"); throw; }` after the two existing catches (or use an exception filter), still without description or event.

### IN-02: `TryStore` swallows only `IOException` and `UnauthorizedAccessException`

**File:** `dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs:61`
**Issue:** The comment says a paid answer must not be thrown away because the cache directory misbehaved, but other failures of the write path (`NotSupportedException` for an odd path, `System.Security.SecurityException`, an `InvalidOperationException` from `Utf8JsonWriter` on text it cannot transcode) would propagate and turn an already paid, successful provider call into a 500. These are unlikely, but the asymmetric cost (a paid call discarded) argues for a wider filter.
**Fix:** `catch (Exception ex) when (ex is not OperationCanceledException)` and return `false`, keeping `Stored: false` as the signal.

### IN-03: `justfile` interpolates recipe arguments unquoted into bash

**File:** `justfile:115, 155, 185`
**Issue:** `{{ max_cost }}`, `{{ max_repairs }}` and `{{ cache_mode }}` are pasted into shell lines without quoting (`Extraction__MaxRepairs={{ max_repairs }}`, `--cache-mode {{ cache_mode }}`), so an argument such as `'read_only; <cmd>'` runs as shell. The only caller is the local user or an agent acting for them, and the Python CLI validates the cache mode afterwards, so the risk is low, but the live recipe runs with the provider key in its environment and the other arguments are not validated before use (`max_repairs=abc` only fails when the Api starts).
**Fix:** Quote the interpolations (`"{{ cache_mode }}"`) and add a leading `case` check in `_skeleton-run` for numeric `max_cost`/`max_repairs` and the four cache modes before starting the Api.

---

_Reviewed: 2026-10-15_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
