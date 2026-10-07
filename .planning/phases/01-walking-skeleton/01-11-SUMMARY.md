---
phase: 01-walking-skeleton
plan: 11
subsystem: llm-gateway
tags: [dotnet, pricing, cost-accounting, decimal, decorator, eval-endpoint, llm-02]
status: complete

requires:
  - phase: 01-walking-skeleton
    provides: "LlmUsage, LlmCost, LlmResponse and ILlmGateway (01-03); the eval endpoint with null cost fields and CarimboApi.CreateApp (01-08); direct-sdk and observed usage fields from the live spike (01-10)"
provides:
  - "dotnet/src/Carimbo.Llm/pricing.json: versioned USD-per-MTok table (anthropic-2026-10-04) for claude-haiku-4-5 and claude-sonnet-5-5 with the dated Haiku snapshot alias"
  - "LlmPricingTable (Version, LoadEmbedded, Price, CanPrice) and CostAccountingLlmGateway in Carimbo.Llm"
  - "Carimbo.Llm.Tests project (xunit.v3) with PricingTests, Fixtures/*.json copied to output"
  - "Eval response fields: cost_usd as an F8 string or null, cost_warning (new), effective.pricing_version always filled"
affects: [01-12, 01-13, 03-extraction-hardening]

actuals:
  tokens: 6600
  tasks: 2
  commits: 4
plan_head_before: 712781730af5a349e1d5ffd53ce73ee1b99f4171
plan_head_after: d6f071ea83cea0776ddb6d038292558ae0249dc3

tech-stack:
  added: []
  patterns:
    - "Prices live in an embedded JSON data file as decimal strings, never as C# constants"
    - "Cost accounting is a gateway decorator applied by the composition root to whichever ILlmGateway is registered, so scripted, stub and real gateways price identically"
    - "A model missing from the table gives a null cost plus an unpriced_model warning, never zero"

key-files:
  created:
    - dotnet/src/Carimbo.Llm/LlmPricing.cs
    - dotnet/src/Carimbo.Llm/pricing.json
    - dotnet/tests/Carimbo.Llm.Tests/Carimbo.Llm.Tests.csproj
    - dotnet/tests/Carimbo.Llm.Tests/PricingTests.cs
  modified:
    - dotnet/src/Carimbo.Llm/Carimbo.Llm.csproj
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/src/Carimbo.Api/EvalEndpoint.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - dotnet/Carimbo.slnx

key-decisions:
  - "The decorator prices by ModelReturned when the table can price it (directly or through an alias) and otherwise by ModelRequested. A dated snapshot the table does not list therefore falls back to the requested alias rather than going unpriced."
  - "effective.pricing_version is the table version, not response.Cost.PricingVersion, so it is filled on infrastructure failures and every other path where no LlmResponse exists."
  - "cost_warning carries an explicit JsonPropertyName so the wire name is greppable and does not depend on the snake-case policy."

patterns-established:
  - "Decorating a registered service in CarimboApi.CreateApp after configureServices runs: remove the last ILlmGateway descriptor and re-add a factory that resolves the original from its instance, factory or type"

requirements-completed: [LLM-02]

