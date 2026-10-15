---
phase: 03-replayable-runs
plan: 01
subsystem: llm-gateway
tags: [cache, sha256, decorator, dotnet, xunit, replay]

requires:
  - phase: 01-foundation
    provides: ILlmGateway seam, CostAccountingLlmGateway decorator pattern, LlmPricingTable
  - phase: 02-extraction
    provides: prompt and repair prompt versions, ExtractorTests request-property guard
provides:
  - LlmCacheKey (llm-cache-key-1): canonical JSON and SHA-256 request identity, pinned test vector
  - LlmCacheStore (llm-cache-entry-1): one JSON file per key, atomic temp-and-move writes, defensive reads
  - CachingLlmGateway: off / read_write / read_only / refresh mode matrix over any ILlmGateway
  - LlmRequest.PromptVersion, RepairPromptVersion, ReplicateSalt, CacheMode; LlmResponse.Cache; LlmFailureKind.CacheMiss
  - LlmCacheMode, LlmCacheStatus, LlmCacheInfo, LlmCacheSettings, LlmCacheModes (wire names)
affects: [03-02, 03-03, 06-fixtures]

actuals:
  tokens: 14000
  tasks: 2
  commits: 2

tech-stack:
  added: []
  patterns:
    - "Decorator over ILlmGateway composed outside cost accounting so a stored answer carries its original cost"
    - "Canonical key by Utf8JsonWriter in fixed member order, large inputs enter as their own SHA-256"
    - "Atomic file publish: temp file in the same directory, File.Move(overwrite: true)"
    - "Key recipe guarded by a pinned vector and a keyed-or-excluded property coverage test"

key-files:
  created:
    - dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs
    - dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs
    - dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs
    - dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs
    - dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs
  modified:
    - dotnet/src/Carimbo.Llm/LlmContracts.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs

key-decisions:
  - "A hit reports HttpAttempts 0 and Cache.Status Hit but replays the original Latency, Usage and Cost, so replay is never mistaken for spend (LLM-05)"
  - "Eligibility is a whitelist: only StopReason EndTurn is stored; refresh never deletes the old entry on a non-cacheable or failed answer"
  - "A cache write failure (IOException, UnauthorizedAccessException) does not discard a paid answer: the response is returned with Stored false"
  - "A null replicate salt and an empty one are the same replicate (the key writes the empty string for null)"
  - "latency_ms is stored as an exact decimal of ticks so a replay returns the original TimeSpan bit for bit"

patterns-established:
  - "Pinned canonical JSON plus pinned hex in CacheKeyTests: a recipe change must bump LlmCacheKey.Version"
  - "Every public LlmRequest property is in KeyedProperties or ExcludedProperties; adding a property forces a key decision"

requirements-completed: [LLM-04, LLM-05, EXT-05]

