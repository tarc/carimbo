---
phase: 03-replayable-runs
plan: 08
subsystem: extraction-api
tags: [prompt-catalog, overrides, pricing-allowlist, repair-budget, response-cache, cli, dotnet, python]

requires:
  - phase: 03-replayable-runs
    provides: plan 03-04 eval contract 3 (overrides object, cache_mode and replicate_salt, runner overrides pass-through)
  - phase: 03-replayable-runs
    provides: plan 03-06 pricing allowlist use (LlmPricingTable.CanPrice), incurred-cost accounting
  - phase: 03-replayable-runs
    provides: plan 03-07 model-call spans carrying the prompt version
provides:
  - "PromptCatalog (extract-002 restored byte-exact, extract-003 default), SHA-pinned, with ExtractionContract.WithPromptVersion"
  - "overrides.prompt_version, overrides.model and overrides.max_repairs on eval contract 3, each with a field-specific HTTP 400 that never echoes the value"
  - "ExtractionSettings.MaxRepairsLimit = 5 shared by the startup check, the endpoint and the extractor"
  - "carimbo-evals run --model / --prompt-version / --max-repairs"
affects: [03-09, 03-10, 04-evals-comparison, 06-fixtures]

actuals:
  tokens: 20950
  tasks: 3
  commits: 6
plan_head_before: 9d85103ed0b0a70e7edd7e39dd816c0e5098a67e
plan_head_after: 0c3f0c359d7aced258756082cb7290714d2a2911

tech-stack:
  added: []
  patterns:
    - "Per-request configuration is resolved once at the top of ExtractAsync (effective contract, model, repair budget) and passed down; no shared mutable state, so concurrent requests cannot see each other's overrides"
    - "Overrides stay raw JsonElement values until the endpoint validates them, so each wrong type or value is reported against its own overrides.<field> and never echoed"
    - "Allowlists, not free text: prompt versions from the compiled-in catalog, models from the pricing table"

key-files:
  created:
    - dotnet/src/Carimbo.Extraction/PromptCatalog.cs
  modified:
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/src/Carimbo.Api/EvalEndpoint.cs
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - python/src/carimbo_evals/cli.py
    - python/tests/test_runner.py
    - python/tests/test_e2e_fake.py

key-decisions:
  - "The model override is validated against LlmPricingTable.CanPrice, so the aliased id claude-haiku-4-5-20251001 is accepted and the pricing version is named in the 400 (T-03-21, T-03-23)"
  - "max_repairs is accepted only when the JSON value is a number for which TryGetInt32 succeeds and lies in 0..5; 2.5, 2.0, 1E0, 99999999999, strings, booleans, arrays and objects are all 400; an explicit null counts as absent"
  - "The extractor re-checks the budget range and throws ArgumentOutOfRangeException (defence in depth); an unknown prompt version in ExtractionOverrides throws ArgumentException rather than silently falling back"
  - "The CLI leaves model and prompt-version validation to the endpoint and only bounds --max-repairs (typer min 0, max 5), so a typo surfaces as the endpoint's field-specific 400 on the record"

patterns-established:
  - "Pinned text catalog: each prompt text is a const raw string and ExtractorTests pins its SHA-256, so a text change forces a new version"
  - "A compile-only skeleton commit gives a TDD RED that fails on assertions rather than on a build error"

requirements-completed: [API-02, EXT-05]