coverage:
  - id: D1
    description: "Cost is computed from a versioned data table over all five token classes with decimal arithmetic and exact results (Haiku 1000 in / 200 out = 0.00200000; Sonnet 12 in / 5000 cache-write-5m / 3000 cache-read / 300 out = 0.01612400)"
    requirement: "LLM-02"
    verification:
      - kind: test
        ref: "dotnet/tests/Carimbo.Llm.Tests/PricingTests.cs#Haiku_uncached_input_and_output_are_priced_exactly, #Sonnet_prices_all_five_token_classes_together, #Cache_write_1h_is_priced_at_its_own_rate, #Cache_read_is_priced_at_its_own_rate"
        status: pass
    human_judgment: false
  - id: D2
    description: "A dated snapshot id returned by the API is priced through the alias map as its base model"
    requirement: "LLM-02"
    verification:
      - kind: test
        ref: "dotnet/tests/Carimbo.Llm.Tests/PricingTests.cs#A_dated_snapshot_id_is_priced_as_its_base_model"
        status: pass
    human_judgment: false
  - id: D3
    description: "A model missing from the table yields a null cost with an explicit unpriced_model warning, never zero; zero usage on a known model is an honest 0.00000000"
    requirement: "LLM-02"
    verification:
      - kind: test
        ref: "dotnet/tests/Carimbo.Llm.Tests/PricingTests.cs#An_unknown_model_is_unpriced_with_a_warning_never_zero, #Zero_usage_on_a_known_model_is_an_honest_zero"
        status: pass
    human_judgment: false
  - id: D4
    description: "CostAccountingLlmGateway prices by the returned model when it resolves, falls back to the requested model, reports unpriced when neither resolves, and passes exceptions through unchanged"
    requirement: "LLM-02"
    verification:
      - kind: test
        ref: "dotnet/tests/Carimbo.Llm.Tests/PricingTests.cs#The_decorator_* (four tests)"
        status: pass
    human_judgment: false
  - id: D5
    description: "Every eval response carries cost_usd (F8 string or null), cost_warning and effective.pricing_version whatever gateway is registered, including on typed infrastructure failures"
    requirement: "LLM-02"
    verification:
      - kind: test
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_priced_model_reports_cost_usd_to_eight_places_and_the_pricing_version, #An_unpriced_model_reports_null_cost_and_an_explicit_warning_never_zero, #An_infrastructure_failure_has_no_cost_and_no_warning_but_still_names_the_pricing_version"
        status: pass
      - kind: command
        ref: "uv run --project python pytest python/tests/test_e2e_fake.py -m e2e -q (3 passed)"
        status: pass
    human_judgment: false
  - id: D6
    description: "Pricing correctness against live billing: the table rates and the assumption that thinking tokens are billed inside output_tokens hold on the real adapter"
    requirement: "LLM-02"
    verification: []
    human_judgment: true
    rationale: "Exact values are proven against the plan's table; agreement with a real invoice or live usage is proven on the real adapter in 01-12 and the live runs in 01-13"
---

# Phase 1 Plan 11: Cost from a versioned pricing table on every call Summary

**A data-driven pricing table (`pricing.json`, decimal-only) and a `CostAccountingLlmGateway` decorator now price every model call across all five token classes, with snapshot aliases, an explicit null-plus-warning for unknown models, and `cost_usd`, `cost_warning` and `effective.pricing_version` populated on every eval response.**

## Performance

- **Duration:** 4 min (2026-10-07T23:43:45Z to 2026-10-07T23:47:12Z for the code; SUMMARY follows)
- **Tasks:** 2 of 2 (both TDD)
- **Files:** 4 created, 5 modified
- **Test totals:** 135 passed across the solution (the new `Carimbo.Llm.Tests` project plus 3 new endpoint tests), e2e tracer 3 passed

## Accomplishments

- `pricing.json` holds the `anthropic-2026-10-04` rates (Haiku 4.5: 1.00 / 1.25 / 2.00 / 0.10 / 5.00; Sonnet 5.5: 2.00 / 2.50 / 4.00 / 0.20 / 10.00 USD per MTok for input, cache write 5m, cache write 1h, cache read, output) and the `claude-haiku-4-5-20251001` alias. It is an embedded resource under the logical name `Carimbo.Llm.pricing.json`.
- `LlmPricingTable.Price` computes `(in x r + cw5m x r + cw1h x r + cacheRead x r + out x r) / 1,000,000` in `decimal`, rounded to 8 places away from zero. The unit tests assert exact equality for the two plan values, the 1h write rate, the cache-read rate, half-up rounding and the alias path.
- `CostAccountingLlmGateway` decorates any `ILlmGateway`. `CarimboApi.CreateApp` registers the table and, after `configureServices` runs, replaces the registered gateway (instance, factory or type registration) with the decorated one. The scripted host, the Api test stubs and the real adapter from 01-12 all get it with no per-gateway code.
- `EvalEndpoint` now emits `cost_usd` as `F8` invariant, the new additive `cost_warning`, and `effective.pricing_version` from the table so it is present on infrastructure failures too (previously null whenever there was no `LlmResponse`).