coverage:
  - id: D1
    description: "A read_write call stores an end_turn answer as one <key>.json and a fresh read_only gateway on the same directory replays it with zero inner calls, HttpAttempts 0 and the original latency, usage and cost"
    requirement: "LLM-05"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs#A_read_write_answer_is_replayed_by_a_fresh_read_only_gateway_with_zero_model_calls"
        status: pass
    human_judgment: false
  - id: D2
    description: "Cache key is the SHA-256 of a fixed-order canonical JSON, pinned to e9ed4fd7...cb41b, sensitive to every keyed field and blind to CacheMode, with every LlmRequest property keyed or excluded"
    requirement: "LLM-04"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs#The_pinned_request_has_the_pinned_key"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs#Every_public_request_property_is_either_keyed_or_explicitly_excluded"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs#Changing_any_keyed_field_changes_the_key"
        status: pass
    human_judgment: false
  - id: D3
    description: "Mode matrix per D-19: read_only miss raises CacheMiss with no inner call, refresh overwrites only on end_turn, off bypasses with Cache null, request CacheMode overrides the default"
    requirement: "LLM-05"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs#Refresh_calls_the_provider_overwrites_the_entry_and_reports_refresh"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs#The_request_cache_mode_overrides_the_default_mode_in_each_direction"
        status: pass
    human_judgment: false
  - id: D4
    description: "Only end_turn answers are cached; garbage, wrong-version and key-mismatched entries are misses; keys never reach a path unless 64 lowercase hex; entries hold no prompt, schema or document"
    requirement: "LLM-04"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs#Only_an_end_turn_answer_is_cached"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs#A_malformed_entry_is_a_miss_for_read_only"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs#An_entry_never_contains_the_prompt_the_schema_or_the_document"
        status: pass
    human_judgment: false
  - id: D5
    description: "Eight concurrent identical read_write misses leave exactly one parseable entry and no temporary file"
    requirement: "LLM-04"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs#Concurrent_identical_misses_leave_one_complete_entry_and_no_temporary_file"
        status: pass
    human_judgment: false
  - id: D6
    description: "Prompt version and repair prompt version are cache key fields (the cache half of EXT-05); the extractor does not stamp them yet (plan 03-03)"
    requirement: "EXT-05"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs#Changing_any_keyed_field_changes_the_key"
        status: pass
    human_judgment: true
    rationale: "EXT-05 is only half delivered here: the extractor stamping the versions on LlmRequest and the API selecting prompt versions land in plan 03-03, so the requirement as a whole is judged at phase verification"

duration: 25min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 01: request-hash cache key, store and mode matrix Summary

**Request-hash response cache for the LLM gateway: a pinned SHA-256 key, an atomic one-file-per-key store and an off/read_write/read_only/refresh decorator that replays an end_turn answer with zero model calls, HttpAttempts 0 and the original latency and cost.**

## Performance

- **Duration:** about 25 min
- **Started:** 2026-10-15T07:25:00Z (approximate; the start time was not recorded)
- **Completed:** 2026-10-15T07:50:00Z
- **Tasks:** 2
- **Files modified:** 7 (5 created, 2 modified)

## Accomplishments

- Tracer: `CachingLlmGateway(CostAccountingLlmGateway(counting inner))` in read_write stores one `<key>.json`; a new gateway and store over the same directory in read_only answers the same request with 0 inner calls, `HttpAttempts` 0, `Latency` 1234 ms, equal `Usage` and `Cost`, and `Cache` Hit.
- Key pinned: canonical JSON and hex `e9ed4fd7f475483da7417e82b5a8de4fbce1f80dacb5071652b9b4480e4cb41b` (computed outside the code under test) match the implementation first time. A coverage test forces every `LlmRequest` property to be keyed or excluded, so Phase 3.1's tool turns cannot silently skip the key.
- Full D-19 mode matrix, whitelist eligibility (only `EndTurn`), defensive reads (garbage, wrong `entry_version`, key mismatch are misses), path-safe keys (`^[0-9a-f]{64}$`), atomic writes with no surviving `.tmp`, and an entry that never contains the prompt, schema, follow-ups or document bytes.
- The ExtractorTests request-property guard now lists the four new members, documented as cache identity, not case metadata.

## Task Commits

1. **Task 1 (tracer): read_write store then read_only zero-call replay** - `2584d7e` (feat)
2. **Task 2: pin the key and cover the full mode matrix** - `5f69f48` (test)

**Plan metadata:** committed separately as `docs(03-01): complete ... plan`.

_TDD note: Task 1 followed RED then GREEN locally (the tracer test failed on its intended assertions, 2 inner calls instead of 1 and no CacheMiss, against a pass-through stub) and was committed once as the plan's task unit. Task 2's tests passed on first run because the full mode matrix was already implemented in Task 1 (see Deviations); a mutation of the eligibility check (accept all but Refusal) failed 4 tests, confirming the tests are not vacuous._

## Files Created/Modified