coverage:
  - id: D1
    description: "PromptCatalog holds exactly extract-002 and extract-003 (default extract-003) with texts pinned by SHA-256 (e1b74d10... and be77df54...); repair-001 is unchanged; WithPromptVersion swaps only the extraction prompt and throws for an unknown version"
    requirement: "EXT-05"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_catalog_holds_exactly_extract_002_and_extract_003_with_pinned_texts_and_extract_003_is_the_default"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_prompt_texts_are_pinned_to_their_versions"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#With_prompt_version_swaps_only_the_extraction_prompt"
        status: pass
    human_judgment: false
  - id: D2
    description: "overrides.prompt_version reaches the model, stamps LlmRequest.PromptVersion, is echoed in effective and attempts[0], and keys the cache: a default recording then the same document with extract-002 in read_only is a cache_miss, and a recorded extract-002 request is a hit; uncatalogued values are HTTP 400 naming overrides.prompt_version"
    requirement: "EXT-05"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_prompt_version_changes_the_cache_key_and_a_repeated_version_hits"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#The_prompt_version_override_reaches_the_model_and_is_echoed_in_effective_and_the_initial_attempt"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_uncatalogued_prompt_version_is_a_bad_request_naming_the_field_and_never_echoing_it"
        status: pass
    human_judgment: false
  - id: D3
    description: "overrides.model reaches the gateway and effective.model and only a model the pricing table can price (alias included) is accepted; overrides.max_repairs bounds the loop (0 means one attempt and CacheDocument false), is echoed, is HTTP 400 outside 0..5 and for every non-integer-literal, and concurrent requests keep their own budgets"
    requirement: "API-02"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#The_model_override_reaches_the_gateway_and_is_echoed_in_effective"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_model_the_pricing_table_cannot_price_is_a_bad_request_naming_the_field_and_never_echoing_it"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_repair_budget_that_is_not_an_integer_from_zero_to_five_is_a_bad_request_naming_the_field"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#Concurrent_requests_with_different_repair_budgets_each_get_their_own"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#A_repair_budget_override_outside_zero_to_the_ceiling_is_refused_before_any_call"
        status: pass
    human_judgment: false
  - id: D4
    description: "Through the documented commands, run --prompt-version extract-002 and run --model claude-sonnet-5-5 (read_only, after a recorded run) miss the cache on every case and echo the override, and run --max-repairs 0 gives case-002 exactly one attempt; the CLI sends only the given flags and refuses --max-repairs outside 0..5 with exit 2 before any request"
    requirement: "API-02"
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#test_model_prompt_version_and_repair_budget_overrides_keep_each_configuration_apart"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_cli_sends_model_prompt_version_and_max_repairs_as_overrides"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_cli_rejects_a_repair_budget_outside_zero_to_five_before_any_request"
        status: pass
    human_judgment: false
  - id: D5
    description: "EXT-05 category was left unclassified by the edge probe; whether a restored extract-002 (with known label defects G-02-1 and G-02-2) is the right second prompt, versus a test-only prompt, is a D-30 judgement the verifier should confirm"
    requirement: "EXT-05"
    verification: []
    human_judgment: true
    rationale: "Flagged assumption A4 in the plan: D-30 would need a superseding record if verification prefers a test-only second prompt; no test can decide that."

duration: 12min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 08: per-request model, prompt version and repair budget Summary

**A request can now pick a SHA-pinned prompt version, a priced model and a repair budget of 0 to 5, each echoed in `effective` and each separating the cache key, with matching `--model`, `--prompt-version` and `--max-repairs` runner flags proven through the documented commands.**

## Performance

- **Duration:** about 12 min (wall clock between the last prior commit and the final task commit; the start time was not captured by the executor)
- **Started:** 2026-10-15T08:38:00Z (approximate)
- **Completed:** 2026-10-15T08:50:22Z
- **Tasks:** 3 (1 tracer, 2 auto), each as a RED and a GREEN commit
- **Files modified:** 9 (1 created)

## Accomplishments

- `PromptCatalog` in `Carimbo.Extraction` holds extract-003 (default, moved out of `ExtractionContract.Build` unchanged) and extract-002 (restored byte-exact from the parent of 44c4323). Both hashes are pinned in `ExtractorTests`: extract-002 `e1b74d10...`, extract-003 `be77df54...`, repair-001 unchanged at `6ee06c84...`.
- `overrides.prompt_version`, `overrides.model` and `overrides.max_repairs` are validated at the edge against the catalog, `LlmPricingTable.CanPrice` and a strict JSON integer 0..5. Every invalid value is HTTP 400 naming the field and never echoing the value; the repair ceiling is the single `ExtractionSettings.MaxRepairsLimit`.
- The extractor resolves the effective contract, model and budget once per call, so concurrent requests with budgets 0 and 2 get 1 and 3 attempts and echo their own `effective.max_repairs`.
- The cache separates each configuration: a prompt-version change misses, a repeated version hits, and a budget of 0 (CacheDocument false) is a new key. Shown over HTTP in `EvalEndpointTests` and through `carimbo-evals run` in the e2e test.

## Task Commits

Tracer gate: after Task 1 the end-to-end verify was re-run and passed, so expansion continued ("Tracer verified end-to-end").

1. **Task 1 (tracer): prompt version end to end**
   - RED `1308a90` (test): catalog pins, WithPromptVersion, override stamping, HTTP echo/400/cache tests, over a compile-only skeleton
   - GREEN `8f9bbdf` (feat): PromptCatalog, WithPromptVersion, effective contract, endpoint validation
2. **Task 2: model and max_repairs overrides**
   - RED `01d89c2` (test): extractor and HTTP tests for model, boundary, precision, null and concurrency
   - GREEN `9331bea` (feat): shared ceiling, price allowlist, strict integer parsing, per-call resolution
3. **Task 3: CLI flags**
   - RED `b74800a` (test): payload, help, range-refusal and e2e tests
   - GREEN `0c3f0c3` (feat): `--model`, `--prompt-version`, `--max-repairs`

**Plan metadata:** recorded in the following docs commit.

## Files Created/Modified