## Task Commits

1. **Task 1 RED: failing pricing tests, project joins the solution** - `7f9cf2a` (test)
2. **Task 1 GREEN: pricing table and decorator** - `7d269cb` (feat)
3. **Task 2 RED: failing endpoint cost tests** - `59a1678` (test)
4. **Task 2 GREEN: gateway decoration and cost fields in the response** - `d6f071e` (feat)

## Decisions Made

See `key-decisions`. One item worth carrying forward: the cache-write multipliers (5m at 1.25x input, 1h at 2x input) were taken from the plan and the research table verified 2026-10-04 and were not re-fetched from the pricing page in this plan. They are consistent with the observed usage field split in the 01-10 spike (5m / 1h counts reported separately). A mismatch found later is a `pricing.json` data change plus a version bump, not a code change.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Task 2 acceptance grep for `cost_warning` needs the literal in source**
- **Found during:** Task 2 acceptance check
- **Issue:** The snake-case naming policy derives `cost_warning` from `CostWarning`, so the literal never appeared in `EvalEndpoint.cs` and the plan's `grep -c 'cost_warning'` returned 0.
- **Fix:** Added `[property: JsonPropertyName("cost_warning")]`, which also pins the wire name explicitly.
- **Files modified:** `dotnet/src/Carimbo.Api/EvalEndpoint.cs`
- **Commit:** `d6f071e`

**Total deviations:** 1 auto-fixed (blocking acceptance check). **Impact:** none on behaviour.

### Environment note

The Task 2 e2e command from the plan, `nix shell nixpkgs#dotnet-sdk_10 -c uv run ...`, fails on this machine because `uv` is not on PATH. It was run as `nix shell nixpkgs#dotnet-sdk_10 nixpkgs#uv -c uv run --project python pytest python/tests/test_e2e_fake.py -m e2e -q` (3 passed).

## Verification

- `cd dotnet && dotnet test`: 135 passed, 0 failed (includes `Carimbo.Llm.Tests` in the run).
- `dotnet format dotnet/Carimbo.slnx --verify-no-changes`: exit 0.
- e2e tracer `test_e2e_fake.py -m e2e`: 3 passed.
- Acceptance greps: `double|float` in `LlmPricing.cs` = 0; `"claude-sonnet-5-5"` in `pricing.json` = 1; `"pricing_version"` = 1; `Carimbo.Llm.pricing.json` in the csproj = 1; `0.01612400` in `PricingTests.cs` >= 1; `CostAccountingLlmGateway` in `Program.cs` = 1; `cost_warning` in `EvalEndpoint.cs` = 1; `"F8"` = 1.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-01-27 mitigated: decimal-only arithmetic, all five token classes, exact-value tests, the table version recorded on every response, and unknown models reported as a null cost plus warning (never zero). T-01-SC: no packages added (xunit.v3 already pinned centrally).

## Flagged Assumption

Thinking tokens are billed inside `output_tokens` (research A3). The 01-10 spike observed `thinking_tokens=0` inside `output_tokens` on Sonnet 5.5 at low effort, which is consistent but does not exercise a case with real thinking output. If 01-12 or 01-13 show otherwise, the correction is a `pricing.json` change recorded in D-21.

## Next Phase Readiness

Plan 01-12 can register the real adapter as a plain `ILlmGateway` and rely on the decorator for cost; it should add a pricing assertion over the three 01-10 fixtures (`messages-cache-usage.json` carries cache read usage). The runner's reserve-amount handling for a null `cost_usd` (01-09) is unchanged and now meets a real `cost_warning` field.

## Self-Check: PASSED

- FOUND: dotnet/src/Carimbo.Llm/LlmPricing.cs, dotnet/src/Carimbo.Llm/pricing.json, dotnet/tests/Carimbo.Llm.Tests/Carimbo.Llm.Tests.csproj, dotnet/tests/Carimbo.Llm.Tests/PricingTests.cs
- FOUND commits: 7f9cf2a, 7d269cb, 59a1678, d6f071e (all ancestors of HEAD)