- `dotnet/src/Carimbo.Llm/LlmContracts.cs` - new request identity members, cache enums and records, `LlmCacheModes` wire names, `LlmResponse.Cache`, `LlmFailureKind.CacheMiss`
- `dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs` - canonical JSON writer, SHA-256 key, keyed and excluded property lists
- `dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs` - entry/key-field records, path-safe store, atomic write, tolerant read
- `dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs` - mode matrix decorator
- `dotnet/tests/Carimbo.Llm.Tests/CachingGatewayTests.cs` - tracer, matrix, eligibility, malformed entries, concurrency, on-disk content; `CountingGateway` and `FixedTimeProvider` doubles
- `dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs` - pinned vector, property coverage, sensitivity, wire names
- `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` - request-property guard updated

## Decisions Made

- A hit replays original latency, usage and cost but reports `HttpAttempts` 0 and `Cache.Status` Hit (LLM-05 prohibition: replay must never read as spend).
- A cache write failure (`IOException`, `UnauthorizedAccessException`) returns the paid answer with `Stored` false instead of throwing; losing a paid answer to a misbehaving cache directory would be worse than an unstored entry.
- Null and empty replicate salt hash identically (null is written as the empty string), per the plan.
- `latency_ms` is serialized as an exact decimal of ticks, so a replayed `Latency` equals the original `TimeSpan` exactly even for fractional milliseconds.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Cache write failures do not discard a paid answer**
- **Found during:** Task 1 (CachingLlmGateway)
- **Issue:** Writing to the cache after a paid provider call could throw (disk full, permissions, a transient move failure on Windows), losing an answer already paid for. The plan did not specify behavior.
- **Fix:** `TryStore` catches `IOException` and `UnauthorizedAccessException` and reports `Cache.Stored` false.
- **Files modified:** dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs
- **Verification:** full Llm test project, `just dotnet-check`
- **Committed in:** 2584d7e

**2. [Plan sequencing] Full mode matrix implemented in Task 1**
- **Found during:** Task 1
- **Issue:** The plan lets Task 1 leave Off as a pass-through and Refresh to Task 2. Implementing `Off` and `Refresh` in the same decorator was simpler and avoided a throwaway branch.
- **Fix:** Task 1 ships the complete matrix; Task 2 contributes the pinned key tests and the full behavior coverage, plus a mutation check showing the new tests fail when eligibility is broken. No behavior differs from the plan's Task 2 specification.
- **Files modified:** dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs
- **Committed in:** 2584d7e

---

**Total deviations:** 2 (1 Rule 2 addition, 1 sequencing change)
**Impact on plan:** No scope creep; both match the plan's final specified behavior.

## Issues Encountered

None. The pinned canonical JSON and key from the plan matched the implementation without change.

## Known Stubs

None.

## Threat Flags

None. T-03-01 (poisoned entry), T-03-02 (path traversal), T-03-04 (entry content) and T-03-05 (replay as fresh call) are mitigated and tested as planned; no package was added (T-03-SC).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 03-03 can register `LlmCacheSettings` and `CachingLlmGateway` in the Api composition root (outside `CostAccountingLlmGateway`) and make the extractor stamp `PromptVersion`, `RepairPromptVersion` and `ReplicateSalt`.
- Note for 03-03: `LlmFailureKind.CacheMiss` is a new value; the Api wire mapping snake-cases it to `cache_miss` through the existing `SnakeCaseLower` conversion, and the Extraction failure-kind theory already covers it.
- Requirements LLM-04, LLM-05 and EXT-05 are declared by sibling plans in this phase, so they stay unmarked until the last declaring plan finishes (shared-ID gate).

## Self-Check: PASSED

- Created files exist: LlmCacheKey.cs, LlmCacheStore.cs, CachingLlmGateway.cs, CachingGatewayTests.cs, CacheKeyTests.cs (FOUND).
- Commits `2584d7e` and `5f69f48` are ancestors of HEAD.
- Acceptance criteria re-run: key and entry version greps, `CacheMiss`, `record LlmCacheSettings`, `LlmCacheKey.Compute` (all 1 or more), pinned hex in CacheKeyTests (1), `ExcludedProperties` (2).
- `just dotnet-check`: build with warnings as errors, format verification and 665 tests passed.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