- `dotnet/src/Carimbo.Extraction/PromptCatalog.cs` - catalogued, pinned prompt texts with the version history
- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` - `WithPromptVersion`, `ExtractionOverrides` gains PromptVersion, Model, MaxRepairs; `ExtractionSettings.MaxRepairsLimit`; per-call resolution
- `dotnet/src/Carimbo.Api/EvalEndpoint.cs` - the three overrides with field-specific 400s
- `dotnet/src/Carimbo.Api/Program.cs` - startup check uses `ExtractionSettings.MaxRepairsLimit`
- `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs`, `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs` - catalog pins, override behaviour, edges, concurrency
- `python/src/carimbo_evals/cli.py` - the three flags merged into the overrides dict
- `python/tests/test_runner.py`, `python/tests/test_e2e_fake.py` - payload, help, range and e2e misses

## Decisions Made

- Model validation uses `CanPrice`, which accepts the table's aliases; the 400 names the pricing version but not the submitted id.
- `max_repairs` null is treated as absent (the deserialiser maps a JSON null on a nullable `JsonElement` to null), matching the other overrides.
- The CLI bounds only `--max-repairs`; the endpoint stays the authority on models and prompt versions so the list cannot drift between the two.
- The existing unknown-member test used `model` as its example of a rejected member; it now uses `top_p`, since `model` is a real override.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Existing test used `model` as an unknown override member**
- **Found during:** Task 2 (RED)
- **Issue:** `An_unknown_member_of_overrides_is_a_bad_request_naming_the_body` listed `model`, which this plan makes valid, so it would fail for the wrong reason.
- **Fix:** replaced the `model` case with `top_p` in the RED commit.
- **Files modified:** dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
- **Verification:** full `just dotnet-check` green.
- **Committed in:** 01d89c2

**2. [Rule 3 - Blocking] RED needed a compile-only skeleton**
- **Found during:** Tasks 1 and 2 (RED)
- **Issue:** the tests reference `PromptCatalog`, `WithPromptVersion`, `ExtractionOverrides.Model/MaxRepairs` and `MaxRepairsLimit`, so without them RED would be a build error (INVALID_RED), not a failing assertion.
- **Fix:** each RED commit includes a stub (empty-effect members) so the targeted tests fail on their assertions.
- **Files modified:** PromptCatalog.cs, InvoiceExtractor.cs
- **Committed in:** 1308a90, 01d89c2

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** both are test-scaffolding adjustments; no scope change.

## TDD Gate Compliance

All three tasks have a `test(03-08)` commit before their `feat(03-08)` commit. The xUnit v3 (Microsoft.Testing.Platform) and pytest console output are not formats `gsd_run check tdd-red-evidence` supports, so `RED_EVIDENCE_OK` could not be produced; semantic assessment instead:

- **Task 1 RED:** the 8 Api and 6 Extraction target tests failed on their planned assertions (catalog Versions differ at index 0, `NotImplementedException` where `ArgumentException` was expected, 400 body lacking `overrides.prompt_version`), not on build or fixture errors.
- **Task 2 RED:** 6 Extraction tests and 27 Api tests failed on assertions (unknown member `overrides.model` answered as `body`, budget not applied); the build was clean.
- **Task 3 RED:** 11 pytest tests failed on "No such option" (exit 2 where 0 was expected) and the range-refusal tests were tightened to assert the option exists, so they failed too; the e2e test fails for the same reason.

There was no REFACTOR commit.

## Issues Encountered

- Two local name clashes in the extractor while introducing the per-call effective contract (a pattern variable shadowing a loop variable, then `this.` on a primary-constructor parameter); fixed before the GREEN commit by renaming the local to `effective`.

## Authentication Gates

None.

## Known Stubs

None.

## Threat Flags

None. The new request members are the ones in the plan's threat model (T-03-21, T-03-22, T-03-23), all mitigated: priced-model allowlist, 0..5 ceiling at the edge and in the extractor, catalogued SHA-pinned prompts with no prompt text crossing HTTP.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for 03-09. A comparison run can now vary model, prompt version and repair budget per request and keep the cached answers apart.
- Open for verification: D-30 (restored extract-002 as the second prompt) rests on research assumption A4; see coverage item D5.

## Self-Check: PASSED

- Created file present: `dotnet/src/Carimbo.Extraction/PromptCatalog.cs` (FOUND).
- Commits reachable from HEAD: 1308a90, 8f9bbdf, 01d89c2, 9331bea, b74800a, 0c3f0c3 (FOUND).
- Acceptance greps: extract-002 and extract-003 hashes in ExtractorTests, `PromptCatalog.TryGet`, `MaxRepairsLimit = 5`, `ExtractionSettings.MaxRepairsLimit` in Program.cs, `overrides.model`, `CanPrice`, `--prompt-version`, `--max-repairs` all present.
- `just check` exits 0 (dotnet-check 828 tests, py-check 363 passed, schema-check, datagen-check, e2e 7 passed, docs-check, secrets-check).

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
